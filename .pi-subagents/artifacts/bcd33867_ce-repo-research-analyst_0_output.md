Now I have all the information needed. Let me compile the comprehensive research report.

```markdown
## Repository Research Summary

### Technology & Infrastructure

**Languages and Frameworks:**
- **C# / .NET 9.0** — Primary runtime (`Jellyfin.Plugin.PreTranscode.csproj`)
- **Jellyfin SDK 10.11.11** — `Jellyfin.Controller` and `Jellyfin.Model` packages
- **JavaScript (Vanilla)** — Plugin UI pages, no frameworks (React/Vue/Angular not used)
- **HTML + Inline CSS** — UI rendering

**Dependencies:**
- `Microsoft.AspNetCore.App` — ASP.NET Core (for API controllers)
- `LiteDB` 5.0.21 — Lightweight embedded database
- `Jellyfin.Controller` 10.11.11 — Jellyfin controller abstractions
- `Jellyfin.Model` 10.11.11 — Jellyfin data models

**Deployment Model:**
- Single-plugin DLL deployed to Jellyfin's `plugins/` directory
- Not a monorepo — single project
- CI/CD via GitHub Actions (`release.yml`) — builds, packages, creates releases

**API Surface:**
- Custom REST API under `/Plugins/PreTranscode/*`
- Endpoints: `GET /Items`, `GET /Jobs`, `POST /Encode`, `POST /Cancel/{itemId}`
- All endpoints use `[Authorize]` — Jellyfin auth required

**Data Layer:**
- Jellyfin's `ILibraryManager` for media library queries
- `IMediaEncoder` for FFmpeg path resolution
- In-memory `ConcurrentDictionary` for job tracking (JobQueueService)
- LiteDB available but not currently used in codebase

**Module Organization:**
```
Jellyfin.Plugin.PreTranscode/
├── Plugin.cs                    # Main plugin entry, IHasWebPages
├── PluginServiceRegistrator.cs  # DI registration
├── Configuration/
│   ├── PluginConfiguration.cs   # Config model + HwAccelType enum
│   ├── list.html                # MAIN UI PAGE (22KB) — Library browser + settings
│   └── configPage.html          # Settings-only page (10KB) — not currently used
├── Controllers/
│   └── PreTranscodeController.cs # REST API endpoints
├── Services/
│   ├── EncoderService.cs        # FFmpeg command building + execution
│   └── JobQueueService.cs       # Job queue, progress tracking, cancellation
└── ScheduledTasks/
    └── PreTranscodeTask.cs      # IScheduledTask implementation
```

---

### Architecture & Structure

**Plugin Entry Point (`Plugin.cs`):**
- Inherits `BasePlugin<PluginConfiguration>`
- Implements `IHasWebPages` for custom UI pages
- Implements `IDisposable`
- Static `Instance` property for singleton access
- Exposes ONE page via `GetPages()`:
  - Name: "PreTranscode"
  - MenuIcon: "movie"
  - EnableInMainMenu: true
  - EmbeddedResourcePath: `Jellyfin.Plugin.PreTranscode.Configuration.list.html`

**Key Architectural Patterns:**
1. **Service Registration** (`PluginServiceRegistrator.cs`):
   - Registers `EncoderService` and `JobQueueService` as singletons via `IPluginServiceRegistrator`
   
2. **Dependency Injection**:
   - `ILibraryManager`, `IMediaEncoder`, `ILogger<T>` injected via constructors
   - Plugin services registered in `RegisterServices()`

3. **Job Queue Architecture**:
   - `JobQueueService` manages concurrent transcoding jobs
   - `SemaphoreSlim` limits concurrency
   - Progress tracked via file size comparison (output growing = progress)
   - Cancellation via `CancellationTokenSource` + polling (500ms interval)

4. **Scheduled Task**:
   - `PreTranscodeTask` implements `IScheduledTask`
   - Scans library, processes items sequentially
   - Uses `IProgress<double>` for Jellyfin's task progress reporting

---

### Current UI Structure (`list.html`) — Analysis

**File Path:** `Configuration/list.html` (22,675 bytes)

**How It Works:**
1. **Single-page application** embedded in Jellyfin via `IHasWebPages`
2. **Two-tab layout** (custom implementation, not Jellyfin native tabs):
   - "Kütüphane" (Library) — movie/series browser
   - "Ayarlar" (Settings) — configuration form
3. **Event-driven** via `pageshow` event listener on the page div
4. **Data loading** via `ApiClient.ajax()` calls to plugin endpoints
5. **Job polling** via `setInterval(pollJobs, 2000)` when jobs are active

**Current UI Components:**
- Movie cards: Custom HTML with pseudo-Jellyfin classes (`card`, `cardBox`, `cardFooter`)
- Series accordion: Custom collapsible sections with season grouping
- Job panel: Custom progress bars with status icons
- Settings form: Uses `emby-select`, `emby-input`, `emby-checkbox` components

**Jellyfin APIs Used in UI:**
- `ApiClient.ajax()` — Custom API calls
- `ApiClient.getUrl()` — URL building
- `ApiClient.getPluginConfiguration(pluginId)` — Load config
- `ApiClient.updatePluginConfiguration(pluginId, config)` — Save config
- `Dashboard.showLoadingMsg()` / `Dashboard.hideLoadingMsg()` — Loading states
- `Dashboard.alert(message)` — Simple alerts
- `Dashboard.processPluginConfigurationUpdateResult(result)` — Config save feedback

**Limitations (confirmed by `ui-redesign-requirements.md`):**
1. **Excessive inline CSS** — ~80% of styling is inline `style=""` attributes
2. **Not using Jellyfin's native component system properly** — classes like `cardBox` are applied but structure doesn't match Jellyfin's expected markup
3. **No filtering or sorting** — items displayed as-is from API
4. **Limited job information** — only progress %, file sizes, status
5. **No GPU usage display** — no encoder details shown
6. **No dashboard integration** — no home card, no notifications
7. **Turkish-only UI** — no localization
8. **No pre-encode preview** — no confirmation dialog with estimates
9. **Settings page duplication** — `configPage.html` exists but is not registered; settings are in `list.html` tab instead

---

### Plugin Architecture — How Config Pages Work

**Pattern 1: IHasWebPages (Current)**
```csharp
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "PreTranscode",
                DisplayName = "PreTranscode",
                EnableInMainMenu = true,
                MenuIcon = "movie",
                EmbeddedResourcePath = "Jellyfin.Plugin.PreTranscode.Configuration.list.html"
            }
        };
    }
}
```

**PluginPageInfo Properties:**
- `Name` — URL slug / page identifier
- `DisplayName` — Menu label
- `EnableInMainMenu` — Show in left sidebar navigation
- `MenuIcon` — Material icon name (e.g., "movie", "settings", "build")
- `EmbeddedResourcePath` — Full assembly resource path
- `MenuSection` — Optional section grouping

**Pattern 2: Embedded Resources (.csproj)**
```xml
<ItemGroup>
    <EmbeddedResource Include="Configuration\configPage.html" />
    <EmbeddedResource Include="Configuration\list.html" />
</ItemGroup>
```

**How Jellyfin Serves Plugin Pages:**
- Jellyfin scans plugins implementing `IHasWebPages`
- Calls `GetPages()` at startup
- Serves embedded resources via `/web/` routes
- Pages are rendered inside Jellyfin's existing layout/navigation

---

### Adding Dashboard Cards

**Current State:** Jellyfin plugins do NOT have a native "dashboard card" API. The dashboard home page is controlled by Jellyfin's web client (`jellyfin-web`), not plugins.

**Options for Dashboard Integration:**

1. **Plugin Page in Main Menu (Current):**
   - Already implemented via `IHasWebPages` + `EnableInMainMenu = true`
   - Shows in left sidebar under "Plugins" section

2. **Home Section via Plugin (Advanced):**
   - Would require modifying Jellyfin's web client or using a separate home-sections plugin
   - Reference: `jellyfin-plugin-home-sections` on GitHub
   - Not a standard plugin capability

3. **WebSocket-based Real-time Updates:**
   - Jellyfin uses WebSockets for live updates
   - Plugin could broadcast job status via Jellyfin's session manager
   - Requires deeper Jellyfin integration (`ISessionManager`, `IWebSocketListener`)

4. **Activity Log Integration:**
   - Use `IActivityManager` to log job completions
   - Shows in Jellyfin's Activity Log (Dashboard → Activity)
   - This is the most straightforward way to surface job events

**Recommended Approach:**
- Keep main menu page as primary UI
- Use Activity Log for job notifications
- Consider WebSocket notifications for real-time job status in the plugin page itself

---

### Existing Patterns — Notifications

**Available Jellyfin Services:**
- `IActivityManager` — Log activity entries (visible in Dashboard → Activity)
- `INotificationManager` — Send notifications (used by Webhook plugin)
- `IWebSocketListener` — Real-time WebSocket events (advanced)

**Current Implementation:** NONE — the plugin does not use any notification system.

**How to Add Notifications:**

1. **Activity Log (Simplest):**
```csharp
// Inject IActivityManager
public class JobQueueService
{
    private readonly IActivityManager _activityManager;
    
    // On job complete:
    _activityManager.Add(new ActivityLogEntry
    {
        Name = "PreTranscode Job Completed",
        Description = $"Transcoded {job.Name} successfully",
        Severity = ActivityLogEntrySeverity.Info
    });
}
```

2. **User Notifications (via NotificationManager):**
```csharp
// Inject INotificationManager
var request = new NotificationRequest
{
    Users = allAdminUsers,
    Subject = "PreTranscode Job Completed",
    Description = $"{job.Name} transcoded successfully"
};
await _notificationManager.SendNotification(request, CancellationToken.None);
```

---

### Existing Patterns — Scheduling

**Current Implementation:** `PreTranscodeTask.cs` implements `IScheduledTask`

**Pattern:**
```csharp
public class PreTranscodeTask : IScheduledTask
{
    public string Name => "Kütüphaneyi Ön-Kodla";
    public string Key => "PreTranscodeLibraryScan";
    public string Description => "...";
    public string Category => "PreTranscode";
    
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
    
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Task logic here
        progress.Report(50.0); // Report progress to Jellyfin
    }
}
```

**How it appears in Jellyfin:**
- Dashboard → Scheduled Tasks → "PreTranscode" category
- Can be run manually or scheduled with triggers
- Progress shown in task dialog

**Adding Schedule Triggers:**
```csharp
public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
{
    return new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(2).Ticks // 2:00 AM daily
        }
    };
}
```

---

### Existing Patterns — API Endpoints

**Current Controller (`PreTranscodeController.cs`):**

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/Plugins/PreTranscode/Items` | Yes | List non-standard media items |
| GET | `/Plugins/PreTranscode/Jobs` | Yes | Get all jobs with status/progress |
| POST | `/Plugins/PreTranscode/Encode` | Yes | Queue items for encoding |
| POST | `/Plugins/PreTranscode/Cancel/{itemId}` | Yes | Cancel a running job |

**Pattern:**
```csharp
[ApiController]
[Route("Plugins/PreTranscode")]
[Authorize]
public class PreTranscodeController : ControllerBase
{
    // Constructor injection
    public PreTranscodeController(ILibraryManager libraryManager, JobQueueService jobQueueService)
    {
        _libraryManager = libraryManager;
        _jobQueueService = jobQueueService;
    }
    
    [HttpGet("Items")]
    public ActionResult<NonStandardItemsResponse> GetNonStandardItems() { ... }
}
```

**Response DTOs:**
- `NonStandardItemDto` — Movie/episode with Id, Name, Codec, SizeMb, PosterUrl
- `SeriesDto` — Series with grouped seasons/episodes
- `JobInfo` — Job with Status, ProgressPercent, Error, sizes

---

### Relevant File Paths for UI Changes

**Primary UI File:**
- `Configuration/list.html` — Main plugin page (library browser + settings)

**Secondary UI File:**
- `Configuration/configPage.html` — Exists but NOT registered. Could be used for a dedicated settings page.

**Backend Files Affecting UI:**
- `Controllers/PreTranscodeController.cs` — API responses consumed by UI
- `Services/JobQueueService.cs` — JobInfo structure (status, progress data)
- `Configuration/PluginConfiguration.cs` — Config fields exposed in settings

**Plugin Registration:**
- `Plugin.cs` — `GetPages()` method controls which pages are exposed
- `Jellyfin.Plugin.PreTranscode.csproj` — `<EmbeddedResource>` entries

---

### How Jellyfin's Native CSS/JS Components Work in Plugins

**Custom Web Components (Emby-style):**
Jellyfin uses custom elements prefixed with `emby-`:

| Component | Usage | Attributes |
|-----------|-------|------------|
| `emby-button` | Buttons | `class="raised button-submit"` |
| `emby-input` | Text/number inputs | `label="..."`, `type="text/number"` |
| `emby-select` | Dropdown selects | `label="..."` |
| `emby-checkbox` | Checkboxes | Inside `<label>` |

**Page Declaration:**
```html
<div id="PageId" 
     data-role="page" 
     class="page type-interior pluginConfigurationPage" 
     data-require="emby-input,emby-button,emby-select,emby-checkbox">
```
- `data-require` lists which components to load

**Card System (for media items):**
```html
<div class="card scalableCard">
    <div class="cardBox visualCardBox">
        <div class="cardScalableImageAspect">
            <img class="cardPadder scalableImageOnTop" src="..." />
        </div>
        <div class="cardFooter">
            <div class="cardText">Title</div>
            <div class="cardText secondary">Metadata</div>
        </div>
    </div>
</div>
```

**Layout Classes:**
- `content-primary` — Main content area
- `verticalSection` — Section with heading
- `selectContainer`, `inputContainer`, `checkboxContainer` — Form field wrappers
- `flex`, `align-items-center` — Flexbox utilities

**JavaScript Patterns:**
- `Dashboard.showLoadingMsg()` / `Dashboard.hideLoadingMsg()` — Loading overlay
- `Dashboard.alert(message)` — Simple alert dialog
- `ApiClient.getPluginConfiguration(pluginId)` — Fetch config
- `ApiClient.updatePluginConfiguration(pluginId, config)` — Save config
- `ApiClient.ajax({type, url, data, contentType})` — Custom API calls
- `ApiClient.getUrl('path')` — Build authenticated URL
- `pageshow` event — Fires when plugin page is displayed

**Color Variables (Jellyfin CSS):**
- `--accent-color` — Primary accent (red by default)
- `--text-primary` — Main text color
- `--text-secondary` — Secondary text
- `--background-primary` — Background

---

### Recommendations for UI Redesign

**Phase 1: Foundation (Immediate)**
1. Remove all inline CSS from `list.html` — move to `<style>` block or use Jellyfin classes
2. Properly implement Jellyfin card markup for movie cards
3. Use `verticalSection`, `selectContainer`, etc. for settings form
4. Register `configPage.html` as a separate settings page OR remove it

**Phase 2: Information Enhancement**
1. Add encoder details to `JobInfo` DTO (codec, GPU type, fps, ETA)
2. Update `EncoderService` to report progress via stderr parsing
3. Extend job panel UI with new details

**Phase 3: Control Features**
1. Add filter/sort API parameters to `GET /Items` endpoint
2. Add bulk operation endpoint `POST /Encode/Bulk` with criteria
3. Add pre-encode preview endpoint `POST /Encode/Preview`

**Phase 4: Notifications**
1. Inject `IActivityManager` into `JobQueueService`
2. Log job completions/failures to Activity Log
3. Optionally use `INotificationManager` for admin alerts

**Phase 5: Dashboard Card**
1. Best option: Use Activity Log integration
2. Alternative: Create WebSocket endpoint for real-time status
3. Note: True dashboard cards require jellyfin-web modification

---

### Residual Risks

1. **Jellyfin version lock** — Tied to 10.11.11 API; upgrades may break compatibility
2. **No frontend testing** — UI changes require manual testing in Jellyfin
3. **Job progress accuracy** — Current file-size-based progress is approximate
4. **GPU monitoring** — Requires system-level queries not available in plugin sandbox
5. **Dashboard card limitation** — No native plugin API for dashboard widgets
```

Now let me produce the acceptance report: