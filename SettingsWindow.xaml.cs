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
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace QuickMedia;

public partial class SettingsWindow : Window
{
    private bool _loading = true;     // kurulum sırasında tetiklenen olayları yoksay
    private HotkeyAction? _capAction;

    // "#8AB4F8" eski varsayılan olduğu için seçenekler arasında yok (boş = temanın rengi)
    private static readonly string[] Accents = ["#4C8DF6", "#81C995", "#F28B82", "#FDD663", "#C58AF9", "#78D9EC"];
    private static readonly int[] Steps = [1, 2, 3, 4, 5, 8, 10, 15, 20, 30, 60];

    private readonly Dictionary<string, FrameworkElement> _pages = new();

    // Denetimler
    private CheckBox _enabled = null!, _autoStart = null!, _portable = null!, _pinned = null!, _pinBtn = null!;
    private CheckBox _track = null!, _play = null!, _fullscreen = null!, _paused = null!;
    private CheckBox _blur = null!, _anim = null!, _art = null!, _prog = null!, _ctrl = null!, _vol = null!, _badge = null!;
    private Slider _opacity = null!, _scale = null!;
    private CheckBox _tbOn = null!, _tbSuppress = null!, _tbAuto = null!, _tbProgress = null!;
    private Slider _tbWidth = null!, _tbOffset = null!;
    private TextBlock _tbWidthText = null!, _tbOffsetText = null!;
    private TextBlock _opacityText = null!, _scaleText = null!, _pathText = null!, _hotkeyStatus = null!;
    private StackPanel _accentPanel = null!, _platformHost = null!;
    private readonly List<RadioButton> _posRadios = new();
    private readonly List<(string Id, Border Tile)> _tiles = new();
    private readonly List<(string Id, Border Tile)> _themeTiles = new();
    private readonly Dictionary<HotkeyAction, Button> _hotkeyBtns = new();

    private string _page = "General";
    public string CurrentPage => _page;

    public SettingsWindow(string startPage = "General")
    {
        _page = startPage;
        InitializeComponent();

        try
        {
            var logo = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
            Icon = logo;
            BrandHost.Children.Insert(0, new Image { Source = logo, Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0) });
        }
        catch (Exception ex) { Log.Write("Ayarlar logosu yüklenemedi: " + ex.Message); }

        BrandSub.Text = L.T("Medya flyout");
        SourceInitialized += (_, _) => ApplyTitleBar();
        App.Applied += OnApplied;
        App.Media.Changed += OnMedia;
        Closed += (_, _) => { App.Applied -= OnApplied; App.Media.Changed -= OnMedia; if (_capAction != null) EndCapture(); };

