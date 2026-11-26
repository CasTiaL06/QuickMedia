using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuickMedia;

public enum MediaEvent { TrackChanged, PlaybackChanged, Updated, Cleared }

public sealed class MediaInfo
{
    public bool HasSession { get; init; }
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AppId { get; init; } = "";
    public string PlatformId { get; set; } = "other";
    public bool IsPlaying { get; init; }
    public BitmapSource? Art { get; set; }
}

/// <summary>
/// Windows yalnızca TEK bir "güncel" medya oturumu bildirir (örn. sonradan açılan YouTube videosu).
/// Burada tüm oturumlar ayrı ayrı izlenir; flyout'ta "Gizle" seçilmemiş olanlar arasından en uygunu
/// (çalan &gt; "Hep göster" &gt; en son etkin) seçilir. Gizli kaynaklar hiç seçilmez.
/// Olaylar arka plan thread'inden gelir.
/// </summary>
public sealed class MediaService
{
    private sealed class Tracker
    {
        public GlobalSystemMediaTransportControlsSession Session = null!;
        public string Key = "";
        public MediaInfo Info = new();
        public DateTime LastActive = DateTime.MinValue;
        public int Version;
        public string? TabTitle;      // tarayıcı sekmesinden okunan başlık (sabit tutulur)
        public string? TabArtist;
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? OnProps;
        public TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? OnPlay;
    }

    private readonly object _gate = new();
    private readonly List<Tracker> _trackers = new();
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _picked;
    private System.Threading.Timer? _tabTimer;

    /// <summary>Flyout'un göstereceği (gizli olmayan, en uygun) oturum; yoksa boş.</summary>
    public MediaInfo Current { get; private set; } = new();
    public event Action<MediaInfo, MediaEvent>? Changed;

