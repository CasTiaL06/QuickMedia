using System.Linq;

namespace QuickMedia;

public static class FlyoutStyles
{
    public static readonly (string Id, string Name)[] All =
    [
        ("Classic", "Klasik"),
        ("Compact", "Kompakt şerit"),
        ("Pill",    "Minimal hap"),
        ("Cover",   "Büyük kapak"),
        ("Island",  "Island"),
        ("Wide",    "Geniş şerit"),
        ("Tile",    "Kare kapak"),
        ("Text",    "Sadece yazı"),
        ("Orb",     "Yuvarlak plak"),
        ("MiniOrb", "Mini plak"),
        ("BigOrb",  "Büyük plak"),
        ("Disc",    "Plak şerit"),
        ("Float",   "Kare yüzer"),
        ("Poster",  "Poster"),
    ];

    public static string Normalize(string? id) => All.Any(a => a.Id == id) ? id! : "Classic";

    public static string Next(string? id)
    {
        int i = System.Array.FindIndex(All, a => a.Id == Normalize(id));
        return All[(i + 1) % All.Length].Id;
    }
}