        Step("Gezinme", BuildNav);
        Step("Denetimler", InitControls);
        BuildPages();
        Step("Alt bilgi", BuildFooter);
        Step("Yükleme", Load);
        Step("Sayfa", () => ShowPage(_page));
    }

    private static void Step(string name, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Write($"Ayarlar penceresi ({name}) hatası: {ex}"); }
    }

    private void OnApplied()
    {
        RefreshTiles();
        RefreshAccents();
        bool was = _loading;
        _loading = true;
        if (_pinned != null) _pinned.IsChecked = App.Settings.Pinned;
        UpdatePinStatus();
        _loading = was;
        UpdateGlow();
        RefreshNowPlaying();
    }

    private void OnMedia(MediaInfo i, MediaEvent e) => Dispatcher.BeginInvoke(RefreshNowPlaying);

    private void UpdateGlow()
    {
        try
        {
            var a = ThemeManager.AccentOf(App.Settings);
            GlowA.Color = Color.FromArgb(0x30, a.R, a.G, a.B);
            GlowB.Color = Color.FromArgb(0x00, a.R, a.G, a.B);
        }
        catch { }
    }

    private void ApplyTitleBar()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            var th = ThemeManager.Get(App.Settings.Theme);
            Native.SetDarkTitleBar(h, th.Dark);
            Native.SetCaption(h, C(th.SBg), C(th.Text));
        }
        catch { }
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
        var r = new RadioButton { Content = L.T(text), GroupName = group, Tag = tag, Style = Res("Seg") };
        r.Click += click;
        return r;
    }

    private Border SegHost(UIElement content)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = content };
        Bind(b, Border.BackgroundProperty, "SolidBgBrush");
        return b;
    }

    private Border RowCore(string title, string? desc, UIElement control, UIElement icon)
    {
        var g = new Grid { Margin = new Thickness(14, 11, 16, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fi = (FrameworkElement)icon;
        fi.HorizontalAlignment = HorizontalAlignment.Left;
        fi.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(fi);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(new TextBlock { Text = L.T(title), FontWeight = FontWeights.SemiBold });
        if (desc != null)
            left.Children.Add(new TextBlock { Text = L.T(desc), Style = Res("Hint"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(left, 1);
        g.Children.Add(left);

        var fe = (FrameworkElement)control;
        fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(fe, 2);
        g.Children.Add(fe);

        return new Border { Style = Res("RowHover"), CornerRadius = new CornerRadius(8), Child = g };
    }

    /// <summary>Sade simgeli, üzerine gelince hafifçe vurgulanan ayar satırı.</summary>
    private Border Row(string title, string? desc, UIElement control, string glyph = "\uE713")
    {
        var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16 };
        Bind(icon, TextBlock.ForegroundProperty, "Text2Brush");
        return RowCore(title, desc, control, icon);
    }

    /// <summary>Platform satırı: markanın kendi simgesiyle.</summary>
    private Border RowIcon(string title, string? desc, UIElement control, UIElement icon) => RowCore(title, desc, control, icon);

    private Border Group(params UIElement[] rows)
    {
        var sp = new StackPanel { Margin = new Thickness(4) };
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(52, 1, 12, 1), Opacity = 0.6 };
                Bind(sep, Border.BackgroundProperty, "SolidBorderBrush");
                sp.Children.Add(sep);
            }
            sp.Children.Add(rows[i]);
        }
        var b = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 24), Child = sp };
        Bind(b, Border.BackgroundProperty, "SolidSurfaceBrush");
        return b;
    }

    private StackPanel PageHeader(string title, string subtitle)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 26) };
        sp.Children.Add(new TextBlock { Text = L.T(title), FontSize = 26, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = L.T(subtitle), Style = Res("Hint"), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        return sp;
    }

    private StackPanel Section(string t)
    {
        var bar = new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0) };
        Bind(bar, Border.BackgroundProperty, "AccentBrush");
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 12) };
        sp.Children.Add(bar);
        sp.Children.Add(new TextBlock { Text = L.T(t), FontWeight = FontWeights.SemiBold, FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    private TextBlock Note(string t) => new() { Text = L.T(t), Style = Res("Hint"), Margin = new Thickness(6, 0, 0, 20) };

    private ScrollViewer Page(params UIElement[] kids)
    {
        var sp = new StackPanel { Margin = new Thickness(44, 34, 44, 28), MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var k in kids) sp.Children.Add(k);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    private Slider Sld(double min, double max)
    {
        var s = new Slider { Minimum = min, Maximum = max, Width = 170, Style = Res("FluentSlider"), VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += OnSlider;
        return s;
    }

    private Button Btn(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = L.T(text), Style = Res("FluentButton"), HorizontalAlignment = HorizontalAlignment.Left };
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
            ("Taskbar", "\uE7C4", "Görev çubuğu"),
            ("Platforms", "\uE71D", "Platformlar"),
            ("Hotkey", "\uE765", "Kısayollar"),
            ("About", "\uE946", "Hakkında"),
        };
        foreach (var (key, glyph, label) in items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15, Width = 28, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = L.T(label), VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Content = sp, GroupName = "nav", Tag = key, Style = Res("Nav"), IsChecked = key == _page };
            rb.Click += (_, _) => ShowPage(key);
            NavHost.Children.Add(rb);
        }
    }

    private TextBlock _pinStatus = null!;

    private void BuildFooter()
    {
        BuildNowPlaying();
        _pinStatus = new TextBlock { Style = Res("Hint"), Margin = new Thickness(4, 0, 0, 10) };
        Bind(_pinStatus, TextBlock.ForegroundProperty, "AccentBrush");
        FooterHost.Children.Add(_pinStatus);
        UpdatePinStatus();

        var preview = Btn("Önizleme göster", (_, _) => App.Flyout.Preview());
        preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        FooterHost.Children.Add(preview);
        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";
        FooterHost.Children.Add(new TextBlock { Text = L.F("Sürüm {0}", ver), Style = Res("Hint"), Margin = new Thickness(4, 10, 0, 0) });
    }

    private Border _npCard = null!, _npArt = null!;
    private TextBlock _npTitle = null!, _npSub = null!, _npLabel = null!;

    /// <summary>Sol alttaki canlı "şu an çalıyor" kartı.</summary>
    private void BuildNowPlaying()
    {
        _npLabel = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 8) };
        _npArt = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 0) };
        _npTitle = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 116 };
        _npSub = new TextBlock { Style = Res("Hint"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, MaxWidth = 116 };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_npTitle);
        info.Children.Add(_npSub);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_npArt);
        row.Children.Add(info);
        var inner = new StackPanel();
        inner.Children.Add(_npLabel);
        inner.Children.Add(row);
        _npCard = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = inner };
        Bind(_npCard, Border.BackgroundProperty, "SolidBgBrush");
        FooterHost.Children.Add(_npCard);
        RefreshNowPlaying();
    }

    private void RefreshNowPlaying()
    {
        if (_npCard == null) return;
        var m = App.Displayed;
        _npLabel.Text = L.T("ŞU AN ÇALIYOR");
        if (!m.HasSession)
        {
            _npTitle.Text = L.T("Şu an çalan bir şey yok");
            _npTitle.FontWeight = FontWeights.Normal;
            _npSub.Text = "";
            _npArt.Child = null;
            Bind(_npArt, Border.BackgroundProperty, "SolidHoverBrush");
            return;
        }
        _npTitle.FontWeight = FontWeights.SemiBold;
        _npTitle.Text = m.Title;
        _npSub.Text = string.IsNullOrEmpty(m.Artist) ? m.Album : m.Artist;
        if (m.Art != null)
        {
            _npArt.Child = null;
            _npArt.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
        }
        else
        {
            Bind(_npArt, Border.BackgroundProperty, "SolidHoverBrush");
            var icon = PlatformIcons.Create(m.PlatformId, 24);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            _npArt.Child = icon;
        }
    }

    private void UpdatePinStatus()
    {
        if (_pinStatus != null) _pinStatus.Text = App.Settings.Pinned ? "📌 " + L.T("Üstte sabit") : "";
    }

    /// <summary>Dışarıdan (ör. görev çubuğu menüsü) belirli bir sayfaya geç.</summary>
    public void GoTo(string key)
    {
        foreach (var rb in NavHost.Children.OfType<RadioButton>())
            rb.IsChecked = (string?)rb.Tag == key;
        ShowPage(key);
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var p)) return;
        _page = key;
        PageHost.Content = p;
        p.Opacity = 0;
        var slide = new TranslateTransform(0, 14);
        p.RenderTransform = slide;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        p.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    // ================= Sayfalar =================

    // Tüm denetimler önce oluşturulur; bir sayfa bozulsa bile alanlar null kalmaz.
    private void InitControls()
    {
        _enabled = Sw(); _autoStart = Sw(); _portable = Sw(OnPortable); _pinned = Sw(OnPinned); _pinBtn = Sw();
        _pathText = new TextBlock { Style = Res("Hint"), TextAlignment = TextAlignment.Right, MaxWidth = 280, VerticalAlignment = VerticalAlignment.Center };

        _track = Sw(); _play = Sw(); _fullscreen = Sw(); _paused = Sw();

        _accentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        BuildAccents();

        _opacity = Sld(30, 100); _opacityText = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _scale = Sld(70, 160); _scaleText = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _blur = Sw(); _anim = Sw();
        _art = Sw(); _prog = Sw(); _ctrl = Sw(); _vol = Sw(); _badge = Sw();

        _tbOn = Sw(); _tbSuppress = Sw(); _tbAuto = Sw(); _tbProgress = Sw();
        _tbWidth = Sld(160, 420); _tbWidthText = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _tbOffset = Sld(0, 800); _tbOffsetText = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        foreach (var (tag, text) in new[] { ("TopLeft", "Üst sol"), ("TopCenter", "Üst orta"), ("TopRight", "Üst sağ"),
                                            ("BottomLeft", "Alt sol"), ("BottomCenter", "Alt orta"), ("BottomRight", "Alt sağ") })
            _posRadios.Add(Seg(text, "pos", tag, OnPosition));

        _platformHost = new StackPanel();

        foreach (var r in HotkeyRows)
        {
            var action = r.A;
            var btn = new Button { Style = Res("FluentButton"), MinWidth = 170, Padding = new Thickness(14, 8, 14, 8), FontWeight = FontWeights.SemiBold };
            btn.Click += (_, _) => BeginCapture(action);
            _hotkeyBtns[action] = btn;
        }
        _hotkeyStatus = new TextBlock { Style = Res("Hint"), Margin = new Thickness(6, 0, 0, 16) };
    }

    private void BuildPages()
    {
        var builders = new (string Key, Func<FrameworkElement> Build)[]
        {
            ("General", PageGeneral), ("Appearance", PageAppearance), ("Visibility", PageVisibility),
            ("Taskbar", PageTaskbar), ("Platforms", PagePlatforms), ("Hotkey", PageHotkey), ("About", PageAbout),
        };
        foreach (var (key, build) in builders)
        {
            try { _pages[key] = build(); }
            catch (Exception ex)
            {
                Log.Write($"Ayarlar sayfası '{key}' oluşturulamadı: {ex}");
                _pages[key] = new ScrollViewer
                {
                    Content = new TextBlock { Text = L.T("Bu sayfa yüklenemedi:") + "\n\n" + ex.Message + "\n\n" + L.T("Ayrıntı:") + " " + Log.FilePath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(36) }
                };
            }
        }
    }

    private FrameworkElement PageGeneral()
    {
        var openFolder = Btn("Klasörü aç", (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Dir}\"") { UseShellExecute = true }); } catch { }
        });

        string lang = App.Settings.Language;
        var langAuto = Seg("Otomatik", "lang", "auto", OnLanguage);
        var langTr = Seg("Türkçe", "lang", "tr", OnLanguage);
        var langEn = Seg("English", "lang", "en", OnLanguage);
        langAuto.IsChecked = lang is not ("tr" or "en");
        langTr.IsChecked = lang == "tr";
        langEn.IsChecked = lang == "en";
        var langRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { langAuto, langTr, langEn } };

        return Page(
            PageHeader("Genel", "Uygulamanın temel davranışı."),
            Group(
                Row("Flyout etkin", "Kapalıyken flyout hiçbir koşulda görünmez.", _enabled, "\uE7E8"),
                Row("Üstte sabitle", "Medya çalarken flyout kapanmaz ve her zaman diğer pencerelerin üstünde kalır. Kapalıyken normal kurallar (platform ayarları) geçerlidir.", _pinned, "\uE718"),
                Row("Sabitleme düğmesini göster", "Flyout'un sağ üst köşesinde, üstüne gelince beliren küçük iğne düğmesi.", _pinBtn, "\uE718")),
            Group(
                Row("Dil", "Arayüz dili. 'Otomatik' Windows diline uyar.", SegHost(langRow), "\uE774"),
                Row("Windows ile başlat", "Oturum açıldığında QuickMedia otomatik çalışsın.", _autoStart, "\uE768"),
                Row("Taşınabilir mod", "Ayarlar exe'nin yanındaki settings.json'da tutulur; klasörü USB belleğe kopyalayıp her yerde çalıştırabilirsiniz.", _portable, "\uE74E")),
            Section("AYAR DOSYASI"),
            Group(Row("Konum", null, new StackPanel { Orientation = Orientation.Horizontal, Children = { _pathText, new Border { Width = 10 }, openFolder } }, "\uE838")));
    }

    private FrameworkElement PageAppearance()
    {
        var tiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (id, name) in FlyoutStyles.All) tiles.Children.Add(MakeTile(id, name));

        var themeTiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var t in ThemeManager.All) themeTiles.Children.Add(MakeThemeTile(t));

        var posGrid = new UniformGrid { Columns = 3 };
        foreach (var r in _posRadios) posGrid.Children.Add(r);

        return Page(
            PageHeader("Stil ve görünüm", "Flyout'un şeklini, rengini ve yerini seçin."),
            Section("STİL"),
            tiles,
            Note("Stile tıklayınca önizleme açılır. Flyout üzerinde sağ tık sıradaki stile geçer; tray menüsünde de 'Stil' var. Plak, Kare kapak ve Island stillerinde kontroller fareyle üstüne gelince çıkar. Flyout'u istediğiniz yere sürükleyin; konum her stil için ayrı hatırlanır."),
            Section("TEMA"),
            themeTiles,
            Note("Her temanın kendi vurgu rengi vardır; aşağıdan istediğiniz rengi seçebilirsiniz."),
            Group(
                Row("Vurgu rengi", "'A' temanın kendi rengi. Varsayılan Dark Glass: #8AB4F8", _accentPanel, "\uE790"),
                Row("Arka plan opaklığı", "Varsayılan %80 (#CC171B24).", new StackPanel { Orientation = Orientation.Horizontal, Children = { _opacity, _opacityText } }, "\uE706"),
                Row("Arka plan bulanıklığı", "Masaüstü duvar kâğıdını hafifçe bulanıklaştırır.", _blur, "\uE7B3")),
            Section("BOYUT VE KONUM"),
            Group(
                Row("Boyut", "Flyout üzerindeyken Ctrl + fare tekerleği ile de değişir.", new StackPanel { Orientation = Orientation.Horizontal, Children = { _scale, _scaleText } }, "\uE71E"),
                Row("Konum", "Hazır bir konum seçin ya da flyout'u fareyle sürükleyin.", SegHost(posGrid), "\uE707")),
            Section("ÖĞELER"),
            Group(
                Row("Albüm kapağı", null, _art, "\uE91B"),
                Row("İlerleme çubuğu", "Çubuğa tıklayıp sürükleyerek şarkıda istediğiniz yere atlayabilirsiniz.", _prog, "\uE713"),
                Row("Oynat / önceki / sonraki", null, _ctrl, "\uE768"),
                Row("Uygulama sesi", "Yalnızca çalan uygulamanın ses seviyesini ve sessize almasını yönetir (Windows ana sesine dokunmaz).", _vol, "\uE767"),
                Row("Platform etiketi", null, _badge, "\uE8EC"),
                Row("Açılış / kapanış animasyonu", null, _anim, "\uE713")));
    }

    private FrameworkElement PageVisibility() => Page(
        PageHeader("Görünürlük", "Flyout'un ne zaman açılıp kapanacağı."),
        Group(
            Row("Parça değişince göster", "Yeni şarkı / video başladığında flyout açılır.", _track, "\uE8D6"),
            Row("Oynat / duraklat olunca göster", "Oynatma durumu değiştiğinde flyout açılır.", _play, "\uE768"),
            Row("Tam ekranda gizle", "Oyun, tam ekran video veya sunum sırasında otomatik gösterme yapılmaz. Kısayol yine çalışır.", _fullscreen, "\uE740"),
            Row("Duraklatılınca gizle", "'Hep göster' modundaki platformlarda medya duraklatılırsa flyout kapanır.", _paused, "\uE769")),
        Note("Her platform için süre ve davranışı 'Platformlar' sayfasından ayarlayabilirsiniz. 'Gizle' seçtiğiniz kaynaklar flyout'ta hiçbir şekilde görünmez."),
        Btn("Önizleme göster", (_, _) => App.Flyout.Preview()));

    private FrameworkElement PageTaskbar() => Page(
        PageHeader("Görev çubuğu", "QuickMedia'yı görev çubuğunun boş sol kısmında kullanın."),
        Group(
            Row("Görev çubuğunda kullan", "Çalan şarkıyı görev çubuğunun sol tarafında küçük bir kontrol olarak gösterir. Tıklayınca flyout açılır, fare tekerleği çalan uygulamanın sesini değiştirir, sağ tık menü açar.", _tbOn, "\uE7C4"),
            Row("Boş alanı otomatik bul", "Görev çubuğundaki düğmelerin yerini okuyup kontrolü sol taraftaki boş alana kendisi sığdırır (Widgets düğmesi varsa onun sağına). Kapalıyken aşağıdaki 'Soldan boşluk' kullanılır.", _tbAuto, "\uE7C4"),
            Row("İlerleme çizgisi", "Kontrolün altında şarkının ilerlemesini gösteren ince çizgi.", _tbProgress, "\uE713"),
            Row("Genişlik", "Kontrolün en fazla genişliği (otomatik modda boş alana sığacak şekilde kısalır).", new StackPanel { Orientation = Orientation.Horizontal, Children = { _tbWidth, _tbWidthText } }, "\uE71E"),
            Row("Soldan boşluk", "Yalnızca otomatik yerleştirme kapalıyken: ortadaki simgelerin üstüne binmemesi için ayarlayın. Windows 11'de Widgets (hava durumu) düğmesi açıksa onun sağına denk gelecek şekilde artırın.", new StackPanel { Orientation = Orientation.Horizontal, Children = { _tbOffset, _tbOffsetText } }, "\uE707"),
            Row("Otomatik açılan flyout'u kapat", "Görev çubuğu kontrolü açıkken flyout kendiliğinden açılmaz; yalnızca kontrole tıklayınca ya da kısayolla açılır.", _tbSuppress, "\uE7B3")),
        Note("Yalnızca alttaki veya üstteki görev çubuğunu destekler. Çalan bir şey yokken ya da tam ekran uygulamada otomatik gizlenir. Beğenmezseniz yukarıdaki anahtarı kapatmanız yeterli."));

    private FrameworkElement PagePlatforms()
    {
        BuildPlatforms();
        return Page(
            PageHeader("Platformlar", "Her kaynak için ayrı davranış."),
            Note("Hep göster: medya varken sürekli açık kalır. Süreli: tetiklenince seçilen saniye kadar görünür. Gizle: bu kaynak flyout'ta hiç görünmez, bilgisi bile gösterilmez. Listede olmayan kaynaklar 'Diğer' kuralını kullanır."),
            _platformHost,
            Btn("Tümünü varsayılana döndür", (_, _) =>
            {
                App.Settings.ResetRules();
                SettingsStore.Save(App.Settings);
                BuildPlatforms();
                App.ApplyAll();
            }));
    }

    private FrameworkElement PageHotkey()
    {
        var hkRows = new List<UIElement>();
        foreach (var r in HotkeyRows)
        {
            var action = r.A;
            var reset = Btn("Sıfırla", (_, _) => ResetOneHotkey(action));
            reset.Margin = new Thickness(8, 0, 0, 0);
            string def = "Varsayılan: " + HotkeyService.Describe(AppSettings.DefaultHotkey(action));
            hkRows.Add(Row(r.Title, def, new StackPanel { Orientation = Orientation.Horizontal, Children = { _hotkeyBtns[action], reset } }, "\uE765"));
        }
        return Page(
            PageHeader("Kısayollar", "Herhangi bir uygulama açıkken çalışır."),
            Note("Düğmeye tıklayıp yeni kombinasyona basın (en az bir Ctrl, Alt veya Win ile)."),
            Group(hkRows.ToArray()),
            _hotkeyStatus,
            Btn("Tümünü varsayılana döndür", (_, _) => ResetAllHotkeys()));
    }

    private FrameworkElement PageAbout()
    {
        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";
        return Page(
            PageHeader("Hakkında", "QuickMedia " + ver),
            Group(
                Row("Nasıl çalışır?", "Windows'un küresel medya kontrollerini dinler. Spotify, YouTube, Netflix, X, Instagram gibi kaynaklar tanınır; tanınmayanlar 'Diğer' kuralını kullanır. Web siteleri adres ve tarayıcı pencere başlığına bakılarak tahmin edilir.", new Border(), "\uE946")),
            Group(Row("Günlük dosyası", "Bir sorun olursa buradaki log.txt'ye bakın.", Btn("Aç", (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Dir}\"") { UseShellExecute = true }); } catch { }
            }), "\uE838")));
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
            case "Wide":
                Shp(c, 2, 18, 108, 32, 8, card);
                Shp(c, 8, 24, 18, 18, 4, ink, .35);
                Shp(c, 32, 26, 36, 4, 2, ink, .8); Shp(c, 32, 34, 24, 3, 1.5, ink, .45);
                Shp(c, 74, 28, 6, 6, 0, ink, .7, true); Shp(c, 82, 27, 8, 8, 0, acc, 1, true); Shp(c, 92, 28, 6, 6, 0, ink, .7, true);
                Shp(c, 8, 45, 96, 2, 1, trk); Shp(c, 8, 45, 40, 2, 1, acc);
                break;
            case "Tile":
                Shp(c, 32, 3, 48, 62, 8, card);
                Shp(c, 37, 8, 38, 38, 5, ink, .35);
                Shp(c, 42, 51, 28, 3, 1.5, ink, .8);
                Shp(c, 37, 58, 38, 2, 1, trk); Shp(c, 37, 58, 16, 2, 1, acc);
                break;
            case "Text":
                Shp(c, 16, 22, 76, 7, 3.5, ink, .9);
                Shp(c, 16, 36, 50, 4, 2, ink, .5);
                break;
            case "Orb":
                Shp(c, 34, 2, 44, 44, 0, trk, .8, true);
                Shp(c, 37, 5, 38, 38, 0, ink, .35, true);
                Shp(c, 50, 18, 12, 12, 0, card, 1, true);
                Shp(c, 52, 0, 8, 8, 0, acc, 1, true);
                Shp(c, 36, 52, 40, 8, 4, card);
                break;
            case "MiniOrb":
                Shp(c, 42, 16, 28, 28, 0, trk, .8, true);
                Shp(c, 44, 18, 24, 24, 0, ink, .35, true);
                Shp(c, 52, 26, 8, 8, 0, card, 1, true);
                Shp(c, 52, 14, 6, 6, 0, acc, 1, true);
                break;
            case "BigOrb":
                Shp(c, 26, 0, 60, 60, 0, trk, .8, true);
                Shp(c, 29, 3, 54, 54, 0, ink, .35, true);
                Shp(c, 47, 21, 18, 18, 0, card, 1, true);
                Shp(c, 52, -2, 8, 8, 0, acc, 1, true);
                Shp(c, 34, 60, 44, 7, 3.5, card);
                break;
            case "Disc":
                Shp(c, 6, 20, 100, 28, 14, card);
                Shp(c, 12, 22, 24, 24, 0, trk, .8, true);
                Shp(c, 14, 24, 20, 20, 0, ink, .35, true);
                Shp(c, 21, 31, 6, 6, 0, card, 1, true);
                Shp(c, 42, 28, 40, 4, 2, ink, .8); Shp(c, 42, 36, 26, 3, 1.5, ink, .45);
                Shp(c, 90, 29, 10, 10, 0, acc, 1, true);
                break;
            case "Float":
                Shp(c, 34, 3, 44, 44, 10, ink, .35);
                Shp(c, 40, 41, 32, 2, 1, trk); Shp(c, 40, 41, 14, 2, 1, acc);
                Shp(c, 36, 54, 40, 8, 4, card);
                break;
            case "Poster":
                Shp(c, 12, 6, 88, 56, 8, ink, .3);
                Shp(c, 12, 40, 88, 22, 8, card, .85);
                Shp(c, 18, 46, 38, 4, 2, ink, .95); Shp(c, 18, 53, 24, 3, 1.5, ink, .55);
                Shp(c, 84, 47, 10, 10, 0, acc, 1, true);
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

        var label = new TextBlock { Text = L.T(name), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        wrap.Children.Add(box);
        wrap.Children.Add(label);
        wrap.MouseLeftButtonUp += (_, _) => App.SetStyle(id);
        Hoverize(wrap, box);

        _tiles.Add((id, box));
        return wrap;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Rect(Canvas c, double x, double y, double w, double h, double r, string color, double op = 1, bool ellipse = false)
    {
        Shape s = ellipse ? new Ellipse() : new Rectangle { RadiusX = r, RadiusY = r };
        s.Width = w; s.Height = h; s.Opacity = op; s.Fill = new SolidColorBrush(C(color));
        Canvas.SetLeft(s, x); Canvas.SetTop(s, y);
        c.Children.Add(s);
    }

    /// <summary>Temanın kendi renkleriyle çizilmiş küçük önizleme kutucuğu.</summary>
    private FrameworkElement MakeThemeTile(ThemeDef t)
    {
        var canvas = new Canvas { Width = 110, Height = 62 };
        Rect(canvas, 6, 8, 98, 40, 8, t.SSurface);
        Rect(canvas, 12, 14, 22, 22, 5, t.Accent, .35);
        Rect(canvas, 42, 16, 44, 5, 2.5, t.Text, .9);
        Rect(canvas, 42, 26, 28, 4, 2, t.Text2);
        Rect(canvas, 12, 41, 86, 3, 1.5, t.SBorder);
        Rect(canvas, 12, 41, 38, 3, 1.5, t.Accent);
        Rect(canvas, 92, 54, 8, 8, 0, t.Accent, 1, true);

        var box = new Border
        {
            Width = 118, Height = 70, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(C(t.SBg)), Child = canvas,
        };
        var label = new TextBlock { Text = t.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        wrap.Children.Add(box);
        wrap.Children.Add(label);
        Hoverize(wrap, box);
        wrap.MouseLeftButtonUp += (_, _) =>
        {
            App.Settings.Theme = t.Id;
            SettingsStore.Save(App.Settings);
            App.ApplyAll();
            ApplyTitleBar();
        };

        _themeTiles.Add((t.Id, box));
        return wrap;
    }

    private static void Hoverize(FrameworkElement hit, FrameworkElement target)
    {
        var sc = new ScaleTransform(1, 1);
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        target.RenderTransform = sc;
        void To(double v)
        {
            var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        hit.MouseEnter += (_, _) => To(1.05);
        hit.MouseLeave += (_, _) => To(1.0);
    }

    private static System.Windows.Media.Effects.DropShadowEffect SelGlow() => new()
    {
        Color = ThemeManager.AccentOf(App.Settings), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55,
    };

    private void RefreshTiles()
    {
        string cur = FlyoutStyles.Normalize(App.Settings.FlyoutStyle);
        foreach (var (id, tile) in _tiles)
        {
            Bind(tile, Border.BorderBrushProperty, id == cur ? "AccentBrush" : "SolidBorderBrush");
            tile.Effect = id == cur ? SelGlow() : null;
        }

        string th = ThemeManager.Get(App.Settings.Theme).Id;
        foreach (var (id, tile) in _themeTiles)
        {
            Bind(tile, Border.BorderBrushProperty, id == th ? "AccentBrush" : "SolidBorderBrush");
            tile.Effect = id == th ? SelGlow() : null;
        }
    }

    // ================= Yükle / topla =================

    private void Load()
    {
        _loading = true;
        var s = App.Settings;

        _enabled.IsChecked = s.Enabled;
        _pinned.IsChecked = s.Pinned;
        _pinBtn.IsChecked = s.ShowPinButton;
        _autoStart.IsChecked = s.StartWithWindows;
        _portable.IsChecked = SettingsStore.IsPortable;
        _pathText.Text = SettingsStore.FilePath;

        _track.IsChecked = s.ShowOnTrackChange;
        _play.IsChecked = s.ShowOnPlayPause;
        _fullscreen.IsChecked = s.HideInFullscreen;
        _paused.IsChecked = s.AlwaysHideWhenPaused;

        _opacity.Value = Math.Round(s.BackgroundOpacity * 100);
        _blur.IsChecked = s.BlurEnabled;
        _scale.Value = Math.Round(s.Scale * 100);
        _anim.IsChecked = s.Animations;

        foreach (var r in _posRadios) r.IsChecked = s.CurrentCustom == null && (string)r.Tag! == s.Position;

        _art.IsChecked = s.ShowAlbumArt;
        _prog.IsChecked = s.ShowProgress;
        _ctrl.IsChecked = s.ShowControls;
        _vol.IsChecked = s.ShowVolumeControls;
        _badge.IsChecked = s.ShowPlatformBadge;

        _tbOn.IsChecked = s.UseTaskbar;
        _tbSuppress.IsChecked = s.TaskbarSuppressFlyout;
        _tbAuto.IsChecked = s.TaskbarAuto;
        _tbProgress.IsChecked = s.TaskbarProgress;
        _tbWidth.Value = s.TaskbarWidth;
        _tbOffset.Value = s.TaskbarOffset;
        _tbWidthText.Text = $"{_tbWidth.Value:0} px";
        _tbOffsetText.Text = $"{_tbOffset.Value:0} px";

        _opacityText.Text = $"{_opacity.Value:0}%";
        _scaleText.Text = $"{_scale.Value:0}%";
        RefreshAccents();
        RefreshTiles();
        UpdateHotkeyText();
        UpdateHotkeyStatus(null);
        UpdateGlow();
        _loading = false;
    }

    private void Collect()
    {
        var s = App.Settings;

        s.Enabled = _enabled.IsChecked == true;
        s.ShowPinButton = _pinBtn.IsChecked == true;
        bool autoStart = _autoStart.IsChecked == true;
        if (autoStart != s.StartWithWindows) AutoStart.Set(autoStart);
        s.StartWithWindows = autoStart;

        s.ShowOnTrackChange = _track.IsChecked == true;
        s.ShowOnPlayPause = _play.IsChecked == true;
        s.HideInFullscreen = _fullscreen.IsChecked == true;
        s.AlwaysHideWhenPaused = _paused.IsChecked == true;

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

        s.UseTaskbar = _tbOn.IsChecked == true;
        s.TaskbarSuppressFlyout = _tbSuppress.IsChecked == true;
        s.TaskbarAuto = _tbAuto.IsChecked == true;
        s.TaskbarProgress = _tbProgress.IsChecked == true;
        s.TaskbarWidth = (int)Math.Round(_tbWidth.Value);
        s.TaskbarOffset = (int)Math.Round(_tbOffset.Value);
        _tbWidthText.Text = $"{_tbWidth.Value:0} px";
        _tbOffsetText.Text = $"{_tbOffset.Value:0} px";

        _opacityText.Text = $"{_opacity.Value:0}%";
        _scaleText.Text = $"{_scale.Value:0}%";

        SettingsStore.Save(s);
        App.ApplyAll();
        ApplyTitleBar();
    }

    private void OnChanged(object sender, RoutedEventArgs e) { if (!_loading) Collect(); }
    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Collect(); }

    private void OnLanguage(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.ChangeLanguage((string)((RadioButton)sender).Tag!);
    }

    private void OnPinned(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Flyout.SetPinned(_pinned.IsChecked == true);
    }

    private void OnPosition(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.ClearCustom();      // hazır konum seçilince bu stil için sürükleyerek belirlenen konum silinir
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
            MessageBox.Show(L.T("Bu klasöre yazılamadı. Taşınabilir mod için uygulamayı yazılabilir bir klasöre (ör. Masaüstü, USB bellek) koyun."),
                "QuickMedia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _pathText.Text = SettingsStore.FilePath;
    }

    // ================= Vurgu rengi =================

    private void BuildAccents()
    {
        // İlk daire: temanın kendi vurgu rengi ("A")
        var auto = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = "",
            Child = new TextBlock { Text = "A", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = L.T("Temanın kendi rengi"),
        };
        auto.MouseLeftButtonUp += (_, _) =>
        {
            App.Settings.AccentColor = "";
            RefreshAccents();
            Collect();
        };
        _accentPanel.Children.Add(auto);

        foreach (var hex in Accents)
        {
            var b = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(C(hex)),
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
        string cur = App.Settings.AccentColor ?? "";
        bool isAuto = string.IsNullOrWhiteSpace(cur) || cur.Equals("#8AB4F8", StringComparison.OrdinalIgnoreCase);

        foreach (Border b in _accentPanel.Children)
        {
            string tag = (string)b.Tag;
            bool sel = tag.Length == 0 ? isAuto : string.Equals(tag, cur, StringComparison.OrdinalIgnoreCase);
            if (tag.Length == 0) b.Background = new SolidColorBrush(C(ThemeManager.Get(App.Settings.Theme).Accent));
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
                secText.Text = $"{rule.Seconds} {L.T("sn")}";
                stepper.Visibility = rule.Mode == VisibilityMode.Timed ? Visibility.Visible : Visibility.Hidden;
            }

            void AddRadio(string text, VisibilityMode mode)
            {
                var rb = new RadioButton { Content = L.T(text), GroupName = "rule_" + p.Id, Style = Res("Seg"), IsChecked = rule.Mode == mode };
                rb.Click += (_, _) =>
                {
                    rule.Mode = mode;
                    Refresh();
                    SettingsStore.Save(App.Settings);
                    App.ApplyAll();          // gizlenen kaynak hemen flyout'tan kalksın
                };
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
            rows.Add(RowIcon(p.Name, p.Kind, ctrl, PlatformIcons.Create(p.Id, 26)));
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
        (HotkeyAction.VolumeUp, "Sesi artır (çalan uygulama)"),
        (HotkeyAction.VolumeDown, "Sesi azalt (çalan uygulama)"),
        (HotkeyAction.OpenSettings, "Ayarları aç / kapat"),
    ];

    private static string TitleOf(HotkeyAction a) => HotkeyRows.First(r => r.A == a).Title;

    private void UpdateHotkeyText()
    {
        foreach (var (a, b) in _hotkeyBtns)
            b.Content = _capAction == a ? L.T("Tuşlara basın…  (Esc: iptal)") : HotkeyService.Describe(a);
    }

    private void UpdateHotkeyStatus(string? msg)
    {
        if (msg != null) { _hotkeyStatus.Text = L.T(msg); return; }

        var failed = HotkeyRows.Where(r => App.Hotkey.ModeOf(r.A) == HotkeyMode.None).Select(r => r.Title).ToList();
        var compat = HotkeyRows.Where(r => App.Hotkey.ModeOf(r.A) == HotkeyMode.Compat).Select(r => r.Title).ToList();

        if (failed.Count > 0)
            _hotkeyStatus.Text = L.F("Kaydedilemeyen kısayol: {0}. Farklı bir kombinasyon deneyin.", string.Join(", ", failed.Select(L.T)));
        else if (compat.Count > 0)
            _hotkeyStatus.Text = L.F("Tüm kısayollar etkin. ({0}: uyumluluk modu — başka bir uygulama da aynı kombinasyonu kullanıyor olabilir.)", string.Join(", ", compat.Select(L.T)));
        else
            _hotkeyStatus.Text = L.T("Tüm kısayollar etkin.");
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
                UpdateHotkeyStatus(L.F("Bu kombinasyon zaten '{0}' için kullanılıyor.", L.T(TitleOf(other))));
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