    public async Task InitAsync()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mgr.SessionsChanged += (_, _) => Rebuild();
        Rebuild();
        _tabTimer = new System.Threading.Timer(_ => RefreshTabTitles(), null, 3000, 3000);
    }

    // ---------- Oturum listesi ----------

    private void Rebuild()
    {
        List<Tracker> fresh = new();
        lock (_gate)
        {
            var old = _trackers.ToDictionary(t => t.Key);
            foreach (var t in _trackers) Detach(t);
            _trackers.Clear();

            IReadOnlyList<GlobalSystemMediaTransportControlsSession> list;
            try { list = _mgr?.GetSessions() ?? (IReadOnlyList<GlobalSystemMediaTransportControlsSession>)Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
            catch { list = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }

            var counts = new Dictionary<string, int>();
            foreach (var s in list)
            {
                string app = s.SourceAppUserModelId ?? "";
                counts[app] = counts.GetValueOrDefault(app) + 1;
                string key = counts[app] == 1 ? app : $"{app}#{counts[app]}";

                var t = new Tracker { Session = s, Key = key };
                if (old.TryGetValue(key, out var o)) { t.LastActive = o.LastActive; t.Info = o.Info; }
                Attach(t);
                _trackers.Add(t);
                fresh.Add(t);
            }
        }
        foreach (var t in fresh) _ = RefreshAsync(t);
        Reevaluate();
    }

    private void Attach(Tracker t)
    {
        t.OnProps = (s, a) => { _ = RefreshAsync(t); };
        t.OnPlay = (s, a) => { _ = RefreshAsync(t); };
        try
        {
            t.Session.MediaPropertiesChanged += t.OnProps;
            t.Session.PlaybackInfoChanged += t.OnPlay;
        }
        catch { }
    }

    private static void Detach(Tracker t)
    {
        try
        {
            if (t.OnProps != null) t.Session.MediaPropertiesChanged -= t.OnProps;
            if (t.OnPlay != null) t.Session.PlaybackInfoChanged -= t.OnPlay;
        }
        catch { }
    }

    private async Task RefreshAsync(Tracker t)
    {
        int v = Interlocked.Increment(ref t.Version);
        try
        {
            var s = t.Session;
            var props = await s.TryGetMediaPropertiesAsync();
            bool playing = s.GetPlaybackInfo().PlaybackStatus ==
                           GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string title = props.Title ?? "", artist = props.Artist ?? "";
            string appId = s.SourceAppUserModelId ?? "";

            MediaInfo info;
            if (title.Length == 0 && artist.Length == 0)
            {
                info = new MediaInfo();   // oturum var ama içinde şarkı/video yok
            }
            else
            {
                var prev = t.Info;
                bool same = prev.HasSession && prev.Title == title && prev.Artist == artist && prev.AppId == appId;
                info = new MediaInfo
                {
                    HasSession = true,
                    Key = t.Key,
                    Title = title,
                    Artist = artist,
                    Album = props.AlbumTitle ?? "",
                    AppId = appId,
                    IsPlaying = playing,
                    PlatformId = same && prev.PlatformId != "other" ? prev.PlatformId : Platforms.Detect(appId, title, artist, props.AlbumTitle ?? ""),
                    Art = same ? prev.Art : null,
                };
                if (props.Thumbnail != null && info.Art == null)
                    info.Art = await LoadThumb(props.Thumbnail);
            }

            if (v != t.Version) return;   // daha yeni bir güncelleme geldi

            lock (_gate)
            {
                if (!_trackers.Contains(t)) return;
                bool newTrack = !(t.Info.HasSession && info.HasSession && t.Info.Title == info.Title && t.Info.Artist == info.Artist && t.Info.AppId == info.AppId);
                if (newTrack) { t.TabTitle = null; t.TabArtist = null; }
                if (info.HasSession && (playing || !t.Info.HasSession || t.Info.Title != info.Title)) t.LastActive = DateTime.Now;
                t.Info = info;
            }
            Reevaluate();
        }
        catch { /* oturum kapanmış olabilir */ }
    }

    // ---------- Seçim ----------

    private static double Score(Tracker t)
    {
        var i = t.Info;
        double s = i.IsPlaying ? 1000 : 0;
        if (App.Settings.GetRule(i.PlatformId).Mode == VisibilityMode.Always) s += 200;
        double ageSec = (DateTime.Now - t.LastActive).TotalSeconds;
        s -= Math.Min(Math.Max(ageSec, 0), 3600) / 36.0;   // en fazla -100: yakın zamanda etkin olan öne geçer
        return s;
    }

    /// <summary>Ayarlar (Gizle / Hep göster) değişince seçimi yeniden yapar.</summary>
    public void Repick()
    {
        if (App.Settings.UseTabTitle) System.Threading.Tasks.Task.Run(RefreshTabTitles);   // pencere taraması arayüzü bekletmesin
        Reevaluate();
    }

    /// <summary>"Sekme başlığını göster" açıksa ve sekmeden bir başlık okunduysa onu kullanır.</summary>
    private static MediaInfo Effective(Tracker t)
    {
        var i = t.Info;
        if (!App.Settings.UseTabTitle || t.TabTitle == null) return i;
        return new MediaInfo
        {
            HasSession = true, Key = i.Key, Title = t.TabTitle, Artist = t.TabArtist ?? i.Artist, Album = i.Album,
            AppId = i.AppId, PlatformId = i.PlatformId, IsPlaying = i.IsPlaying, Art = i.Art,
        };
    }

    private void RefreshTabTitles()
    {
        try
        {
            if (!App.Settings.UseTabTitle) return;
            // Ağır iş (pencere taraması) kilidin DIŞINDA yapılır; kilit yalnızca kısa süre tutulur
            List<(Tracker T, MediaInfo I)> snapshot;
            lock (_gate)
                snapshot = _trackers.Where(t => t.Info.HasSession && TabTitle.IsWeb(t.Info)).Select(t => (t, t.Info)).ToList();

            bool changed = false;
            foreach (var (t, i) in snapshot)
            {
                var (title, artist) = TabTitle.Resolve(i);
                if (title == null) continue;
                lock (_gate)
                {
                    if (title != t.TabTitle) { t.TabTitle = title; t.TabArtist = artist; changed = true; }
                }
            }
            if (changed) Reevaluate();
        }
        catch { }
    }

    private void Reevaluate()
    {
        MediaInfo pick;
        MediaEvent? ev;
        lock (_gate)
        {
            Tracker? best = null;
            double bestScore = double.MinValue;
            foreach (var t in _trackers)
            {
                if (!t.Info.HasSession) continue;
                if (App.Settings.GetRule(t.Info.PlatformId).Mode == VisibilityMode.Never) continue;   // gizli kaynak hiç seçilmez
                double sc = Score(t);
                if (sc > bestScore) { best = t; bestScore = sc; }
            }
            pick = best != null ? Effective(best) : new MediaInfo();
            _picked = best?.Session;

            var prev = Current;
            if (!pick.HasSession) ev = prev.HasSession ? MediaEvent.Cleared : null;
            else if (!prev.HasSession || prev.Key != pick.Key || prev.Title != pick.Title || prev.Artist != pick.Artist) ev = MediaEvent.TrackChanged;
            else if (prev.IsPlaying != pick.IsPlaying) ev = MediaEvent.PlaybackChanged;
            else if (!ReferenceEquals(prev.Art, pick.Art) || prev.PlatformId != pick.PlatformId) ev = MediaEvent.Updated;
            else ev = null;

            Current = pick;
        }
        if (ev != null && pick.HasSession) Log.Write($"Seçilen oturum: {pick.AppId} → {pick.PlatformId} · {pick.Title}");   // disk yazımı kilidin dışında
        if (ev != null) Changed?.Invoke(pick, ev.Value);
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

    // ---------- Zaman çizelgesi / kontroller: hep gösterilen (seçilen) oturum üzerinde ----------

    public (TimeSpan Pos, TimeSpan Dur)? GetTimeline()
    {
        try
        {
            var s = _picked;
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
            var s = _picked;
            if (s == null) return;
            var t = s.GetTimelineProperties();
            var dur = t.EndTime - t.StartTime;
            if (dur <= TimeSpan.Zero) return;
            var target = t.StartTime + TimeSpan.FromTicks((long)(dur.Ticks * Math.Clamp(ratio, 0, 1)));
            _ = s.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch { }
    }

    public void TogglePlay() { try { _ = _picked?.TryTogglePlayPauseAsync(); } catch { } }
    public void Next() { try { _ = _picked?.TrySkipNextAsync(); } catch { } }
    public void Previous() { try { _ = _picked?.TrySkipPreviousAsync(); } catch { } }
}
