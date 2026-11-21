using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QuickMedia;

/// <summary>
/// Platform simgeleri. Marka renkleriyle koddan çizilmiş sade yaklaşık logolardır.
/// Resmi logoları kullanmak için 'Icons' klasörüne (exe'nin yanına ya da ayar klasörüne)
/// platform kimliğiyle PNG koyun: spotify.png, youtube.png, twitch.png ... — bunlar önceliklidir.
/// </summary>
public static class PlatformIcons
{
    public static FrameworkElement Create(string id, double size)
    {
        var custom = TryCustom(id, size);
        if (custom != null) return custom;

        var c = new Canvas { Width = 24, Height = 24 };
        try { Build(c, id); } catch { }
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    private static FrameworkElement? TryCustom(string id, double size)
    {
        foreach (var dir in new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "Icons"), System.IO.Path.Combine(SettingsStore.Dir, "Icons") })
        {
            try
            {
                var f = System.IO.Path.Combine(dir, id + ".png");
                if (!System.IO.File.Exists(f)) continue;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(f);
                bi.EndInit();
                bi.Freeze();
                return new Image { Source = bi, Width = size, Height = size };
            }
            catch { }
        }
        return null;
    }

    // ---------- çizim yardımcıları ----------

    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static void Put(Canvas c, Shape s, double x, double y)
    {
        Canvas.SetLeft(s, x); Canvas.SetTop(s, y);
        c.Children.Add(s);
    }

    private static void Rr(Canvas c, double x, double y, double w, double h, double r, Brush fill) =>
        Put(c, new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill }, x, y);

    private static void Rs(Canvas c, double x, double y, double w, double h, double r, Brush stroke, double st) =>
        Put(c, new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Stroke = stroke, StrokeThickness = st }, x, y);

    private static void El(Canvas c, double x, double y, double w, double h, Brush? fill, Brush? stroke = null, double st = 0) =>
        Put(c, new Ellipse { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = st }, x, y);

    private static void Pg(Canvas c, Brush fill, params double[] p)
    {
        var pts = new PointCollection();
        for (int i = 0; i + 1 < p.Length; i += 2) pts.Add(new Point(p[i], p[i + 1]));
        c.Children.Add(new Polygon { Points = pts, Fill = fill });
    }

    private static void Pa(Canvas c, string data, Brush? fill, Brush? stroke = null, double st = 0)
    {
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = st,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private static void Tx(Canvas c, string text, double x, double y, double size, Brush color)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = color, FontFamily = new FontFamily("Segoe UI") };
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
        c.Children.Add(t);
    }

    private static LinearGradientBrush Grad(string a, string b, double angle = 90) =>
        new((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), angle);

    private static void Note(Canvas c, double dx, Brush color)
    {
        Rr(c, 12.2 + dx, 5, 2, 10, 1, color);
        El(c, 8 + dx, 13.2, 5.4, 5.2, color);
        Pa(c, $"M{N(14.2 + dx)},5 Q{N(14.6 + dx)},8.4 {N(18 + dx)},8.6 L{N(18 + dx)},10.8 Q{N(15.6 + dx)},10.8 {N(14.2 + dx)},9.4 Z", color);
    }

    private static void Build(Canvas c, string id)
    {
        var white = Brushes.White;
        switch (id)
        {
            case "spotify":
                El(c, 0, 0, 24, 24, B("#1DB954"));
                Pa(c, "M5.5,9.2 Q12,6.6 18.5,10", null, white, 1.9);
                Pa(c, "M6.5,12.7 Q12,10.6 17.5,13.3", null, white, 1.7);
                Pa(c, "M7.6,16 Q12,14.6 16.4,16.6", null, white, 1.5);
                break;
            case "ytmusic":
                El(c, 0, 0, 24, 24, B("#FF0000"));
                El(c, 4.5, 4.5, 15, 15, null, white, 1.4);
                Pg(c, white, 10, 8.4, 10, 15.6, 16, 12);
                break;
            case "youtube":
                Rr(c, 0.5, 4, 23, 16, 4.5, B("#FF0000"));
                Pg(c, white, 9.6, 8.4, 9.6, 15.6, 16, 12);
                break;
            case "applemusic":
                Rr(c, 0, 0, 24, 24, 5.5, Grad("#FB5C74", "#FA233B"));
                Note(c, -0.5, white);
                break;
            case "netflix":
                Rr(c, 0, 0, 24, 24, 5, B("#141414"));
                Rr(c, 7, 4.5, 3, 15, 0, B("#B1060F"));
                Rr(c, 14, 4.5, 3, 15, 0, B("#B1060F"));
                Pg(c, B("#E50914"), 7, 4.5, 10, 4.5, 17, 19.5, 14, 19.5);
                break;
            case "primevideo":
                Rr(c, 0, 0, 24, 24, 5, B("#00A8E1"));
                Pg(c, white, 9, 6.8, 9, 13.8, 15.5, 10.3);
                Pa(c, "M6,17 Q12,20.4 18,17", null, white, 1.6);
                break;
            case "disney":
                Rr(c, 0, 0, 24, 24, 5, B("#113CCF"));
                Tx(c, "D+", 4.2, 3.8, 13, white);
                break;
            case "twitch":
                Rr(c, 0, 0, 24, 24, 5, B("#9146FF"));
                Pg(c, white, 5, 3.5, 19, 3.5, 19, 13.5, 15, 17.5, 12.5, 17.5, 9.5, 20.5, 9.5, 17.5, 5, 17.5);
                Rr(c, 10.3, 7, 1.6, 5, 0.4, B("#9146FF"));
                Rr(c, 14.6, 7, 1.6, 5, 0.4, B("#9146FF"));
                break;
            case "kick":
                Rr(c, 0, 0, 24, 24, 5, B("#53FC18"));
                Tx(c, "K", 5.4, 1.4, 19, B("#0B0E0F"));
                break;
            case "twitter":
                Rr(c, 0, 0, 24, 24, 5, B("#000000"));
                Pa(c, "M6.5,5.8 L17.5,18.2", null, white, 2.4);
                Pa(c, "M17.5,5.8 L6.5,18.2", null, white, 1.4);
                break;
            case "facebook":
                El(c, 0, 0, 24, 24, B("#1877F2"));
                Tx(c, "f", 8.2, 1.2, 22, white);
                break;
            case "instagram":
                {
                    var g = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
                    g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FEDA75"), 0));
                    g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FA7E1E"), 0.25));
                    g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#D62976"), 0.55));
                    g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#962FBF"), 0.8));
                    g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#4F5BD5"), 1));
                    Rr(c, 0, 0, 24, 24, 6, g);
                    Rs(c, 5, 5, 14, 14, 4.2, white, 1.7);
                    El(c, 8.8, 8.8, 6.4, 6.4, null, white, 1.7);
                    El(c, 15.4, 6.6, 1.8, 1.8, white);
                    break;
                }
            case "tiktok":
                Rr(c, 0, 0, 24, 24, 5, B("#000000"));
                Note(c, -0.9, B("#25F4EE"));
                Note(c, 0.9, B("#FE2C55"));
                Note(c, 0, white);
                break;
            case "soundcloud":
                Rr(c, 0, 0, 24, 24, 5, B("#FF5500"));
                Rr(c, 4, 11.5, 1.4, 5, 0.7, white);
                Rr(c, 6.4, 9.5, 1.4, 7, 0.7, white);
                Rr(c, 8.8, 8.5, 1.4, 8, 0.7, white);
                Rr(c, 11.2, 7.5, 1.4, 9, 0.7, white);
                Pa(c, "M13.8,16.5 L13.8,8.4 Q15.2,6.4 17.6,7 Q20.6,7.8 20.4,11.2 Q20.4,14.6 17.4,16.5 Z", white);
                break;
            case "deezer":
                Rr(c, 0, 0, 24, 24, 5, B("#121216"));
                {
                    string[] cols = ["#40AB5D", "#A7D12E", "#FFED00", "#F2562B", "#A238FF"];
                    for (int i = 0; i < 5; i++) Rr(c, 3.5 + i * 3.4, 17 - (3 + i * 2.6), 2.6, 3 + i * 2.6, 0.6, B(cols[i]));
                }
                break;
            case "vlc":
                Pg(c, B("#FF8800"), 12, 2.5, 20.5, 18.5, 3.5, 18.5);
                Pg(c, white, 10.4, 8.8, 13.6, 8.8, 14.5, 11.2, 9.5, 11.2);
                Pg(c, white, 8.4, 13.2, 15.6, 13.2, 16.5, 15.6, 7.5, 15.6);
                Rr(c, 2.5, 19, 19, 3, 1.5, B("#E6E6E6"));
                break;
            case "mediaplayer":
                Rr(c, 0, 0, 24, 24, 5.5, Grad("#2AA3F3", "#0F6CBD"));
                Pg(c, white, 9, 6.8, 9, 17.2, 17.5, 12);
                break;
            case "lol":
                Rr(c, 0, 0, 24, 24, 5, B("#0A1428"));
                El(c, 3.2, 3.2, 17.6, 17.6, null, B("#C8AA6E"), 1.6);
                Tx(c, "LoL", 5.6, 6.6, 9.2, B("#C8AA6E"));
                break;
            case "discord":
                Rr(c, 0, 0, 24, 24, 5.5, B("#5865F2"));
                Rr(c, 4.5, 7.5, 15, 10, 5, white);
                El(c, 8.2, 10.8, 3, 3.8, B("#5865F2"));
                El(c, 12.8, 10.8, 3, 3.8, B("#5865F2"));
                break;
            case "steam":
                El(c, 0, 0, 24, 24, B("#171A21"));
                El(c, 11.8, 4.8, 7.4, 7.4, null, white, 1.6);
                El(c, 4.2, 12.4, 6.4, 6.4, white);
                Pa(c, "M9.6,14.2 L13,11", null, white, 1.6);
                break;
            default:
                El(c, 0, 0, 24, 24, B("#6B7280"));
                Tx(c, "?", 8.4, 2.6, 17, white);
                break;
        }
    }
}
