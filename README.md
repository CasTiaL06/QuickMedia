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
- Dark Glass (+ Light Glass) tema; renkler `ThemeManager.cs` içinde
- Küresel kısayol (varsayılan Alt+P): Ayarlar > Kısayol'dan değiştirilir, "Sıfırla" ile Alt+P'ye döner
- Boyut: Ayarlar > Görünüm > Boyut veya flyout üstünde Ctrl + fare tekerleği
- Konum: 6 hazır konum veya flyout'u fareyle sürükleyin
- Ses aç/kapat + ses seviyesi (Ayarlar > Görünüm'den kapatılabilir)
- Platform bazlı kurallar: Spotify, YouTube, YouTube Music, Apple Music, Netflix, Prime Video, Disney+, Twitch,
  X, Facebook, Instagram, TikTok, SoundCloud, Deezer, VLC, Medya Oynatıcı ve "Diğer"
  (her biri: Hep göster / Süreli (saniye) / Gizle)

## Notlar
- Web siteleri (YouTube, Instagram...) tarayıcı pencere başlığına bakılarak tahmin edilir; arka plan sekmesinde
  çalan medya bazen "Diğer" olarak görünebilir.
- Yeni platform eklemek için `Platforms.cs` içindeki listeye bir satır eklemeniz yeterli.
