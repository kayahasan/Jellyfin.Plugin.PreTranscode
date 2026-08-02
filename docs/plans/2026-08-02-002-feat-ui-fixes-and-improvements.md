---
name: UI Düzeltmeleri ve İyileştirmeler
created: 2026-08-02
status: active
type: implementation-plan
---

# UI Düzeltmeleri ve İyileştirmeler

## Problem

Mevcut UI'da kullanıcı deneyimini olumsuz etkileyen 5 temel sorun var:

1. **Toplu işlemlerde seçim görünmüyor:** "HEVC→H.264" gibi butonlara basınca hangi dosyaların seçildiği görsel olarak belli olmuyor
2. **Seçilenleri Kodla butonu çok uzak:** Liste uzun olunca aşağıda seçilen dosyalarla yukarıdaki buton arasında çok mesafe var
3. **Türkçe karakter sorunları:** "Seçilenleri Kodla", "İşler" gibi yerlerde Türkçe karakterler düzgün render edilemiyor
4. **İşler sekmesi güncellenmiyor:** İş tamamlandığında veya iptal edildiğinde job öylece kalıyor, sayfa yenilemeden düzelmiyor
5. **Encoder ayarları gereksiz:** Donanım hızlandırma seçenekleri zaten Jellyfin'de var, plugin'de tekrar göstermeye gerek yok

## Hedef

Her sorun için net, test edilebilir düzeltmeler. Her birini ayrı versiyon olarak release et.

## Kapsam

