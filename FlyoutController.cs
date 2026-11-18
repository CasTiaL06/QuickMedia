using System;
using System.Windows.Threading;

namespace QuickMedia;

/// <summary>Flyout'un ne zaman görüneceğine/gizleneceğine karar verir.</summary>
public sealed class FlyoutController
{
    private readonly FlyoutWindow _win;
    private readonly DispatcherTimer _hide = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _manual;      // kısayolla açıldı: kapatılana kadar kalır
    private bool _suppressed;  // kısayolla kapatıldı: bir sonraki tetikleyiciye kadar "Hep göster"/sabit susar

    public FlyoutController(FlyoutWindow win)
    {
        _win = win;
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (App.Settings.Pinned && HasDisplayable(App.Media.Current)) return;
            if (_win.IsMouseOver) { _hide.Interval = TimeSpan.FromSeconds(1); _hide.Start(); return; } // fare üstündeyken kapanma
            _manual = false;
            _win.HideFlyout();
        };
        _watch.Tick += (_, _) => Reevaluate();
        _watch.Start();
        _win.ApplySettings();
    }

    /// <summary>"Gizle" seçilen bir kaynaktan geliyor mu? Öyleyse flyout onu hiçbir şekilde göstermez.</summary>
    public static bool IsHidden(MediaInfo i) =>
        i.HasSession && App.Settings.GetRule(i.PlatformId).Mode == VisibilityMode.Never;

    /// <summary>Flyout'ta gösterilebilecek bir medya var mı? (oturum var ve gizli değil)</summary>
    public static bool HasDisplayable(MediaInfo i) => i.HasSession && !IsHidden(i);

    /// <summary>Flyout'a verilecek bilgi: gizli kaynaklar için boş görünüm.</summary>
    public static MediaInfo Shown(MediaInfo i) => IsHidden(i) ? new MediaInfo() : i;

    public void OnMediaChanged(MediaInfo info, MediaEvent ev)
    {
        _win.Update(Shown(info));
        if (ev == MediaEvent.Cleared) { Apply(info, false); return; }
        if (ev == MediaEvent.Updated) return;

        var s = App.Settings;
        if (!s.Enabled) return;

        bool trigger = ev == MediaEvent.TrackChanged ? s.ShowOnTrackChange : s.ShowOnPlayPause;
        if (trigger) _suppressed = false;
        Apply(info, trigger);
    }

    private void Apply(MediaInfo info, bool trigger)
    {
        var s = App.Settings;
        if (!s.Enabled) { HideNow(); return; }

        // Gösterilecek medya yok (hiç oturum yok ya da kaynak gizli): flyout hiç görünmez
        if (!HasDisplayable(info)) { _manual = false; HideNow(); return; }
        if (_manual) return;

        if (s.UseTaskbar && s.TaskbarSuppressFlyout && !s.Pinned) { HideNow(); return; }   // yalnızca görev çubuğu kontrolü
        if (s.HideInFullscreen && Native.IsFullscreenBusy()) { HideNow(); return; }
        if (s.Pinned) { if (!_suppressed) Show(0); return; }

        var rule = s.GetRule(info.PlatformId);
        if (rule.Mode == VisibilityMode.Always)
        {
            if (_suppressed || (s.AlwaysHideWhenPaused && !info.IsPlaying)) { HideNow(); return; }
            Show(0);
            return;
        }

        if (trigger) Show(Math.Max(1, rule.Seconds));
    }

    private void Reevaluate()
    {
        var s = App.Settings;
        if (!s.Enabled) return;
        var info = App.Media.Current;

        if (!HasDisplayable(info))
        {
            if (_win.IsShown && !_win.IsDemo) { _manual = false; HideNow(); }
            return;
        }
        if (_manual) return;

        if (s.Pinned) { if (!_suppressed) Apply(info, false); return; }
        if (s.GetRule(info.PlatformId).Mode == VisibilityMode.Always) Apply(info, false);
        else if (_win.IsShown && s.HideInFullscreen && Native.IsFullscreenBusy()) HideNow();
    }

    /// <summary>Kısayol: görünüyorsa gizle, gizliyse aç (gösterilecek medya yoksa hiçbir şey yapmaz).</summary>
    public void Toggle()
    {
        if (!App.Settings.Enabled) return;
        if (_win.IsShown)
        {
            _manual = false;
            _suppressed = true;
            HideNow();
            return;
        }
        if (!HasDisplayable(App.Media.Current)) return;   // boş kutu göstermeyiz
        _manual = true;
        _suppressed = false;
        _win.Demo = null;
        _win.Update(App.Displayed);
        Show(0);
    }

    /// <summary>Ayarlardaki önizleme: medya yoksa örnek bir şarkıyla gösterir.</summary>
    public void Preview()
    {
        if (HasDisplayable(App.Media.Current)) { _win.Demo = null; _win.Update(App.Displayed); }
        else
        {
            _win.Demo = new MediaInfo
            {
                HasSession = true, IsPlaying = true, PlatformId = "spotify",
                Title = L.T("Örnek şarkı"), Artist = "QuickMedia", Album = "",
            };
            _win.Update(_win.Demo);
        }
        Show(3);
    }

    /// <summary>Ses kısayolundan sonra kısa süre göster (zaten görünüyorsa sadece güncelle).</summary>
    public void NudgeVolume()
    {
        var s = App.Settings;
        if (!s.Enabled || !s.ShowVolumeControls || !HasDisplayable(App.Media.Current)) return;
        if (_win.IsShown) { _win.Poke(); return; }
        _win.Demo = null;
        _win.Update(App.Displayed);
        Show(2);
    }

    /// <summary>Medya çalarken flyout'u ekranda sabitler (kapanmaz, hep üstte kalır) ya da sabitlemeyi kaldırır.</summary>
    public void SetPinned(bool pinned)
    {
        var s = App.Settings;
        s.Pinned = pinned;
        SettingsStore.Save(s);
        _manual = false;
        _suppressed = false;

        var info = App.Media.Current;
        if (HasDisplayable(info))
        {
            if (pinned) { _win.Demo = null; _win.Update(App.Displayed); Show(0); }
            else
            {
                bool always = s.GetRule(info.PlatformId).Mode == VisibilityMode.Always;
                if (always) Show(0); else Show(2);   // kısa süre sonra normal davranışa döner
            }
        }
        _win.UpdatePin();
        App.RaiseApplied();
    }

    public void ApplySettings()
    {
        _win.ApplySettings();
        if (!App.Settings.Enabled) HideNow();
    }

    private void Show(int seconds)
    {
        _win.ShowFlyout();
        _hide.Stop();
        if (seconds > 0 && !(App.Settings.Pinned && HasDisplayable(App.Media.Current)))
        {
            _hide.Interval = TimeSpan.FromSeconds(seconds);
            _hide.Start();
        }
    }

    private void HideNow()
    {
        _hide.Stop();
        _win.HideFlyout();
    }
}
