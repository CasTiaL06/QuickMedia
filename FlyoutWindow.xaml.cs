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
            // Odağı çalma + Alt-Tab'da görünme
            int ex = Native.GetWindowLong(_hwnd, -20);
            Native.SetWindowLong(_hwnd, -20, ex | 0x08000000 | 0x00000080);
            UpdateBlur();
        };
        SizeChanged += (_, _) => { UpdateBlur(); if (_shown) Reposition(); };
        Card.MouseLeftButtonDown += Card_MouseLeftButtonDown;
        Update(new MediaInfo());
    }

    // ---------- Görünüm güncelleme ----------

    public void Update(MediaInfo i)
    {
        var s = App.Settings;
        bool has = i.HasSession;

        TitleText.Text = has ? i.Title : "Medya oynatılmıyor";
        ArtistText.Text = has ? (string.IsNullOrEmpty(i.Artist) ? i.Album : i.Artist) : "Bir şey oynatınca burada görünür";
        BadgeText.Text = Platforms.Get(i.PlatformId).Name;
        BadgeBorder.Visibility = has && s.ShowPlatformBadge ? Visibility.Visible : Visibility.Collapsed;
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
        ArtBorder.Visibility = s.ShowAlbumArt ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = s.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        ControlsPanel.Visibility = s.ShowControls ? Visibility.Visible : Visibility.Collapsed;
        VolumePanel.Visibility = s.ShowVolumeControls ? Visibility.Visible : Visibility.Collapsed;
        BottomRow.Visibility = (s.ShowControls || s.ShowVolumeControls) ? Visibility.Visible : Visibility.Collapsed;
        Update(App.Media?.Current ?? new MediaInfo());
        if (_shown) { UpdateLayout(); Reposition(); UpdateBlur(); }
    }

    private void RefreshLive()
    {
        var s = App.Settings;

        if (s.ShowProgress)
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
        }

        if (s.ShowVolumeControls && !VolSlider.IsMouseCaptureWithin)
        {
            _updatingVol = true;
            bool muted = AudioService.GetMute();
            VolSlider.Value = muted ? 0 : AudioService.GetVolume() * 100;
            MuteBtn.Content = muted ? "\uE74F" : "\uE767";
            _updatingVol = false;
        }
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---------- Göster / gizle ----------

    public void ShowFlyout()
    {
        if (_shown) return;
        _shown = true;

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
        double dir = (s.CustomX == null && s.Position.StartsWith("Top")) ? -1 : 1;

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
            const double m = 6; // kartın etrafında zaten gölge boşluğu var
            string p = s.Position ?? "BottomCenter";
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
        if (_hwnd == IntPtr.Zero || !IsLoaded && !_shown) return;
        try
        {
            if (!App.Settings.BlurEnabled) { Native.SetBlur(_hwnd, false, 0, 0, 0, 0, 0); return; }
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget == null) return;
            double dx = src.CompositionTarget.TransformToDevice.M11;
            double dy = src.CompositionTarget.TransformToDevice.M22;
            var r = Card.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Card.ActualWidth, Card.ActualHeight));
            double diameter = 24 * App.Settings.Scale * dx;
            Native.SetBlur(_hwnd, true, (int)(r.Left * dx), (int)(r.Top * dy), (int)(r.Right * dx), (int)(r.Bottom * dy), (int)diameter);
        }
        catch { /* henüz görsel ağaca bağlanmamış olabilir */ }
    }

    // ---------- Etkileşim ----------

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        // Ctrl + tekerlek: büyüt / küçült
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var s = App.Settings;
            s.Scale = Math.Round(Math.Clamp(s.Scale + (e.Delta > 0 ? 0.05 : -0.05), 0.7, 1.6), 2);
            SettingsStore.Save(s);
            ApplySettings();
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
        AudioService.ToggleMute();
        RefreshLive();
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingVol) return;
        if (AudioService.GetMute() && e.NewValue > 0) AudioService.SetMute(false);
        AudioService.SetVolume((float)(e.NewValue / 100.0));
    }
}