- `Configuration/list.html` (ana UI)
- `Controllers/PreTranscodeController.cs` (API endpoint'leri)
- `Services/JobQueueService.cs` (job yönetimi)
- `Configuration/PluginConfiguration.cs` (ayarlar)

---

## U1: Toplu İşlem Seçimlerinin Görsel Gösterimi

**Problem:** Bulk butonlara basınca dosyalar seçiliyor ama kartlar üzerinde seçim durumu görsel olarak belli olmuyor.

**Çözüm:**
- Bulk butona basınca seçilen her kartın üzerine yeşil/kırmızı seçim overlay'i gelmeli
- Seçim durumu zaten `sel` Set'inde tutuluyor, sadece render'da göstermek lazım
- Seçili kartlar: sol üst köşede dolu kutucuk (✓) + hafif arka plan rengi

**Detaylar:**
- `render()` fonksiyonunda her kart için `sel.has(id)` kontrolü
- Seçiliyse: `.cardBox` üzerine `position: relative;` + sol üst köşeye `::after` ile ✓ ikonu
- Arka plan: `background: rgba(229,9,20,.12);` (mevcut seçim rengi)

**Dosyalar:**
- `Configuration/list.html` → `render()` fonksiyonu

**Test:**
1. "HEVC→H.264" butonuna bas
2. Tüm HEVC kartlarında sol üst köşede ✓ ikonu görülmeli
3. Arka plan rengi hafif kırmızı olmalı
4. "Tümünü Seç" ile aynı görünüm olmalı

**Versiyon:** v1.0.0.23

---

## U2: Yapışkan "Seçilenleri Kodla" Butonu

**Problem:** Liste uzun olduğunda aşağıda gezinirken "Seçilenleri Kodla" butonu çok yukarıda kalıyor.

**Çözüm:** Sticky header - sayfa kaydırılsa bile butonlar en üstte sabit kalsın.

**Detaylar:**
- Buton bar'ı (`#RefreshBtn`, `#SelectAllBtn`, `#EncodeBtn` vb.) `position: sticky; top: 0; z-index: 100;` yap
- Arka plan: `background: #1a1a1a;` (sayfa kaydırılınca arkadaki içerik görünmesin)
- Alt kenar: `box-shadow: 0 2px 8px rgba(0,0,0,.4);` (derinlik hissi)
- Filtreler ve toplu işlemler de sticky olmalı

**Dosyalar:**
- `Configuration/list.html` → Library tab header bölümü

**Test:**
1. Uzun bir liste yükle
2. Sayfayı aşağı kaydır
3. Buton bar ve filtreler en üstte sabit kalmalı
4. Arka plan rengi içeriği gizlemeli

**Versiyon:** v1.0.0.24

---

## U3: Türkçe Karakter Sorunları

**Problem:** "Seçilenleri Kodla", "İşler" gibi Türkçe karakterler düzgün render edilemiyor.

**Kök Sebep:** HTML entity'ler yanlış kullanılıyor. `&#305;` yerine `&#130;` gibi hatalar olabilir.

**Çözüm:**
- Tüm Türkçe metinleri düz Unicode olarak yaz (`Seçilenleri Kodla`, `İşler`, `İptal` vb.)
- HTML entity kullanma, modern tarayıcılar Unicode'u düzgün render eder
- `list.html` başında zaten `<meta charset="utf-8">` var

**Değiştirilecek Metinler:**
- `Se&#305;ilenleri Kodla` → `Seçilenleri Kodla`
- `Is&#305;ler` → `İşler`
- `Se&#305;enekler` → `Seçenekler`
- `Iptal` → `İptal`
- `Yenile` → `Yenile`
- `T&#252;m&#252;n&#252; Se&#305;` → `Tümünü Seç`
- `Kald&#305;r` → `Kaldır`
- `Orijinali de&#287;i&#351;tir` → `Orijinali değiştir`
- `geri al&#305;namaz` → `geri alınamaz`
- `Donan&#305;m H&#305;zland&#305;rma` → `Donanım Hızlandırma`
- vs.

**Dosyalar:**
- `Configuration/list.html` → tüm Türkçe metinler

**Test:**
1. Her tab'ı aç
2. Türkçe karakterler düzgün görünüyor mu kontrol et
3. Ö, Ü, Ş, İ, Ğ, Ç harfleri doğru mu?

**Versiyon:** v1.0.0.25

---

## U4: İşler Sekmesi - Gerçek Zamanlı Güncelleme ve Loglama

**Problem:** İş tamamlandığında veya iptal edildiğinde job kartı öylece kalıyor. Sayfa yenilemeden güncellenmiyor.

**Çözüm:**
1. **Durum güncellemesi:** Polling interval'de job durumu değişirse kartı güncelle veya kaldır
2. **Tamamlanan işler paneli:** İşler bittiğinde "Tamamlanan İşler" bölümünde göster
3. **Loglama:** Her iş için kısa özet göster (dosya adı, codec dönüşümü, tasarruf)

**Detaylar:**

### 4.1 Gerçek Zamanlı Güncelleme
- `pollJobs()` fonksiyonu zaten çalışıyor (2 saniyede bir)
- Job durumu `Completed`, `Failed`, `Cancelled` olunca:
  - Aktif işlerden kaldır
  - "Tamamlanan İşler" bölümüne ekle
  - Kart rengi: ✅ Yeşil (Completed), ❌ Kırmızı (Failed), ⚠️ Turuncu (Cancelled)

### 4.2 Tamamlanan İşler Paneli
- Job panelinde 2 bölüm:
  - **Aktif İşler** (üstte, animasyonlu progress bar)
  - **Tamamlanan İşler** (altta, statik kartlar)
- Her kartta:
  - Dosya adı
  - Codec dönüşümü: `HEVC → H.264`
  - Boyut: `4.2 GB → 2.1 GB (2.1 GB tasarruf)`
  - Durum ikonu: ✅ / ❌ / ⚠️
  - Zaman: `Tamamlandı: 14:32`

### 4.3 Loglama
- `JobInfo` DTO'suna `ResolutionConversion` ve `SavedSpaceMb` ekle
- `pollJobs()` sonucu `completedJobs` array'i dönsün
- UI'da ayrı bölümde göster

**Dosyalar:**
- `Configuration/list.html` → `pollJobs()` ve `renderJobs()` fonksiyonları
- `Controllers/PreTranscodeController.cs` → `GetJobs` endpoint'i (`completedJobs` ekle)

**Test:**
1. Bir dosya encode et
2. İş tamamlanınca aktif işlerden silinsin
3. "Tamamlanan İşler" bölümünde görünsün
4. Tasarruf bilgisi doğru mu kontrol et
5. Sayfayı yenilemeden güncelleme olmalı

**Versiyon:** v1.0.0.26

---

## U5: Encoder Ayarlarını Jellyfin'den Al

**Problem:** Plugin'de "Donanım Hızlandırma" seçeneği var ama bu zaten Jellyfin'de ayarlanmış. Kullanıcı iki yerde aynı ayarı yapmamalı.

**Çözüm:**
- Plugin, Jellyfin'in mevcut transcode ayarlarını otomatik kullanmalı
- UI'dan "Donanım Hızlandırma", "Render Device Path" gibi ayarları kaldır
- Sadece şunları bırak:
  - **Hedef Codec:** H.264 / HEVC / VP9
  - **Kalite:** QP değeri
  - **Maksimum Çözünürlük:** 4K / 1080p / 720p
  - **Minimum Dosya Boyutu:** GB cinsinden
  - **Eşzamanlı İş Sayısı:** 1-4 arası

**Detaylar:**

### 5.1 Jellyfin Ayarlarını Okuma
- `EncoderManager` veya `IMediaEncoder` servisi üzerinden Jellyfin'in mevcut ayarlarını al
- `MediaEncoderPath`, `HwAccel`, `VaapiDevice` gibi değerleri oku
- Plugin kendi ayarlarını değil, Jellyfin'in ayarlarını kullan

### 5.2 UI Temizliği
- Kaldırılacaklar:
  - `HardwareAcceleration` dropdown
  - `RenderDevicePath` input
- Kalacaklar:
  - `TargetVideoCodec` dropdown (H.264 / HEVC / VP9)
  - `Quality` input (QP değeri)
  - `MaxWidth` input (maksimum çözünürlük)
  - `MinimumFileSizeMb` input
  - `MaxConcurrentJobs` input
  - `ReplaceOriginal` checkbox

**Dosyalar:**
- `Configuration/list.html` → Settings tab
- `Configuration/PluginConfiguration.cs` → `HardwareAcceleration`, `RenderDevicePath` kaldır
- `EncoderManager.cs` → Jellyfin ayarlarını kullanacak şekilde güncelle

**Test:**
1. Jellyfin'de VAAPI ayarlı olsun
2. Plugin'de ayarlar sayfasında "Donanım Hızlandırma" yok
3. Encode işlemi Jellyfin'in VAAPI ayarını kullanıyor mu kontrol et

**Versiyon:** v1.0.0.27

---

## Versiyon Planı

| Sıra | Unit | Açıklama | Versiyon |
|------|------|----------|----------|
| 1 | U1 | Toplu seçim görsel gösterimi | v1.0.0.23 |
| 2 | U2 | Yapışkan buton bar | v1.0.0.24 |
| 3 | U3 | Türkçe karakter düzeltmeleri | v1.0.0.25 |
| 4 | U4 | İşler sekmesi güncelleme + loglama | v1.0.0.26 |
| 5 | U5 | Encoder ayarlarını Jellyfin'den al | v1.0.0.27 |

## Bağımlılıklar

- U1 → Bağımlılık yok
- U2 → Bağımlılık yok
- U3 → Bağımlılık yok (tüm dosyalarda düzeltilebilir)
- U4 → U3'ten önce yapılabilir ama birlikte daha iyi (Türkçe metinler dahil)
- U5 → Bağımlılık yok

## Riskler

1. **U5 - Jellyfin uyumluluğu:** Jellyfin'in transcode ayarlarını doğru okuyamama riski var
   - Mitigasyon: Fallback olarak plugin'in mevcut ayarlarını kullan
2. **U4 - Performans:** Tamamlanan işler listesi çok uzun olabilir
   - Mitigasyon: Son 20 işi göster, geri kalanı gizle

## Notlar

- Her versiyon için: Build → Tag → Release → Checksum doğrula → Manifest güncelle
- Token ile API sorguları yap (rate limit sorunu olmaması için)
- Her değişiklik sonrası `Restart-Service JellyfinService` ve test
