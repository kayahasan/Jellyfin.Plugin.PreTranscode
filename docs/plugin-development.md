# Jellyfin Plugin Geliştirme Rehberi

> Resmi kaynaklardan derlenmiştir: [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template) · [Jellyfin Docs](https://jellyfin.org/docs/)

## İçindekiler

- [Gereksinimler](#gereksinimler)
- [Hızlı Başlangıç](#hizli-baslangic)
- [Proje Oluşturma](#proje-olusturma)
- [Temel Sınıflar](#temel-siniflar)
- [Plugin Bilgisi](#plugin-bilgisi)
- [Fonksiyonellik Ekleme](#fonksiyonellik-ekleme)
- [Yaygın Arayüzler](#yaygin-arayuzler)
- [Core Servis Enjeksiyonu](#core-servis-enjeksiyonu)
- [Yayın ve Kurulum](#yayin-ve-kurulum)
- [Debug Kurulumu](#debug-kurulumu)
- [Lisanslama](#lisanslama)

---

## Gereksinimler

- **[.NET SDK 9.0](https://dotnet.microsoft.com/en-us/download/dotnet)**
- Bir editör:
  - [Visual Studio Code](https://code.visualstudio.com) (ücretsiz)
  - [Visual Studio Community](https://visualstudio.microsoft.com/downloads) (ücretsiz)
  - [MonoDevelop](https://www.monodevelop.com) (ücretsiz)

Jellyfin plugin'leri .NET standard framework ile yazılır. Örnekler C# dilindedir ancak F#, Visual Basic ve IronPython da net9.0'a derlenirse uyumludur.

---

## Hızlı Başlangıç

### Seçenek 1: Örnek Projeyi İndir

```bash
git clone https://github.com/jellyfin/jellyfin-plugin-template.git
# Jellyfin.Plugin.Template klasörünü IDE'de aç → Adım 3'e geç
```

### Seçenek 2: dotnet new template

```bash
dotnet new -i /path/to/dotnet-template/content
dotnet new Jellyfin-plugin -name MyPlugin
```

### Seçenek 3: Sıfırdan Başla

Aşağıdaki adımları takip et.

---

## Proje Oluşturma

```bash
dotnet new classlib -f net9.0 -n MyJellyfinPlugin
```

Jellyfin ortak kütüphanelerini ekle:

```bash
dotnet add package Jellyfin.Model
dotnet add package Jellyfin.Controller
```

Otomatik oluşturulan `Class1.cs` dosyasını sil.

`.csproj` dosyasındaki package referanslarını düzenle — **bu adım atlanırsa plugin kaydolmaz:**

```xml
<ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="10.11.3">
        <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Jellyfin.Model" Version="10.11.3">
        <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
</ItemGroup>
```

> **Önemli:** Package versiyonu, kurulu Jellyfin server versiyonu ile eşleşmelidir. Eşleşmezse plugin "NotSupported" olarak görünür.

---

## Temel Sınıflar

### PluginConfiguration

`Configuration` adında bir klasör oluştur, içine `PluginConfiguration.cs` koy:

```csharp
using MediaBrowser.Model.Plugins;

namespace MyJellyfinPlugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    // Ayarlar buraya
}
```

### Plugin

Proje kökünde `Plugin.cs` oluştur:

```csharp
using MediaBrowser.Common.Plugins;
using MyJellyfinPlugin.Configuration;

namespace MyJellyfinPlugin;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    public override string Name => "My Jellyfin Plugin";
    public override Guid Id => Guid.Parse("YOUR-GUID-HERE");
}
```

> Eğer `PluginConfiguration` sınıfına farklı isim verdiysen, `<>` arasına o ismi yaz.

---

## Plugin Bilgisi

### GUID Oluşturma

- **Windows:** PowerShell → `New-Guid` veya `[guid]::NewGuid()`
- **Linux/macOS:** `uuidgen` veya:
  ```bash
  od -x /dev/urandom | head -n1 | awk '{OFS="-"; srand($6); sub(/./,"4",$5); sub(/./,substr("89ab",1+rand()*4,1),$6); print $2$3,$4,$5,$6,$7$8$9}'
  ```

GUID'yi `Guid.Parse("")` içine yerleştir.

---

## Fonksiyonellik Ekleme

### Derleme ve Kurulum

```bash
dotnet publish -c Release
```

Oluşan `.dll` dosyasını plugin klasörüne kopyala:

```
Jellyfin veri klasörü/plugins/PluginAdi/PluginAdi.dll
```

- **Windows varsayılan:** `C:\Users\{Kullanici}\AppData\Local\jellyfin\plugins\`
- **Linux varsayılan:** `~/.local/share/jellyfin/plugins/`

Jellyfin Server'ı yeniden başlat.

### 4a. Arayüzler Uygula

Jellyfin, plugin'lerde uyguladığın arayüzleri otomatik keşfeder ve enjekte eder. Yaygın arayüzler:

| Arayüz | Açıklama |
|---|---|
| `IAuthenticationProvider` | Kullanıcı adı/şifre ile kimlik doğrulama sağlayıcısı |
| `IBaseItemComparer` | Medya sıralama kuralları |
| `IIntroProvider` | Medya öncesi başka medya oynatma (fragman, bumper vb.) |
| `IItemResolver` | Özel medya tipleri tanımlama |
| `ILibraryPostScanTask` | Kütüphane taraması sonrası tetiklenen görev |
| `IMetadataSaver` | Jellyfin'in yazabileceği metadata standardı |
| `IResolverIgnoreRule` | Resolver'ın görmezden geleceği alt yollar |
| `IScheduledTask` | Dashboard'da görünen planlanmış görev |

### 4b. Özel Fonksiyonellik Arayüzleri

| Arayüz | Açıklama |
|---|---|
| `IPluginConfigurationPage` | Dashboard'da plugin ayar sayfası |
| `IPluginServiceRegistrator` | Server başlangıcında DI container'a servis ekleme |
| `IHostedService` | Arka plan görevi — başlangıçta çalışır, bellekte kalır |
| `ControllerBase` | Özel REST API endpoint'leri |

> **Not:** Ana plugin sınıfı (`IBasePlugin`) aynı zamanda `IHostedService` olamaz. `IHostedService` kullanan sınıfları `IPluginServiceRegistrator` ile Jellyfin'e bildir.

---

## Core Servis Enjeksiyonu

Plugin constructor'ına parametre olarak ekleyebileceğin Jellyfin core servisleri:

| Servis | Açıklama |
|---|---|
| `IBlurayExaminer` | Blu-ray klasörlerini inceleme |
| `IDtoService` | Data transport object oluşturma |
| `ILibraryManager` | Medya kütüphanelerine direkt erişim |
| `ILocalizationManager` | Çeviri, rating sistemleri, birimler |
| `INetworkManager` | Server ağ durumu bilgisi |
| `IServerApplicationPaths` | Çalışan server'ın yolları |
| `IServerConfigurationManager` | Server yapılandırma okuma/yazma |
| `ITaskManager` | Planlanmış görevleri çalıştırma/yönetme |
| `IUserManager` | Kullanıcı bilgisi ve kütüphane verisi |
| `IXmlSerializer` | Ana XML serializer |
| `IZipClient` | Zip sıkıştırma/çözme |

---

## Yayın ve Kurulum

### Manuel Kurulum

1. `dotnet publish -c Release`
2. `.dll` dosyasını `plugins/` klasörüne kopyala
3. Jellyfin Server'ı yeniden başlat

### Plugin Repozitorü

Özel bir plugin repository oluştur: [jellyfin.org/posts/plugin-updates](https://jellyfin.org/posts/plugin-updates/)

### Geliştirme Modu

Geliştirme sırasında symlink veya direkt kopyalama kullan:

```bash
# Linux
ln -s /path/to/your/plugin/bin/Debug/net9.0/publish/ ~/.local/share/jellyfin/plugins/YourPlugin/
```

---

## Debug Kurulumu

### Genel Süreç

1. Debug modunda plugin'i derle
2. Plugin klasörünü oluştur (yoksa)
3. Plugin'i server'ın plugin klasörüne kopyala
4. Debug işlevi için `.pdb` dosyasını da kopyala
5. Çalışan dizini Jellyfin Server'ın dizinine ayarla
6. Server'ı başlat

### Visual Studio

1. Solution'a sağ tık → Add → Existing Project...
2. Jellyfin kurulum klasöründeki `Jellyfin.exe`'yi seç
3. Yeni projeye sağ tık → Set as Startup Project
4. Sağ tık → Properties → Attach: No

### Visual Studio Code

`.vscode/settings.json`:

```jsonc
{
    "jellyfinDir": "${workspaceFolder}/../jellyfin/Jellyfin.Server",
    "jellyfinWebDir": "${workspaceFolder}/../jellyfin-web",
    "jellyfinDataDir": "${env:LOCALAPPDATA}/jellyfin",
    "pluginName": "Jellyfin.Plugin.Template"
}
```

`.vscode/launch.json`:

```jsonc
{
    "version": "0.2.0",
    "configurations": [
        {
            "type": "coreclr",
            "name": "Launch",
            "request": "launch",
            "preLaunchTask": "build-and-copy",
            "program": "${config:jellyfinDir}/bin/Debug/net9.0/jellyfin.dll",
            "args": ["--webdir", "${config:jellyfinWebDir}/dist/"],
            "cwd": "${config:jellyfinDir}"
        }
    ]
}
```

`.vscode/tasks.json` — `build-and-copy` görevi:

```jsonc
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build-and-copy",
            "dependsOrder": "sequence",
            "dependsOn": ["build", "make-plugin-dir", "copy-dll"]
        },
        {
            "label": "build",
            "command": "dotnet",
            "type": "shell",
            "args": [
                "publish",
                "${workspaceFolder}/${config:pluginName}.sln",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "group": "build",
            "presentation": { "reveal": "silent" },
            "problemMatcher": "$msCompile"
        },
        {
            "label": "make-plugin-dir",
            "type": "shell",
            "command": "mkdir",
            "args": ["-Force", "-Path", "${config:jellyfinDataDir}/plugins/${config:pluginName}/"]
        },
        {
            "label": "copy-dll",
            "type": "shell",
            "command": "cp",
            "args": [
                "./${config:pluginName}/bin/Debug/net9.0/publish/*",
                "${config:jellyfinDataDir}/plugins/${config:pluginName}/"
            ]
        }
    ]
}
```

---

## Plugin Architecture (Mimari)

Jellyfin plugin'leri .NET assembly'leridir ve `IPlugin` arayüzünü uygular. Yapabilecekleri:

- Metadata provider ekleme (filmler, diziler, müzik)
- Özel kimlik doğrulama provider'ı
- Yeni API endpoint'leri
- Server event'lerine tepki verme
- Özel yapılandırma sayfaları
- Medya tarama ve organizasyon genişletme

### Plugin Yapısı

```
plugins/
  └── YourPlugin/
      ├── YourPlugin.dll
      ├── YourPlugin.pdb          (debug için)
      └── config.json             (otomatik oluşturulur)
```

### BasePlugin<TConfigurationType> Özellikleri

- **Configuration Management**: Otomatik XML serialization/deserialization
- **Data Folder Access**: Plugin'e özel veri depolama
- **Lifecycle Hooks**: `OnUninstalling()` metodu
- **Plugin Info**: Otomatik `PluginInfo` üretimi

---

## Plugin API Yönetimi

`PluginsController` üzerinden yönetilen endpoint'ler:

| Endpoint | Açıklama |
|---|---|
| `GET /Plugins` | Tüm kurulu plugin'leri listele |
| `GET /Plugins/{id}/Configuration` | Mevcut yapılandırmayı getir |
| `POST /Plugins/{id}/Configuration` | Yapılandırmayı güncelle |
| `POST /Plugins/{id}/Enable` | Plugin'i etkinleştir |
| `POST /Plugins/{id}/Disable` | Plugin'i devre dışı bırak |
| `DELETE /Plugins/{id}` | Plugin'i kaldır |

---

## Örnek Plugin'ler

Jellyfin kaynak kodundaki built-in plugin'lere göz at:

- **TMDb Plugin**: `MediaBrowser.Providers/Plugins/Tmdb/` — Metadata provider
- **MusicBrainz Plugin**: `MediaBrowser.Providers/Plugins/MusicBrainz/` — Müzik metadata
- **Studio Images Plugin**: `MediaBrowser.Providers/Plugins/StudioImages/` — Görsel provider
- **AudioDB Plugin**: `MediaBrowser.Providers/Plugins/AudioDb/` — Ses metadata

---

## Test

```bash
dotnet test
```

---

## Lisanslama

Jellyfin plugin'leri GPLv3 lisanslı NuGet paketlerine bağlanır. Bu nedenle derlenmiş plugin binarysi de GPLv3 lisansı altında olur.

- **Varsayılan:** GPLv3 (template ile gelir)
- **Alternatif:** GPLv3 ile uyumlu permissive lisans (MIT, Apache 2.0 vb.)
- **Yasak:** Kapalı kaynak / proprietary plugin'ler dağıtılamaz

---

## Kaynaklar

- [Resmi Plugin Template](https://github.com/jellyfin/jellyfin-plugin-template)
- [Jellyfin Docs — Plugin Development](https://jellyfin-jellyfin.mintlify.app/development/plugin-development)
- [Jellyfin Kaynak Kodu](https://github.com/jellyfin/jellyfin)
- [Katkı Rehberi](https://docs.jellyfin.org/general/contributing/index.html)
