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
    public static TaskbarWidget Taskbar { get; private set; } = null!;

    /// <summary>Ayarlar uygulandığında (tema, stil, boyut...) tetiklenir.</summary>
    public static event Action? Applied;

    private Mutex? _mutex;
    private TrayService? _tray;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("Yakalanmamış hata: " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("Ölümcül hata: " + ex.ExceptionObject);

        _mutex = new Mutex(true, "QuickMedia.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        try
        {
            Log.Write("QuickMedia başlıyor");
            Settings = SettingsStore.Load();
            L.Set(Settings.Language);
            ThemeManager.Apply(Settings);

            Media = new MediaService();
            Flyout = new FlyoutController(new FlyoutWindow());
            Taskbar = new TaskbarWidget();

            Hotkey = new HotkeyService();
            Hotkey.Pressed += a => Dispatcher.BeginInvoke(() =>
            {
                try { HandleHotkey(a); }
                catch (Exception ex) { Log.Write("Kısayol işleme hatası: " + ex); }
            });

            _tray = new TrayService();
            int bad = Hotkey.RegisterAllFromSettings();
            if (bad > 0)
                _tray.Notify(L.F("{0} kısayol kaydedilemedi. Ayarlar > Kısayollar bölümünden başka bir kombinasyon seçin (günlük: {1}).", bad, Log.FilePath));
        }
        catch (Exception ex)
        {
            Log.Write("Başlatma hatası: " + ex);
            MessageBox.Show(L.T("QuickMedia başlatılamadı:") + "\n" + ex.Message + "\n\n" + L.T("Günlük:") + " " + Log.FilePath, "QuickMedia");
            Shutdown();
            return;
        }

        Media.Changed += (info, ev) => Dispatcher.BeginInvoke(() =>
        {
            try { Flyout.OnMediaChanged(info, ev); Taskbar?.Refresh(); }
            catch (Exception ex) { Log.Write("Medya olayı hatası: " + ex); }
        });
        try { await Media.InitAsync(); }
        catch (Exception ex)
        {
            Log.Write("Medya servisi hatası: " + ex);
            _tray?.Notify(L.T("Medya servisi başlatılamadı:") + " " + ex.Message);
        }
    }

    private static void HandleHotkey(HotkeyAction a)
    {
        switch (a)
        {
            case HotkeyAction.Toggle: Flyout.Toggle(); break;
            case HotkeyAction.PlayPause: Media.TogglePlay(); break;
            case HotkeyAction.Next: Media.Next(); break;
            case HotkeyAction.Previous: Media.Previous(); break;
            case HotkeyAction.VolumeUp: AdjustVolume(+0.05f); break;
            case HotkeyAction.VolumeDown: AdjustVolume(-0.05f); break;
            case HotkeyAction.OpenSettings: ToggleSettings(); break;
        }
    }

    /// <summary>Windows ana sesine değil, yalnızca çalan uygulamanın sesine dokunur.</summary>
    public static void AdjustVolume(float delta, bool nudge = true)
    {
        string id = Media.Current.AppId;
        var st = AppVolume.GetState(id);
        if (st == null) return;
        if (delta > 0 && st.Value.Muted) AppVolume.SetMute(id, false);
        AppVolume.SetVolume(id, Math.Clamp(st.Value.Volume + delta, 0f, 1f));
        if (nudge) Flyout.NudgeVolume();
    }

    public static void ToggleSettings()
    {
        var app = (App)Current;
        if (app._settingsWindow != null) app._settingsWindow.Close();
        else ShowSettings();
    }

    /// <summary>Flyout'un göstereceği medya: "Gizle" seçilen kaynaklar için boş.</summary>
    public static MediaInfo Displayed => Media == null ? new MediaInfo() : FlyoutController.Shown(Media.Current);

    public static void RaiseApplied() => Applied?.Invoke();

    public static void ApplyAll()
    {
        Media?.Repick();      // Gizle / Hep göster kuralları değişmiş olabilir: gösterilecek oturumu yeniden seç
        ThemeManager.Apply(Settings);
        Flyout.ApplySettings();
        Taskbar?.Refresh();
        Applied?.Invoke();
    }

    public static void SetStyle(string id)
    {
        Settings.FlyoutStyle = FlyoutStyles.Normalize(id);
        SettingsStore.Save(Settings);
        ApplyAll();
        Flyout.Preview();
    }

    public static void CycleStyle() => SetStyle(FlyoutStyles.Next(Settings.FlyoutStyle));

    public static void ShowSettings(string? page = null)
    {
        var app = (App)Current;
        try
        {
            if (app._settingsWindow == null)
            {
                app._settingsWindow = new SettingsWindow(page ?? "General");
                app._settingsWindow.Closed += (_, _) => app._settingsWindow = null;
                app._settingsWindow.Show();
            }
            else if (page != null) app._settingsWindow.GoTo(page);
            app._settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Write("Ayarlar açılamadı: " + ex);
            app._settingsWindow = null;
            MessageBox.Show(L.T("Ayarlar penceresi açılamadı:") + "\n\n" + ex.Message + "\n\n" + L.T("Ayrıntı:") + " " + Log.FilePath, "QuickMedia");
        }
    }

    /// <summary>Dili değiştirir; ayarlar penceresi aynı sayfada yeni dille yeniden açılır.</summary>
    public static void ChangeLanguage(string setting)
    {
        Settings.Language = setting;
        SettingsStore.Save(Settings);
        L.Set(setting);
        ApplyAll();

        var app = (App)Current;
        var old = app._settingsWindow;
        string page = old?.CurrentPage ?? "General";
        app.Dispatcher.BeginInvoke(() =>
        {
            if (old != null) { app._settingsWindow = null; old.Close(); }
            ShowSettings(page);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); Hotkey?.Dispose(); Taskbar?.Close(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
