using System;
using System.Linq;

namespace QuickMedia;

public sealed record PlatformInfo(string Id, string Name, string Kind, string[] AppIds, string[] Keywords);

public static class Platforms
{
    // Sıra önemli: "YouTube Music", "YouTube"tan önce gelmeli.
    public static readonly PlatformInfo[] All =
    [
        new("spotify",     "Spotify",        "Uygulama / Web", ["spotify"],                              ["Spotify"]),
        new("ytmusic",     "YouTube Music",  "Web",            [],                                       ["YouTube Music"]),
        new("youtube",     "YouTube",        "Web",            [],                                       ["YouTube"]),
        new("applemusic",  "Apple Music",    "Uygulama / Web", ["applemusic", "itunes"],                 ["Apple Music"]),
        new("netflix",     "Netflix",        "Web",            [],                                       ["Netflix"]),
        new("primevideo",  "Prime Video",    "Web",            [],                                       ["Prime Video"]),
        new("disney",      "Disney+",        "Web",            [],                                       ["Disney+"]),
        new("twitch",      "Twitch",         "Web",            [],                                       ["Twitch"]),
        new("twitter",     "X (Twitter)",    "Web",            [],                                       [" / X", "Twitter"]),
        new("facebook",    "Facebook",       "Web",            [],                                       ["Facebook"]),
        new("instagram",   "Instagram",      "Web",            [],                                       ["Instagram"]),
        new("tiktok",      "TikTok",         "Web",            [],                                       ["TikTok"]),
        new("soundcloud",  "SoundCloud",     "Web",            [],                                       ["SoundCloud"]),
        new("deezer",      "Deezer",         "Uygulama / Web", ["deezer"],                               ["Deezer"]),
        new("vlc",         "VLC",            "Uygulama",       ["vlc"],                                  []),
        new("mediaplayer", "Medya Oynatıcı", "Uygulama",       ["zunemusic", "zunevideo", "mediaplayer"], []),
        new("other",       "Diğer",          "Bilinmeyen platformlar", [],                               []),
    ];

    public static PlatformInfo Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[^1];

    public static PlatformRule DefaultRule(string id) => id switch
    {
        "spotify" => new PlatformRule { Mode = VisibilityMode.Always, Seconds = 5 },
        // Akışlarda otomatik oynayan videolar sık tetiklenir; kısa tutuldu.
        "twitter" or "facebook" or "instagram" or "tiktok" => new PlatformRule { Mode = VisibilityMode.Timed, Seconds = 3 },
        _ => new PlatformRule { Mode = VisibilityMode.Timed, Seconds = 5 },
    };

    private static readonly string[] BrowserHints = ["chrome", "edge", "firefox", "opera", "brave", "vivaldi", "arc", "zen", "chromium"];

    /// <summary>
    /// Medya oturumunun hangi platformdan geldiğini tahmin eder.
    /// Masaüstü uygulamalar AppUserModelId ile, web siteleri tarayıcı pencere başlığıyla bulunur.
    /// </summary>
    public static string Detect(string appId, string mediaTitle)
    {
        string app = (appId ?? "").ToLowerInvariant();
        foreach (var p in All)
            if (p.AppIds.Any(a => app.Contains(a))) return p.Id;

        var titles = Native.GetBrowserWindowTitles();

        // 1) Başlığında çalan medyanın adı geçen tarayıcı penceresi
        if (!string.IsNullOrWhiteSpace(mediaTitle) && mediaTitle.Length >= 3)
            foreach (var w in titles)
                if (w.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    var id = MatchKeyword(w);
                    if (id != null) return id;
                }

        // 2) Oturum bir tarayıcıdan geliyorsa (Firefox gibi bazılarının kimliği hash'tir) açık sekme başlıklarına bak
        bool browserLike = BrowserHints.Any(b => app.Contains(b)) || (app.Length >= 12 && app.All(Uri.IsHexDigit));
        if (browserLike)
            foreach (var w in titles)
            {
                var id = MatchKeyword(w);
                if (id != null) return id;
            }

        return "other";
    }

    private static string? MatchKeyword(string windowTitle)
    {
        foreach (var p in All)
            foreach (var k in p.Keywords)
                if (windowTitle.Contains(k, StringComparison.OrdinalIgnoreCase)) return p.Id;
        return null;
    }
}
