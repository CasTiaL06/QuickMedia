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
    private bool _suppressed;  // kısayolla kapatıldı: bir sonraki tetikleyiciye kadar "Hep göster" susar

    public FlyoutController(FlyoutWindow win)
    {
        _win = win;
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (_win.IsMouseOver) { _hide.Interval = TimeSpan.FromSeconds(1); _hide.Start(); return; } // fare üstündeyken kapanma
            _manual = false;
            _win.HideFlyout();
        };
        _watch.Tick += (_, _) => Reevaluate();
        _watch.Start();
        _win.ApplySettings();
    }

    public void OnMediaChanged(MediaInfo info, MediaEvent ev)
    {
        _win.Update(info);
        if (ev == MediaEvent.Cleared) { if (!_manual) HideNow(); return; }
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
        if (_manual) return;

        var rule = s.GetRule(info.PlatformId);
        if (rule.Mode == VisibilityMode.Never) { HideNow(); return; }
        if (s.HideInFullscreen && Native.IsFullscreenBusy()) { HideNow(); return; }

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
        if (_manual || !s.Enabled) return;
        var info = App.Media.Current;
        if (!info.HasSession) return;

        if (s.GetRule(info.PlatformId).Mode == VisibilityMode.Always) Apply(info, false);
        else if (_win.IsShown && s.HideInFullscreen && Native.IsFullscreenBusy()) HideNow();
    }

    /// <summary>Kısayol: görünüyorsa gizle, gizliyse aç.</summary>
    public void Toggle()
    {
        if (!App.Settings.Enabled) return;
        if (_win.IsShown)
        {
            _manual = false;
            _suppressed = true;
            HideNow();
        }
        else
        {
            _manual = true;
            _suppressed = false;
            _win.Update(App.Media.Current);
            Show(0);
        }
    }

    /// <summary>Ses kısayolundan sonra kısa süre göster (zaten görünüyorsa sadece güncelle).</summary>
    public void NudgeVolume()
    {
        var s = App.Settings;
        if (!s.Enabled || !s.ShowVolumeControls) return;
        if (_win.IsShown) { _win.Poke(); return; }
        _win.Update(App.Media.Current);
        Show(2);
    }

    public void Preview()
    {
        _win.Update(App.Media.Current);
        Show(3);
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
        if (seconds > 0)
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
