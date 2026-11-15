using System;
using System.Windows;
using System.Windows.Media;

namespace QuickMedia;

public static class ThemeManager
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Set(string key, Color c) =>
        Application.Current.Resources[key] = new SolidColorBrush(c);

    public static void Apply(AppSettings s)
    {
        bool dark = s.Theme != "Light";
        bool mid = s.Theme == "Midnight";

        Color accent;
        try { accent = C(s.AccentColor); } catch { accent = C("#8AB4F8"); }

        // Dark Glass: #CC171B24 / #401F242F / #30FFFFFF / #F2F4F8 / #A6AFBD / #8AB4F8
        var bg = mid ? C("#000000") : dark ? C("#171B24") : C("#F3F5F9");
        byte alpha = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0.3, 1.0) * 255);

        Set("BgBrush", Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        Set("SurfaceBrush", mid ? C("#40181818") : dark ? C("#401F242F") : C("#66FFFFFF"));
        Set("BorderBrushC", mid ? C("#26FFFFFF") : dark ? C("#30FFFFFF") : C("#30000000"));
        Set("TextBrush", mid ? C("#FFFFFF") : dark ? C("#F2F4F8") : C("#1B1F27"));
        Set("Text2Brush", dark ? C("#A6AFBD") : C("#5B6575"));
        Set("AccentBrush", accent);
        Set("ProgressBrush", accent);
        Set("OnAccentBrush", C("#0B1220"));
        Set("HoverBrush", dark ? C("#1AFFFFFF") : C("#14000000"));
        Set("TrackBrush", dark ? C("#33FFFFFF") : C("#26000000"));

        // Ayarlar penceresi (opak yüzeyler)
        Set("SolidBgBrush", dark ? C("#171B24") : C("#F3F5F9"));
        Set("SolidSurfaceBrush", dark ? C("#1F242F") : C("#FFFFFF"));
        Set("SolidBorderBrush", dark ? C("#2B3140") : C("#DCE0E8"));
        Set("SolidHoverBrush", dark ? C("#2A3040") : C("#E8EBF1"));
    }
}
