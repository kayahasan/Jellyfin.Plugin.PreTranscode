---
title: "feat: Premium UI Redesign for PreTranscode Plugin"
type: feat
status: active
date: 2026-08-02
origin: docs/ui-redesign-requirements.md
---

# Premium UI Redesign for PreTranscode Plugin

## Overview

PreTranscode plugin'inin UI'i, Jellyfin'in native tasarım diline tam uyumlu, premium görünümlü, zengin bilgi sunan ve kullanıcıyı her adımda bilgilendiren bir deneyime dönüştürülecek. Mevcut UI çalışır durumda ama aşırı inline CSS kullanıyor, Jellyfin'in native componentlerini doğru kullanmıyor, sınırlı bilgi gösteriyor ve dashboard entegrasyonu yok.

---

## Problem Frame

Kullanıcılar şu anda:
- Kodlama sırasında GPU kullanımı, hız, ETA gibi kritik bilgileri göremiyor
- Filtre/sıralama yapamıyor, büyük kütüphanelerde kayboluyorlar
- Toplu işlemler için tek tek seçmek zorunda
- Dashboard'dan plugin durumunu göremiyorlar
- Kodlama başlamadan ne olacağını öngöremiyorlar

Bu plan, tüm bu eksikleri gidererek plugin'i "premium" kategorideki Jellyfin eklentileri seviyesine taşıyacak.

---

## Requirements Trace

- R1. UI, Jellyfin'in native görünümüne tam uyumlu olmalı
- R2. Kullanıcı kodlama sürecini tam olarak anlayabilmeli (ne oluyor, ne kadar sürecek)
- R3. Tek tıkla toplu işlemler yapılabilir olmalı
- R4. Her job için GPU/CPU kullanımı görülebilir olmalı
- R5. Dashboard'dan plugin durumu anında anlaşılmalı
- R6. Kodlama öncesi önizleme ve güvenli işlemler sağlanmalı

---

## Scope Boundaries

### In Scope
- list.html UI redesign (native components, CSS cleanup, visual polish)
- Job panel enhancement (GPU info, ETA, speed, codec details)
- Filter/sort functionality
- Bulk operations with preview
- Activity Log integration for notifications
- Enhanced scheduled task UI

### Deferred to Follow-Up Work
- Multi-language support (i18n) - separate effort
- WebSocket real-time updates - requires deeper Jellyfin integration
- Mobile responsive improvements - Jellyfin web UI desktop-first

### Outside this product's identity
- Jellyfin dashboard home card creation - requires jellyfin-web modification (not plugin scope)
- Plugin marketplace rating system

---

## Context & Research

### Relevant Code and Patterns

- `Configuration/list.html` — Main UI page (22KB, single-page app with tabs)
- `Controllers/PreTranscodeController.cs` — REST API endpoints
- `Services/JobQueueService.cs` — Job management, progress tracking
- `Services/EncoderService.cs` — FFmpeg command building
- `ScheduledTasks/PreTranscodeTask.cs` — IScheduledTask implementation
- `Configuration/PluginConfiguration.cs` — Config model with HwAccelType enum

### Jellyfin Plugin Architecture

- **IHasWebPages** — Plugin pages via embedded HTML resources
- **Custom elements** — `emby-button`, `emby-input`, `emby-select`, `emby-checkbox`
- **Card system** — `card`, `cardBox`, `cardScalableImage`, `cardFooter` classes
- **APIs** — `ApiClient.ajax()`, `Dashboard.alert()`, `Dashboard.showLoadingMsg()`
- **Notifications** — `IActivityManager`, `INotificationManager` (not currently used)
- **Scheduling** — `IScheduledTask` with `GetDefaultTriggers()` for cron-like scheduling

### Key Findings

