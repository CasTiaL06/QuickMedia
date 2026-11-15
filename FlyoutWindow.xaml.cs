using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickMedia;

public partial class FlyoutWindow : Window
{
    private IntPtr _hwnd;
    private bool _shown;
    private bool _ready;
    private bool _updatingVol;
    private bool _expanded;          // Island stili: fare üstündeyken genişler
    private bool _showArtist = true;
    private bool _showBadge;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public bool IsShown => _shown;

    public FlyoutWindow()
    {
        InitializeComponent();
        _ready = true;

        _tick.Tick += (_, _) => RefreshLive();

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

        ApplyLayout();
    }

    // ================= Yerleşim (stiller) =================

    private void SetExpanded(bool v)
    {
        if (App.Settings.FlyoutStyle != "Island" || _expanded == v) return;
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
    }

    private void ApplyLayout()
    {
        var s = App.Settings;
        string st = FlyoutStyles.Normalize(s.FlyoutStyle);
        bool exp = st == "Island" && _expanded;

        // Varsayılanlar
        Card.MinWidth = 0; Card.MaxWidth = double.PositiveInfinity; Card.MinHeight = 0;
        bool art = true, gear = false, progress = false, volume = false, controls = true, prev = true, next = true;
        _showArtist = true; _showBadge = false;
        double artW = 64, glyph = 26, titleSize = 15, artistSize = 13, trackH = 4, artRadius = 8;
        var align = TextAlignment.Left;
        var badgeAlign = HorizontalAlignment.Left;

        switch (st)
        {
            case "Compact":
                Card.Width = 340; Card.CornerRadius = new CornerRadius(12); Card.Padding = new Thickness(10);
                artW = 40; glyph = 18; titleSize = 13; artistSize = 12; trackH = 2;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 10, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ControlsPanel, 0, 2, 1, new Thickness(6, 0, 0, 0), HorizontalAlignment.Right);
                P(ProgressPanel, 1, 0, 3, new Thickness(0, 8, 0, 0));
                P(VolumePanel, 2, 0, 3, new Thickness(0));
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
                P(ProgressPanel, 1, 0, 3, new Thickness(0));
                P(VolumePanel, 2, 0, 3, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                art = false; prev = false; next = false;
                PlayLook(true, 36, 36, 15);
                break;

            case "Cover":
                Card.Width = 300; Card.CornerRadius = new CornerRadius(16); Card.Padding = new Thickness(14);
                artW = 272; glyph = 56; titleSize = 17; artistSize = 13; artRadius = 12;
                align = TextAlignment.Center; badgeAlign = HorizontalAlignment.Center;
                P(ArtBorder, 0, 0, 3, new Thickness(0), HorizontalAlignment.Center);
                P(InfoPanel, 1, 0, 3, new Thickness(0, 12, 0, 0));
                P(ProgressPanel, 2, 0, 3, new Thickness(0, 12, 0, 0));
                P(VolumePanel, 3, 0, 3, new Thickness(0, 6, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 3, 0, 3, new Thickness(0, 6, 0, 0), HorizontalAlignment.Center);
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
                P(ProgressPanel, 1, 0, 3, new Thickness(0));
                P(VolumePanel, 2, 0, 3, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0));
                _showArtist = false; prev = false; next = false;
                PlayLook(true, 30, 30, 13);
                break;

            case "Island":
                Card.Width = 350; Card.CornerRadius = new CornerRadius(18); Card.Padding = new Thickness(12);
                artW = 48; glyph = 20; titleSize = 14; artRadius = 12;
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 12, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(ProgressPanel, 1, 0, 3, new Thickness(0, 12, 0, 0));
                P(VolumePanel, 2, 0, 3, new Thickness(0, 4, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 2, 0, 3, new Thickness(0, 4, 0, 0), HorizontalAlignment.Center);
                P(GearBtn, 0, 2, 1, new Thickness(0));
                Sz(PrevBtn, 40, 36, 16); Sz(NextBtn, 40, 36, 16); PlayLook(true, 40, 40, 17);
                _showBadge = true; progress = true; volume = true;
                break;

            default: // Classic
                Card.Width = 380; Card.CornerRadius = new CornerRadius(12); Card.Padding = new Thickness(14);
                P(ArtBorder, 0, 0, 1, new Thickness(0, 0, 12, 0));
                P(InfoPanel, 0, 1, 1, new Thickness(0));
                P(GearBtn, 0, 2, 1, new Thickness(0), HorizontalAlignment.Right, VerticalAlignment.Top);
                P(ProgressPanel, 1, 0, 3, new Thickness(0, 14, 0, 0));
                P(VolumePanel, 2, 0, 3, new Thickness(0, 6, 0, 0), HorizontalAlignment.Left);
                P(ControlsPanel, 2, 0, 3, new Thickness(0, 6, 0, 0), HorizontalAlignment.Center);
                Sz(PrevBtn, 40, 36, 16); Sz(NextBtn, 40, 36, 16); PlayLook(false, 48, 36, 20);
                gear = true; _showBadge = true; progress = true; volume = true;
                break;
        }

        // Öğe boyutları / yazı ölçüleri
        ArtBorder.Width = artW; ArtBorder.Height = artW; ArtBorder.CornerRadius = new CornerRadius(artRadius);
        ArtGlyph.FontSize = glyph;
        TitleText.FontSize = titleSize; ArtistText.FontSize = artistSize;
        TitleText.TextAlignment = align; ArtistText.TextAlignment = align;
        BadgeBorder.HorizontalAlignment = badgeAlign;
        TrackGrid.Height = trackH;

        // Ayarlardaki öğe anahtarlarıyla birleştir
        V(ArtBorder, art && s.ShowAlbumArt);
        V(GearBtn, gear);
        V(ProgressPanel, progress && s.ShowProgress);
        V(VolumePanel, volume && s.ShowVolumeControls);
        V(ControlsPanel, controls && s.ShowControls);
        V(PrevBtn, prev);
        V(NextBtn, next);

        Update(App.Media?.Current ?? new MediaInfo());
    }

    // ================= İçerik =================

    public void Update(MediaInfo i)
    {
        var s = App.Settings;
        bool has = i.HasSession;

        TitleText.Text = has ? i.Title : "Medya oynatılmıyor";
        ArtistText.Text = has ? (string.IsNullOrEmpty(i.Artist) ? i.Album : i.Artist) : "Bir şey oynatınca burada görünür";
        V(ArtistText, _showArtist);
        BadgeText.Text = Platforms.Get(i.PlatformId).Name;
        V(BadgeBorder, _showBadge && has && s.ShowPlatformBadge);
        PlayBtn.Content = i.IsPlaying ? "\uE769" : "\uE768";

        if (i.Art != null)
        {
            ArtBorder.Background = new ImageBrush(i.Art) { Stretch = Stretch.UniformToFill };
            ArtGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            ArtBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            ArtGlyph.Visibility = Visibility.Visible;
        }
        if (_shown) RefreshLive();
    }

    public void ApplySettings()
    {
        var s = App.Settings;
        Root.LayoutTransform = new ScaleTransform(s.Scale, s.Scale);
        ApplyLayout();
        if (_shown) { UpdateLayout(); Reposition(); UpdateBlur(); }
    }

    private void RefreshLive()
    {
        var s = App.Settings;

        if (ProgressPanel.Visibility == Visibility.Visible)
        {
            var tl = App.Media.GetTimeline();
            if (tl is { } t)
            {
                double ratio = t.Dur.TotalSeconds > 0 ? t.Pos.TotalSeconds / t.Dur.TotalSeconds : 0;
                Fill.Width = Math.Max(0, TrackGrid.ActualWidth * ratio);
                PosText.Text = Fmt(t.Pos);
                DurText.Text = Fmt(t.Dur);
            }
            else
            {
                Fill.Width = 0;
                PosText.Text = DurText.Text = "";
            }
            TimeRow.Visibility = TrackGrid.Height >= 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Ses: Windows ana sesi DEĞİL, yalnızca çalan uygulamanın ses oturumu
        if (VolumePanel.Visibility == Visibility.Visible)
        {
            var st = AppVolume.GetState(App.Media.Current.AppId);
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

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

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
        _tick.Start();
        Animate(true);
    }

    public void HideFlyout()
    {
        if (!_shown) return;
        _shown = false;
        _tick.Stop();
        Animate(false);
    }

    private bool IsTopAnchored => App.Settings.CustomX == null &&
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
        if (s.CustomX is double cx && s.CustomY is double cy)
        {
            left = cx; top = cy;
        }
        else
        {
            const double m = 6;
            // Island stili varsayılan olarak ekranın üst ortasında durur
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
            if (!App.Settings.BlurEnabled) { Native.SetBlur(_hwnd, false, 0, 0, 0, 0, 0); return; }
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
            if (d is ButtonBase or Slider) return;

        try { DragMove(); } catch { return; }
        var s = App.Settings;
        s.CustomX = Left;
        s.CustomY = Top;
        SettingsStore.Save(s);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => App.Media.TogglePlay();
    private void Prev_Click(object sender, RoutedEventArgs e) => App.Media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => App.Media.Next();
    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        string id = App.Media.Current.AppId;
        var st = AppVolume.GetState(id);
        if (st != null) AppVolume.SetMute(id, !st.Value.Muted);
        RefreshLive();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingVol) return;
        string id = App.Media.Current.AppId;
        if (e.NewValue > 0 && AppVolume.GetState(id)?.Muted == true) AppVolume.SetMute(id, false);
        AppVolume.SetVolume(id, (float)(e.NewValue / 100.0));
    }
}
