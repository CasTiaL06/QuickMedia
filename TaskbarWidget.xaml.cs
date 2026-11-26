using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickMedia;

/// <summary>
/// Görev çubuğunun boş sol kısmına oturan "şu an çalıyor" kontrolü.
/// Fare tekerleği: çalan uygulamanın sesi · Shift + tekerlek: 5 sn ileri/geri · Alt çizgiye tıkla/sürükle: şarkıda atla
/// Orta tık: oynat/duraklat · Sağ tık: menü · (flyout modunda) gövdeye tıkla: flyout aç/kapat.
/// </summary>
public partial class TaskbarWidget : Window
{
    private IntPtr _hwnd;
    private bool _shown;               // mantıksal görünürlük (animasyonlar için)
    private bool _eqOn;
    private bool _hover;
    private bool _light;
    private bool _seeking;
    private bool _compact;
    private int _miss;                 // üst üste "gösterme" kararı sayısı (kısa kesintilerde titreşmesin)
    private int _fsCount;              // üst üste "tam ekran" sayısı
    private double _lastDur;
    private string _key = "";
    private string _trackKey = "";
    private bool? _lastPlaying;
    private DateTime _themeStamp = DateTime.MinValue;
    private DateTime _seekHold = DateTime.MinValue;
    private (int, int, int, int) _lastRect;
    private Rect _rectDip;

    // Görev çubuğu dikdörtgeni: Windows'a (explorer'a) sormak arayüz thread'ini bekletebilir; arka planda yenilenir
    private readonly object _tbGate = new();
    private Native.RECT _tbRect;
    private uint _tbEdge = 3;
    private bool _tbKnown;
    private bool _tbBusy;
    private DateTime _tbStamp = DateTime.MinValue;

    // Boş alan ölçümü (UI Automation): arka planda, son başarılı sonuç korunur
    private volatile Tuple<int, int>? _probe;
    private DateTime _probeOk = DateTime.MinValue;
    private DateTime _probeStamp = DateTime.MinValue;
    private bool _probing;

    // Gömme
    private bool _embedded, _embedFailed;
    private bool? _lastEmbedSetting;
    private int _embedRetries;
    private DateTime _embedFailedAt = DateTime.MinValue;
    private IntPtr _embedParent;
    private int _embedChecks;

    // Z-sırası bekçisi (gömülü olmayan mod)
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private DateTime _burstUntil = DateTime.MinValue;
    private IntPtr _fgHook;
    private Native.WinEventDelegate? _fgCb;

