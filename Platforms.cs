using System;
using System.Linq;

namespace QuickMedia;

public sealed record PlatformInfo(string Id, string Name, string Kind, string[] AppIds, string[] Keywords, string[] Domains);

public static class Platforms
{
    // Sıra önemli: "YouTube Music", "YouTube"tan önce gelmeli.
    public static readonly PlatformInfo[] All =
    [
        new("spotify",     "Spotify",        "Uygulama / Web", ["spotify"],                               ["Spotify"],        ["spotify.com"]),
        new("ytmusic",     "YouTube Music",  "Web",            [],                                        ["YouTube Music"],  ["music.youtube.com"]),
        new("youtube",     "YouTube",        "Web",            [],                                        ["YouTube"],        ["youtube.com", "youtu.be"]),
        new("applemusic",  "Apple Music",    "Uygulama / Web", ["applemusic", "itunes"],                  ["Apple Music"],    ["music.apple.com"]),
        new("netflix",     "Netflix",        "Web",            [],                                        ["Netflix"],        ["netflix.com"]),
        new("primevideo",  "Prime Video",    "Web",            [],                                        ["Prime Video"],    ["primevideo.com"]),
        new("disney",      "Disney+",        "Web",            [],                                        ["Disney+"],        ["disneyplus.com"]),
        new("twitch",      "Twitch",         "Web",            [],                                        ["Twitch"],         ["twitch.tv"]),
        new("twitter",     "X (Twitter)",    "Web",            [],                                        [" / X", "Twitter"], ["x.com", "twitter.com"]),
        new("facebook",    "Facebook",       "Web",            [],                                        ["Facebook"],       ["facebook.com", "fb.watch"]),
        new("instagram",   "Instagram",      "Web",            [],                                        ["Instagram"],      ["instagram.com"]),
        new("tiktok",      "TikTok",         "Web",            [],                                        ["TikTok"],         ["tiktok.com"]),
        new("soundcloud",  "SoundCloud",     "Web",            [],                                        ["SoundCloud"],     ["soundcloud.com"]),
        new("deezer",      "Deezer",         "Uygulama / Web", ["deezer"],                                ["Deezer"],         ["deezer.com"]),
        new("vlc",         "VLC",            "Uygulama",       ["vlc"],                                   [],                 []),
        new("mediaplayer", "Medya Oynatıcı", "Uygulama",       ["zunemusic", "zunevideo", "mediaplayer"], [],                 []),
        new("lol",         "League of Legends", "Oyun",        ["league", "riot", "lol"],                 [],                 []),
        new("discord",     "Discord",        "Uygulama",       ["discord"],                               [],                 []),
        new("steam",       "Steam",          "Uygulama",       ["steam"],                                 [],                 []),
        new("other",       "Diğer",          "Bilinmeyen kaynaklar", [],                                  [],                 []),
    ];

    public static PlatformInfo Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[^1];

    public static PlatformRule DefaultRule(string id) => id switch
    {
        "spotify" => new PlatformRule { Mode = VisibilityMode.Always, Seconds = 5 },
        "lol" => new PlatformRule { Mode = VisibilityMode.Never, Seconds = 5 },
        // Akışlarda otomatik oynayan videolar sık tetiklenir; kısa tutuldu.
        "twitter" or "facebook" or "instagram" or "tiktok" => new PlatformRule { Mode = VisibilityMode.Timed, Seconds = 3 },
        _ => new PlatformRule { Mode = VisibilityMode.Timed, Seconds = 5 },
    };

    private static readonly string[] BrowserHints = ["chrome", "edge", "firefox", "opera", "brave", "vivaldi", "arc", "zen", "chromium"];

    /// <summary>
    /// Medya oturumunun hangi platformdan geldiğini tahmin eder: uygulama kimliği → site adresi
    /// (Chrome/Edge siteler için sanatçı alanına alan adını yazar) → tarayıcı pencere başlığı.
    /// </summary>
    public static string Detect(string appId, string mediaTitle, string artist = "", string album = "")
    {
        string app = (appId ?? "").ToLowerInvariant();
        foreach (var p in All)
            if (p.AppIds.Any(a => app.Contains(a))) return p.Id;

        foreach (var p in All)
            foreach (var d in p.Domains)
                if (DomainMatch(artist, d) || DomainMatch(album, d) || DomainMatch(mediaTitle, d)) return p.Id;

        var titles = Native.GetBrowserWindowTitles();

        if (!string.IsNullOrWhiteSpace(mediaTitle) && mediaTitle.Length >= 3)
            foreach (var w in titles)
                if (w.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    var id = MatchKeyword(w);
                    if (id != null) return id;
                }

        bool browserLike = BrowserHints.Any(b => app.Contains(b)) || (app.Length >= 12 && app.All(Uri.IsHexDigit));
        if (browserLike)
            foreach (var w in titles)
            {
                var id = MatchKeyword(w);
                if (id != null) return id;
            }

        return "other";
    }

    private static bool DomainMatch(string? s, string domain)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().ToLowerInvariant();
        if (s.StartsWith("https://")) s = s[8..];
        else if (s.StartsWith("http://")) s = s[7..];
        if (s.StartsWith("www.")) s = s[4..];
        return s == domain || s.StartsWith(domain + "/") || s.StartsWith(domain + " ") || s.EndsWith("." + domain);
    }

    private static string? MatchKeyword(string windowTitle)
    {
        foreach (var p in All)
            foreach (var k in p.Keywords)
                if (windowTitle.Contains(k, StringComparison.OrdinalIgnoreCase)) return p.Id;
        return null;
    }
}