1. **No native dashboard card API** — Jellyfin plugins can't add home dashboard widgets without modifying jellyfin-web
2. **Activity Log is the alternative** — Use `IActivityManager` for job notifications visible in Dashboard → Activity
3. **Job progress is file-size based** — Current implementation tracks output file growth; can enhance with FFmpeg stderr parsing
4. **GPU monitoring requires system queries** — Not available in plugin sandbox; will show encoder type instead of real GPU %

---

## Release Strategy

**One feature per version.** Each implementation unit (or small group) gets its own version:
- Implement → test → build → tag → manifest update → release
- User can update incrementally via Jellyfin dashboard
- If a version breaks something, easy to identify and fix

Example:
- v1.0.0.16: Three-tab layout + Jobs tab
- v1.0.0.17: Enhanced JobInfo + job panel
- v1.0.0.18: Filters and sorting
- etc.

## Key Technical Decisions

- **Single page, enhanced tabs**: Keep single-page architecture but add a third "Jobs" tab for dedicated job management
- **No dashboard home card**: Use Activity Log integration instead (plugin-scope solution)
- **Encoder type display over GPU metrics**: Show "h264_vaapi (AMD Vega 64)" instead of fake GPU % (honest, useful)
- **ETA from progress rate**: Calculate ETA from file growth rate (already tracked), not system-level metrics
- **Keep inline CSS**: Jellyfin reads inline CSS properly, external CSS doesn't load. Design breaks without inline styles.
- **Preview dialog before bulk encode**: Show estimated sizes, count, duration before starting

---

## Open Questions

### Resolved During Planning

- **Dashboard card?** → No, use Activity Log integration (plugin-scope)
- **WebSocket updates?** → Defer to future, polling is sufficient for now
- **Separate settings page?** → No, keep settings as tab in main page (simpler UX)

### Deferred to Implementation

- Exact FFmpeg stderr parsing regex for frame rate extraction
- Optimal ETA calculation smoothing algorithm
- Specific Jellyfin CSS variable names for theme colors

---

## High-Level Technical Design

> *This illustrates the intended approach and is directional guidance for review, not implementation specification.*

```
┌─────────────────────────────────────────────────────┐
│ PreTranscode Plugin Page                            │
├─────────────────────────────────────────────────────┤
│ [📚 Kütüphane] [⚙️ Ayarlar] [📊 İşler]              │ ← 3 tabs
├─────────────────────────────────────────────────────┤
│                                                     │
│  Library Tab:                                       │
│  ┌─────────────────────────────────────────────┐   │
│  │ [🔄] [☑️ Tümünü Seç] [✗ Kaldır]           │   │
│  │ [Filtre: HEVC ▼] [Sırala: Boyut ▼]      │   │
│  │ [▶️ Seçilenleri Kodla (5)]              │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐                       │
│  │ 🎬 │ │ 🎬 │ │ 🎬 │ │ 🎬 │  ← Jellyfin native    │
│  │Film│ │Film│ │Film│ │Film│     card components   │
│  │4.2 │ │3.8 │ │2.1 │ │5.6 │                       │
│  │GB  │ │GB  │ │GB  │ │GB  │                       │
│  └────┘ └────┘ └────┘ └────┘                       │
│                                                     │
│  ┌─────────────────────────────────────────────┐   │
│  │ 📺 Dizi Adı ▼                               │   │
│  │   ☑ Tüm bölümleri seç                       │   │
│  │   S01: [1] [2] [3] [4] [5]                  │   │
│  │   S02: [1] [2] [3]                          │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  Jobs Tab:                                          │
│  ┌─────────────────────────────────────────────┐   │
│  │ 📊 Özet: 15 dosya, 450GB | ✅ 8 | 🔄 2    │   │
│  │    💾 Tasarruf: 240GB → 145GB (%40)       │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  ┌─────────────────────────────────────────────┐   │
│  │ ▶ Dosya Adı.mp4                       %67 │   │
│  │ ▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░                     │   │
│  │ HEVC→H264 | 4K→1080p | h264_vaapi       │   │
│  │ 1.2GB/3.5GB | 15fps | ETA: 12dk        │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
└─────────────────────────────────────────────────────┘
```

