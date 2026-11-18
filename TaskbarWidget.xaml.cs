using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickMedia;

/// <summary>
/// Görev çubuğunun boş sol kısmına oturan küçük "şu an çalıyor" kontrolü.
/// Tıkla: flyout aç/kapat · Fare tekerleği: çalan uygulamanın sesi · Sağ tık: menü.
/// </summary>
public partial class TaskbarWidget : Window
{
    private IntPtr _hwnd;
    private string _key = "";
    private bool _light;
    private DateTime _themeStamp = DateTime.MinValue;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

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

        Hit.MouseEnter += (_, _) => Hit.Background = new SolidColorBrush(_light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1A, 255, 255, 255));
        Hit.MouseLeave += (_, _) => Hit.Background = Brushes.Transparent;

        Hit.MouseLeftButtonUp += (_, e) =>
        {
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != Hit;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ButtonBase) return;
            App.Flyout.Toggle();
        };

        Hit.MouseWheel += (_, e) => App.AdjustVolume(e.Delta > 0 ? 0.05f : -0.05f, nudge: false);
        Hit.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
    }

    private void ShowMenu()
    {
        var m = new ContextMenu { PlacementTarget = Hit };

        var settings = new MenuItem { Header = L.T("Ayarlar") };
        settings.Click += (_, _) => App.ShowSettings("Taskbar");
        var toggle = new MenuItem { Header = L.T("Göster / Gizle") };
        toggle.Click += (_, _) => App.Flyout.Toggle();
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

    // ---------- Görünürlük / konum ----------

    /// <summary>Ayar ve medya durumuna göre gösterir, gizler, yerleştirir. Saniyede bir ve olaylarda çağrılır.</summary>
    public void Refresh()
    {
        try
        {
            var s = App.Settings;
            bool want = s.UseTaskbar
                        && FlyoutController.HasDisplayable(App.Media.Current)
                        && !(s.HideInFullscreen && Native.IsFullscreenBusy());

            if (!want || !Native.GetTaskbar(out var rc, out uint edge) || (edge != 1 && edge != 3))
            {
                if (IsVisible) Hide();
                return;
            }

            UpdateColors();
            Update(App.Displayed);
            if (!IsVisible) Show();
            Place(rc);
        }
        catch (Exception ex) { Log.Write("Görev çubuğu kontrolü hatası: " + ex.Message); }
    }

    private void Place(Native.RECT rc)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        int tbH = rc.bottom - rc.top;

        double hDip = Math.Max(24, Math.Min(40, tbH / sy - 8));
        double wDip = Math.Clamp(App.Settings.TaskbarWidth, 160, 600);
        Width = wDip;
        Height = hDip;

        int wpx = (int)Math.Round(wDip * sx), hpx = (int)Math.Round(hDip * sy);
        int x = rc.left + (int)Math.Round(App.Settings.TaskbarOffset * sx);
        int y = rc.top + (tbH - hpx) / 2;
        Native.PlaceTopmost(_hwnd, x, y, wpx, hpx);   // her saniye yeniden: görev çubuğunun önünde kalsın
    }

    // ---------- Görünüm ----------

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
        foreach (var b in new[] { PrevBtn, PlayBtn, NextBtn }) b.Foreground = new SolidColorBrush(main);
        Art.Background = new SolidColorBrush(_light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x26, 255, 255, 255));
    }

    private void Update(MediaInfo m)
    {
        string key = $"{m.Key}|{m.Title}|{m.Artist}|{m.IsPlaying}|{m.Art != null}|{L.Code}";
        if (key == _key) return;
        _key = key;

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
            _themeStamp = DateTime.MinValue;   // placeholder arka planı yeniden uygulansın
            UpdateColors();
            ArtIcon.Content = PlatformIcons.Create(m.PlatformId, 18);
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => App.Media.TogglePlay();
    private void Prev_Click(object sender, RoutedEventArgs e) => App.Media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => App.Media.Next();
}
