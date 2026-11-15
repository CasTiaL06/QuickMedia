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
- 5 flyout stili: Klasik, Kompakt şerit, Minimal hap, Büyük kapak, Island (üst orta, fareyle genişler).
  Geçiş: Ayarlar > Stil ve görünüm, tray menüsü > Stil veya flyout üzerinde sağ tık (sıradaki stil)
- Temalar: Dark Glass, Light Glass, Midnight; renkler `ThemeManager.cs` içinde
- Küresel kısayollar (Ayarlar > Kısayollar'dan değiştirilir, her biri için "Sıfırla" var):
  Alt+P göster/gizle · Ctrl+Alt+Boşluk oynat/duraklat · Ctrl+Alt+→ sonraki · Ctrl+Alt+← önceki.
  RegisterHotKey başarısız olursa otomatik olarak klavye kancası (uyumluluk modu) kullanılır.
- Boyut: Ayarlar > Görünüm > Boyut veya flyout üstünde Ctrl + fare tekerleği
- Konum: 6 hazır konum veya flyout'u fareyle sürükleyin
- Ses aç/kapat + ses seviyesi: Windows ana sesine dokunmaz, yalnızca çalan uygulamanın ses oturumunu yönetir
  (tarayıcıda tüm sekmeler tek oturumdur). Ayarlar > Stil ve görünüm > Öğeler'den kapatılabilir
- Platform bazlı kurallar: Spotify, YouTube, YouTube Music, Apple Music, Netflix, Prime Video, Disney+, Twitch,
  X, Facebook, Instagram, TikTok, SoundCloud, Deezer, VLC, Medya Oynatıcı ve "Diğer"
  (her biri: Hep göster / Süreli (saniye) / Gizle)

## Notlar
- Web siteleri (YouTube, Instagram...) tarayıcı pencere başlığına bakılarak tahmin edilir; arka plan sekmesinde
  çalan medya bazen "Diğer" olarak görünebilir.
- Yeni platform eklemek için `Platforms.cs` içindeki listeye bir satır eklemeniz yeterli.

## Sorun giderme
Hata günlüğü: `%AppData%\QuickMedia\log.txt` (Ayarlar > Hakkında > Günlük dosyası > Aç).
Kısayol durumu Ayarlar > Kısayollar'da yazar; ayrıntılar log.txt'dedir (1409 = başka uygulama kullanıyor).
