# Jellyfin PreTranscode Plugin

Kütüphaneyi arka planda, sen tanımladığın hedef codec/kalite/GPU ayarına göre
önceden kodlayan bir Jellyfin scheduled task plugin'i. Orijinal dosyalara
dokunmaz; her dosyanın yanına `<isim>-optimized.mkv` üretir.

## Kurulum

### Yöntem 1: Plugin Repository (Önerilen)

Jellyfin Dashboard > Plugins > Repositories > Add:

```
https://raw.githubusercontent.com/kayah/Jellyfin.Plugin.PreTranscode/main/manifest.json
```

Sonra Plugins > Catalog > PreTranscode > Install

### Yöntem 2: Manuel

```bash
# Release'dan zip indir
wget https://github.com/kayah/Jellyfin.Plugin.PreTranscode/releases/latest/download/PreTranscode_*.zip

# Plugin klasörüne çıkar
mkdir -p /var/lib/jellyfin/plugins/PreTranscode
unzip PreTranscode_*.zip -d /var/lib/jellyfin/plugins/PreTranscode/

# Jellyfin'i yeniden başlat
systemctl restart jellyfin
```

Windows için:
```
C:\Users\{Kullanici}\AppData\Local\jellyfin\plugins\PreTranscode\
```

## Derleme

```bash
# .NET 9 SDK gerekli
dotnet restore
dotnet build -c Release
```

Çıktı: `bin/Release/net9.0/Jellyfin.Plugin.PreTranscode.dll`

## Release Yapma

```bash
# Versiyonu .csproj'de güncelle
# <Version>1.0.0.1</Version>

# Tag oluştur ve push et
git tag v1.0.0.1
git push origin v1.0.0.1
```

GitHub Actions otomatik olarak:
1. Plugin'i build eder
2. ZIP oluşturur
3. GitHub Release açar
4. `manifest.json`'ü günceller

## Yapılandırma

Dashboard > Plugins > PreTranscode'dan ayarları yapılandır,
sol menüden "PreTranscode Kütüphane" sayfasını aç, işlemek istediğin
film/dizileri seçip "Seçilenleri Kodla"ya bas.

## Bilinen sınırlamalar

1. **Alternate version otomatik bağlama yok.** Şu an yalnızca dosyayı
   üretiyor; Jellyfin'in "Merge Versions" mekanizmasına otomatik kaydını
   ayrıca yazman gerekir.
2. **HDR/tonemap desteği yok.** 10-bit HDR kaynaklar için `-vf` filtresine
   tonemap eklenmesi gerekir.
3. **Progress/iptal** temel seviyede; büyük kütüphanelerde dosya bazlı
   ilerleme eklemek daha iyi UX verir.