---

## Implementation Units

### Phase 1: Foundation

- [ ] U1. **CSS Cleanup and Native Component Migration**

**Goal:** Remove all inline CSS, use proper Jellyfin native components and CSS classes.

**Requirements:** R1

**Dependencies:** None

**Files:**
- Modify: `Configuration/list.html`

**Approach:**
- Keep inline CSS (Jellyfin requirement - external CSS doesn't load properly)
- Refine inline styles for better visual consistency
- Use Jellyfin CSS variables where possible within inline styles
- Replace custom card markup with proper Jellyfin card structure (`card scalableCard visualCardBox`)
- Use Jellyfin layout classes: `verticalSection`, `selectContainer`, `inputContainer`, `checkboxContainer`
- Use Jellyfin CSS variables: `--accent-color`, `--text-primary`, `--background-primary`
- Keep functional behavior identical, only visual changes

**Patterns to follow:**
- Jellyfin's built-in plugin pages (Dashboard → Plugins → any plugin settings)
- Jellyfin's library browsing cards

**Test scenarios:**
- Happy path: Page loads without errors in browser console
- Happy path: All interactive elements (buttons, checkboxes, selects) work correctly
- Edge case: Dark theme colors are readable
- Edge case: Light theme (if enabled) colors are readable
- Verification: Visual comparison with Jellyfin native pages shows consistent styling

---

- [ ] U2. **Three-Tab Layout with Jobs Tab**

**Goal:** Add dedicated "Jobs" tab for job management and monitoring.

**Requirements:** R1, R2

**Dependencies:** U1

**Files:**
- Modify: `Configuration/list.html`

**Approach:**
- Add third tab: "📊 İşler" between Library and Settings
- Move job panel from Library tab to dedicated Jobs tab
- Add job summary section (total jobs, completed, running, saved space)
- Add job history toggle (show/hide completed jobs)
- Keep job polling logic, just relocate UI

**Test scenarios:**
- Happy path: Clicking each tab shows correct content
- Happy path: Active job appears in Jobs tab with progress
- Edge case: No jobs → Jobs tab shows empty state message
- Edge case: Many completed jobs → can toggle visibility
- Verification: Tab navigation works, job panel relocated successfully

---

### Phase 2: Information Enhancement

- [ ] U3. **Enhanced JobInfo DTO with Encoder Details**

**Goal:** Extend JobInfo to include encoder type, codec conversion, speed, ETA.

**Requirements:** R2, R4

**Dependencies:** None

**Files:**
- Modify: `Services/JobQueueService.cs`
- Modify: `Controllers/PreTranscodeController.cs`

**Approach:**
- Add to JobInfo:
  - `string EncoderType` (e.g., "h264_vaapi", "hevc_nvenc", "libx264")
  - `string CodecConversion` (e.g., "HEVC → H.264")
  - `string ResolutionConversion` (e.g., "4K → 1080p" or null)
  - `double EncodeSpeedFps` (parsed from FFmpeg stderr, or estimated)
  - `string EtaText` (calculated from progress rate)
  - `DateTime StartedAt`
  - `DateTime? CompletedAt`
- Calculate ETA from: `(remaining_bytes / bytes_per_second) / 60`
- Store encoder type from config when job starts

**Test scenarios:**
- Happy path: Running job returns all new fields populated
- Happy path: ETA updates as job progresses
- Edge case: Job just started → ETA shows "Hesaplanıyor..."
- Edge case: Job completed → CompletedAt populated, ETA null
- Verification: API response contains new fields, values are reasonable

---

- [ ] U4. **Enhanced Job Panel UI**

**Goal:** Display rich job information including encoder details, speed, ETA.

**Requirements:** R2, R4

**Dependencies:** U2, U3

**Files:**
- Modify: `Configuration/list.html`

**Approach:**
- Each job card shows:
  - Status icon + name + progress % + cancel button
  - Progress bar (gradient for running jobs)
  - Codec conversion badge: "HEVC → H.264"
  - Encoder badge: "h264_vaapi (GPU)" or "libx264 (CPU)"
  - Speed + ETA: "15fps | ETA: 12dk"
  - File sizes: "1.2GB / 3.5GB"
- Use color-coded badges (green=GPU, gray=CPU)
- Tooltip on hover for full FFmpeg command

**Test scenarios:**
- Happy path: Running job shows encoder type, speed, ETA
- Happy path: GPU encoder shows green badge, CPU shows gray
- Edge case: No speed data yet → shows "--fps"
- Verification: All new fields from U3 displayed correctly

---

- [ ] U5. **Job Summary Section**

**Goal:** Show aggregate job statistics at top of Jobs tab.

**Requirements:** R2

**Dependencies:** U2

**Files:**
- Modify: `Configuration/list.html`

**Approach:**
- Summary bar shows:
  - Total jobs (queued + running + completed)
  - Status counts: ✅ completed | 🔄 running | ⏳ queued | ❌ failed
  - Total size: "450GB"
  - Saved space: "240GB → 145GB (%40 tasarruf)"
- Update on every poll cycle
- Click to expand detailed stats

**Test scenarios:**
- Happy path: Summary shows correct counts
- Happy path: Saved space calculates correctly
- Edge case: No jobs → shows "Henüz işlem yok"
- Verification: Summary matches actual job data

---

### Phase 3: Control Features

- [ ] U6. **Filter and Sort Functionality**

**Goal:** Add quick filters and sorting to library view.

**Requirements:** R2

**Dependencies:** U1

**Files:**
- Modify: `Configuration/list.html`
- Modify: `Controllers/PreTranscodeController.cs`

**Approach:**
- Filters (dropdowns):
  - Codec: "Tümü", "HEVC", "H.264", "AV1", "VP9"
  - Resolution: "Tümü", "4K", "1080p", "720p"
  - Size: "Tümü", "1GB+", "5GB+", "10GB+"
- Sort (dropdown):
  - "Boyut (büyük→küçük)", "Boyut (küçük→büyük)", "İsim", "Codec"
- Apply filters client-side (API returns all, JS filters)
- Multiple filters combinable

**Test scenarios:**
- Happy path: Filter by HEVC shows only HEVC files
- Happy path: Sort by size shows largest first
- Happy path: Combine filters (HEVC + 5GB+)
- Edge case: No results → shows "Hiçbir dosya bulunamadı"
- Verification: Filtered/sorted list matches expectations

---

- [ ] U7. **Bulk Operations with Preview**

**Goal:** Enable bulk transcoding with pre-encode preview dialog.

**Requirements:** R3, R6

**Dependencies:** U6

**Files:**
- Modify: `Configuration/list.html`
- Modify: `Controllers/PreTranscodeController.cs`

**Approach:**
- Add "Toplu Dönüştür" button
- Opens dialog with options:
  - "Tüm HEVC dosyalarını H.264'e çevir"
  - "Tüm 4K dosyalarını 1080p'ye düşür"
  - "Seçili dosyaları kodla"
- Preview shows:
  - File count
  - Total size
  - Estimated output size (based on quality setting)
  - Estimated duration
- Confirm → queue all jobs

**Test scenarios:**
- Happy path: Select "HEVC → H.264" → preview shows correct files
- Happy path: Confirm → all files queued
- Edge case: No matching files → shows "Eşleşen dosya yok"
- Verification: Preview matches actual files that would be encoded

---

### Phase 4: Notifications & Safety

- [ ] U8. **Activity Log Integration**

**Goal:** Log job events to Jellyfin Activity Log.

**Requirements:** R5

**Dependencies:** None

**Files:**
- Modify: `PluginServiceRegistrator.cs`
- Modify: `Services/JobQueueService.cs`

**Approach:**
- Inject `IActivityManager` into JobQueueService
- Log on job events:
  - Started: "PreTranscode: {Name} kodlanmaya başlandı"
  - Completed: "PreTranscode: {Name} tamamlandı ({saved}GB tasarruf)"
  - Failed: "PreTranscode: {Name} başarısız: {error}"
  - Cancelled: "PreTranscode: {Name} iptal edildi"
- Use appropriate severity levels (Info, Warning, Error)

**Test scenarios:**
- Happy path: Completed job appears in Dashboard → Activity
- Happy path: Failed job shows error message in Activity
- Edge case: Activity Log shows correct plugin name
- Verification: Job events visible in Jellyfin Activity Log

---

- [ ] U9. **Undo Window for Replaced Files**

**Goal:** Add configurable delay before deleting original files.

**Requirements:** R6

**Dependencies:** None

**Files:**
- Modify: `Configuration/PluginConfiguration.cs`
- Modify: `Services/JobQueueService.cs`
- Modify: `Configuration/list.html`

**Approach:**
- Add config option: `UndoWindowMinutes` (0, 60, 1440, 10080)
- When ReplaceOriginal=true:
  - Rename original to `.pretranscode-pending-delete`
  - Schedule deletion after undo window
  - Show countdown in UI: "Orijinal 23dk içinde silinecek [İptal]"
- Add cleanup task to delete expired pending files

**Test scenarios:**
- Happy path: Replaced file renamed, not deleted immediately
- Happy path: Undo button restores original file
- Edge case: Undo window expires → file deleted
- Edge case: Undo window = 0 → immediate deletion (current behavior)
- Verification: Original file recoverable within undo window

---

### Phase 5: Automation

- [ ] U10. **Enhanced Scheduled Task UI**

**Goal:** Improve scheduled task configuration and visibility.

**Requirements:** R3

**Dependencies:** None

**Files:**
- Modify: `ScheduledTasks/PreTranscodeTask.cs`
- Modify: `Configuration/list.html`

**Approach:**
- Add default trigger option in Settings:
  - "Her gece 02:00'de otomatik kodla"
  - "Haftasonu boş zamanlarda kodla"
- Show scheduled task status in Settings tab
- Link to Dashboard → Scheduled Tasks for manual run

**Test scenarios:**
- Happy path: Enable daily trigger → appears in Scheduled Tasks
- Happy path: Task runs at scheduled time
- Verification: Scheduled task configured and visible

---

## System-Wide Impact

- **Interaction graph:** UI changes only affect `list.html` and API responses; no changes to Jellyfin core
- **Error propagation:** New API fields are nullable; missing data shows gracefully in UI
- **State lifecycle risks:** Undo window adds file rename + scheduled delete; cleanup task needed
- **API surface parity:** JobInfo DTO changes are additive (backward compatible)
- **Integration coverage:** Activity Log integration tested via Jellyfin Dashboard
- **Unchanged invariants:** Core encoding logic, job queue, cancellation all unchanged

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Jellyfin CSS classes change between versions | Scope CSS to `#PreTranscodeListPage`, use widely-used classes |
| ETA calculation inaccurate for small files | Show ETA only after 30s of progress data |
| Undo window leaves orphan files | Add cleanup task on plugin startup |
| Activity Log spam for many jobs | Aggregate: log summary every 10 jobs, not each one |
| Large library filter performance | Client-side filter on already-loaded data; no API impact |

---

## Documentation / Operational Notes

- Update plugin description to mention new features
- Add FAQ: "Nasıl toplu kodlama yaparım?", "ETA nasıl hesaplanıyor?"
- Consider screenshot for plugin marketplace

---

## Sources & References

- **Origin document:** [docs/ui-redesign-requirements.md](../ui-redesign-requirements.md)
- Current UI: `Configuration/list.html`
- Jellyfin plugin docs: Jellyfin.Controller namespace
- Jellyfin web components: `emby-button`, `emby-input`, etc.
