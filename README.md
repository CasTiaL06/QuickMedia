# QuickMedia

FluentFlyout tarzı, sade ve şık bir Windows medya flyout'u. C# / WPF / .NET 8.

## Derleme
Gereken: Windows 10 (19041+) veya 11, .NET 8 SDK.

```powershell
cd QuickMedia
dotnet run
```

Taşınabilir tek dosya (kurulum gerektirmez):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Çıkan `QuickMedia.exe`'nin yanına boş bir `portable.flag` dosyası koyarsanız (veya Ayarlar > Genel > Taşınabilir mod)
ayarlar exe'nin yanındaki `settings.json`'da tutulur.

## Özellikler
- Tray ikonu: sağ tık > Göster/Gizle, Ayarlar, Çıkış; çift tık > Ayarlar
- 14 flyout stili: Klasik, Kompakt şerit, Minimal hap, Büyük kapak, Island, Geniş şerit, Kare kapak, Sadece yazı,
  Yuvarlak plak, Mini plak, Büyük plak, Plak şerit (dönen kapak + ilerleme halkası), Kare yüzer ve Poster.
  Hepsi sürüklenebilir; konum stil başına hatırlanır
  Geçiş: Ayarlar > Stil ve görünüm, tray menüsü > Stil veya flyout üzerinde sağ tık (sıradaki stil)
- 9 tema: Dark Glass, Light Glass, Midnight, Ocean, Forest, Rose, Mocha, Violet, Paper (her birinin kendi vurgu rengi var, istenirse değiştirilir); renkler `ThemeManager.cs` içinde
- Küresel kısayollar (Ayarlar > Kısayollar'dan değiştirilir, her biri için "Sıfırla" var):
  Alt+P göster/gizle · Ctrl+Alt+Boşluk oynat/duraklat · Ctrl+Alt+→ sonraki · Ctrl+Alt+← önceki ·
  Ctrl+Alt+↑/↓ çalan uygulamanın sesi · Ctrl+Alt+S ayarları aç/kapat.
- İlerleme çubuğuna tıklayıp sürükleyerek şarkı/videoda istediğin bölüme atlayabilirsin.
  RegisterHotKey başarısız olursa otomatik olarak klavye kancası (uyumluluk modu) kullanılır.
- Boyut: Ayarlar > Görünüm > Boyut veya flyout üstünde Ctrl + fare tekerleği
- Konum: 6 hazır konum veya flyout'u fareyle sürükleyin
- Ses aç/kapat + ses seviyesi: Windows ana sesine dokunmaz, yalnızca çalan uygulamanın ses oturumunu yönetir
  (tarayıcıda tüm sekmeler tek oturumdur). Ayarlar > Stil ve görünüm > Öğeler'den kapatılabilir
- Platform bazlı kurallar: Spotify, YouTube, YouTube Music, Apple Music, Netflix, Prime Video, Disney+, Twitch,
  X, Facebook, Instagram, TikTok, SoundCloud, Deezer, VLC, Medya Oynatıcı ve "Diğer"
  (her biri: Hep göster / Süreli (saniye) / Gizle)

- Üstte sabitle: flyout köşesindeki küçük iğne düğmesi (üstüne gelince görünür), tray menüsü veya Ayarlar > Genel
- "Gizle" seçilen kaynaklar flyout'ta hiçbir şekilde (bilgisi bile) görünmez

- Dil: Türkçe / English / Otomatik (Ayarlar > Genel > Dil). Çeviriler `Loc.cs` içindeki tabloda
- Platform simgeleri koddan çizilmiş sade marka logolarıdır. Resmi logoları kullanmak için exe'nin yanına
  `Icons` klasörü açıp platform kimliğiyle PNG koyun (spotify.png, youtube.png, twitch.png, x → twitter.png ...);
  bunlar otomatik olarak öncelik alır
- Tüm medya oturumları ayrı izlenir: ör. Spotify "Hep göster", YouTube "Gizle" ise, YouTube izlerken de flyout
  Spotify'ı göstermeye devam eder; kısayollar (oynat/sonraki/önceki/ses) da gösterilen oturum üzerinde çalışır
- Flyout yalnızca gösterilecek medya varken açılır; medya yokken ya da kaynak "Gizle" ise hiç görünmez

- Görev çubuğu modu (Ayarlar > Görev çubuğu): çalan şarkıyı görev çubuğunun boş sol kısmında küçük bir kontrol olarak gösterir.
  Boş alanı UI Automation ile otomatik bulur (Widgets düğmesinin sağına sığar); kapak, kayan yazı, çalarken hareket eden
  çubuklar, alt çizgide ilerleme, üzerine gelince kontroller. Tıkla: flyout kontrolün üstünden açılır · Tekerlek: çalan
  uygulamanın sesi · Sağ tık: menü
- "Yalnızca görev çubuğunda göster": flyout hiç açılmaz, sadece görev çubuğu kontrolü görünür (tıklama = oynat/duraklat,
  Alt+P = kontrolü geçici gizle / geri getir)

## Notlar
- Web siteleri (YouTube, Instagram...) tarayıcı pencere başlığına bakılarak tahmin edilir; arka plan sekmesinde
  çalan medya bazen "Diğer" olarak görünebilir.
- Yeni platform eklemek için `Platforms.cs` içindeki listeye bir satır eklemeniz yeterli.

## Sorun giderme
Hata günlüğü: `%AppData%\QuickMedia\log.txt` (Ayarlar > Hakkında > Günlük dosyası > Aç).
Kısayol durumu Ayarlar > Kısayollar'da yazar; ayrıntılar log.txt'dedir (1409 = başka uygulama kullanıyor).
