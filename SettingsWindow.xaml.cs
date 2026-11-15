using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickMedia;

public partial class SettingsWindow : Window
{
    private bool _loading = true;     // kurulum sırasında tetiklenen olayları yoksay
    private HotkeyAction? _capAction;

    private static readonly string[] Accents = ["#8AB4F8", "#81C995", "#F28B82", "#FDD663", "#C58AF9", "#78D9EC"];
    private static readonly int[] Steps = [1, 2, 3, 4, 5, 8, 10, 15, 20, 30, 60];

    private readonly Dictionary<string, FrameworkElement> _pages = new();

    // Denetimler
    private CheckBox _enabled = null!, _autoStart = null!, _portable = null!;
    private CheckBox _track = null!, _play = null!, _fullscreen = null!, _paused = null!;
    private CheckBox _blur = null!, _anim = null!, _art = null!, _prog = null!, _ctrl = null!, _vol = null!, _badge = null!;
    private RadioButton _themeDark = null!, _themeLight = null!, _themeMid = null!;
    private Slider _opacity = null!, _scale = null!;
    private TextBlock _opacityText = null!, _scaleText = null!, _pathText = null!, _hotkeyStatus = null!;
    private StackPanel _accentPanel = null!, _platformHost = null!;
    private readonly List<RadioButton> _posRadios = new();
    private readonly List<(string Id, Border Tile)> _tiles = new();
    private readonly Dictionary<HotkeyAction, Button> _hotkeyBtns = new();

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBar();
        App.Applied += OnApplied;
        Closed += (_, _) => { App.Applied -= OnApplied; if (_capAction != null) EndCapture(); };

