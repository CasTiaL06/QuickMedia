using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuickMedia;

public enum MediaEvent { TrackChanged, PlaybackChanged, Updated, Cleared }

public sealed class MediaInfo
{
    public bool HasSession { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AppId { get; init; } = "";
    public string PlatformId { get; set; } = "other";
    public bool IsPlaying { get; init; }
    public BitmapSource? Art { get; set; }
}

/// <summary>Windows'un küresel medya oturumunu (SMTC) izler. Olaylar arka plan thread'inden gelir.</summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _version;

    public MediaInfo Current { get; private set; } = new();
    public event Action<MediaInfo, MediaEvent>? Changed;

    public async Task InitAsync()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mgr.CurrentSessionChanged += (_, _) => Hook();
        Hook();
    }

    private void Hook()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = _mgr?.GetCurrentSession();
        if (_session == null)
        {
            Current = new MediaInfo();
            Changed?.Invoke(Current, MediaEvent.Cleared);
            return;
        }
        _session.MediaPropertiesChanged += OnProps;
        _session.PlaybackInfoChanged += OnPlayback;
        _ = RefreshAsync();
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, object a) => _ = RefreshAsync();
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, object a) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var s = _session;
        if (s == null) return;
        int v = Interlocked.Increment(ref _version);
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            bool playing = s.GetPlaybackInfo().PlaybackStatus ==
                           GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string title = props.Title ?? "", artist = props.Artist ?? "";
            if (title.Length == 0 && artist.Length == 0)
            {
                // Oturum var ama içinde şarkı/video yok (ör. Spotify açık ama boşta): eski bilgiyi tutma
                if (v == _version && Current.HasSession)
                {
                    Current = new MediaInfo();
                    Changed?.Invoke(Current, MediaEvent.Cleared);
                }
                return;
            }

            var prev = Current;
            string appId = s.SourceAppUserModelId ?? "";
            bool sameTrack = prev.HasSession && prev.Title == title && prev.Artist == artist && prev.AppId == appId;

            var info = new MediaInfo
            {
                HasSession = true,
                Title = title,
                Artist = artist,
                Album = props.AlbumTitle ?? "",
                AppId = appId,
                IsPlaying = playing,
                PlatformId = sameTrack && prev.PlatformId != "other" ? prev.PlatformId : Platforms.Detect(appId, title, artist, props.AlbumTitle ?? ""),
                Art = sameTrack ? prev.Art : null,
            };

            if (props.Thumbnail != null && info.Art == null)
                info.Art = await LoadThumb(props.Thumbnail);

            if (v != _version) return; // daha yeni bir güncelleme geldi

            MediaEvent ev = !sameTrack ? MediaEvent.TrackChanged
                          : prev.IsPlaying != playing ? MediaEvent.PlaybackChanged
                          : MediaEvent.Updated;
            Current = info;
            Changed?.Invoke(info, ev);
        }
        catch { /* oturum kapanmış olabilir */ }
    }

    private static async Task<BitmapSource?> LoadThumb(IRandomAccessStreamReference r)
    {
        try
        {
            using var rs = await r.OpenReadAsync();
            using var ns = rs.AsStreamForRead();
            var ms = new MemoryStream();
            await ns.CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public (TimeSpan Pos, TimeSpan Dur)? GetTimeline()
    {
        try
        {
            var s = _session;
            if (s == null) return null;
            var t = s.GetTimelineProperties();
            var dur = t.EndTime - t.StartTime;
            if (dur <= TimeSpan.Zero) return null;
            var pos = t.Position - t.StartTime;
            if (Current.IsPlaying) pos += DateTimeOffset.Now - t.LastUpdatedTime;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > dur) pos = dur;
            return (pos, dur);
        }
        catch { return null; }
    }

    /// <summary>Şarkı / videoda belirtilen orana (0..1) atlar.</summary>
    public void Seek(double ratio)
    {
        try
        {
            var s = _session;
            if (s == null) return;
            var t = s.GetTimelineProperties();
            var dur = t.EndTime - t.StartTime;
            if (dur <= TimeSpan.Zero) return;
            var target = t.StartTime + TimeSpan.FromTicks((long)(dur.Ticks * Math.Clamp(ratio, 0, 1)));
            _ = s.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch { }
    }

    public void TogglePlay() { try { _ = _session?.TryTogglePlayPauseAsync(); } catch { } }
    public void Next() { try { _ = _session?.TrySkipNextAsync(); } catch { } }
    public void Previous() { try { _ = _session?.TrySkipPreviousAsync(); } catch { } }
}
