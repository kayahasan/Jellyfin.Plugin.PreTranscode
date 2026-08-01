# Jellyfin PreTranscode Plugin

Kutuphanedeki uyumsuz video dosyalarini tarar, secerek veya zamanli gorev ile
hedef codec'e (HEVC/H.264) yeniden kodlayan Jellyfin plugin'i. VAAPI, Intel QSV
ve NVIDIA NVENC donanim hizlandirmasini destekler.

Orijinal dosyalara dokunmaz; her dosyanin yanina `<isim>-optimized.mkv` uretir.

## Kurulum

### Yontem 1: Plugin Repository (Onerilen)

Jellyfin Dashboard > Plugins > Repositories > Add:

```
https://raw.githubusercontent.com/kayahasan/Jellyfin.Plugin.PreTranscode/main/manifest.json
```

Sonra Plugins > Catalog > PreTranscode > Install

### Yontem 2: Manuel

```bash
# Release'dan zip indir
wget https://github.com/kayahasan/Jellyfin.Plugin.PreTranscode/releases/latest/download/Jellyfin.Plugin.PreTranscode.*.zip

# Plugin klasorune cikar
mkdir -p /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreTranscode
unzip Jellyfin.Plugin.PreTranscode.*.zip -d /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreTranscode/

# Jellyfin'i yeniden baslat
systemctl restart jellyfin
```

Windows icin:
```
C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.PreTranscode\
```

## Ozellikler

- **Kutuphane taramasi** — uyumsuz dosyalar otomatik tespit edilir
- **Gorsel arayuz** — film ve diziler poster ile listelenir, sezon/bolum bazli gruplama
- **Donanim hizlandirma** — VAAPI (Intel/AMD), QSV (Intel), NVENC (NVIDIA)
- **Canli ilerleme takibi** — dosya boyutu bazli yuzde gosterimi
- **Zamanli gorev** — arka planda otomatik kodlama
- **Cross-platform** — Windows ve Linux (Proxmox LXC dahil)
- **10-bit kaynak destegi** — otomatik format donusumu (p010 -> nv12)
- **Izin guvenligi** — yazma testi ve Unix permission kopyalama

## Derleme

```bash
# .NET 9 SDK gerekli
dotnet restore
dotnet build -c Release
```

Cikti: `bin/Release/net9.0/Jellyfin.Plugin.PreTranscode.dll`

## Release Yapma

```bash
# Versiyonu .csproj'de guncelle
# <Version>1.0.0.3</Version>

# manifest.json'a yeni versiyon entry'si ekle
# Commit, tag ve push
git add -A && git commit -m "bump: version X.X.X.X - aciklama"
git tag vX.X.X.X
git push origin main && git push origin vX.X.X.X
```

GitHub Actions otomatik olarak:
1. Plugin'i build eder
2. ZIP olusturur
3. GitHub Release acar

## Yapilandirma

Dashboard > Plugins > PreTranscode'dan ayarlari yapilandir,
sol menuden "PreTranscode" sayfasini ac, islemek istedigim
film/dizileri secip "Secilenleri Kodla"ya bas.

## Uninstall

Windows'ta DLL process tarafindan kullanildigi icin otomatik silme calismaz.
Jellyfin'i durdurup klasoru manuel silin:

```powershell
Stop-Process -Name jellyfin -Force
rmdir /s /q "C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.PreTranscode"
```

Linux'ta:
```bash
systemctl stop jellyfin
rm -rf /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreTranscode
```

## Sistem gereksinimleri

- Jellyfin 10.11.x (.NET 9)
- FFmpeg (VAAPI/QSV/NVENC destegi ile)
- Donanim hizlandirma icin uygun GPU ve suruculer