    // ses
    private bool _updatingVol;
    private DateTime _toastUntil = DateTime.MinValue;
    private readonly DispatcherTimer _popTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };

    private static bool Anim => App.Settings.Animations;

    /// <summary>Ayarlar penceresinde gösterilen kısa durum metni (neden görünür / görünmez).</summary>
    public string Status { get; private set; } = "";

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

        _guard.Tick += (_, _) =>
        {
            EnsureOnTop();
            if (DateTime.Now > _burstUntil) _guard.Interval = TimeSpan.FromMilliseconds(150);
        };
        _guard.Start();

        // Başka bir uygulamaya / görev çubuğuna tıklanınca Windows görev çubuğunu öne alır:
        // o anda kısa süre sık sık kontrol edip kontrolü hemen geri öne alıyoruz.
        _fgCb = (_, _, _, _, _, _, _) =>
        {
            EnsureOnTop();
            _burstUntil = DateTime.Now.AddMilliseconds(700);
            _guard.Interval = TimeSpan.FromMilliseconds(30);
        };
        _fgHook = Native.HookForeground(_fgCb);
        Closed += (_, _) => Native.Unhook(_fgHook);

        Hit.MouseEnter += (_, _) => SetHover(true);
        Hit.MouseLeave += (_, _) => SetHover(false);

        // Gövdeye tıklama: yalnızca flyout modunda flyout'u açar. "Yalnızca görev çubuğu" modunda hiçbir şey yapmaz
        // (yanlışlıkla müziği durdurmasın); kompakt görünümde (kontrol düğmeleri yok) oynat/duraklat yapar.
        Hit.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != Hit;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ButtonBase) return;
            if (App.TaskbarOnly) { if (_compact) App.Media.TogglePlay(); }
            else App.Flyout.Toggle(_rectDip);     // flyout kontrolün hemen üstünden açılır
        };
        Hit.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) { App.Media.TogglePlay(); e.Handled = true; }
        };

        Hit.MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SeekBy(e.Delta > 0 ? 5 : -5);
            else { App.AdjustVolume(e.Delta > 0 ? 0.05f : -0.05f, nudge: false); ToastVolume(); }
        };
        Hit.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };

        // Ses düğmesi: üstüne gelince kaydırıcı açılır, ayrılınca kısa gecikmeyle kapanır
        _popTimer.Tick += (_, _) => { _popTimer.Stop(); if (!MuteBtn.IsMouseOver && !VolPop.IsMouseOver) ShowVolPop(false); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastUntil = DateTime.MinValue; RestoreSub(); };
        MuteBtn.MouseEnter += (_, _) => { _popTimer.Stop(); ShowVolPop(true); };
        MuteBtn.MouseLeave += (_, _) => _popTimer.Start();
        VolPop.MouseEnter += (_, _) => _popTimer.Stop();
        VolPop.MouseLeave += (_, _) => _popTimer.Start();
        Hit.MouseLeave += (_, _) => ShowVolPop(false);

        SeekHit.MouseEnter += (_, _) => SetSeekHover(true);
        SeekHit.MouseLeave += (_, _) => { if (!_seeking) SetSeekHover(false); };
        SeekHit.MouseLeftButtonDown += SeekHit_Down;
        SeekHit.MouseMove += SeekHit_Move;
        SeekHit.MouseLeftButtonUp += SeekHit_Up;

        TextHost.SizeChanged += (_, _) => RefreshMarquee();
    }

    // ================= Menü =================

    private void ShowMenu()
    {
        var m = new ContextMenu { PlacementTarget = Hit };

        var settings = new MenuItem { Header = L.T("Ayarlar") };
        settings.Click += (_, _) => App.ShowSettings("Taskbar");
        var toggle = new MenuItem
        {
            Header = App.TaskbarOnly
                ? L.F("Geçici olarak gizle ({0} ile geri gelir)", HotkeyService.Describe(HotkeyAction.Toggle))
                : L.T("Göster / Gizle")
        };
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

    private void SetStatus(string tr) => Status = L.T(tr);

    /// <summary>Görev çubuğu dikdörtgeni: önbellekten okunur, 2 sn'de bir arka planda yenilenir (arayüz bekletilmez).</summary>
    private bool GetTaskbarCached(out Native.RECT rc, out uint edge)
    {
        lock (_tbGate)
        {
            if (!_tbKnown)
            {
                _tbKnown = Native.GetTaskbar(out _tbRect, out _tbEdge);   // yalnızca ilk seferde eşzamanlı
                _tbStamp = DateTime.Now;
            }
            else if (!_tbBusy && (DateTime.Now - _tbStamp).TotalSeconds > 2)
            {
                _tbBusy = true;
                _tbStamp = DateTime.Now;
                Task.Run(() =>
                {
                    try
                    {
                        if (Native.GetTaskbar(out var r, out var e)) lock (_tbGate) { _tbRect = r; _tbEdge = e; }
                    }
                    finally { _tbBusy = false; }
                });
            }
            rc = _tbRect;
            edge = _tbEdge;
            return _tbKnown;
        }
    }

    /// <summary>Ayar ve medya durumuna göre gösterir, gizler, yerleştirir (yaklaşık saniyede iki kez).</summary>
    public void Refresh()
    {
        try
        {
            var s = App.Settings;
            if (!s.UseTaskbar || !s.TaskbarOnly) UserHidden = false;

            // Kullanıcı kapattıysa / gizlediyse hemen kaybol; geçici durumlarda (medya boşluğu, tam ekran, ölçüm)
            // birkaç tur bekle: aksi halde kontrol "açılıp kapanıyor" gibi görünür.
            if (!s.UseTaskbar) { _miss = 0; SetStatus("Kapalı"); SlideOut(); return; }
            if (UserHidden) { _miss = 0; SetStatus("Gizli: kısayolla gizlendi"); SlideOut(); return; }

            // Tam ekran: pencere tabanlı algılama (tarayıcıda tam ekran video dahil) güvenilir; anında gizle.
            // Windows'un "meşgul" bildirimi bazen anlık takılır: onu birkaç tur üst üste görmeden gizleme.
            bool strong = s.HideInFullscreen && Native.ForegroundCoversMonitor();
            bool weak = s.HideInFullscreen && Native.IsNotificationBusy();
            _fsCount = weak ? _fsCount + 1 : 0;
            if (strong) { _miss = 0; SetStatus("Gizli: tam ekran"); SlideOut(); return; }

            bool media = FlyoutController.HasDisplayable(App.Media.Current);
            bool tbOk = GetTaskbarCached(out var rc, out uint edge) && (edge == 1 || edge == 3);
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

            string? why = null;
            (int X, int W)? area = null;
            if (!media) why = "Gizli: gösterilecek medya yok";
            else if (_fsCount >= 3) why = "Gizli: tam ekran";
            else if (!tbOk) why = "Gizli: görev çubuğu alt/üst değil";
            else
            {
                if (s.TaskbarAuto) StartProbe(rc);
                area = ComputeArea(rc, sx);
                if (area == null) why = "Gizli: boş alan yok";
            }

            if (why != null)
            {
                SetStatus(why);
                if (++_miss >= 3) SlideOut();
                return;
            }
            _miss = 0;

            UpdateColors();
            Update(App.Displayed);
            SlideIn();
            bool compact = area!.Value.W < (int)Math.Round(170 * sx);
            SetCompact(compact);
            Place(rc, area.Value.X, area.Value.W, sx, sy);
            UpdateProgress();
            SyncVolume();
            UpdateEq(App.Displayed.IsPlaying);

            Status = $"{L.T("Görünür")} · {L.T(_embedded ? "gömülü" : "üstte")}" +
                     (compact ? " · " + L.T("kompakt") : "") +
                     $" · {area.Value.W}px · {L.T(s.TaskbarAuto && _probe != null ? "otomatik" : "elle")}";
        }
        catch (Exception ex) { Log.Write("Görev çubuğu kontrolü hatası: " + ex.Message); }
    }

    private (int X, int W)? ComputeArea(Native.RECT rc, double sx)
    {
        var s = App.Settings;
        int userW = (int)Math.Round(Math.Clamp(s.TaskbarWidth, 160, 600) * sx);
        int minW = (int)Math.Round(56 * sx);    // bunun altına sığmıyorsa hiç gösterme

        // Son başarılı ölçüm 2 dakika boyunca geçerli: ölçüm bir an başarısız olsa da kontrol zıplamaz
        if (s.TaskbarAuto && _probe is { } p && (DateTime.Now - _probeOk).TotalSeconds < 120)
        {
            int x = p.Item1 + (int)Math.Round(8 * sx);
            int avail = p.Item2 - p.Item1 - (int)Math.Round(20 * sx);
            int w = Math.Min(userW, avail);
            return w < minW ? null : (x, w);   // dar alanda kompakt görünüm (kapak + çubuklar) kullanılır
        }
        return (rc.left + (int)Math.Round(s.TaskbarOffset * sx), userW);
    }

    /// <summary>Soldaki boş alanı arka planda (UI Automation ile) bulur. Başarısız ölçüm eskisini silmez; küçük değişiklikler yok sayılır.</summary>
    private void StartProbe(Native.RECT rc)
    {
        if (_probing || (DateTime.Now - _probeStamp).TotalSeconds < 4) return;
        _probing = true;
        _probeStamp = DateTime.Now;
        Task.Run(() =>
        {
            try
            {
                var r = TaskbarProbe.FindFreeArea(rc);
                if (r == null) return;
                var old = _probe;
                _probeOk = DateTime.Now;
                if (old == null || Math.Abs(old.Item1 - r.Item1) > 6 || Math.Abs(old.Item2 - r.Item2) > 6) _probe = r;
            }
            finally { _probing = false; }
        });
    }

    /// <summary>Dar alanda: yalnızca kapak + ses çubukları; geniş alanda tam görünüm.</summary>
    private void SetCompact(bool c)
    {
        if (_compact == c) return;
        _compact = c;
        TextHost.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
        CtlPanel.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
        Art.Margin = c ? new Thickness(0) : new Thickness(0, 0, 9, 0);
        RightHost.MinWidth = c ? 0 : 88;
        RightHost.Margin = c ? new Thickness(8, 0, 2, 0) : new Thickness(8, 0, 0, 0);
        if (c) ShowVolPop(false);
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

        // Gömülü mod: kontrol görev çubuğunun alt penceresi olur. Başlat menüsü / arama açılınca Windows görev çubuğunu
        // daha üst bir katmana alır; "üstte duran" pencereler arkada kalıp kaybolur, gömülü olan ise görev çubuğuyla birlikte kalır.
        var s = App.Settings;
        if (_lastEmbedSetting != s.TaskbarEmbed) { _lastEmbedSetting = s.TaskbarEmbed; _embedFailed = false; _embedRetries = 0; }
        // Başarısız bir deneme kalıcı sayılmaz: 60 sn sonra (en fazla 3 kez) yeniden denenir
        if (_embedFailed && _embedRetries < 3 && (DateTime.Now - _embedFailedAt).TotalSeconds > 60) { _embedFailed = false; _embedRetries++; }

        bool wantEmbed = s.TaskbarEmbed && !_embedFailed;
        var tb = Native.FindTaskbarWindow();
        if (wantEmbed && tb != IntPtr.Zero)
        {
            if (!_embedded || _embedParent != tb) TryEmbed(tb);
        }
        else if (_embedded) Unembed();

        var r = (x, y, wpx, hpx);
        if (_embedded)
        {
            if (r != _lastRect) { Native.PlaceChild(_hwnd, x - rc.left, y - rc.top, wpx, hpx); _lastRect = r; }
            VerifyEmbed(x + wpx / 2, y + hpx / 2);
        }
        else if (r != _lastRect)
        {
            Native.PlaceTopmost(_hwnd, x, y, wpx, hpx);
            _lastRect = r;
        }
        _rectDip = new Rect(x / sx, y / sy, wDip, hDip);
    }

    private void TryEmbed(IntPtr tb)
    {
        try
        {
            _embedded = Native.Embed(_hwnd, tb);
            _embedParent = tb;
            _lastRect = default;
            _embedChecks = _embedded ? 4 : 0;
            if (_embedded) Log.Write("Görev çubuğu kontrolü görev çubuğuna gömüldü");
            else
            {
                _embedFailed = true;
                _embedFailedAt = DateTime.Now;
                Native.Unembed(_hwnd);
                Log.Write("Görev çubuğuna gömme başarısız oldu; üstte yöntemi kullanılıyor");
            }
        }
        catch (Exception ex)
        {
            _embedded = false;
            _embedFailed = true;
            _embedFailedAt = DateTime.Now;
            Log.Write("Gömme hatası: " + ex.Message);
        }
    }

    private void Unembed()
    {
        try { Native.Unembed(_hwnd); } catch { }
        _embedded = false;
        _embedParent = IntPtr.Zero;
        _lastRect = default;
    }

    /// <summary>Gömme sonrası ilk birkaç turda (belirme animasyonu bittikten sonra) kontrolün görünür olduğunu doğrular.</summary>
    private void VerifyEmbed(int cx, int cy)
    {
        if (_embedChecks <= 0 || !IsVisible || Opacity < 0.99) return;   // belirirken fare testi şeffaf sayılır: bekle
        _embedChecks--;
        if (Native.WindowAt(cx, cy) == _hwnd) { _embedChecks = 0; return; }
        Native.RaiseChild(_hwnd);
        if (Native.WindowAt(cx, cy) == _hwnd) { _embedChecks = 0; return; }
        if (_embedChecks == 0)
        {
            _embedFailed = true;
            _embedFailedAt = DateTime.Now;
            Unembed();
            Log.Write("Gömülü kontrol görünmedi; üstte yöntemine dönüldü");
        }
    }

    /// <summary>Kontrol arkada kaldıysa (yalnızca o zaman) yeniden öne alır.</summary>
    private void EnsureOnTop()
    {
        if (!_shown || _hwnd == IntPtr.Zero || _lastRect.Item3 == 0) return;
        if (_embedded)
        {
            if (!Native.IsTopChild(_hwnd)) Native.RaiseChild(_hwnd);   // görev çubuğunun XAML katmanı öne geçtiyse geri al
            return;
        }
        if (Native.IsAboveTaskbar(_hwnd)) return;
        var (x, y, w, h) = _lastRect;
        Native.PlaceTopmost(_hwnd, x, y, w, h);
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
        _lastRect = default;     // gizlenip yeniden gösterilince konum / görünürlük bir kez daha kesin olarak uygulansın

        if (!Anim) { Opacity = 1; Shift.X = 0; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-22, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void SlideOut()
    {
        if (!_shown) { if (IsVisible && Opacity <= 0.01) Hide(); return; }
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
        if (_compact) return;
        SetupMarquee(TitleClip, TitleText, TitleMove);
        SetupMarquee(SubClip, SubText, SubMove);
    }

    // ================= Ses (yalnızca çalan uygulamanın sesi; durum arka planda okunur) =================

    private void ShowVolPop(bool on)
    {
        if (on && _compact) return;
        VolPop.IsHitTestVisible = on;
        VolPop.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(Anim ? 130 : 1)));
    }

    /// <summary>Kaydırıcıyı ve ses düğmesini çalan uygulamanın durumuyla eşitler (önbellekten: arayüzü bekletmez).</summary>
    private void SyncVolume()
    {
        if (_compact) return;
        var cur = App.Displayed;
        var st = cur.HasSession ? AppVolume.GetState(cur.AppId) : null;
        bool ok = st != null;
        MuteBtn.IsEnabled = VolSlider.IsEnabled = ok;
        MuteBtn.Opacity = ok ? 1 : 0.45;
        if (!ok || VolSlider.IsMouseCaptureWithin) return;
        _updatingVol = true;
        VolSlider.Value = st!.Value.Muted ? 0 : st.Value.Volume * 100;
        MuteBtn.Content = st.Value.Muted || st.Value.Volume <= 0.001f ? "\uE74F" : "\uE767";
        _updatingVol = false;
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVol) return;
        var cur = App.Displayed;
        if (!cur.HasSession) return;
        if (e.NewValue > 0 && AppVolume.GetState(cur.AppId)?.Muted == true) AppVolume.SetMute(cur.AppId, false);
        AppVolume.SetVolume(cur.AppId, (float)(e.NewValue / 100.0));
        MuteBtn.Content = e.NewValue <= 0.1 ? "\uE74F" : "\uE767";
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        var cur = App.Displayed;
        if (!cur.HasSession) return;
        var st = AppVolume.GetState(cur.AppId);
        if (st != null) AppVolume.SetMute(cur.AppId, !st.Value.Muted);
        SyncVolume();
    }

    /// <summary>Tekerlekle ses değişince alt satırda kısa süre "Ses %45" gösterir.</summary>
    private void ToastVolume()
    {
        if (_compact) return;
        var cur = App.Displayed;
        var st = cur.HasSession ? AppVolume.GetState(cur.AppId) : null;
        if (st == null) return;
        string text = st.Value.Muted ? L.T("Sessiz") : L.F("Ses %{0}", (int)Math.Round(st.Value.Volume * 100));
        _toastUntil = DateTime.Now.AddMilliseconds(1300);
        SubText.Text = text;
        SetupMarquee(SubClip, SubText, SubMove);
        _toastTimer.Stop();
        _toastTimer.Start();
        SyncVolume();
    }

    // ================= İlerleme çizgisi: göster, tıkla / sürükle ile atla =================

    private static string Fmt(double sec) => $"{(int)(sec / 60)}:{(int)(sec % 60):00}";

    private void SetSeekHover(bool on)
    {
        double d = Anim ? 140 : 1;
        ProgressTrack.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 4 : 2, TimeSpan.FromMilliseconds(d)));
        SeekKnob.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(d)));
    }

    private void MoveKnob() => SeekKnob.Margin = new Thickness(Math.Max(0, ProgressFill.Width - 4.5), 0, 0, -1);

    private double SeekRatio(MouseEventArgs e)
    {
        double w = SeekHit.ActualWidth;
        return w <= 0 ? 0 : Math.Clamp(e.GetPosition(SeekHit).X / w, 0, 1);
    }

    private void PreviewSeek(double ratio)
    {
        ProgressFill.BeginAnimation(WidthProperty, null);
        ProgressFill.Width = ProgressTrack.ActualWidth * ratio;
        MoveKnob();
        if (_lastDur > 0 && !_compact) SubText.Text = $"{Fmt(_lastDur * ratio)} / {Fmt(_lastDur)}";   // hedef zaman
    }

    private void RestoreSub()
    {
        var m = App.Displayed;
        SubText.Text = string.IsNullOrEmpty(m.Artist) ? m.Album : m.Artist;
        Dispatcher.InvokeAsync(RefreshMarquee, DispatcherPriority.Loaded);
    }

    private void SeekHit_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;                       // gövde tıklamasına karışmasın
        if (_lastDur <= 0) return;              // süre bilgisi yoksa atlanamaz
        _seeking = true;
        SeekHit.CaptureMouse();
        PreviewSeek(SeekRatio(e));
    }

    private void SeekHit_Move(object sender, MouseEventArgs e)
    {
        if (_seeking) PreviewSeek(SeekRatio(e));
    }

    private void SeekHit_Up(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_seeking) return;
        _seeking = false;
        SeekHit.ReleaseMouseCapture();
        double r = SeekRatio(e);
        PreviewSeek(r);
        App.Media.Seek(r);
        _seekHold = DateTime.Now.AddMilliseconds(1000);   // çizgi eski konuma geri zıplamasın
        if (!SeekHit.IsMouseOver) SetSeekHover(false);
        RestoreSub();
    }

    private void SeekBy(double seconds)
    {
        var tl = App.Media.GetTimeline();
        if (tl is not { } t || t.Dur.TotalSeconds <= 0) return;
        App.Media.Seek(Math.Clamp((t.Pos.TotalSeconds + seconds) / t.Dur.TotalSeconds, 0, 1));
        _seekHold = DateTime.Now.AddMilliseconds(800);
    }

    private void UpdateProgress()
    {
        bool show = App.Settings.TaskbarProgress;
        SeekHit.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var tl = App.Media.GetTimeline();
        _lastDur = tl?.Dur.TotalSeconds ?? 0;
        if (_seeking || DateTime.Now < _seekHold) return;

        double ratio = tl is { } t && t.Dur.TotalSeconds > 0 ? t.Pos.TotalSeconds / t.Dur.TotalSeconds : 0;
        double target = Math.Max(0, ProgressTrack.ActualWidth * ratio);
        if (!Anim || Math.Abs(ProgressFill.Width - target) > 60)
        {
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = target;
        }
        else
        {
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(600)));   // akıcı ilerleme
        }
        MoveKnob();
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
        foreach (var b in new[] { MuteBtn, PrevBtn, PlayBtn, NextBtn }) b.Foreground = new SolidColorBrush(main);
        VolPop.Background = new SolidColorBrush(_light ? Color.FromArgb(0xEE, 0xF1, 0xF3, 0xF5) : Color.FromArgb(0xEE, 0x25, 0x28, 0x2D));

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
        if (!_seeking && DateTime.Now >= _toastUntil) SubText.Text = string.IsNullOrEmpty(m.Artist) ? m.Album : m.Artist;
        PlayBtn.Content = m.IsPlaying ? "\uE769" : "\uE768";
        if (_lastPlaying != null && _lastPlaying != m.IsPlaying && _shown && Anim)
        {
            // oynat / duraklat değişince düğme küçük bir "pop" yapar
            var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.9, EasingMode = EasingMode.EaseOut } };
            PlayPop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            PlayPop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        _lastPlaying = m.IsPlaying;

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

    private void Play_Click(object sender, RoutedEventArgs e) => App.Media.TogglePlay();
    private void Prev_Click(object sender, RoutedEventArgs e) => App.Media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => App.Media.Next();
}
