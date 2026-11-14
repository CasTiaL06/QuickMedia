using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace QuickMedia;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static MediaService Media { get; private set; } = null!;
    public static FlyoutController Flyout { get; private set; } = null!;
    public static HotkeyService Hotkey { get; private set; } = null!;

    private Mutex? _mutex;
    private TrayService? _tray;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "QuickMedia.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        Settings = SettingsStore.Load();
        ThemeManager.Apply(Settings);

        Media = new MediaService();
        Flyout = new FlyoutController(new FlyoutWindow());

        Hotkey = new HotkeyService();
        Hotkey.Pressed += () => Dispatcher.BeginInvoke(() => Flyout.Toggle());

        _tray = new TrayService();
        if (!Hotkey.RegisterFromSettings())
            _tray.Notify($"{HotkeyService.Describe(Settings)} kısayolu başka bir uygulama tarafından kullanılıyor. Ayarlar > Kısayol bölümünden değiştirin.");

        Media.Changed += (info, ev) => Dispatcher.BeginInvoke(() => Flyout.OnMediaChanged(info, ev));
        try { await Media.InitAsync(); }
        catch (Exception ex) { _tray.Notify("Medya servisi başlatılamadı: " + ex.Message); }
    }

    public static void ApplyAll()
    {
        ThemeManager.Apply(Settings);
        Flyout.ApplySettings();
    }

    public static void ShowSettings()
    {
        var app = (App)Current;
        if (app._settingsWindow == null)
        {
            app._settingsWindow = new SettingsWindow();
            app._settingsWindow.Closed += (_, _) => app._settingsWindow = null;
            app._settingsWindow.Show();
        }
        app._settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); Hotkey?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
