using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickMedia;

public enum VisibilityMode { Timed, Always, Never }

public sealed class PlatformRule
{
    public VisibilityMode Mode { get; set; } = VisibilityMode.Timed;
    public int Seconds { get; set; } = 5;
}

public sealed class AppSettings
{
    // Genel
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; }

    // Görünürlük koşulları
    public bool ShowOnTrackChange { get; set; } = true;
    public bool ShowOnPlayPause { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool AlwaysHideWhenPaused { get; set; }

    // Görünüm
    public string Theme { get; set; } = "Dark";          // Dark | Light
    public string AccentColor { get; set; } = "#8AB4F8";
    public double BackgroundOpacity { get; set; } = 0.8; // 0xCC
    public bool BlurEnabled { get; set; } = true;
    public bool Animations { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public string Position { get; set; } = "BottomCenter";
    public double? CustomX { get; set; }
    public double? CustomY { get; set; }

    // Öğeler
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool ShowControls { get; set; } = true;
    public bool ShowVolumeControls { get; set; } = true;
    public bool ShowPlatformBadge { get; set; } = true;

    // Kısayol (ModifierKeys bayrakları: Alt=1, Ctrl=2, Shift=4, Win=8)
    public int HotkeyModifiers { get; set; } = 1;
    public string HotkeyKey { get; set; } = "P";

    // Platform kuralları
    public Dictionary<string, PlatformRule> Rules { get; set; } = new();

    public PlatformRule GetRule(string id)
    {
        if (!Rules.TryGetValue(id, out var r)) Rules[id] = r = Platforms.DefaultRule(id);
        return r;
    }

    public void EnsureDefaults()
    {
        foreach (var p in Platforms.All) GetRule(p.Id);
    }

    public void ResetRules()
    {
        Rules.Clear();
        EnsureDefaults();
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string PortableFlag => Path.Combine(AppContext.BaseDirectory, "portable.flag");
    public static bool IsPortable => File.Exists(PortableFlag);

    public static string Dir => IsPortable
        ? AppContext.BaseDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickMedia");

    public static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opt);
                if (s != null) { s.EnsureDefaults(); return s; }
            }
        }
        catch { /* bozuk dosya: varsayılanlara dön */ }
        var d = new AppSettings();
        d.EnsureDefaults();
        return d;
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Opt));
        }
        catch { /* yazma izni yoksa sessizce geç */ }
    }

    /// <summary>Taşınabilir mod: ayarlar exe'nin yanındaki settings.json'da tutulur.</summary>
    public static bool SetPortable(bool on, AppSettings s)
    {
        string oldFile = FilePath;
        try
        {
            if (on) File.WriteAllText(PortableFlag, "QuickMedia portable mode");
            else if (File.Exists(PortableFlag)) File.Delete(PortableFlag);
            Save(s);
            if (oldFile != FilePath && File.Exists(oldFile)) File.Delete(oldFile);
            return IsPortable == on;
        }
        catch { return false; }
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Set(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue("QuickMedia", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("QuickMedia", false);
        }
        catch { }
    }
}
