using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickMedia;

/// <summary>
/// Görev çubuğunun boş sol kısmına oturan "şu an çalıyor" kontrolü.
/// Tıkla: flyout aç/kapat (kontrolün üstünden açılır) · Tekerlek: çalan uygulamanın sesi · Sağ tık: menü.
/// Normalde kapak + kayan yazı + ses çubukları; üzerine gelince kontroller belirir.
/// </summary>
public partial class TaskbarWidget : Window
{
    private IntPtr _hwnd;
    private bool _shown;               // mantıksal görünürlük (animasyonlar için)
    private bool _eqOn;
    private bool _hover;
    private bool _light;
    private string _key = "";
    private string _trackKey = "";
    private DateTime _themeStamp = DateTime.MinValue;
    private (int, int, int, int) _lastRect;
    private Rect _rectDip;
    private volatile Tuple<int, int>? _probe;
    private DateTime _probeStamp = DateTime.MinValue;
    private bool _probing;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private static bool Anim => App.Settings.Animations;

    /// <summary>"Yalnızca görev çubuğunda" modunda kısayolla geçici gizlenmiş mi?</summary>
    public bool UserHidden { get; private set; }

    public void ToggleUserHidden()
    {
        UserHidden = !UserHidden;
        Refresh();
    }

    public TaskbarWidget()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, -20);
            Native.SetWindowLong(_hwnd, -20, ex | 0x08000000 | 0x00000080); // NOACTIVATE | TOOLWINDOW
        };

        _tick.Tick += (_, _) => Refresh();
        _tick.Start();

        Hit.MouseEnter += (_, _) => SetHover(true);
        Hit.MouseLeave += (_, _) => SetHover(false);

        Hit.MouseLeftButtonUp += (_, e) =>
        {
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != Hit;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ButtonBase) return;
            if (App.TaskbarOnly) App.Media.TogglePlay();   // flyout yok: tıklama oynat / duraklat
            else App.Flyout.Toggle(_rectDip);              // flyout kontrolün hemen üstünden açılır
        };

        Hit.MouseWheel += (_, e) => App.AdjustVolume(e.Delta > 0 ? 0.05f : -0.05f, nudge: false);
        Hit.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };

        TextHost.SizeChanged += (_, _) => RefreshMarquee();
    }

    // ================= Menü =================

    private void ShowMenu()
    {
        var m = new ContextMenu { PlacementTarget = Hit };

        var settings = new MenuItem { Header = L.T("Ayarlar") };
        settings.Click += (_, _) => App.ShowSettings("Taskbar");
        var toggle = new MenuItem { Header = App.TaskbarOnly ? L.F("Geçici olarak gizle ({0} ile geri gelir)", HotkeyService.Describe(HotkeyAction.Toggle)) : L.T("Göster / Gizle") };
        toggle.Click += (_, _) => { if (App.TaskbarOnly) ToggleUserHidden(); else App.Flyout.Toggle(_rectDip); };
        var off = new MenuItem { Header = L.T("Görev çubuğu modunu kapat") };
        off.Click += (_, _) =>
        {
            App.Settings.UseTaskbar = false;
            SettingsStore.Save(App.Settings);
            App.ApplyAll();
        };

        m.Items.Add(settings);
        m.Items.Add(toggle);
        m.Items.Add(new Separator());
        m.Items.Add(off);
        m.IsOpen = true;
    }

    // ================= Görünürlük / yerleşim =================

    /// <summary>Ayar ve medya durumuna göre gösterir, gizler, yerleştirir (yaklaşık saniyede iki kez).</summary>
    public void Refresh()
    {
        try
        {
            var s = App.Settings;
            if (!s.UseTaskbar || !s.TaskbarOnly) UserHidden = false;   // geçici gizleme yalnızca bu modda anlamlı
            bool want = s.UseTaskbar
                        && !UserHidden
                        && FlyoutController.HasDisplayable(App.Media.Current)
                        && !(s.HideInFullscreen && Native.IsFullscreenBusy());

            if (!want || !Native.GetTaskbar(out var rc, out uint edge) || (edge != 1 && edge != 3))
            {
                SlideOut();
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

            if (s.TaskbarAuto) StartProbe(rc);
            var area = ComputeArea(rc, sx);
            if (area == null) { SlideOut(); return; }

            UpdateColors();
            Update(App.Displayed);
            SlideIn();
            Place(rc, area.Value.X, area.Value.W, sx, sy);
            UpdateProgress();
            UpdateEq(App.Displayed.IsPlaying);
        }
        catch (Exception ex) { Log.Write("Görev çubuğu kontrolü hatası: " + ex.Message); }
    }

    private (int X, int W)? ComputeArea(Native.RECT rc, double sx)
    {
        var s = App.Settings;
        int userW = (int)Math.Round(Math.Clamp(s.TaskbarWidth, 160, 600) * sx);

        if (s.TaskbarAuto && _probe is { } p)
        {
            int x = p.Item1 + (int)Math.Round(8 * sx);
            int avail = p.Item2 - p.Item1 - (int)Math.Round(20 * sx);
            int w = Math.Min(userW, avail);
            return w < (int)Math.Round(110 * sx) ? null : (x, w);
        }
        return (rc.left + (int)Math.Round(s.TaskbarOffset * sx), userW);
    }

    /// <summary>Soldaki boş alanı arka planda (UI Automation ile) bulur; sonuç birkaç saniye geçerlidir.</summary>
    private void StartProbe(Native.RECT rc)
    {
        if (_probing || (DateTime.Now - _probeStamp).TotalSeconds < 4) return;
        _probing = true;
        _probeStamp = DateTime.Now;
        Task.Run(() =>
        {
            try { _probe = TaskbarProbe.FindFreeArea(rc); }
            finally { _probing = false; }
        });
    }

    private void Place(Native.RECT rc, int x, int wpx, double sx, double sy)
    {
        int tbH = rc.bottom - rc.top;
        double hDip = Math.Max(24, Math.Min(40, tbH / sy - 8));
        int hpx = (int)Math.Round(hDip * sy);
        int y = rc.top + (tbH - hpx) / 2;
        double wDip = wpx / sx;

        if (Math.Abs(Width - wDip) > 0.5) Width = wDip;
        if (Math.Abs(Height - hDip) > 0.5) Height = hDip;

        // Yalnızca konum değiştiyse ya da görev çubuğunun arkasında kaldıysa öne al: gereksiz çağrı titreşim yapar
        var r = (x, y, wpx, hpx);
        if (r != _lastRect || !Native.IsAboveTaskbar(_hwnd))
        {
            Native.PlaceTopmost(_hwnd, x, y, wpx, hpx);
            _lastRect = r;
        }
        _rectDip = new Rect(x / sx, y / sy, wDip, hDip);
    }

    // ================= Animasyonlar =================

    private void SlideIn()
    {
        if (_shown) return;
        _shown = true;
        BeginAnimation(OpacityProperty, null);
        Shift.BeginAnimation(TranslateTransform.XProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();

        if (!Anim) { Opacity = 1; Shift.X = 0; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-22, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void SlideOut()
    {
        if (!_shown) { if (IsVisible && !_shown && Opacity <= 0.01) Hide(); return; }
        _shown = false;
        UpdateEq(false);

        if (!Anim) { Hide(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (!_shown) Hide(); };
        BeginAnimation(OpacityProperty, fade);
        Shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, -14, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    private void SetHover(bool on)
    {
        if (_hover == on) return;
        _hover = on;
        double d = Anim ? 150 : 1;
        HoverBg.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(d)));
        CtlPanel.IsHitTestVisible = on;
        CtlPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(d)));
        CtlShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(on ? 0 : 10, TimeSpan.FromMilliseconds(Anim ? 180 : 1)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        EqBars.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 1, TimeSpan.FromMilliseconds(d)));
        PausedGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 0.6, TimeSpan.FromMilliseconds(d)));
    }

    /// <summary>Çalarken hareket eden ses çubukları.</summary>
    private void UpdateEq(bool playing)
    {
        EqBars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PausedGlyph.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;

        bool on = playing && _shown && Anim;
        if (on == _eqOn) return;
        _eqOn = on;

        var bars = new[] { Eq1, Eq2, Eq3, Eq4 };
        double[] secs = { 0.55, 0.8, 0.65, 0.9 };
        double[] lows = { 0.25, 0.5, 0.3, 0.45 };
        for (int i = 0; i < bars.Length; i++)
        {
            if (on)
            {
                var a = new DoubleAnimation(lows[i], 1, TimeSpan.FromSeconds(secs[i]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase()
                };
                bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                bars[i].ScaleY = 0.4;
            }
        }
    }

    // ================= Kayan yazı =================

    private static void SetupMarquee(Grid clip, TextBlock tb, TranslateTransform move)
    {
        move.BeginAnimation(TranslateTransform.XProperty, null);
        move.X = 0;
        clip.OpacityMask = null;

        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textW = tb.DesiredSize.Width, boxW = clip.ActualWidth;
        if (boxW <= 0 || textW <= boxW + 1) return;

        // sağ kenarda yumuşak solma
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.9));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        clip.OpacityMask = mask;

        if (!Anim) return;
        double ov = textW - boxW + 14;
        double travel = ov / 30.0;                    // ~30 px/sn
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(-ov, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + travel))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(-ov, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + travel))));
        move.BeginAnimation(TranslateTransform.XProperty, a);
    }

    private void RefreshMarquee()
    {
        SetupMarquee(TitleClip, TitleText, TitleMove);
        SetupMarquee(SubClip, SubText, SubMove);
    }

    // ================= İçerik / renkler =================

    /// <summary>Görev çubuğu Windows'un sistem temasını izler (koyu / açık).</summary>
    private void UpdateColors()
    {
        if ((DateTime.Now - _themeStamp).TotalSeconds < 5) return;
        _themeStamp = DateTime.Now;
        try
        {
            object? v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
            _light = v != null && Convert.ToInt32(v) == 1;
        }
        catch { _light = false; }

        var main = _light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xF2, 0xF2, 0xF2);
        var sub = _light ? Color.FromRgb(0x5A, 0x5A, 0x5A) : Color.FromRgb(0xB4, 0xB4, 0xB4);
        TitleText.Foreground = new SolidColorBrush(main);
        SubText.Foreground = new SolidColorBrush(sub);
        PausedGlyph.Foreground = new SolidColorBrush(main);
        foreach (var b in new[] { PrevBtn, PlayBtn, NextBtn }) b.Foreground = new SolidColorBrush(main);

        HoverBg.Background = new SolidColorBrush(_light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x1C, 255, 255, 255));
        ProgressTrack.Background = new SolidColorBrush(_light ? Color.FromArgb(0x22, 0, 0, 0) : Color.FromArgb(0x30, 255, 255, 255));
        if (ArtIcon.Content != null) Art.Background = new SolidColorBrush(_light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x26, 255, 255, 255));
    }

    private void Update(MediaInfo m)
    {
        string key = $"{m.Key}|{m.Title}|{m.Artist}|{m.IsPlaying}|{m.Art != null}|{L.Code}";
        if (key == _key) return;
        _key = key;

        string trackKey = $"{m.Key}|{m.Title}|{m.Artist}";
        bool trackChanged = trackKey != _trackKey;
        _trackKey = trackKey;

        TitleText.Text = m.Title;
        SubText.Text = string.IsNullOrEmpty(m.Artist) ? m.Album : m.Artist;
        PlayBtn.Content = m.IsPlaying ? "\uE769" : "\uE768";

        if (m.Art != null)
        {
            Art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            ArtIcon.Content = null;
        }
        else
        {
            ArtIcon.Content = PlatformIcons.Create(m.PlatformId, 18);
            Art.Background = new SolidColorBrush(_light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x26, 255, 255, 255));
        }

        Dispatcher.InvokeAsync(RefreshMarquee, DispatcherPriority.Loaded);

        if (trackChanged && _shown && Anim)
        {
            // yeni parça: yazı yukarı kayarak belirir, kapak yumuşakça gelir
            TextHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            TextShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Art.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)));
        }
    }

    private void UpdateProgress()
    {
        bool show = App.Settings.TaskbarProgress;
        ProgressTrack.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var tl = App.Media.GetTimeline();
        double ratio = tl is { } t && t.Dur.TotalSeconds > 0 ? t.Pos.TotalSeconds / t.Dur.TotalSeconds : 0;
        double target = Math.Max(0, ProgressTrack.ActualWidth * ratio);
        if (!Anim || Math.Abs(ProgressFill.Width - target) > 60) { ProgressFill.BeginAnimation(WidthProperty, null); ProgressFill.Width = target; return; }
        ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(600)));   // akıcı ilerleme
    }

    private void Play_Click(object sender, RoutedEventArgs e) => App.Media.TogglePlay();
    private void Prev_Click(object sender, RoutedEventArgs e) => App.Media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => App.Media.Next();
}