        BuildNav();
        BuildPages();
        Load();
        ShowPage("General");
    }

    private void OnApplied() { RefreshTiles(); }

    private void ApplyTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.SetDarkTitleBar(h, App.Settings.Theme != "Light");
    }

    // ================= Yardımcı oluşturucular =================

    private Style Res(string key) => (Style)FindResource(key);

    private static void Bind(FrameworkElement e, DependencyProperty p, string key) => e.SetResourceReference(p, key);

    private CheckBox Sw(RoutedEventHandler? h = null)
    {
        var c = new CheckBox { Style = Res("Switch"), VerticalAlignment = VerticalAlignment.Center };
        c.Click += h ?? new RoutedEventHandler(OnChanged);
        return c;
    }

    private RadioButton Seg(string text, string group, object? tag, RoutedEventHandler click)
    {
        var r = new RadioButton { Content = text, GroupName = group, Tag = tag, Style = Res("Seg") };
        r.Click += click;
        return r;
    }

    private Border SegHost(UIElement content)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = content };
        Bind(b, Border.BackgroundProperty, "SolidBgBrush");
        return b;
    }

    private Border SegRow(params RadioButton[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) sp.Children.Add(i);
        return SegHost(sp);
    }

    private Grid Row(string title, string? desc, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (desc != null)
            left.Children.Add(new TextBlock { Text = desc, Style = Res("Hint"), Margin = new Thickness(0, 2, 0, 0) });
        g.Children.Add(left);

        var fe = (FrameworkElement)control;
        fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(fe, 1);
        g.Children.Add(fe);
        return g;
    }

    private Border Group(params UIElement[] rows)
    {
        var sp = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var sep = new Border { Height = 1 };
                Bind(sep, Border.BackgroundProperty, "SolidBorderBrush");
                sp.Children.Add(sep);
            }
            sp.Children.Add(rows[i]);
        }
        var b = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), Child = sp };
        Bind(b, Border.BackgroundProperty, "SolidSurfaceBrush");
        Bind(b, Border.BorderBrushProperty, "SolidBorderBrush");
        return b;
    }

    private TextBlock PageTitle(string t) => new() { Text = t, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };

    private TextBlock Section(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(4, 2, 0, 8), Opacity = 0.75 };

    private TextBlock Note(string t) => new() { Text = t, Style = Res("Hint"), Margin = new Thickness(4, 0, 0, 14) };

    private ScrollViewer Page(params UIElement[] kids)
    {
        var sp = new StackPanel { Margin = new Thickness(36, 30, 36, 24) };
        foreach (var k in kids) sp.Children.Add(k);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    private Slider Sld(double min, double max)
    {
        var s = new Slider { Minimum = min, Maximum = max, Width = 170, Style = Res("FluentSlider"), VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += OnSlider;
        return s;
    }

    private Button Btn(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = text, Style = Res("FluentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += click;
        return b;
    }

    // ================= Gezinme =================

    private void BuildNav()
    {
        var items = new[]
        {
            ("General", "\uE713", "Genel"),
            ("Appearance", "\uE790", "Stil ve görünüm"),
            ("Visibility", "\uE7B3", "Görünürlük"),
            ("Platforms", "\uE71D", "Platformlar"),
            ("Hotkey", "\uE765", "Kısayollar"),
            ("About", "\uE946", "Hakkında"),
        };
        foreach (var (key, glyph, label) in items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15, Width = 28, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Content = sp, GroupName = "nav", Tag = key, Style = Res("Nav"), IsChecked = key == "General" };
            rb.Click += (_, _) => ShowPage(key);
            NavHost.Children.Add(rb);
        }
    }

    private void ShowPage(string key) => Host.Content = _pages[key];

    // ================= Sayfalar =================

    private void BuildPages()
    {
        // ---- Genel ----
        _enabled = Sw(); _autoStart = Sw(); _portable = Sw(OnPortable);
        _pathText = new TextBlock { Style = Res("Hint"), TextAlignment = TextAlignment.Right, MaxWidth = 300 };
        var openFolder = Btn("Klasörü aç", (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Dir}\"") { UseShellExecute = true }); } catch { }
        });
        _pages["General"] = Page(
            PageTitle("Genel"),
            Group(
                Row("Flyout etkin", "Kapalıyken flyout hiçbir koşulda (kısayol dahil) görünmez.", _enabled),
                Row("Windows ile başlat", "Oturum açıldığında QuickMedia otomatik çalışsın.", _autoStart),
                Row("Taşınabilir mod", "Ayarlar exe'nin yanındaki settings.json'da tutulur; klasörü USB belleğe kopyalayıp her yerde çalıştırabilirsiniz.", _portable)),
            Section("AYAR DOSYASI"),
            Group(Row("Konum", null, new StackPanel { Orientation = Orientation.Horizontal, Children = { _pathText, new Border { Width = 10 }, openFolder } })));

        // ---- Stil ve görünüm ----
        var tiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (id, name) in FlyoutStyles.All) tiles.Children.Add(MakeTile(id, name));

        _themeDark = Seg("Dark Glass", "theme", "Dark", OnChanged);
        _themeLight = Seg("Light Glass", "theme", "Light", OnChanged);
        _themeMid = Seg("Midnight", "theme", "Midnight", OnChanged);

        _accentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        BuildAccents();

        _opacity = Sld(30, 100); _opacityText = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _scale = Sld(70, 160); _scaleText = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _blur = Sw(); _anim = Sw();
        _art = Sw(); _prog = Sw(); _ctrl = Sw(); _vol = Sw(); _badge = Sw();

        var posGrid = new UniformGrid { Columns = 3 };
        foreach (var (tag, text) in new[] { ("TopLeft", "Üst sol"), ("TopCenter", "Üst orta"), ("TopRight", "Üst sağ"),
                                            ("BottomLeft", "Alt sol"), ("BottomCenter", "Alt orta"), ("BottomRight", "Alt sağ") })
        {
            var r = Seg(text, "pos", tag, OnPosition);
            _posRadios.Add(r);
            posGrid.Children.Add(r);
        }

        _pages["Appearance"] = Page(
            PageTitle("Stil ve görünüm"),
            Section("STİL"),
            tiles,
            Note("Stile tıklayınca flyout önizlemesi açılır. Flyout üzerinde sağ tık sıradaki stile geçer; tray menüsünde de 'Stil' var. Island stili ekranın üst ortasında durur ve fareyle üstüne gelince genişler."),
            Section("TEMA"),
            Group(
                Row("Tema", "Dark Glass: yarı saydam koyu yüzey, ince kenarlık, yumuşak gölge.", SegRow(_themeDark, _themeLight, _themeMid)),
                Row("Vurgu rengi", "İlerleme çubuğu ve düğmeler. Varsayılan #8AB4F8", _accentPanel),
                Row("Arka plan opaklığı", "Varsayılan %80 (#CC171B24).", new StackPanel { Orientation = Orientation.Horizontal, Children = { _opacity, _opacityText } }),
                Row("Arka plan bulanıklığı", "Masaüstü duvar kâğıdını hafifçe bulanıklaştırır.", _blur)),
            Section("BOYUT VE KONUM"),
            Group(
                Row("Boyut", "Flyout üzerindeyken Ctrl + fare tekerleği ile de değişir.", new StackPanel { Orientation = Orientation.Horizontal, Children = { _scale, _scaleText } }),
                Row("Konum", "Hazır bir konum seçin ya da flyout'u fareyle sürükleyin.", SegHost(posGrid))),
            Section("ÖĞELER"),
            Group(
                Row("Albüm kapağı", null, _art),
                Row("İlerleme çubuğu", null, _prog),
                Row("Oynat / önceki / sonraki", null, _ctrl),
                Row("Uygulama sesi", "Yalnızca çalan uygulamanın ses seviyesini ve sessize almasını yönetir (Windows ana sesine dokunmaz).", _vol),
                Row("Platform etiketi", null, _badge),
                Row("Açılış / kapanış animasyonu", null, _anim)),
            Btn("Önizleme göster", (_, _) => App.Flyout.Preview()));

        // ---- Görünürlük ----
        _track = Sw(); _play = Sw(); _fullscreen = Sw(); _paused = Sw();
        _pages["Visibility"] = Page(
            PageTitle("Görünürlük"),
            Group(
                Row("Parça değişince göster", "Yeni şarkı / video başladığında flyout açılır.", _track),
                Row("Oynat / duraklat olunca göster", "Oynatma durumu değiştiğinde flyout açılır.", _play),
                Row("Tam ekranda gizle", "Oyun, tam ekran video veya sunum sırasında otomatik gösterme yapılmaz. Kısayol yine çalışır.", _fullscreen),
                Row("Duraklatılınca gizle", "'Hep göster' modundaki platformlarda medya duraklatılırsa flyout kapanır.", _paused)),
            Note("Her platform için süre ve davranışı 'Platformlar' sayfasından ayarlayabilirsiniz."),
            Btn("Önizleme göster", (_, _) => App.Flyout.Preview()));

        // ---- Platformlar ----
        _platformHost = new StackPanel();
        BuildPlatforms();
        _pages["Platforms"] = Page(
            PageTitle("Platformlar"),
            Note("Hep göster: medya varken sürekli açık kalır. Süreli: tetiklenince seçilen saniye kadar görünür. Gizle: bu platformda otomatik açılmaz. Listede olmayan kaynaklar 'Diğer' kuralını kullanır."),
            _platformHost,
            Btn("Tümünü varsayılana döndür", (_, _) =>
            {
                App.Settings.ResetRules();
                SettingsStore.Save(App.Settings);
                BuildPlatforms();
            }));

        // ---- Kısayollar ----
        var hkRows = new List<UIElement>();
        foreach (var r in HotkeyRows)
        {
            var action = r.A;
            var btn = new Button { Style = Res("FluentButton"), MinWidth = 170, Padding = new Thickness(14, 8, 14, 8), FontWeight = FontWeights.SemiBold };
            btn.Click += (_, _) => BeginCapture(action);
            _hotkeyBtns[action] = btn;
            var reset = Btn("Sıfırla", (_, _) => ResetOneHotkey(action));
            reset.Margin = new Thickness(8, 0, 0, 0);
            string def = "Varsayılan: " + HotkeyService.Describe(AppSettings.DefaultHotkey(action));
            hkRows.Add(Row(r.Title, def, new StackPanel { Orientation = Orientation.Horizontal, Children = { btn, reset } }));
        }
        _hotkeyStatus = new TextBlock { Style = Res("Hint"), Margin = new Thickness(4, 0, 0, 14) };
        _pages["Hotkey"] = Page(
            PageTitle("Kısayollar"),
            Note("Düğmeye tıklayıp yeni kombinasyona basın (en az bir Ctrl, Alt veya Win ile). Bu kısayollar herhangi bir uygulama açıkken çalışır."),
            Group(hkRows.ToArray()),
            _hotkeyStatus,
            Btn("Tümünü varsayılana döndür", (_, _) => ResetAllHotkeys()));

        // ---- Hakkında ----
        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";
        _pages["About"] = Page(
            PageTitle("Hakkında"),
            Group(
                Row("QuickMedia", "Sürüm " + ver, new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 36, Height = 36 }),
                Row("Nasıl çalışır?", "Windows'un küresel medya kontrollerini dinler. Spotify, YouTube, Netflix, X, Instagram gibi kaynaklar tanınır; tanınmayanlar 'Diğer' kuralını kullanır. Web siteleri tarayıcı pencere başlığına bakılarak tahmin edilir.", new Border())),
            Group(Row("Günlük dosyası", "Bir sorun olursa buradaki log.txt'ye bakın.", Btn("Aç", (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Dir}\"") { UseShellExecute = true }); } catch { }
            }))));
    }

    // ================= Stil kutucukları =================

    private static void Shp(Canvas c, double x, double y, double w, double h, double r, string key, double op = 1, bool ellipse = false)
    {
        Shape s = ellipse ? new Ellipse() : new Rectangle { RadiusX = r, RadiusY = r };
        s.Width = w; s.Height = h; s.Opacity = op;
        s.SetResourceReference(Shape.FillProperty, key);
        Canvas.SetLeft(s, x); Canvas.SetTop(s, y);
        c.Children.Add(s);
    }

    private static void Draw(Canvas c, string id)
    {
        const string card = "SolidHoverBrush", ink = "Text2Brush", acc = "AccentBrush", trk = "TrackBrush";
        switch (id)
        {
            case "Classic":
                Shp(c, 6, 6, 100, 56, 8, card);
                Shp(c, 14, 14, 18, 18, 4, ink, .35);
                Shp(c, 38, 16, 44, 4, 2, ink, .8); Shp(c, 38, 25, 30, 3, 1.5, ink, .45);
                Shp(c, 14, 40, 84, 3, 1.5, trk); Shp(c, 14, 40, 34, 3, 1.5, acc);
                Shp(c, 45, 49, 6, 6, 0, ink, .7, true); Shp(c, 53, 48, 8, 8, 0, ink, .9, true); Shp(c, 63, 49, 6, 6, 0, ink, .7, true);
                break;
            case "Compact":
                Shp(c, 6, 18, 100, 32, 8, card);
                Shp(c, 12, 24, 18, 18, 4, ink, .35);
                Shp(c, 36, 26, 36, 4, 2, ink, .8); Shp(c, 36, 34, 24, 3, 1.5, ink, .45);
                Shp(c, 84, 28, 6, 6, 0, ink, .7, true); Shp(c, 92, 27, 8, 8, 0, ink, .9, true);
                Shp(c, 12, 45, 88, 2, 1, trk); Shp(c, 12, 45, 36, 2, 1, acc);
                break;
            case "Pill":
                Shp(c, 10, 22, 92, 26, 13, card);
                Shp(c, 22, 29, 40, 4, 2, ink, .8); Shp(c, 22, 37, 26, 3, 1.5, ink, .45);
                Shp(c, 78, 27, 16, 16, 0, acc, 1, true);
                break;
            case "Cover":
                Shp(c, 28, 2, 56, 64, 9, card);
                Shp(c, 34, 8, 44, 30, 5, ink, .35);
                Shp(c, 40, 43, 32, 3, 1.5, ink, .8);
                Shp(c, 34, 51, 44, 2, 1, trk); Shp(c, 34, 51, 18, 2, 1, acc);
                Shp(c, 50, 56, 12, 8, 0, acc, 1, true);
                break;
            case "Island":
                Shp(c, 20, 4, 72, 20, 10, card);
                Shp(c, 25, 8, 12, 12, 0, ink, .4, true);
                Shp(c, 43, 12, 24, 3, 1.5, ink, .8);
                Shp(c, 74, 8, 12, 12, 0, acc, 1, true);
                Shp(c, 14, 30, 84, 34, 8, card, .45);
                Shp(c, 22, 38, 40, 3, 1.5, ink, .5); Shp(c, 22, 48, 68, 2, 1, trk); Shp(c, 22, 48, 28, 2, 1, acc);
                break;
        }
    }

    private FrameworkElement MakeTile(string id, string name)
    {
        var canvas = new Canvas { Width = 112, Height = 68 };
        Draw(canvas, id);

        var box = new Border { Width = 128, Height = 82, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), Child = canvas };
        Bind(box, Border.BackgroundProperty, "SolidBgBrush");

        var label = new TextBlock { Text = name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        wrap.Children.Add(box);
        wrap.Children.Add(label);
        wrap.MouseLeftButtonUp += (_, _) => App.SetStyle(id);

        _tiles.Add((id, box));
        return wrap;
    }

    private void RefreshTiles()
    {
        string cur = FlyoutStyles.Normalize(App.Settings.FlyoutStyle);
        foreach (var (id, tile) in _tiles)
            Bind(tile, Border.BorderBrushProperty, id == cur ? "AccentBrush" : "SolidBorderBrush");
    }

    // ================= Yükle / topla =================

    private void Load()
    {
        _loading = true;
        var s = App.Settings;

        _enabled.IsChecked = s.Enabled;
        _autoStart.IsChecked = s.StartWithWindows;
        _portable.IsChecked = SettingsStore.IsPortable;
        _pathText.Text = SettingsStore.FilePath;

        _track.IsChecked = s.ShowOnTrackChange;
        _play.IsChecked = s.ShowOnPlayPause;
        _fullscreen.IsChecked = s.HideInFullscreen;
        _paused.IsChecked = s.AlwaysHideWhenPaused;

        _themeDark.IsChecked = s.Theme != "Light" && s.Theme != "Midnight";
        _themeLight.IsChecked = s.Theme == "Light";
        _themeMid.IsChecked = s.Theme == "Midnight";
        _opacity.Value = Math.Round(s.BackgroundOpacity * 100);
        _blur.IsChecked = s.BlurEnabled;
        _scale.Value = Math.Round(s.Scale * 100);
        _anim.IsChecked = s.Animations;

        foreach (var r in _posRadios) r.IsChecked = s.CustomX == null && (string)r.Tag! == s.Position;

        _art.IsChecked = s.ShowAlbumArt;
        _prog.IsChecked = s.ShowProgress;
        _ctrl.IsChecked = s.ShowControls;
        _vol.IsChecked = s.ShowVolumeControls;
        _badge.IsChecked = s.ShowPlatformBadge;

        _opacityText.Text = $"{_opacity.Value:0}%";
        _scaleText.Text = $"{_scale.Value:0}%";
        RefreshAccents();
        RefreshTiles();
        UpdateHotkeyText();
        UpdateHotkeyStatus(null);
        _loading = false;
    }

    private void Collect()
    {
        var s = App.Settings;

        s.Enabled = _enabled.IsChecked == true;
        bool autoStart = _autoStart.IsChecked == true;
        if (autoStart != s.StartWithWindows) AutoStart.Set(autoStart);
        s.StartWithWindows = autoStart;

        s.ShowOnTrackChange = _track.IsChecked == true;
        s.ShowOnPlayPause = _play.IsChecked == true;
        s.HideInFullscreen = _fullscreen.IsChecked == true;
        s.AlwaysHideWhenPaused = _paused.IsChecked == true;

        s.Theme = _themeLight.IsChecked == true ? "Light" : _themeMid.IsChecked == true ? "Midnight" : "Dark";
        s.BackgroundOpacity = _opacity.Value / 100.0;
        s.BlurEnabled = _blur.IsChecked == true;
        s.Scale = Math.Round(_scale.Value) / 100.0;
        s.Animations = _anim.IsChecked == true;

        var pos = _posRadios.FirstOrDefault(r => r.IsChecked == true);
        if (pos != null) s.Position = (string)pos.Tag!;

        s.ShowAlbumArt = _art.IsChecked == true;
        s.ShowProgress = _prog.IsChecked == true;
        s.ShowControls = _ctrl.IsChecked == true;
        s.ShowVolumeControls = _vol.IsChecked == true;
        s.ShowPlatformBadge = _badge.IsChecked == true;

        _opacityText.Text = $"{_opacity.Value:0}%";
        _scaleText.Text = $"{_scale.Value:0}%";

        SettingsStore.Save(s);
        App.ApplyAll();
        ApplyTitleBar();
    }

    private void OnChanged(object sender, RoutedEventArgs e) { if (!_loading) Collect(); }
    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Collect(); }

    private void OnPosition(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.CustomX = null;     // hazır konum seçilince sürükleyerek belirlenen konum silinir
        App.Settings.CustomY = null;
        Collect();
    }

    private void OnPortable(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool want = _portable.IsChecked == true;
        if (!SettingsStore.SetPortable(want, App.Settings))
        {
            _loading = true;
            _portable.IsChecked = !want;
            _loading = false;
            MessageBox.Show("Bu klasöre yazılamadı. Taşınabilir mod için uygulamayı yazılabilir bir klasöre (ör. Masaüstü, USB bellek) koyun.",
                "QuickMedia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _pathText.Text = SettingsStore.FilePath;
    }

    // ================= Vurgu rengi =================

    private void BuildAccents()
    {
        foreach (var hex in Accents)
        {
            var b = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = hex,
            };
            b.MouseLeftButtonUp += (_, _) =>
            {
                App.Settings.AccentColor = hex;
                RefreshAccents();
                Collect();
            };
            _accentPanel.Children.Add(b);
        }
    }

    private void RefreshAccents()
    {
        foreach (Border b in _accentPanel.Children)
        {
            bool sel = string.Equals((string)b.Tag, App.Settings.AccentColor, StringComparison.OrdinalIgnoreCase);
            if (sel) b.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            else b.BorderBrush = Brushes.Transparent;
        }
    }

    // ================= Platform kuralları =================

    private void BuildPlatforms()
    {
        _platformHost.Children.Clear();
        var rows = new List<UIElement>();

        foreach (var p in Platforms.All)
        {
            var rule = App.Settings.GetRule(p.Id);

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            var minus = new Button { Content = "−", Width = 28, Padding = new Thickness(0, 3, 0, 3), Style = Res("FluentButton") };
            var plus = new Button { Content = "+", Width = 28, Padding = new Thickness(0, 3, 0, 3), Style = Res("FluentButton") };
            var secText = new TextBlock { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var stepper = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { minus, secText, plus } };

            void Refresh()
            {
                secText.Text = $"{rule.Seconds} sn";
                stepper.Visibility = rule.Mode == VisibilityMode.Timed ? Visibility.Visible : Visibility.Hidden;
            }

            void AddRadio(string text, VisibilityMode mode)
            {
                var rb = new RadioButton { Content = text, GroupName = "rule_" + p.Id, Style = Res("Seg"), IsChecked = rule.Mode == mode };
                rb.Click += (_, _) => { rule.Mode = mode; Refresh(); SettingsStore.Save(App.Settings); };
                seg.Children.Add(rb);
            }
            AddRadio("Hep göster", VisibilityMode.Always);
            AddRadio("Süreli", VisibilityMode.Timed);
            AddRadio("Gizle", VisibilityMode.Never);

            void Step(int dir)
            {
                int idx = Array.FindIndex(Steps, v => v >= rule.Seconds);
                if (idx < 0) idx = Steps.Length - 1;
                rule.Seconds = Steps[Math.Clamp(idx + dir, 0, Steps.Length - 1)];
                Refresh();
                SettingsStore.Save(App.Settings);
            }
            minus.Click += (_, _) => Step(-1);
            plus.Click += (_, _) => Step(+1);
            Refresh();

            var ctrl = new StackPanel { Orientation = Orientation.Horizontal, Children = { SegHost(seg), stepper } };
            rows.Add(Row(p.Name, p.Kind, ctrl));
        }
        _platformHost.Children.Add(Group(rows.ToArray()));
    }

    // ================= Kısayollar =================

    private static readonly (HotkeyAction A, string Title)[] HotkeyRows =
    [
        (HotkeyAction.Toggle, "Flyout'u göster / gizle"),
        (HotkeyAction.PlayPause, "Oynat / duraklat"),
        (HotkeyAction.Next, "Sonraki şarkı"),
        (HotkeyAction.Previous, "Önceki şarkı"),
    ];

    private static string TitleOf(HotkeyAction a) => HotkeyRows.First(r => r.A == a).Title;

    private void UpdateHotkeyText()
    {
        foreach (var (a, b) in _hotkeyBtns)
            b.Content = _capAction == a ? "Tuşlara basın…  (Esc: iptal)" : HotkeyService.Describe(a);
    }

    private void UpdateHotkeyStatus(string? msg)
    {
        if (msg != null) { _hotkeyStatus.Text = msg; return; }

        var failed = HotkeyRows.Where(r => App.Hotkey.ModeOf(r.A) == HotkeyMode.None).Select(r => r.Title).ToList();
        var compat = HotkeyRows.Where(r => App.Hotkey.ModeOf(r.A) == HotkeyMode.Compat).Select(r => r.Title).ToList();

        if (failed.Count > 0)
            _hotkeyStatus.Text = "Kaydedilemeyen kısayol: " + string.Join(", ", failed) + ". Farklı bir kombinasyon deneyin.";
        else if (compat.Count > 0)
            _hotkeyStatus.Text = "Tüm kısayollar etkin. (" + string.Join(", ", compat) + ": uyumluluk modu — başka bir uygulama da aynı kombinasyonu kullanıyor olabilir.)";
        else
            _hotkeyStatus.Text = "Tüm kısayollar etkin.";
    }

    private void BeginCapture(HotkeyAction a)
    {
        if (_capAction != null) return;
        _capAction = a;
        App.Hotkey.UnregisterAll();       // mevcut kısayollar yakalamayı engellemesin
        UpdateHotkeyStatus("");
        UpdateHotkeyText();
        _hotkeyBtns[a].Focus();
    }

    private void EndCapture()
    {
        _capAction = null;
        App.Hotkey.RegisterAllFromSettings();
        UpdateHotkeyText();
        UpdateHotkeyStatus(null);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capAction is not HotkeyAction cap) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (key == Key.Escape) { EndCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                 or Key.LWin or Key.RWin or Key.System or Key.None or Key.DeadCharProcessed) return;

        var mods = Keyboard.Modifiers;
        if (!mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt) && !mods.HasFlag(ModifierKeys.Windows))
        {
            UpdateHotkeyStatus("En az bir Ctrl, Alt veya Win tuşunu da basılı tutun.");
            return;
        }

        string ks = key.ToString();
        foreach (HotkeyAction other in Enum.GetValues(typeof(HotkeyAction)))
        {
            if (other == cap) continue;
            var b = App.Settings.GetHotkey(other);
            if (b.Modifiers == (int)mods && b.Key == ks)
            {
                UpdateHotkeyStatus($"Bu kombinasyon zaten '{TitleOf(other)}' için kullanılıyor.");
                return;
            }
        }

        var bind = App.Settings.GetHotkey(cap);
        bind.Modifiers = (int)mods;
        bind.Key = ks;
        SettingsStore.Save(App.Settings);

        _capAction = null;
        App.Hotkey.RegisterAllFromSettings();
        UpdateHotkeyText();
        UpdateHotkeyStatus(App.Hotkey.ModeOf(cap) switch
        {
            HotkeyMode.Native => "Kısayol kaydedildi ve etkin.",
            HotkeyMode.Compat => "Kısayol kaydedildi (uyumluluk modu): başka bir uygulama da bu kombinasyonu kullanıyor olabilir.",
            _ => "Kısayol kaydedilemedi, başka bir kombinasyon deneyin."
        });
    }

    private void ResetOneHotkey(HotkeyAction a)
    {
        if (_capAction != null) return;
        var d = AppSettings.DefaultHotkey(a);
        var b = App.Settings.GetHotkey(a);
        b.Modifiers = d.Modifiers;
        b.Key = d.Key;
        SettingsStore.Save(App.Settings);
        App.Hotkey.RegisterAllFromSettings();
        UpdateHotkeyText();
        UpdateHotkeyStatus(null);
    }

    private void ResetAllHotkeys()
    {
        if (_capAction != null) return;
        App.Settings.ResetHotkeys();
        SettingsStore.Save(App.Settings);
        App.Hotkey.RegisterAllFromSettings();
        UpdateHotkeyText();
        UpdateHotkeyStatus(null);
    }
}
