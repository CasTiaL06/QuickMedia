using System.Collections.Generic;
using System.Globalization;

namespace QuickMedia;

/// <summary>
/// Basit yerelleştirme: kaynak metinler Türkçe'dir; İngilizce seçiliyse aşağıdaki tablodan karşılığı kullanılır.
/// Tabloda olmayan metin olduğu gibi (Türkçe) gösterilir. Yeni dil eklemek için ikinci bir tablo yeterli.
/// </summary>
public static class L
{
    public static string Code { get; private set; } = "tr";

    /// <summary>"auto" Windows diline uyar (Türkçe değilse İngilizce).</summary>
    public static void Set(string? setting)
    {
        if (setting == "en") Code = "en";
        else if (setting == "tr") Code = "tr";
        else Code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    }

    public static string T(string tr) =>
        Code == "en" && En.TryGetValue(tr, out var e) ? e : tr;

    public static string F(string tr, params object[] args) => string.Format(T(tr), args);

    private static readonly Dictionary<string, string> En = new()
    {
        // ---- Gezinme / başlıklar ----
        ["Genel"] = "General",
        ["Stil ve görünüm"] = "Style & appearance",
        ["Görünürlük"] = "Visibility",
        ["Platformlar"] = "Platforms",
        ["Kısayollar"] = "Hotkeys",
        ["Hakkında"] = "About",
        ["Ayarlar"] = "Settings",
        ["Medya flyout"] = "Media flyout",
        ["Önizleme göster"] = "Show preview",
        ["Sürüm {0}"] = "Version {0}",
        ["Üstte sabit"] = "Pinned on top",

        // ---- Görev çubuğu ----
        ["Görev çubuğu"] = "Taskbar",
        ["QuickMedia'yı görev çubuğunun boş sol kısmında kullanın."] = "Use QuickMedia in the empty left part of the taskbar.",
        ["Görev çubuğunda kullan"] = "Use in the taskbar",
        ["Çalan şarkıyı görev çubuğunun sol tarafında küçük bir kontrol olarak gösterir. Tıklayınca flyout açılır, fare tekerleği çalan uygulamanın sesini değiştirir, sağ tık menü açar."] =
            "Shows the playing song as a small control on the left side of the taskbar. Click opens the flyout, the mouse wheel changes the playing app's volume, right-click opens a menu.",
        ["Genişlik"] = "Width",
        ["Görev çubuğundaki kontrolün genişliği."] = "Width of the taskbar control.",
        ["Soldan boşluk"] = "Left offset",
        ["Ortadaki simgelerin üstüne binmemesi için ayarlayın. Windows 11'de Widgets (hava durumu) düğmesi açıksa onun sağına denk gelecek şekilde artırın."] =
            "Adjust so it doesn't overlap the centered icons. If the Windows 11 Widgets (weather) button is on, increase it so the control sits to its right.",
        ["Otomatik açılan flyout'u kapat"] = "Disable the auto-opening flyout",
        ["Görev çubuğu kontrolü açıkken flyout kendiliğinden açılmaz; yalnızca kontrole tıklayınca ya da kısayolla açılır."] =
            "While the taskbar control is on, the flyout does not open by itself; it opens only when you click the control or use the hotkey.",
        ["Yalnızca alttaki veya üstteki görev çubuğunu destekler. Çalan bir şey yokken ya da tam ekran uygulamada otomatik gizlenir. Beğenmezseniz yukarıdaki anahtarı kapatmanız yeterli."] =
            "Only a bottom or top taskbar is supported. It hides automatically when nothing is playing or in a fullscreen app. If you don't like it, just turn the switch above off.",
        ["Görev çubuğu modunu kapat"] = "Turn off taskbar mode",
        ["Boş alanı otomatik bul"] = "Find the free space automatically",
        ["Görev çubuğundaki düğmelerin yerini okuyup kontrolü sol taraftaki boş alana kendisi sığdırır (Widgets düğmesi varsa onun sağına). Kapalıyken aşağıdaki 'Soldan boşluk' kullanılır."] =
            "Reads the positions of the taskbar buttons and fits the control into the free area on the left by itself (to the right of the Widgets button if present). When off, the 'Left offset' below is used.",
        ["İlerleme çizgisi"] = "Progress line",
        ["Kontrolün altında şarkının ilerlemesini gösteren ince çizgi."] = "A thin line under the control showing the song's progress.",
        ["Kontrolün en fazla genişliği (otomatik modda boş alana sığacak şekilde kısalır)."] = "Maximum width of the control (in automatic mode it shrinks to fit the free area).",
        ["Yalnızca otomatik yerleştirme kapalıyken: ortadaki simgelerin üstüne binmemesi için ayarlayın. Windows 11'de Widgets (hava durumu) düğmesi açıksa onun sağına denk gelecek şekilde artırın."] =
            "Only when automatic placement is off: adjust so it doesn't overlap the centered icons. If the Windows 11 Widgets (weather) button is on, increase it so the control sits to its right.",
        ["ŞU AN ÇALIYOR"] = "NOW PLAYING",
        ["Şu an çalan bir şey yok"] = "Nothing is playing right now",

        // ---- Genel ----
        ["Uygulamanın temel davranışı."] = "Core behavior of the app.",
        ["Flyout etkin"] = "Flyout enabled",
        ["Kapalıyken flyout hiçbir koşulda görünmez."] = "When off, the flyout never appears (hotkey included).",
        ["Üstte sabitle"] = "Pin on top",
        ["Sabitlemeyi kaldır"] = "Unpin",
        ["Medya çalarken flyout kapanmaz ve her zaman diğer pencerelerin üstünde kalır. Kapalıyken normal kurallar (platform ayarları) geçerlidir."] =
            "While media plays the flyout never closes and stays above other windows. When off, the normal (per-platform) rules apply.",
        ["Sabitleme düğmesini göster"] = "Show the pin button",
        ["Flyout'un sağ üst köşesinde, üstüne gelince beliren küçük iğne düğmesi."] = "A small pin button at the flyout's top-right corner, shown on hover.",
        ["Dil"] = "Language",
        ["Otomatik"] = "Automatic",
        ["Türkçe"] = "Türkçe",
        ["Arayüz dili. 'Otomatik' Windows diline uyar."] = "Interface language. 'Automatic' follows the Windows language.",
        ["Windows ile başlat"] = "Start with Windows",
        ["Oturum açıldığında QuickMedia otomatik çalışsın."] = "Launch QuickMedia automatically when you sign in.",
        ["Taşınabilir mod"] = "Portable mode",
        ["Ayarlar exe'nin yanındaki settings.json'da tutulur; klasörü USB belleğe kopyalayıp her yerde çalıştırabilirsiniz."] =
            "Settings are kept in settings.json next to the exe; copy the folder to a USB drive and run it anywhere.",
        ["AYAR DOSYASI"] = "SETTINGS FILE",
        ["Konum"] = "Location",
        ["Klasörü aç"] = "Open folder",
        ["Bu klasöre yazılamadı. Taşınabilir mod için uygulamayı yazılabilir bir klasöre (ör. Masaüstü, USB bellek) koyun."] =
            "Cannot write to this folder. For portable mode put the app in a writable folder (e.g. Desktop, USB drive).",

        // ---- Stil ve görünüm ----
        ["Flyout'un şeklini, rengini ve yerini seçin."] = "Choose the flyout's shape, color and position.",
        ["STİL"] = "STYLE",
        ["Stile tıklayınca önizleme açılır. Flyout üzerinde sağ tık sıradaki stile geçer; tray menüsünde de 'Stil' var. Plak, Kare kapak ve Island stillerinde kontroller fareyle üstüne gelince çıkar. Flyout'u istediğiniz yere sürükleyin; konum her stil için ayrı hatırlanır."] =
            "Clicking a style shows a preview. Right-click the flyout to switch to the next style; the tray menu has a 'Style' item too. In the disc, square cover and Island styles the controls appear on hover. Drag the flyout anywhere; the position is remembered per style.",
        ["TEMA"] = "THEME",
        ["Her temanın kendi vurgu rengi vardır; aşağıdan istediğiniz rengi seçebilirsiniz."] = "Every theme has its own accent color; you can pick any color below.",
        ["Vurgu rengi"] = "Accent color",
        ["'A' temanın kendi rengi. Varsayılan Dark Glass: #8AB4F8"] = "'A' is the theme's own color. Dark Glass default: #8AB4F8",
        ["Temanın kendi rengi"] = "Theme's own color",
        ["Arka plan opaklığı"] = "Background opacity",
        ["Varsayılan %80 (#CC171B24)."] = "Default 80% (#CC171B24).",
        ["Arka plan bulanıklığı"] = "Background blur",
        ["Masaüstü duvar kâğıdını hafifçe bulanıklaştırır."] = "Softly blurs the desktop wallpaper behind the flyout.",
        ["BOYUT VE KONUM"] = "SIZE & POSITION",
        ["Boyut"] = "Size",
        ["Flyout üzerindeyken Ctrl + fare tekerleği ile de değişir."] = "Also changes with Ctrl + mouse wheel over the flyout.",
        ["Hazır bir konum seçin ya da flyout'u fareyle sürükleyin."] = "Pick a preset position or drag the flyout with the mouse.",
        ["Üst sol"] = "Top left", ["Üst orta"] = "Top center", ["Üst sağ"] = "Top right",
        ["Alt sol"] = "Bottom left", ["Alt orta"] = "Bottom center", ["Alt sağ"] = "Bottom right",
        ["ÖĞELER"] = "ELEMENTS",
        ["Albüm kapağı"] = "Album art",
        ["İlerleme çubuğu"] = "Progress bar",
        ["Çubuğa tıklayıp sürükleyerek şarkıda istediğiniz yere atlayabilirsiniz."] = "Click or drag the bar to jump to any point in the song or video.",
        ["Oynat / önceki / sonraki"] = "Play / previous / next",
        ["Uygulama sesi"] = "App volume",
        ["Yalnızca çalan uygulamanın ses seviyesini ve sessize almasını yönetir (Windows ana sesine dokunmaz)."] =
            "Controls only the playing app's volume and mute (the Windows master volume is untouched).",
        ["Platform etiketi"] = "Platform badge",
        ["Açılış / kapanış animasyonu"] = "Open / close animation",

        // ---- Stil adları ----
        ["Klasik"] = "Classic", ["Kompakt şerit"] = "Compact strip", ["Minimal hap"] = "Minimal pill",
        ["Büyük kapak"] = "Large cover", ["Geniş şerit"] = "Wide strip", ["Kare kapak"] = "Square cover",
        ["Sadece yazı"] = "Text only", ["Yuvarlak plak"] = "Round disc", ["Mini plak"] = "Mini disc",
        ["Büyük plak"] = "Large disc", ["Plak şerit"] = "Disc strip", ["Kare yüzer"] = "Floating square",
        ["Stil"] = "Style",

        // ---- Görünürlük ----
        ["Flyout'un ne zaman açılıp kapanacağı."] = "When the flyout opens and closes.",
        ["Parça değişince göster"] = "Show on track change",
        ["Yeni şarkı / video başladığında flyout açılır."] = "The flyout opens when a new song or video starts.",
        ["Oynat / duraklat olunca göster"] = "Show on play / pause",
        ["Oynatma durumu değiştiğinde flyout açılır."] = "The flyout opens when the playback state changes.",
        ["Tam ekranda gizle"] = "Hide in fullscreen",
        ["Oyun, tam ekran video veya sunum sırasında otomatik gösterme yapılmaz. Kısayol yine çalışır."] =
            "No automatic showing during games, fullscreen video or presentations. The hotkey still works.",
        ["Duraklatılınca gizle"] = "Hide when paused",
        ["'Hep göster' modundaki platformlarda medya duraklatılırsa flyout kapanır."] = "On 'Always show' platforms the flyout closes when media is paused.",
        ["Her platform için süre ve davranışı 'Platformlar' sayfasından ayarlayabilirsiniz. 'Gizle' seçtiğiniz kaynaklar flyout'ta hiçbir şekilde görünmez."] =
            "Set the duration and behavior per platform on the 'Platforms' page. Sources set to 'Hide' never appear in the flyout.",

        // ---- Platformlar ----
        ["Her kaynak için ayrı davranış."] = "Separate behavior for each source.",
        ["Hep göster: medya varken sürekli açık kalır. Süreli: tetiklenince seçilen saniye kadar görünür. Gizle: bu kaynak flyout'ta hiç görünmez, bilgisi bile gösterilmez. Listede olmayan kaynaklar 'Diğer' kuralını kullanır."] =
            "Always show: stays open while media exists. Timed: shows for the chosen seconds when triggered. Hide: this source never appears in the flyout, not even its info. Sources not in the list use the 'Other' rule.",
        ["Hep göster"] = "Always show", ["Süreli"] = "Timed", ["Gizle"] = "Hide", ["sn"] = "s",
        ["Tümünü varsayılana döndür"] = "Reset all to default",
        ["Diğer"] = "Other", ["Medya Oynatıcı"] = "Media Player", ["Bilinmeyen kaynaklar"] = "Unknown sources",
        ["Uygulama"] = "App", ["Uygulama / Web"] = "App / Web", ["Oyun"] = "Game",

        // ---- Kısayollar ----
        ["Herhangi bir uygulama açıkken çalışır."] = "Works while any app is in the foreground.",
        ["Düğmeye tıklayıp yeni kombinasyona basın (en az bir Ctrl, Alt veya Win ile)."] = "Click a button and press the new combination (with at least one of Ctrl, Alt or Win).",
        ["Flyout'u göster / gizle"] = "Show / hide the flyout",
        ["Oynat / duraklat"] = "Play / pause",
        ["Sonraki şarkı"] = "Next song",
        ["Önceki şarkı"] = "Previous song",
        ["Sesi artır (çalan uygulama)"] = "Volume up (playing app)",
        ["Sesi azalt (çalan uygulama)"] = "Volume down (playing app)",
        ["Ayarları aç / kapat"] = "Open / close settings",
        ["Varsayılan: "] = "Default: ",
        ["Sıfırla"] = "Reset",
        ["Boşluk"] = "Space",
        ["Tuşlara basın…  (Esc: iptal)"] = "Press the keys…  (Esc: cancel)",
        ["En az bir Ctrl, Alt veya Win tuşunu da basılı tutun."] = "Also hold at least one of Ctrl, Alt or Win.",
        ["Tüm kısayollar etkin."] = "All hotkeys are active.",
        ["Tüm kısayollar etkin. ({0}: uyumluluk modu — başka bir uygulama da aynı kombinasyonu kullanıyor olabilir.)"] =
            "All hotkeys are active. ({0}: compatibility mode — another app may use the same combination.)",
        ["Kaydedilemeyen kısayol: {0}. Farklı bir kombinasyon deneyin."] = "Could not register: {0}. Try a different combination.",
        ["Bu kombinasyon zaten '{0}' için kullanılıyor."] = "This combination is already used for '{0}'.",
        ["Kısayol kaydedildi ve etkin."] = "Hotkey saved and active.",
        ["Kısayol kaydedildi (uyumluluk modu): başka bir uygulama da bu kombinasyonu kullanıyor olabilir."] =
            "Hotkey saved (compatibility mode): another app may use this combination too.",
        ["Kısayol kaydedilemedi, başka bir kombinasyon deneyin."] = "Could not save the hotkey, try another combination.",
        ["{0} kısayol kaydedilemedi. Ayarlar > Kısayollar bölümünden başka bir kombinasyon seçin (günlük: {1})."] =
            "{0} hotkey(s) could not be registered. Pick another combination in Settings > Hotkeys (log: {1}).",

        // ---- Hakkında ----
        ["Nasıl çalışır?"] = "How does it work?",
        ["Windows'un küresel medya kontrollerini dinler. Spotify, YouTube, Netflix, X, Instagram gibi kaynaklar tanınır; tanınmayanlar 'Diğer' kuralını kullanır. Web siteleri adres ve tarayıcı pencere başlığına bakılarak tahmin edilir."] =
            "It listens to the Windows global media controls. Sources such as Spotify, YouTube, Netflix, X and Instagram are recognized; unknown ones use the 'Other' rule. Websites are guessed from the page address and browser window title.",
        ["Günlük dosyası"] = "Log file",
        ["Bir sorun olursa buradaki log.txt'ye bakın."] = "If something goes wrong, check log.txt here.",
        ["Aç"] = "Open",

        // ---- Flyout ----
        ["Medya oynatılmıyor"] = "Nothing is playing",
        ["Bir şey oynatınca burada görünür"] = "Appears here when something plays",
        ["Medya yok"] = "No media",
        ["Örnek şarkı"] = "Sample song",
        ["Bu uygulamanın sesini aç / kapat"] = "Mute / unmute this app",
        ["Önceki"] = "Previous",
        ["Oynat / Duraklat"] = "Play / Pause",
        ["Sonraki"] = "Next",

        // ---- Tray ----
        ["Göster / Gizle"] = "Show / Hide",
        ["Çıkış"] = "Exit",

        // ---- Hata iletileri ----
        ["Bu sayfa yüklenemedi:"] = "This page could not be loaded:",
        ["Ayrıntı:"] = "Details:",
        ["Günlük:"] = "Log:",
        ["Ayarlar penceresi açılamadı:"] = "The settings window could not be opened:",
        ["QuickMedia başlatılamadı:"] = "QuickMedia could not start:",
        ["Medya servisi başlatılamadı:"] = "The media service could not start:",
    };
}
