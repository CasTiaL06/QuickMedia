using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickMedia;

public partial class SettingsWindow : Window
{
    private bool _loading = true;   // InitializeComponent sırasında tetiklenen olayları yoksay
    private bool _capturing;

    private static readonly string[] Accents =
        ["#8AB4F8", "#81C995", "#F28B82", "#FDD663", "#C58AF9", "#78D9EC"];
    private static readonly int[] Steps = [1, 2, 3, 4, 5, 8, 10, 15, 20, 30, 60];

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBar();
        Closed += (_, _) => { if (_capturing) EndCapture(); };

        BuildAccents();
        BuildPlatforms();
        Load();
        UpdateHotkeyText();
        VersionText.Text = "Sürüm " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    }

    private void ApplyTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.SetDarkTitleBar(h, App.Settings.Theme != "Light");
    }

    // ---------- Gezinme ----------

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        string tag = (string)((FrameworkElement)sender).Tag;
        foreach (var sv in Pages.Children.OfType<ScrollViewer>())
            sv.Visibility = sv.Name == tag ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Ayarları UI'a yükle / UI'dan topla ----------

    private void Load()
    {
        _loading = true;
        var s = App.Settings;

        EnabledSw.IsChecked = s.Enabled;
        AutoStartSw.IsChecked = s.StartWithWindows;
        PortableSw.IsChecked = SettingsStore.IsPortable;
        PathText.Text = SettingsStore.FilePath;

        TrackSw.IsChecked = s.ShowOnTrackChange;
        PlaySw.IsChecked = s.ShowOnPlayPause;
        FullscreenSw.IsChecked = s.HideInFullscreen;
        PausedSw.IsChecked = s.AlwaysHideWhenPaused;

        ThemeDark.IsChecked = s.Theme != "Light";
        ThemeLight.IsChecked = s.Theme == "Light";
        OpacitySlider.Value = Math.Round(s.BackgroundOpacity * 100);
        BlurSw.IsChecked = s.BlurEnabled;
        ScaleSlider.Value = Math.Round(s.Scale * 100);
        AnimSw.IsChecked = s.Animations;

        foreach (var r in PositionGrid.Children.OfType<RadioButton>())
            r.IsChecked = s.CustomX == null && (string)r.Tag == s.Position;

        ArtSw.IsChecked = s.ShowAlbumArt;
        ProgSw.IsChecked = s.ShowProgress;
        CtrlSw.IsChecked = s.ShowControls;
        VolSw.IsChecked = s.ShowVolumeControls;
        BadgeSw.IsChecked = s.ShowPlatformBadge;

        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        ScaleText.Text = $"{ScaleSlider.Value:0}%";
        RefreshAccents();
        _loading = false;
    }

    private void Collect()
    {
        var s = App.Settings;

        s.Enabled = EnabledSw.IsChecked == true;
        bool autoStart = AutoStartSw.IsChecked == true;
        if (autoStart != s.StartWithWindows) AutoStart.Set(autoStart);
        s.StartWithWindows = autoStart;

        s.ShowOnTrackChange = TrackSw.IsChecked == true;
        s.ShowOnPlayPause = PlaySw.IsChecked == true;
        s.HideInFullscreen = FullscreenSw.IsChecked == true;
        s.AlwaysHideWhenPaused = PausedSw.IsChecked == true;

        s.Theme = ThemeLight.IsChecked == true ? "Light" : "Dark";
        s.BackgroundOpacity = OpacitySlider.Value / 100.0;
        s.BlurEnabled = BlurSw.IsChecked == true;
        s.Scale = Math.Round(ScaleSlider.Value) / 100.0;
        s.Animations = AnimSw.IsChecked == true;

        var pos = PositionGrid.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (pos != null) s.Position = (string)pos.Tag;

        s.ShowAlbumArt = ArtSw.IsChecked == true;
        s.ShowProgress = ProgSw.IsChecked == true;
        s.ShowControls = CtrlSw.IsChecked == true;
        s.ShowVolumeControls = VolSw.IsChecked == true;
        s.ShowPlatformBadge = BadgeSw.IsChecked == true;

        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        ScaleText.Text = $"{ScaleSlider.Value:0}%";

        SettingsStore.Save(s);
        App.ApplyAll();
        ApplyTitleBar();
    }

    private void OnChanged(object sender, RoutedEventArgs e) { if (!_loading) Collect(); }
    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Collect(); }

    private void OnPosition(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.CustomX = null;   // hazır konum seçilince sürükleyerek belirlenen konum silinir
        App.Settings.CustomY = null;
        Collect();
    }

    private void OnPortable(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool want = PortableSw.IsChecked == true;
        if (!SettingsStore.SetPortable(want, App.Settings))
        {
            _loading = true;
            PortableSw.IsChecked = !want;
            _loading = false;
            MessageBox.Show("Bu klasöre yazılamadı. Taşınabilir mod için uygulamayı yazılabilir bir klasöre (ör. Masaüstü, USB bellek) koyun.",
                "QuickMedia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        PathText.Text = SettingsStore.FilePath;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Dir}\"") { UseShellExecute = true }); } catch { }
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => App.Flyout.Preview();

    // ---------- Vurgu rengi ----------

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
            AccentPanel.Children.Add(b);
        }
    }

    private void RefreshAccents()
    {
        foreach (Border b in AccentPanel.Children)
        {
            bool sel = string.Equals((string)b.Tag, App.Settings.AccentColor, StringComparison.OrdinalIgnoreCase);
            if (sel) b.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            else b.BorderBrush = Brushes.Transparent;
        }
    }

    // ---------- Platform kuralları ----------

    private void BuildPlatforms()
    {
        PlatformList.Children.Clear();
        foreach (var p in Platforms.All)
        {
            var rule = App.Settings.GetRule(p.Id);

            var card = new Border { Style = (Style)FindResource("Card") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = p.Kind, Style = (Style)FindResource("Hint") });
            grid.Children.Add(left);

            // Seçenekler: Hep göster | Süreli | Gizle
            var segHost = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            segHost.SetResourceReference(Border.BackgroundProperty, "SolidBgBrush");
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            segHost.Child = seg;
            Grid.SetColumn(segHost, 1);
            grid.Children.Add(segHost);

            // Süre ayarı
            var stepper = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var minus = new Button { Content = "−", Width = 28, Padding = new Thickness(0, 4, 0, 4), Style = (Style)FindResource("FluentButton") };
            var plus = new Button { Content = "+", Width = 28, Padding = new Thickness(0, 4, 0, 4), Style = (Style)FindResource("FluentButton") };
            var secText = new TextBlock { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stepper.Children.Add(minus); stepper.Children.Add(secText); stepper.Children.Add(plus);
            Grid.SetColumn(stepper, 2);
            grid.Children.Add(stepper);

            void Refresh()
            {
                secText.Text = $"{rule.Seconds} sn";
                stepper.Visibility = rule.Mode == VisibilityMode.Timed ? Visibility.Visible : Visibility.Hidden;
            }

            void AddRadio(string text, VisibilityMode mode)
            {
                var rb = new RadioButton
                {
                    Content = text, GroupName = "rule_" + p.Id, Style = (Style)FindResource("Seg"),
                    IsChecked = rule.Mode == mode,
                };
                rb.Click += (_, _) => { rule.Mode = mode; Refresh(); SaveRules(); };
                seg.Children.Add(rb);
            }
            AddRadio("Hep göster", VisibilityMode.Always);
            AddRadio("Süreli", VisibilityMode.Timed);
            AddRadio("Gizle", VisibilityMode.Never);

            void Step(int dir)
            {
                int idx = Array.FindIndex(Steps, v => v >= rule.Seconds);
                if (idx < 0) idx = Steps.Length - 1;
                idx = Math.Clamp(idx + dir, 0, Steps.Length - 1);
                rule.Seconds = Steps[idx];
                Refresh();
                SaveRules();
            }
            minus.Click += (_, _) => Step(-1);
            plus.Click += (_, _) => Step(+1);

            Refresh();
            card.Child = grid;
            PlatformList.Children.Add(card);
        }
    }

    private void SaveRules() => SettingsStore.Save(App.Settings);

    private void ResetRules_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ResetRules();
        SaveRules();
        BuildPlatforms();
    }

    // ---------- Kısayol ----------

    private void UpdateHotkeyText()
    {
        HotkeyBtn.Content = _capturing ? "Tuşlara basın…  (Esc: iptal)" : HotkeyService.Describe(App.Settings);
    }

    private void HotkeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing) return;
        _capturing = true;
        App.Hotkey.Unregister(); // mevcut kısayol yakalamayı engellemesin
        HotkeyStatus.Text = "";
        UpdateHotkeyText();
        HotkeyBtn.Focus();
    }

    private void EndCapture()
    {
        _capturing = false;
        App.Hotkey.RegisterFromSettings();
        UpdateHotkeyText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing) { base.OnPreviewKeyDown(e); return; }
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
            HotkeyStatus.Text = "En az bir Ctrl, Alt veya Win tuşunu da basılı tutun.";
            return;
        }

        if (!App.Hotkey.Register(mods, key))
        {
            HotkeyStatus.Text = "Bu kısayol başka bir uygulama tarafından kullanılıyor, başka bir tane deneyin.";
            return;
        }

        App.Settings.HotkeyModifiers = (int)mods;
        App.Settings.HotkeyKey = key.ToString();
        SettingsStore.Save(App.Settings);
        _capturing = false;
        UpdateHotkeyText();
        HotkeyStatus.Text = "Kısayol kaydedildi.";
    }

    private void HotkeyReset_Click(object sender, RoutedEventArgs e)
    {
        _capturing = false;
        App.Settings.HotkeyModifiers = (int)ModifierKeys.Alt;
        App.Settings.HotkeyKey = "P";
        SettingsStore.Save(App.Settings);
        bool ok = App.Hotkey.RegisterFromSettings();
        UpdateHotkeyText();
        HotkeyStatus.Text = ok ? "Varsayılan kısayol (Alt + P) geri yüklendi." : "Alt + P başka bir uygulama tarafından kullanılıyor.";
    }
}
