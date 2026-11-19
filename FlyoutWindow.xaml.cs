using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace QuickMedia;

public partial class FlyoutWindow : Window
{
    private static readonly string[] HoverStyles = ["Island", "Orb", "Tile", "MiniOrb", "BigOrb", "Float"];

    private IntPtr _hwnd;
    private bool _shown;
    private bool _ready;
    private bool _updatingVol;
    private bool _expanded;          // fare üstündeyken kontroller açılır (Island, plaklar, kare stiller)
    private bool _showArtist = true;
    private bool _showBadge;
    private bool _showTime = true;
    private bool _orbSub;
    private bool _miniTip;
    private bool _chromeless;        // kart çerçevesi yok
    private bool _ringStyle;         // plak stilleri: dönen kapak + ilerleme halkası
    private double _ringSize = 144;
    private bool _spinning;
    private bool _seeking;
    private DateTime _seekHold = DateTime.MinValue;
    private double _lastDur;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly DropShadowEffect _cardShadow = new() { BlurRadius = 22, ShadowDepth = 3, Direction = 270, Opacity = 0.3, Color = Colors.Black };
    private readonly DropShadowEffect _artShadow = new() { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black };

    public bool IsShown => _shown;

    /// <summary>Önizleme için örnek medya (gerçek medya yokken).</summary>
    public MediaInfo? Demo { get; set; }
    public bool IsDemo => Demo != null && !App.Displayed.HasSession;
    private MediaInfo Cur => IsDemo ? Demo! : App.Displayed;
    private string _badgeIconId = "";

    /// <summary>Görev çubuğu kontrolünden açıldıysa onun (DIP) dikdörtgeni: flyout oradan yukarı / aşağı açılır.</summary>
    public Rect? AnchorDip { get; set; }
    private string _artIconKey = "";
    private string _lastTitle = "";
    private ImageSource? _lastArt;
    private double _artW = 64;
    private bool _eqOn;

    public FlyoutWindow()
    {
        InitializeComponent();
        _ready = true;

        _tick.Tick += (_, _) =>
        {
            RefreshLive();
            if (App.Settings.Pinned) Native.KeepOnTop(_hwnd);   // sabitliyken diğer üstteki pencerelerin de önünde kal
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, -20);
            Native.SetWindowLong(_hwnd, -20, ex | 0x08000000 | 0x00000080); // NOACTIVATE | TOOLWINDOW
            UpdateBlur();
        };
        SizeChanged += (_, _) => { UpdateBlur(); if (_shown) Reposition(); };

        Card.MouseLeftButtonDown += Card_MouseLeftButtonDown;
        Card.MouseRightButtonUp += (_, e) => { App.CycleStyle(); e.Handled = true; }; // sağ tık: sıradaki stil
        Card.MouseEnter += (_, _) => SetExpanded(true);
        Card.MouseLeave += (_, _) => SetExpanded(false);
        Root.MouseEnter += (_, _) => UpdatePin();
        Root.MouseLeave += (_, _) => UpdatePin();

        SeekArea.MouseEnter += (_, _) => SeekKnob.Opacity = 1;
        SeekArea.MouseLeave += (_, _) => { if (!_seeking) SeekKnob.Opacity = 0; };
        SeekArea.MouseLeftButtonDown += SeekArea_Down;
        SeekArea.MouseMove += SeekArea_Move;
        SeekArea.MouseLeftButtonUp += SeekArea_Up;

