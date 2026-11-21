using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuickMedia;

/// <summary>
/// Tarayıcıdan gelen yayın/videolarda medya bilgisi genel kalabilir (ör. Kick: "Watch live").
/// Bu durumda açık sekmenin pencere başlığından yayıncı / yayın adı okunur.
/// </summary>
internal static class TabTitle
{
    private static readonly string[] BrowserSuffix =
    {
        " - Google Chrome", " - Microsoft Edge", " - Brave", " - Mozilla Firefox", " — Mozilla Firefox",
        " - Opera", " - Vivaldi", " - Chromium", " - Yandex Browser", " - Arc",
    };
    private static readonly string[] Generic = { "watch live", "live stream", "livestream", "live", "canlı yayın" };

    /// <summary>Kaynak bir web sitesi mi (masaüstü uygulaması değil)?</summary>
    public static bool IsWeb(MediaInfo i)
    {
        var p = Platforms.Get(i.PlatformId);
        if (p.Keywords.Length == 0) return false;
        string app = i.AppId.ToLowerInvariant();
        return !p.AppIds.Any(a => app.Contains(a));
    }

    /// <returns>(başlık, sanatçı) ya da yeni bir şey bulunamazsa (null, null).</returns>
    public static (string? Title, string? Artist) Resolve(MediaInfo i)
    {
        var p = Platforms.Get(i.PlatformId);
        var titles = Native.GetBrowserWindowTitles();

        // Medyanın kendi başlığı zaten bir sekmede görünüyorsa bilgi iyidir: dokunma
        if (!string.IsNullOrWhiteSpace(i.Title) && i.Title.Length >= 3 &&
            titles.Any(w => w.Contains(i.Title, StringComparison.OrdinalIgnoreCase)))
            return (null, null);

        foreach (var w in titles)
        {
            if (!p.Keywords.Any(k => w.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            var c = Clean(w, p);
            if (c.Length >= 2) return (c, p.Name);
        }
        return (null, null);
    }

    private static string Clean(string window, PlatformInfo p)
    {
        string s = window.Trim();
        foreach (var suf in BrowserSuffix)
        {
            int idx = s.LastIndexOf(suf, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) { s = s[..idx]; break; }
        }
        s = Regex.Replace(s, @"^\(\d+\)\s*", "");   // "(3) " bildirim sayısı

        var parts = Regex.Split(s, @"\s+[-|–—•·]\s+")
                         .Select(x => x.Trim()).Where(x => x.Length > 0 && !IsSiteWord(x, p)).ToList();
        s = string.Join(" · ", parts);
        return s.Length > 90 ? s[..90] + "…" : s;
    }

    private static bool IsSiteWord(string x, PlatformInfo p)
    {
        string t = x.Trim().Trim('.');
        if (t.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Keywords.Any(k => k.Trim(' ', '-', '|', '–', '—').Equals(t, StringComparison.OrdinalIgnoreCase))) return true;
        if (p.Domains.Any(d => d.Equals(t, StringComparison.OrdinalIgnoreCase))) return true;
        return Generic.Any(g => g.Equals(t, StringComparison.OrdinalIgnoreCase));
    }
}
