using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace QuickMedia;

public sealed record ThemeDef(
    string Id, string Name, bool Dark,
    string Bg, string Surface, string Border, string Text, string Text2, string Accent, string OnAccent,
    string SBg, string SSurface, string SBorder, string SHover);

public static class ThemeManager
{
    // Dark Glass: #CC171B24 / #401F242F / #30FFFFFF / #F2F4F8 / #A6AFBD / #8AB4F8
    public static readonly ThemeDef[] All =
    [
        new("Dark",     "Dark Glass",  true,  "#171B24", "#401F242F", "#30FFFFFF", "#F2F4F8", "#A6AFBD", "#8AB4F8", "#0B1220", "#171B24", "#1F242F", "#2B3140", "#2A3040"),
        new("Light",    "Light Glass", false, "#F3F5F9", "#66FFFFFF", "#30000000", "#1B1F27", "#5B6575", "#1A73E8", "#FFFFFF", "#F3F5F9", "#FFFFFF", "#DCE0E8", "#E8EBF1"),
        new("Midnight", "Midnight",    true,  "#000000", "#40181818", "#26FFFFFF", "#FFFFFF", "#9AA0AA", "#8AB4F8", "#0B1220", "#0A0A0A", "#141414", "#262626", "#1E1E1E"),
        new("Ocean",    "Ocean",       true,  "#0E1A2B", "#40182A44", "#30A9D6FF", "#EAF4FF", "#9DB6D1", "#5CC8FF", "#06121F", "#0E1A2B", "#15253B", "#24395A", "#1D3250"),
        new("Forest",   "Forest",      true,  "#0F1C17", "#40163026", "#308FE3B0", "#EAF7F0", "#9DB8A9", "#6FCF97", "#08140F", "#0F1C17", "#16281F", "#234234", "#1E3328"),
        new("Rose",     "Rose",        true,  "#22141C", "#40351F2B", "#30FFB8D4", "#FFF1F6", "#C9A3B4", "#F28BB2", "#2A0F1B", "#22141C", "#2E1B26", "#43283A", "#38222F"),
        new("Mocha",    "Mocha",       true,  "#1F1814", "#40302520", "#30FFD9A8", "#F7EEE6", "#BBA999", "#E0B27A", "#1F1410", "#1F1814", "#2A211B", "#3F332A", "#352A22"),
        new("Violet",   "Violet",      true,  "#171227", "#40261E45", "#30CDB8FF", "#F2EEFF", "#AFA5CC", "#B69CFF", "#140E26", "#171227", "#211A38", "#342B52", "#2C2447"),
        new("Paper",    "Paper",       false, "#F7F3EC", "#66FFFFFF", "#30000000", "#2B2620", "#7A6E60", "#C2672D", "#FFFFFF", "#F7F3EC", "#FFFDF9", "#E3DACB", "#EFE7DA"),
    ];

    public static ThemeDef Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
    public static bool IsDark(string? id) => Get(id).Dark;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>
    /// Fırçayı yerinde günceller: yeni nesne atamak, ona bağlı yüzlerce DynamicResource başvurusunun
    /// (ayarlar sayfaları, flyout, görev çubuğu) yeniden çözülmesine ve arayüzün takılmasına yol açar.
    /// </summary>
    private static void Set(string key, Color c)
    {
        var res = Application.Current.Resources;
        if (res[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else res[key] = new SolidColorBrush(c);
    }

    /// <summary>Kullanıcı bir renk seçtiyse onu, yoksa temanın vurgu rengini döndürür.</summary>
    public static Color AccentOf(AppSettings s)
    {
        var t = Get(s.Theme);
        string a = s.AccentColor ?? "";
        if (!string.IsNullOrWhiteSpace(a) && !a.Equals("#8AB4F8", StringComparison.OrdinalIgnoreCase))
        {
            try { return C(a); } catch { }
        }
        return C(t.Accent);
    }

    public static void Apply(AppSettings s)
    {
        var t = Get(s.Theme);
        var bg = C(t.Bg);
        byte alpha = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0.3, 1.0) * 255);
        var accent = AccentOf(s);

        Set("BgBrush", Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        Set("SurfaceBrush", C(t.Surface));
        Set("BorderBrushC", C(t.Border));
        Set("TextBrush", C(t.Text));
        Set("Text2Brush", C(t.Text2));
        Set("AccentBrush", accent);
        Set("ProgressBrush", accent);
        Set("AccentSoftBrush", Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        Set("OnAccentBrush", C(t.OnAccent));
        Set("HoverBrush", t.Dark ? C("#1AFFFFFF") : C("#14000000"));
        Set("TrackBrush", t.Dark ? C("#33FFFFFF") : C("#26000000"));

        // Ayarlar penceresi (opak yüzeyler)
        Set("SolidBgBrush", C(t.SBg));
        Set("SolidSurfaceBrush", C(t.SSurface));
        Set("SolidBorderBrush", C(t.SBorder));
        Set("SolidHoverBrush", C(t.SHover));
    }
}