        ApplyLayout();
        UpdatePin();
    }

    // ================= Sabitleme düğmesi =================

    public void UpdatePin()
    {
        var s = App.Settings;
        bool pinned = s.Pinned;
        bool vis = s.ShowPinButton && (pinned || Root.IsMouseOver);
        PinBtn.Opacity = vis ? 1 : 0;
        PinBtn.IsHitTestVisible = vis;
        PinBtn.Tag = pinned ? "Pinned" : null;
        PinBtn.ToolTip = L.T(pinned ? "Sabitlemeyi kaldır" : "Üstte sabitle");
    }

    private void LocalizeStatic()
    {
        GearBtn.ToolTip = L.T("Ayarlar");
        MuteBtn.ToolTip = L.T("Bu uygulamanın sesini aç / kapat");
        PrevBtn.ToolTip = L.T("Önceki");
        PlayBtn.ToolTip = L.T("Oynat / Duraklat");
        NextBtn.ToolTip = L.T("Sonraki");
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => App.Flyout.SetPinned(!App.Settings.Pinned);

    // ================= Yerleşim (stiller) =================

    private void SetExpanded(bool v)
    {
        string st = FlyoutStyles.Normalize(App.Settings.FlyoutStyle);
        if (Array.IndexOf(HoverStyles, st) < 0 || _expanded == v) return;
        _expanded = v;
        ApplyLayout();
    }

    private void P(FrameworkElement e, int row, int col, int span, Thickness m,
                   HorizontalAlignment ha = HorizontalAlignment.Stretch,
                   VerticalAlignment va = VerticalAlignment.Center)
    {
        Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span);
        e.Margin = m; e.HorizontalAlignment = ha; e.VerticalAlignment = va;
    }

    private static void V(UIElement e, bool v) => e.Visibility = v ? Visibility.Visible : Visibility.Collapsed;

    private static void Sz(Button b, double w, double h, double fs) { b.Width = w; b.Height = h; b.FontSize = fs; }

    private void PlayLook(bool round, double w, double h, double fs)
    {
        PlayBtn.Style = (Style)FindResource(round ? "PlayRound" : "IconButton");
        Sz(PlayBtn, w, h, fs);
        PlayBtn.Effect = round
            ? new DropShadowEffect { Color = ThemeManager.AccentOf(App.Settings), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.5 }
            : null;
    }

    private static void IconColor(Button b, bool white)
    {
        if (white) b.Foreground = Brushes.White; else b.ClearValue(Control.ForegroundProperty);
    }

    /// <summary>Cam kenarı: üstte belirgin, altta silik ince çerçeve.</summary>
    private static Brush EdgeBrush()
    {
        var c = (Application.Current.Resources["BorderBrushC"] as SolidColorBrush)?.Color ?? Colors.Gray;
        var bottom = Color.FromArgb((byte)(c.A * 0.35), c.R, c.G, c.B);
        return new LinearGradientBrush(c, bottom, 90);
    }

    private static void FadeIn(UIElement e, int ms = 260, double start = 0)
    {
        if (!App.Settings.Animations) return;
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(start, 1, TimeSpan.FromMilliseconds(ms)));
    }

    /// <summary>Çalarken hareket eden küçük ses çubukları.</summary>
    private void UpdateEq(bool playing)
    {
        EqBars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        bool on = playing && _shown && App.Settings.Animations;
        if (on == _eqOn) return;
        _eqOn = on;

        var tfs = new[] { Eq1, Eq2, Eq3 };
        double[] secs = { 0.55, 0.8, 0.65 };
        double[] lows = { 0.25, 0.45, 0.3 };
        for (int i = 0; i < tfs.Length; i++)
        {
            if (on)
            {
                var a = new DoubleAnimation(lows[i], 1, TimeSpan.FromSeconds(secs[i]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase()
                };
                tfs[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                tfs[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                tfs[i].ScaleY = 0.4;
            }
        }
    }

    private void SetKnob() => SeekKnob.Margin = new Thickness(Math.Max(0, Fill.Width - 5), 0, 0, 0);

    private void ApplyLayout()
    {
        var s = App.Settings;
        string st = FlyoutStyles.Normalize(s.FlyoutStyle);
        bool hover = _expanded && Array.IndexOf(HoverStyles, st) >= 0;
        bool exp = st == "Island" && _expanded;

        Card.MinWidth = 0; Card.MaxWidth = double.PositiveInfinity; Card.MinHeight = 0;
        bool art = true, gear = false, progress = false, volume = false, controls = true, prev = true, next = true;
        bool chrome = true, ring = false, shadowArt = false, scrim = false, label = false, text = false, info = true;
        bool poster = false, whiteIcons = false, showTime = true, orbSub = false, miniTip = false;
        _showArtist = true; _showBadge = false;
        double artW = 64, artH = -1, glyph = 26, titleSize = 15, artistSize = 13, trackH = 4, artRadius = 8, seekH = 16, scrimR = 0;
        double ringSize = 144, holeSize = 22, labelRadius = 11;
        var align = TextAlignment.Left;
        var badgeAlign = HorizontalAlignment.Left;

        switch (st)
        {
            case "Compact":
                Card.Width = 340; Card.CornerRadius = new CornerRadius(12); Card.Padding = new Thickness(10);
                artW = 40; glyph = 18; titleSize = 13; artistSize = 12; trackH = 2; seekH = 10; showTime = false;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 10, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ControlsPanel, 0, 2, 1, new Thickness(6, 0, 0, 0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 4, new Thickness(0, 4, 0, 0));
                P(VolumePanel, 2, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 30, 30, 12); Sz(NextBtn, 30, 30, 12); PlayLook(false, 34, 30, 15);
                progress = true;
                break;

            case "Pill":
                Card.Width = double.NaN; Card.MinWidth = 230; Card.MaxWidth = 330; Card.MinHeight = 52;
                Card.CornerRadius = new CornerRadius(26); Card.Padding = new Thickness(18, 8, 10, 8);
                titleSize = 13; artistSize = 11.5;
                P(ArtBorder, 0, 0, 1, new Thickness(0));
                P(InfoPanel, 0, 1, 1, new Thickness(0, 0, 12, 0));
                P(ControlsPanel, 0, 2, 1, new Thickness(0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 4, new Thickness(0));
                P(VolumePanel, 2, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                art = false; prev = false; next = false;
                PlayLook(true, 36, 36, 15);
                break;

            case "Cover":
                Card.Width = 300; Card.CornerRadius = new CornerRadius(16); Card.Padding = new Thickness(14);
                artW = 272; glyph = 56; titleSize = 17; artistSize = 13; artRadius = 12;
                align = TextAlignment.Center; badgeAlign = HorizontalAlignment.Center;
                P(ArtBorder, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(InfoPanel, 1, 0, 4, new Thickness(0, 12, 0, 0));
                P(ProgressPanel, 2, 0, 4, new Thickness(0, 8, 0, 0));
                P(VolumePanel, 3, 0, 4, new Thickness(0, 6, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 3, 0, 4, new Thickness(0, 6, 0, 0), HorizontalAlignment.Center);
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 44, 40, 18); Sz(NextBtn, 44, 40, 18); PlayLook(true, 52, 52, 22);
                _showBadge = true; progress = true; volume = true;
                break;

            case "Island" when !exp:
                Card.Width = 270; Card.CornerRadius = new CornerRadius(22); Card.Padding = new Thickness(8, 8, 10, 8);
                artW = 32; glyph = 14; titleSize = 13; artRadius = 16;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 10, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ControlsPanel, 0, 2, 1, new Thickness(6, 0, 0, 0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 4, new Thickness(0));
                P(VolumePanel, 2, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                _showArtist = false; prev = false; next = false;
                PlayLook(true, 30, 30, 13);
                break;

            case "Island":
                Card.Width = 350; Card.CornerRadius = new CornerRadius(18); Card.Padding = new Thickness(12);
                artW = 48; glyph = 20; titleSize = 14; artRadius = 12;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 12, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ProgressPanel, 1, 0, 4, new Thickness(0, 8, 0, 0));
                P(VolumePanel, 2, 0, 4, new Thickness(0, 4, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 2, 0, 4, new Thickness(0, 4, 0, 0), HorizontalAlignment.Center);
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 40, 36, 16); Sz(NextBtn, 40, 36, 16); PlayLook(true, 40, 40, 17);
                _showBadge = true; progress = true; volume = true;
                break;

            case "Wide":
                Card.Width = 500; Card.CornerRadius = new CornerRadius(14); Card.Padding = new Thickness(12);
                artW = 56; glyph = 22; titleSize = 15; artistSize = 12.5; trackH = 3; seekH = 12; showTime = false;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 12, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ControlsPanel, 0, 2, 1, new Thickness(8, 0, 0, 0), HorizontalAlignment.Right);
                P(VolumePanel, 0, 3, 1, new Thickness(6, 0, 0, 0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 4, new Thickness(0, 6, 0, 0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 36, 34, 14); Sz(NextBtn, 36, 34, 14); PlayLook(false, 40, 34, 18);
                progress = true; volume = true;
                break;

            case "Tile":
                Card.Width = 128; Card.CornerRadius = new CornerRadius(14); Card.Padding = new Thickness(8);
                artW = 112; glyph = 40; artRadius = 10; titleSize = 12; artistSize = 11; trackH = 2; seekH = 10; showTime = false;
                align = TextAlignment.Center; _showArtist = false;
                P(ArtBorder, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(Scrim, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(ControlsPanel, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(InfoPanel, 1, 0, 4, new Thickness(0, 8, 0, 0));
                P(ProgressPanel, 2, 0, 4, new Thickness(0, 2, 0, 0));
                P(VolumePanel, 3, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 28, 28, 12); Sz(NextBtn, 28, 28, 12); PlayLook(true, 34, 34, 15);
                controls = hover; scrim = hover; scrimR = 10; progress = true; whiteIcons = true;
                break;

            case "Text":
                chrome = false;
                Card.Width = double.NaN; Card.MinWidth = 140; Card.MaxWidth = 520; Card.CornerRadius = new CornerRadius(0); Card.Padding = new Thickness(8, 4, 8, 4);
                titleSize = 19; artistSize = 13.5;
                P(InfoPanel, 0, 0, 4, new Thickness(0));
                P(ArtBorder, 0, 0, 1, new Thickness(0));
                P(ControlsPanel, 1, 0, 4, new Thickness(0));
                P(ProgressPanel, 2, 0, 4, new Thickness(0));
                P(VolumePanel, 3, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                art = false; controls = false; text = true;
                break;

            case "Orb":
            case "MiniOrb":
            case "BigOrb":
                chrome = false; ring = true; shadowArt = true; info = false; label = st != "MiniOrb";
                Card.Width = double.NaN; Card.CornerRadius = new CornerRadius(0); Card.Padding = new Thickness(4);
                if (st == "MiniOrb") { artW = 72; glyph = 24; ringSize = 92; holeSize = 12; miniTip = true; }
                else if (st == "BigOrb") { artW = 180; glyph = 52; ringSize = 208; holeSize = 34; orbSub = true; labelRadius = 14; }
                else { artW = 120; glyph = 38; ringSize = 144; holeSize = 22; }
                artRadius = artW / 2;
                P(OrbRing, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(ArtBorder, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(OrbHole, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(Scrim, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(ControlsPanel, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(OrbLabel, 1, 0, 4, new Thickness(0, 6, 0, 0), HorizontalAlignment.Center);
                P(InfoPanel, 2, 0, 4, new Thickness(0));
                P(ProgressPanel, 2, 0, 4, new Thickness(0));
                P(VolumePanel, 3, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                if (st == "MiniOrb") { Sz(PrevBtn, 22, 22, 10); Sz(NextBtn, 22, 22, 10); PlayLook(true, 28, 28, 12); }
                else if (st == "BigOrb") { Sz(PrevBtn, 36, 36, 15); Sz(NextBtn, 36, 36, 15); PlayLook(true, 46, 46, 19); }
                else { Sz(PrevBtn, 28, 28, 12); Sz(NextBtn, 28, 28, 12); PlayLook(true, 34, 34, 15); }
                controls = hover; scrim = hover; scrimR = artW / 2; whiteIcons = true;
                _showArtist = false;
                break;

            case "Disc":   // kapsül içinde dönen plak
                Card.Width = double.NaN; Card.MinWidth = 290; Card.MaxWidth = 380;
                Card.CornerRadius = new CornerRadius(40); Card.Padding = new Thickness(10, 10, 18, 10);
                ring = true; artW = 56; glyph = 20; artRadius = 28; ringSize = 72; holeSize = 12; titleSize = 14; artistSize = 12;
                P(OrbRing, 0, 0, 1, new Thickness(0), HorizontalAlignment.Center);
                P(ArtBorder, 0, 0, 1, new Thickness(0), HorizontalAlignment.Center);
                P(OrbHole, 0, 0, 1, new Thickness(0), HorizontalAlignment.Center);
                P(InfoPanel, 0, 1, 1, new Thickness(10, 0, 12, 0));
                P(ControlsPanel, 0, 2, 1, new Thickness(0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 4, new Thickness(0));
                P(VolumePanel, 2, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 28, 28, 12); Sz(NextBtn, 28, 28, 12); PlayLook(true, 36, 36, 15);
                break;

            case "Float":  // yüzen kare kapak
                chrome = false; shadowArt = true; label = true;
                Card.Width = double.NaN; Card.CornerRadius = new CornerRadius(0); Card.Padding = new Thickness(4);
                artW = 132; glyph = 44; artRadius = 24; trackH = 3; seekH = 12; showTime = false; info = false;
                P(ArtBorder, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(Scrim, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(ControlsPanel, 0, 0, 4, new Thickness(0), HorizontalAlignment.Center);
                P(ProgressPanel, 0, 0, 4, new Thickness(14, 0, 14, 10), HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
                P(OrbLabel, 1, 0, 4, new Thickness(0, 8, 0, 0), HorizontalAlignment.Center);
                P(InfoPanel, 2, 0, 4, new Thickness(0));
                P(VolumePanel, 3, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 30, 30, 13); Sz(NextBtn, 30, 30, 13); PlayLook(true, 38, 38, 16);
                controls = hover; scrim = hover; scrimR = artRadius; progress = true; whiteIcons = true;
                _showArtist = false;
                break;

            case "Poster": // kapak arka plan, yazılar üstünde
                poster = true; whiteIcons = true; showTime = false;
                Card.Width = 340; Card.CornerRadius = new CornerRadius(16); Card.Padding = new Thickness(0);
                artW = 338; artH = 160; glyph = 56; artRadius = 15; titleSize = 16; artistSize = 12.5; trackH = 3; seekH = 12;
                P(ArtBorder, 0, 0, 4, new Thickness(0), HorizontalAlignment.Stretch, VerticalAlignment.Top);
                P(PosterFade, 0, 0, 4, new Thickness(0), HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
                P(InfoPanel, 0, 0, 4, new Thickness(16, 0, 150, 34), HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
                P(ControlsPanel, 0, 0, 4, new Thickness(0, 0, 10, 26), HorizontalAlignment.Right, VerticalAlignment.Bottom);
                P(ProgressPanel, 0, 0, 4, new Thickness(16, 0, 16, 8), HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
                P(VolumePanel, 1, 0, 4, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 32, 32, 13); Sz(NextBtn, 32, 32, 13); PlayLook(true, 38, 38, 16);
                progress = true;
                break;

            default: // Classic
                Card.Width = 380; Card.CornerRadius = new CornerRadius(12); Card.Padding = new Thickness(14);
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 12, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0), HorizontalAlignment.Right, VerticalAlignment.Top);
                P(ProgressPanel, 1, 0, 4, new Thickness(0, 10, 0, 0));
                P(VolumePanel, 2, 0, 4, new Thickness(0, 6, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 2, 0, 4, new Thickness(0, 6, 0, 0), HorizontalAlignment.Center);
                Sz(PrevBtn, 40, 36, 16); Sz(NextBtn, 40, 36, 16); PlayLook(false, 48, 36, 20);
                gear = true; _showBadge = true; progress = true; volume = true;
                break;
        }

        _ringStyle = ring; _ringSize = ringSize; _showTime = showTime; _orbSub = orbSub; _miniTip = miniTip;

        // Kart çerçevesi
        _chromeless = !chrome;
        if (chrome)
        {
            Card.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            Card.BorderBrush = EdgeBrush();
            Card.BorderThickness = new Thickness(1);
            Card.Effect = _cardShadow;
        }
        else
        {
            // 1/255 opaklık: tamamen şeffaf pikseller fare tıklamasını arkadaki pencereye geçirir; böylece her yerinden sürüklenebilir
            Card.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Card.BorderBrush = Brushes.Transparent;
            Card.BorderThickness = new Thickness(0);
            Card.Effect = null;
        }
        ArtBorder.Effect = shadowArt ? _artShadow : null;
        InfoPanel.Effect = text
            ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.9, Color = ThemeManager.IsDark(s.Theme) ? Colors.Black : Colors.White }
            : null;

        // Öğe boyutları / yazı ölçüleri
        _artW = artW;
        ArtBorder.Width = artW; ArtBorder.Height = artH > 0 ? artH : artW; ArtBorder.CornerRadius = new CornerRadius(artRadius);
        ArtGlyph.FontSize = glyph;
        TitleText.FontSize = titleSize; ArtistText.FontSize = artistSize;
        TitleText.TextAlignment = align; ArtistText.TextAlignment = align;
        BadgeBorder.HorizontalAlignment = badgeAlign;
        TrackGrid.Height = trackH;
        SeekArea.Height = seekH;
        Scrim.Width = artW; Scrim.Height = artH > 0 ? artH : artW; Scrim.CornerRadius = new CornerRadius(scrimR);
        OrbRing.Width = ringSize; OrbRing.Height = ringSize;
        RingTrack.Width = ringSize - 4; RingTrack.Height = ringSize - 4;
        OrbHole.Width = holeSize; OrbHole.Height = holeSize;
        OrbLabel.CornerRadius = new CornerRadius(labelRadius);

        // Poster: beyaz yazı / ikonlar
        if (poster)
        {
            TitleText.Foreground = Brushes.White;
            ArtistText.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255));
        }
        else
        {
            TitleText.ClearValue(TextBlock.ForegroundProperty);
            ArtistText.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        }
        IconColor(PrevBtn, whiteIcons);
        IconColor(NextBtn, whiteIcons);

        // Ayarlardaki öğe anahtarlarıyla birleştir
        V(InfoPanel, info);
        V(ArtBorder, art && s.ShowAlbumArt);
        V(GearBtn, gear);
        V(ProgressPanel, progress && s.ShowProgress);
        V(VolumePanel, volume && s.ShowVolumeControls);
        V(ControlsPanel, controls && s.ShowControls);
        V(PrevBtn, prev);
        V(NextBtn, next);
        V(OrbRing, ring && s.ShowProgress);
        V(OrbHole, ring && s.ShowAlbumArt);
        V(Scrim, scrim);
        V(PosterFade, poster && s.ShowAlbumArt);
        V(OrbLabel, label);
        V(OrbSub, orbSub);

        LocalizeStatic();
        Update(Cur);
        UpdateSpin();
        UpdatePin();
    }

    // ================= İçerik =================

    public void Update(MediaInfo i)
    {
        var s = App.Settings;
        bool has = i.HasSession;
        string artist = has ? (string.IsNullOrEmpty(i.Artist) ? i.Album : i.Artist) : "";

        string newTitle = has ? i.Title : L.T("Medya oynatılmıyor");
        bool titleChanged = _shown && has && newTitle != _lastTitle;
        _lastTitle = newTitle;
        TitleText.Text = newTitle;
        ArtistText.Text = has ? artist : L.T("Bir şey oynatınca burada görünür");
        OrbText.Text = has ? i.Title : L.T("Medya yok");
        OrbSub.Text = artist;
        V(ArtistText, _showArtist);
        V(OrbSub, _orbSub && has && artist.Length > 0);
        Card.ToolTip = _miniTip && has ? (artist.Length > 0 ? $"{i.Title} — {artist}" : i.Title) : null;
        BadgeText.Text = L.T(Platforms.Get(i.PlatformId).Name);
        if (_badgeIconId != i.PlatformId)
        {
            _badgeIconId = i.PlatformId;
            BadgeIcon.Content = PlatformIcons.Create(i.PlatformId, 14);
        }
        V(BadgeBorder, _showBadge && has && s.ShowPlatformBadge);
        PlayBtn.Content = i.IsPlaying ? "\uE769" : "\uE768";

        if (i.Art != null)
        {
            ArtBorder.Background = new ImageBrush(i.Art) { Stretch = Stretch.UniformToFill };
            ArtGlyph.Visibility = Visibility.Collapsed;
            ArtIcon.Content = null; _artIconKey = "";
            if (!ReferenceEquals(i.Art, _lastArt) && _shown) FadeIn(ArtBorder, 320, 0.25);
        }
        else
        {
            ArtBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            if (has && i.PlatformId != "other")
            {
                // Kapak yoksa platformun kendi simgesi
                double sz = Math.Clamp(_artW * 0.52, 14, 64);
                string key = $"{i.PlatformId}:{(int)sz}";
                if (key != _artIconKey) { ArtIcon.Content = PlatformIcons.Create(i.PlatformId, sz); _artIconKey = key; }
                ArtGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                ArtIcon.Content = null; _artIconKey = "";
                ArtGlyph.Visibility = Visibility.Visible;
            }
        }
        _lastArt = i.Art;
        if (titleChanged) { FadeIn(TitleText); FadeIn(ArtistText, 320); FadeIn(OrbText); }
        UpdateEq(i.IsPlaying);
        UpdateSpin();
        if (_shown) RefreshLive();
    }

    public void ApplySettings()
    {
        var s = App.Settings;
        Root.LayoutTransform = new ScaleTransform(s.Scale, s.Scale);
        ApplyLayout();
        if (_shown) { UpdateLayout(); Reposition(); UpdateBlur(); }
    }

    /// <summary>Plak stilleri: müzik çalarken kapak yavaşça döner.</summary>
    private void UpdateSpin()
    {
        bool spin = _shown && _ringStyle && App.Settings.Animations && Cur.IsPlaying;
        if (spin == _spinning) return;
        _spinning = spin;

        if (spin)
        {
            double a = ArtRot.Angle % 360;
            var anim = new DoubleAnimation(a, a + 360, TimeSpan.FromSeconds(14)) { RepeatBehavior = RepeatBehavior.Forever };
            ArtRot.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        else
        {
            double a = ArtRot.Angle;
            ArtRot.BeginAnimation(RotateTransform.AngleProperty, null);
            ArtRot.Angle = _ringStyle ? a % 360 : 0;
        }
    }

    public void Poke() => RefreshLive();

    private void RefreshLive()
    {
        var cur = Cur;   // "Gizle" seçilen kaynakların bilgisi hiçbir yerde kullanılmaz

        bool needTimeline = ProgressPanel.Visibility == Visibility.Visible || OrbRing.Visibility == Visibility.Visible;
        if (needTimeline)
        {
            (TimeSpan Pos, TimeSpan Dur)? tl = cur.HasSession ? App.Media.GetTimeline() : null;
            if (IsDemo) tl = (TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(238));   // önizleme için örnek ilerleme
            _lastDur = tl?.Dur.TotalSeconds ?? 0;
            double ratio = tl is { } t && t.Dur.TotalSeconds > 0 ? t.Pos.TotalSeconds / t.Dur.TotalSeconds : 0;
            bool hold = _seeking || DateTime.Now < _seekHold;   // atlama sırasında çubuk geri zıplamasın

            if (ProgressPanel.Visibility == Visibility.Visible && !hold)
            {
                if (tl is { } t2)
                {
                    Fill.Width = Math.Max(0, TrackGrid.ActualWidth * ratio);
                    SetKnob();
                    PosText.Text = Fmt(t2.Pos);
                    DurText.Text = Fmt(t2.Dur);
                }
                else
                {
                    Fill.Width = 0;
                    PosText.Text = DurText.Text = "";
                }
                V(TimeRow, _showTime);
            }
            if (OrbRing.Visibility == Visibility.Visible) UpdateRing(ratio);
        }

        // Ses: Windows ana sesi DEĞİL, yalnızca çalan uygulamanın ses oturumu
        if (VolumePanel.Visibility == Visibility.Visible)
        {
            var st = cur.HasSession ? AppVolume.GetState(cur.AppId) : null;
            bool ok = st != null;
            MuteBtn.IsEnabled = VolSlider.IsEnabled = ok;
            VolumePanel.Opacity = ok ? 1 : 0.4;
            if (ok && !VolSlider.IsMouseCaptureWithin)
            {
                _updatingVol = true;
                VolSlider.Value = st!.Value.Muted ? 0 : st.Value.Volume * 100;
                MuteBtn.Content = st.Value.Muted ? "\uE74F" : "\uE767";
                _updatingVol = false;
            }
        }
    }

    private void UpdateRing(double ratio)
    {
        if (ratio <= 0.002) { RingFill.Data = null; return; }
        ratio = Math.Min(ratio, 0.9999);
        double c = _ringSize / 2, r = c - 3.5;
        double ang = ratio * 2 * Math.PI - Math.PI / 2;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Cos(ang), c + r * Math.Sin(ang)), new Size(r, r), 0, ratio > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        RingFill.Data = g;
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ================= İlerleme çubuğu: tıkla / sürükle ile atla =================

    private double SeekRatio(MouseEventArgs e)
    {
        double w = SeekArea.ActualWidth;
        return w <= 0 ? 0 : Math.Clamp(e.GetPosition(SeekArea).X / w, 0, 1);
    }

    private void PreviewSeek(double ratio)
    {
        Fill.Width = TrackGrid.ActualWidth * ratio;
        SetKnob();
        if (_lastDur > 0) PosText.Text = Fmt(TimeSpan.FromSeconds(_lastDur * ratio));
    }

    private void SeekArea_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;                       // kartın sürükleme işleyicisine gitmesin
        if (_lastDur <= 0) return;
        _seeking = true;
        SeekArea.CaptureMouse();
        PreviewSeek(SeekRatio(e));
    }

    private void SeekArea_Move(object sender, MouseEventArgs e)
    {
        if (_seeking) PreviewSeek(SeekRatio(e));
    }

    private void SeekArea_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        SeekArea.ReleaseMouseCapture();
        if (!SeekArea.IsMouseOver) SeekKnob.Opacity = 0;
        double r = SeekRatio(e);
        PreviewSeek(r);
        App.Media.Seek(r);
        _seekHold = DateTime.Now.AddMilliseconds(900);
        e.Handled = true;
    }

    // ================= Göster / gizle =================

    public void ShowFlyout()
    {
        if (_shown) return;
        _shown = true;

        if (_expanded) { _expanded = false; ApplyLayout(); }

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        UpdateLayout();
        Reposition();
        UpdateBlur();
        RefreshLive();
        UpdateSpin();
        UpdatePin();
        UpdateEq(Cur.IsPlaying);
        Native.KeepOnTop(_hwnd);
        _tick.Start();
        Animate(true);
    }

    public void HideFlyout()
    {
        if (!_shown) return;
        _shown = false;
        _tick.Stop();
        Demo = null;
        AnchorDip = null;
        UpdateSpin();
        UpdateEq(false);
        Animate(false);
    }

    private bool IsTopAnchored => App.Settings.CurrentCustom == null &&
        (App.Settings.FlyoutStyle == "Island" || (App.Settings.Position ?? "").StartsWith("Top"));

    private void Animate(bool show)
    {
        var s = App.Settings;
        if (!s.Animations)
        {
            BeginAnimation(OpacityProperty, null);
            Shift.BeginAnimation(TranslateTransform.YProperty, null);
            Shift.Y = 0;
            Opacity = show ? 1 : 0;
            if (!show) Hide();
            return;
        }

        var d = TimeSpan.FromMilliseconds(show ? 200 : 140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double dir = IsTopAnchored ? -1 : 1;

        var fade = new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, d) { EasingFunction = ease };
        if (!show) fade.Completed += (_, _) => { if (!_shown) Hide(); };
        BeginAnimation(OpacityProperty, fade);

        var slide = new DoubleAnimation(show ? 14 * dir : 0, show ? 0 : 10 * dir, d) { EasingFunction = ease };
        Shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    public void Reposition()
    {
        var s = App.Settings;
        var wa = SystemParameters.WorkArea;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double left, top;
        if (AnchorDip is { } a)
        {
            double inset = 18 * s.Scale;     // pencerenin kart çevresindeki gölge boşluğu
            left = a.Left - inset;
            top = a.Top > wa.Top + wa.Height / 2
                ? a.Top - 6 - (h - inset)    // kontrol altta: flyout üstünde
                : a.Bottom + 6 - inset;      // kontrol üstte: flyout altında
        }
        else if (s.CurrentCustom is { } c)
        {
            left = c.X; top = c.Y;
        }
        else
        {
            const double m = 6;
            string p = s.FlyoutStyle == "Island" ? "TopCenter" : (s.Position ?? "BottomCenter");
            left = p.EndsWith("Left") ? wa.Left + m
                 : p.EndsWith("Right") ? wa.Right - w - m
                 : wa.Left + (wa.Width - w) / 2;
            top = p.StartsWith("Top") ? wa.Top + m : wa.Bottom - h - m;
        }
        Left = Math.Clamp(left, wa.Left - 20, Math.Max(wa.Left, wa.Right - w + 20));
        Top = Math.Clamp(top, wa.Top - 20, Math.Max(wa.Top, wa.Bottom - h + 20));
    }

    private void UpdateBlur()
    {
        if (_hwnd == IntPtr.Zero || (!IsLoaded && !_shown)) return;
        try
        {
            if (!App.Settings.BlurEnabled || _chromeless) { Native.SetBlur(_hwnd, false, 0, 0, 0, 0, 0); return; }
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget == null) return;
            double dx = src.CompositionTarget.TransformToDevice.M11;
            double dy = src.CompositionTarget.TransformToDevice.M22;
            var r = Card.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Card.ActualWidth, Card.ActualHeight));
            double diameter = Card.CornerRadius.TopLeft * 2 * App.Settings.Scale * dx;
            Native.SetBlur(_hwnd, true, (int)(r.Left * dx), (int)(r.Top * dy), (int)(r.Right * dx), (int)(r.Bottom * dy), (int)diameter);
        }
        catch { }
    }

    // ================= Etkileşim =================

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))   // Ctrl + tekerlek: büyüt / küçült
        {
            var s = App.Settings;
            s.Scale = Math.Round(Math.Clamp(s.Scale + (e.Delta > 0 ? 0.05 : -0.05), 0.7, 1.6), 2);
            SettingsStore.Save(s);
            App.ApplyAll();
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseWheel(e);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != Card;
             d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or Slider || d == SeekArea) return;

        try { DragMove(); } catch { return; }
        App.Settings.SetCustom(Left, Top);      // konum bu stile özel saklanır
        SettingsStore.Save(App.Settings);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => App.Media.TogglePlay();
    private void Prev_Click(object sender, RoutedEventArgs e) => App.Media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => App.Media.Next();
    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        var cur = App.Displayed;
        if (!cur.HasSession) return;
        var st = AppVolume.GetState(cur.AppId);
        if (st != null) AppVolume.SetMute(cur.AppId, !st.Value.Muted);
        RefreshLive();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingVol) return;
        var cur = App.Displayed;
        if (!cur.HasSession) return;
        if (e.NewValue > 0 && AppVolume.GetState(cur.AppId)?.Muted == true) AppVolume.SetMute(cur.AppId, false);
        AppVolume.SetVolume(cur.AppId, (float)(e.NewValue / 100.0));
    }
}
