# PreTranscode UI Redesign Requirements

**Date:** 2026-08-02  
**Status:** Draft  
**Author:** Brainstorm session

## Vision

PreTranscode UI'i, Jellyfin'in native tasarım diline tam uyumlu, premium görünümlü, zengin bilgi sunan ve kullanıcıyı her adımda bilgilendiren bir deneyime dönüştürülecek.

## Current State

Mevcut UI çalışır durumda ama:
- Aşırı inline CSS kullanımı, tutarsız stil
- Jellyfin'in native componentlerini kullanmıyor
- Sınırlı bilgi gösterimi (sadece progress bar)
- Filtre/sıralama yok
- GPU kullanım bilgisi yok
- Dashboard entegrasyonu yok

## Requirements

### 1. Design & Jellyfin Integration

#### 1.1 Native Component Usage
- Tüm inline CSS'leri kaldır
- Jellyfin'in `card`, `cardBox`, `cardScalableImage`, `cardFooter` sistemini kullan
- `emby-button`, `emby-input`, `emby-select`, `emby-checkbox` native bileşenleri
- Jellyfin'in renk değişkenlerini kullan (`--accent-color`, `--text-primary`, vb.)

#### 1.2 Dashboard Home Card
- Plugin sayfası açılmadan anasayfada özet göster
- İçerik:
  - "X uyumsuz dosya bulundu"
  - "Y kodlanıyor (%Z tamamlandı)"
  - "Toplam tasarruf: A GB → B GB"
- Tıklayınca plugin sayfasına yönlendir

#### 1.3 Visual Polish
- Smooth transitions ve hover efektleri
- Gradient progress bars (running jobs için animasyonlu)
- İkon kullanımı (Material Icons veya Jellyfin'in mevcut ikon seti)
- Tipografi hiyerarşisi (headings, labels, metadata)

### 2. Information & Control

#### 2.1 GPU Usage Display
- Aktif job'lar için gerçek zamanlı:
  - Encoder tipi (h264_vaapi, hevc_nvenc, vb.)
  - GPU kullanımı (%)
  - Encode hızı (fps)
  - Tahmini bitiş zamanı (ETA)
- İmleç ile üzerine gelince detaylı bilgi (tooltip)

#### 2.2 Enhanced Job Panel
- Her job için:
  - Kaynak → hedef codec dönüşümü (örn: "HEVC → H.264")
  - Çözünürlük dönüşümü (örn: "4K → 1080p")
  - Bitrate bilgisi (kaynak vs çıktı)
  - GPU vs CPU göstergesi (ikon)
- Job status'ları daha belirgin renkler ve ikonlar

#### 2.3 Quick Filters
- Library görünümünde filtre çubuğu:
  - Codec: "HEVC", "H.264", "AV1", "VP9"
  - Çözünürlük: "4K", "1080p", "720p"
  - Boyut: "1GB+", "5GB+", "10GB+"
  - HDR: "HDR içerikli"
- Birden fazla filtre bir arada uygulanabilir

#### 2.4 Sorting
- Sıralama seçenekleri:
  - Boyut (büyük → küçük / küçük → büyük)
  - Codec
  - İsim
  - Tarih (ekleme tarihi)
- Tıklayınca toggle (ascending/descending)

### 3. UX Improvements

#### 3.1 Bulk Operations
- "Toplu dönüştür" butonu:
  - "Tüm HEVC dosyalarını H.264'e çevir"
  - "Tüm 4K dosyalarını 1080p'ye düşür"
  - Kullanıcı kriter seçer, plugin uygular
- Onay dialogu: "X dosya kodlanacak, tahmini süre: Y saat"

#### 3.2 Scheduling
- Settings'de zamanlama bölümü:
  - "Belirli saatlerde otomatik kodla"
  - Haftalık program (örn: her gece 02:00-06:00)
  - "Sadece boş zamanlarda kodla" (GPU kullanılmıyorsa)

#### 3.3 Notifications
- Jellyfin native bildirim sistemi entegrasyonu:
  - Her job tamamlandığında bildirim
  - Hata durumunda acil bildirim
  - Özet bildirim (örn: "5 dosya kodlandı, 12GB tasarruf")

#### 3.4 Progress Summary
- Job panelinin üstünde özet:
  - "Toplam: 15 dosya, 450 GB"
  - "Tamamlandı: 8 dosya, 240 GB → 145 GB (%40 tasarruf)"
  - "Devam eden: 2 dosya"
  - "Kuyrukta: 5 dosya"

### 4. Safety Features

#### 4.1 Pre-encode Preview
- Encode başlamadan önce:
  - Hangi dosyalar kodlanacak (liste)
  - Tahmini çıktı boyutları
  - Tahmini süre
  - Kullanıcı "Başlat" veya "İptal" yapar

#### 4.2 Undo Window
- Orijinal dosya silinmeden önce:
  - "Bu dosya X saat içinde otomatik silinecek"
  - Manuel iptal butonu
  - Settings'de undo süresi ayarlanabilir (0 = anında sil, 1h, 24h, 7g)

## Non-Goals

- Multi-language support (şimdilik sadece Türkçe)
- Mobile responsive design (Jellyfin web UI desktop-first)
- Plugin marketplace rating/review sistemi

## Success Criteria

1. UI, Jellyfin'in native görünümüne tam uyumlu
2. Kullanıcı kodlama sürecini tam olarak anlayabiliyor (ne oluyor, ne kadar sürecek)
3. Tek tıkla toplu işlemler yapılabilir
4. Her job için GPU/CPU kullanımı görülebilir
5. Dashboard'dan plugin durumu anında anlaşılıyor

## Implementation Phases (suggested)

### Phase 1: Foundation
- Native component migration
- CSS cleanup
- Visual polish

### Phase 2: Information
- GPU usage display
- Enhanced job panel
- Progress summary

### Phase 3: Control
- Filters and sorting
- Bulk operations
- Pre-encode preview

### Phase 4: Automation
- Scheduling
- Notifications
- Undo window

### Phase 5: Dashboard Integration
- Home card
- Real-time updates
