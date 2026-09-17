# 🗺️ AtcSoft.VideoSurveillance.Wpf.App Roadmap

## 🎯 Vision

A native WPF desktop client for the Linksoft VideoSurveillance REST API server. Unlike the standalone CameraWall app (which connects directly to cameras via RTSP/HTTP), this app is a **thin client** that communicates exclusively through the API server and SignalR hub.

Key differentiator: native recording playback over HTTP using VideoEngine/MediaElement instead of browser-based HLS streaming.

## 🏛️ Architecture

```
+-------------------------------------------+     +-------------------------------------------+
|  AtcSoft.CameraWall.Wpf.App              |     |  AtcSoft.VideoSurveillance.Wpf.App       |
|  (Thin Shell: Ribbon, DI)                 |     |  (Thin Shell: Ribbon, Serilog, DI)        |
+-------------------------------------------+     +-------------------------------------------+
|  AtcSoft.CameraWall.Wpf                  |     |  AtcSoft.VideoSurveillance.Wpf            |
|  (CameraWallEngine, CameraWallManager,    |     |  (GatewayService, HubService,             |
|   VideoEngineMediaPipeline, local storage)|     |   API Views, API ViewModels)               |
+-------------------------------------------+     +-------------------------------------------+
                    \                                          /
                     +----------------------------------------+
                     |  AtcSoft.VideoSurveillance.Wpf.Core   |
                     |  (Shared: Models, Dialogs, Controls,   |
                     |   Factories, Converters, Themes,       |
                     |   Service Interfaces, Events, Helpers) |
                     +----------------------------------------+
                     |  AtcSoft.VideoPlayer.Wpf               |
                     +----------------------------------------+
                     |  AtcSoft.VideoSurveillance.Core        |
                     +----------------------------------------+

Communication:
  WPF App --[REST]--> API Server (CRUD, snapshots, recordings)
  WPF App --[SignalR]--> /hubs/surveillance (real-time events)
  WPF App --[HTTP]--> /recordings-files/{path} (native video playback)
```

## 📋 Phases

### Phase 1: Init-Setup ✅
- Project scaffolding with DI, Serilog, splash screen
- GatewayService stubs (OpenAPI-generated API client)
- SurveillanceHubService stub (SignalR client)
- Fluent.Ribbon shell with placeholder buttons
- Aspire integration for service discovery
- Build verification (0 errors, 0 warnings)

### Phase 2: Dashboard & Camera Management ✅
- Dashboard view showing live server stats (connected cameras, active recordings)
- Camera list with CRUD operations via GatewayService
- SignalR connection state display in status bar
- Start/stop recording from UI
- Snapshot capture and display
- ContentControl navigation with DataTemplates
- CameraEditDialog for add/edit with full form fields
- Real-time SignalR updates for connection state and recording status

### Phase 3: Live View ✅
- Camera grid layout using VideoHost controls
- HLS or direct HTTP stream playback via VideoEngine
- Motion bounding box overlay (port from CameraWall)
- Camera overlay (title, status, recording indicator)
- Full-screen camera view

### Phase 4: Recording Playback ✅
- Recording browser with filtering by camera, day, and time
- Native HTTP video playback via MediaElement
- Seek, speed controls (1x-16x), full-screen playback
- Port FullScreenRecordingWindow from CameraWall (adapted for HTTP URLs)
- RecordingEntryViewModel adapter for API Recording model

### Phase 5: Layout Management ✅
- Layout CRUD via API (list, create, edit name/grid size, delete)
- Apply layout (server-side activation)
- DataGrid with action buttons (edit, apply, delete)

### Phase 6: Settings & Polish ✅
- Settings dialog with tabbed UI (General, Camera Display, Connection, Performance, Recording, Advanced)
- Theme support (Dark/Light with accent color, live preview)
- About dialog with version display
- Check for Updates dialog via GitHub Releases API

### Phase 7: Shared WPF Library (Wpf.Core)

#### 7.1 Project Setup ✅
- [x] Create `AtcSoft.VideoSurveillance.Wpf.Core` project (net10.0-windows, WPF library)
- [x] Add project references to Core and VideoPlayer.Wpf
- [x] Add shared Atc.* package references (Wpf, Controls, Forms, Network, Theming, XamlToolkit)
- [x] Add to solution file
- [x] Configure GlobalUsings.cs

#### 7.2 Move Shared Infrastructure (CameraWall.Wpf → Wpf.Core) ✅
- [x] Models: CameraConfiguration, CameraLayout, CameraStorageData, RecordingEntry, Settings/*
- [x] Events: DialogClosedEventArgs, CameraConnectionChangedEventArgs, CameraPositionChangedEventArgs, FullScreenRequestedEventArgs
- [x] Factories: DropDownItemsFactory
- [x] Helpers: AppHelper, BoundingBoxExtensions, GridLayoutHelper
- [x] Extensions: CameraProtocolExtensions
- [x] Messages: CameraAddMessage, CameraRemoveMessage, CameraSwapMessage
- [x] ValueConverters: BoolToOpacity, ConnectionStateToColor/Text, CameraConfigurationJson, OverrideOrDefaultMulti
- [x] ApplicationPaths (parameterized with Configure() for app-specific folder names)

#### 7.3 Move Themes & Resources ✅
- [x] FullScreenStyles.xaml, OverrideStyles.xaml
- [x] Translations.resx (en-US, da-DK, de-DE)
- [x] Application icons (cctv-camera.ico/.png)

#### 7.4 Move Service Interfaces & Generic Implementations ✅
- [x] Interfaces: IApplicationSettingsService, ICameraStorageService, ICameraWallManager, IDialogService, IRecordingService, ITimelapseService
- [x] Implementations: ApplicationSettingsService, CameraStorageService, DialogService, GitHubReleaseService

#### 7.5 Move Dialogs & Parts ✅
- [x] CameraConfigurationDialog + 11 configuration parts (Scanner, Connection, Auth, Stream, Display/Connection/Performance/Capture/Timelapse/MotionDetection overrides)
- [x] SettingsDialog + 21 settings parts
- [x] AboutDialog, CheckForUpdatesDialog, RecordingsBrowserDialog, AssignCameraDialog

#### 7.6 Move UserControls & Windows ✅
- [x] CameraGrid, CameraTile, CameraOverlay, MotionBoundingBoxOverlay
- [x] FullScreenCameraWindow, FullScreenRecordingWindow
- [x] CameraTile refactored: replaced `new VideoEngineMediaPipeline()` with `MediaPipelineFactory` dependency property

#### 7.7 Update CameraWall.Wpf ✅
- [x] Add ProjectReference to Wpf.Core
- [x] Verify CameraWall.Wpf.App builds and runs (0 errors, 0 warnings)
- [x] Delete 135 duplicate files from CameraWall.Wpf (now in Wpf.Core)
- [x] Update GlobalUsings to Wpf.Core namespaces
- [x] Remove transitive NuGet packages (now provided by Wpf.Core)
- [x] Update CameraWall.Wpf.App XAML references (UserControls, Translations, icon)
- [x] Verify full solution builds (0 errors)

#### 7.8 Update VideoSurveillance.Wpf ✅
- [x] Add ProjectReference to Wpf.Core (replaces direct VideoPlayer.Wpf reference)
- [x] Remove transitive NuGet packages now provided by Wpf.Core
- [x] Verify VideoSurveillance.Wpf.App builds (0 errors)
- [x] Delete duplicates: DialogClosedEventArgs, GridLayoutHelper, FullScreenStyles.xaml
- [x] Update GlobalUsings to Wpf.Core namespaces (Events, Helpers)
- [x] Update XAML references (CameraListView converter, FullScreen windows ResourceDictionary)
- [x] Add CameraModelMappingExtensions (API Camera ↔ Wpf.Core CameraConfiguration, with enum mappings)
- [ ] Replace CameraEditDialog with shared CameraConfigurationDialog (deferred to Phase 8)
- [ ] Update DI registrations in Wpf.App (deferred to Phase 8)

### Phase 8: Unified Dialog Migration ✅

#### 8.1 XAML Namespace Migration ✅
- [x] Fix 273 stale `AtcSoft.CameraWall.Wpf` references in 40 Wpf.Core XAML files
- [x] Fix Translations.Designer.cs ResourceManager name for Wpf.Core namespace

#### 8.2 About & Updates Dialogs ✅
- [x] Delete VS-specific AboutDialog (XAML + code-behind)
- [x] Delete VS-specific CheckForUpdatesDialog (XAML + code-behind + ViewModel)
- [x] Add Wpf.Core.Dialogs to App GlobalUsings
- [x] Fix SettingsDialog ambiguity with fully qualified namespace

#### 8.3 Full Screen Windows ✅
- [x] Replace VS-specific FullScreenRecordingWindow with Wpf.Core version (HTTP URL works via `new Uri(filePath)`)
- [x] Delete 3 VS-specific FullScreenRecordingWindow files (XAML, code-behind, ViewModel)
- [x] Add file-level using for Wpf.Core.Windows in RecordingsViewModel
- [x] Keep VS-specific FullScreenCameraWindow (fundamentally different: SurveillanceHubService vs CameraConfiguration)

#### 8.4 Cleanup & Verification ✅
- [x] Clean up unnecessary GlobalUsings (Wpf.Dialogs no longer needed in App)
- [x] Verify VideoSurveillance.Wpf.App builds (0 errors, 4 warnings)
- [x] Verify CameraWall.Wpf.App builds (0 errors, 0 warnings)

#### Not Migrated (By Design)
The following VS.Wpf dialogs/windows intentionally remain app-specific because they serve fundamentally different purposes than their Wpf.Core counterparts:
- **SettingsDialog** — API async flat record with snapshot preservation vs Wpf.Core's local sync 7-group model
- **CameraEditDialog** — lightweight API CRUD vs Wpf.Core's CameraConfigurationDialog (requires IApplicationSettingsService, IVideoPlayerFactory, network scanner)
- **FullScreenCameraWindow** — SurveillanceHubService (remote) vs Wpf.Core's CameraConfiguration (local camera connection)
- **LayoutEditDialog** — API layout model with server-side grid configuration

### Phase 9: Enhanced Features

#### 9.1 Server Connection Management ✅
- [x] Server connection dialog on startup (URL, optional auth token)
- [x] Remember last connected server URL (persisted locally)
- [x] Multiple server profiles (add, edit, delete, switch)
- [x] Server profile storage in local JSON config (`%LocalAppData%\Linksoft\VideoSurveillance\servers.json`)
- [x] "Change Server" backstage button (restarts with `--choose-server` flag)
- [x] Aspire detection (skip dialog, hide Change Server when Aspire-managed)
- [ ] Auto-reconnect to API and SignalR on network recovery (deferred)

#### 9.2 Notifications ✅
- [x] Toast notification service (WPF toast via IToastNotificationService)
- [x] Camera disconnect/reconnect notifications
- [x] Motion detection notifications (with camera name and rate limiting)
- [x] Recording start/stop notifications
- [x] Notification settings (enable/disable per event type, optional sounds, local JSON persistence)
- [x] Notification history panel (recent events with timestamps, ribbon button, clear action)

#### 9.3 UI Enhancements ✅
- [x] Keyboard shortcuts for common actions (Ctrl+N new camera, F5 refresh, F11 full screen, etc.)
- [x] Keyboard shortcut reference dialog
- [x] Window state persistence (size, position, maximized state, saved to local config)
- [x] Ribbon quick access toolbar customization
- [x] Status bar enhancements (server latency, connected camera count, active recordings)

#### 9.4 Installer ✅
- [x] WiX installer project for Wpf.App (matching CameraWall.Wpf.App installer pattern)
- [x] Desktop and Start Menu shortcuts
- [x] Auto-start option (Windows startup registry) — `IAutoStartService` / `AutoStartService` write the per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value (HKCU so no admin elevation needed). Surfaced as a "Start with Windows" checkbox in the ribbon Backstage; `MainWindowViewModel.StartWithWindows` seeds from the registry at construction and routes toggle changes back through the service. Registry I/O failures are logged and treated as "disabled" so the UI never crashes the app.
- [x] Upgrade support (preserve local config on update)

### Phase 10: Advanced Capabilities

#### 10.1 Multi-Monitor Support
- [ ] Detach camera grid to secondary monitor
- [ ] Independent full-screen windows per monitor
- [ ] Monitor layout persistence (remember which cameras on which monitor)

#### 10.2 Camera Groups & Filtering
- [ ] Camera group/tag management (assign tags via API)
- [ ] Filter camera list and grid by group/tag
- [ ] Group-based recording and notification rules

#### 10.3 Audit & Diagnostics
- [ ] Event log viewer (API server events streamed via SignalR)
- [ ] Camera health dashboard (uptime, reconnect frequency, stream quality)
- [x] Export diagnostics report (camera states, server info, connection logs) — `IDiagnosticsExportService` builds a JSON snapshot from `ApplicationHelper.GetVersion()` + `RuntimeInformation` (client), `SurveillanceHubService.ConnectionState` + `GatewayService.GetCamerasAsync` (server), and the per-camera DTOs (id / name / source / IP or USB device id / connection state / recording flag). Surfaced as a "Export Diagnostics" button in the ribbon Backstage; `SaveFileDialog` defaults to Desktop with a timestamped filename. Gateway failures are captured in `GatewayErrorMessage` instead of thrown so a degraded-server scenario still produces a useful report. Log file contents are deliberately not bundled (size + rolling-lock concerns); the report includes the log path so support knows where to fetch them separately.

#### 10.4 Timelapse Management
- [ ] Timelapse browser (list server-side timelapse recordings)
- [ ] Timelapse playback via HTTP (VideoHost)
- [ ] Configure timelapse schedules per camera via API

## 📊 Phase Summary

| Phase | Focus | Status | Key Deliverable |
|-------|-------|--------|-----------------|
| **Phase 1** | Init-Setup | ✅ | Project scaffold, GatewayService, SignalR, Aspire |
| **Phase 2** | Dashboard & Cameras | ✅ | Dashboard stats, Camera CRUD, CameraEditDialog, SignalR events |
| **Phase 3** | Live View | ✅ | Camera grid, HTTP stream playback, motion overlay, full screen |
| **Phase 4** | Recording Playback | ✅ | Recording browser, HTTP playback, seek/speed, full screen |
| **Phase 5** | Layout Management | ✅ | Layout CRUD via API, DataGrid with actions |
| **Phase 6** | Settings & Polish | ✅ | Settings dialog, themes, About, Check for Updates |
| **Phase 7** | Shared WPF Library | ✅ | Wpf.Core shared library, ~120 files moved from CameraWall.Wpf |
| **Phase 8** | Dialog Migration | ✅ | AboutDialog, CheckForUpdates, FullScreenRecordingWindow migrated to Wpf.Core |
| **Phase 9** | Enhanced Features | ✅ | Server profiles, notifications, shortcuts, installer |
| **Phase 10** | Advanced Capabilities | Future | Multi-monitor, camera groups, audit log, timelapse |

## 🧩 Shared Code Strategy

### Approach: Shared WPF Library (Wpf.Core)

Shared WPF components live in `AtcSoft.VideoSurveillance.Wpf.Core`, referenced by both `CameraWall.Wpf` and `VideoSurveillance.Wpf`. This replaces the earlier copy+adapt approach with a proper shared library.

### What Lives in Wpf.Core

| Category | Components |
|----------|-----------|
| **Models** | CameraConfiguration, CameraLayout, CameraStorageData, RecordingEntry, Settings/* |
| **Dialogs** | CameraConfigurationDialog (+ 11 Parts), SettingsDialog (+ 21 Parts), AboutDialog, CheckForUpdatesDialog, RecordingsBrowserDialog, AssignCameraDialog |
| **UserControls** | CameraGrid, CameraTile, CameraOverlay, MotionBoundingBoxOverlay |
| **Windows** | FullScreenCameraWindow, FullScreenRecordingWindow |
| **Services** | Interfaces (IApplicationSettingsService, ICameraStorageService, IDialogService, etc.) + generic implementations |
| **Infrastructure** | DropDownItemsFactory, ValueConverters, Events, Helpers, Extensions, Messages, Themes, Translations |

### What Stays App-Specific

| CameraWall.Wpf | VideoSurveillance.Wpf |
|----------------|----------------------|
| CameraWallEngine (core engine) | GatewayService (REST API client) |
| CameraWallManager (orchestrator) | SurveillanceHubService (SignalR client) |
| VideoEngineMediaPipeline | API-specific Views & ViewModels |
| Local recording/motion/timelapse services | ApiSettingsAdapter (bridges API settings to Wpf.Core) |
| | CameraModelMappingExtensions (API ↔ shared models) |
| | Server profile management (Phase 9) |

## 🔄 Feature Comparison

| Feature | CameraWall.Wpf.App | VideoSurveillance.Wpf.App | Blazor.App |
|---------|-------------------|--------------------------|------------|
| Camera connection | Direct RTSP/HTTP | Via API server | Via API server |
| Video rendering | D3D11VA + DComp | VideoHost (HTTP) | HLS in browser |
| Recording | Local FFmpeg remux | API start/stop | API start/stop |
| Playback | Local file | HTTP via VideoEngine | HLS in browser |
| Motion detection | Local processing | Server-side + SignalR | Server-side + SignalR |
| Real-time events | Direct callbacks | SignalR hub | SignalR hub |
| Shared UI components | Via Wpf.Core | Via Wpf.Core | N/A (Blazor) |
| Server profiles | N/A (direct) | Phase 9 | N/A (URL-based) |
| Toast notifications | N/A | Phase 9 | N/A |
| Multi-monitor | N/A | Phase 10 | N/A |
| Camera groups/tags | N/A | Phase 10 | Phase 10 |
| Timelapse management | Local | Phase 10 (HTTP) | Phase 10 (HLS) |
| Deployment | Standalone installer | Installer (needs API) | Browser (needs API) |
| Offline capable | Yes | No | No |

## 🔗 Phase Dependencies

```
Phase 7 (Wpf.Core)
  ├── Phase 8 (Dialog Migration)  ── requires Wpf.Core dialogs to exist
  │     └── Phase 9.4 (Installer) ── should include unified dialogs
  ├── Phase 9.1-9.3 (Features)   ── can start in parallel with Phase 8
  └── Phase 10 (Advanced)        ── requires Phase 8 + 9 complete
```

| Phase | Depends On | Can Parallelize With |
|-------|-----------|---------------------|
| **Phase 7** | None (next up) | — |
| **Phase 8** | Phase 7 | Phase 9.1-9.3 |
| **Phase 9.1-9.3** | Phase 7 | Phase 8 |
| **Phase 9.4** | Phase 8 | — |
| **Phase 10** | Phase 8 + 9 | — |

## 💾 Local vs Server Storage

VideoSurveillance.Wpf.App is a thin client, but some data must be stored locally:

| Data | Storage | Location |
|------|---------|----------|
| Server profiles | Local JSON | `%LocalAppData%\Linksoft\VideoSurveillance\servers.json` |
| Window state | Local JSON | `%LocalAppData%\Linksoft\VideoSurveillance\window-state.json` |
| Notification preferences | Local JSON | `%LocalAppData%\Linksoft\VideoSurveillance\notifications.json` |
| Keyboard shortcuts | Local JSON | `%LocalAppData%\Linksoft\VideoSurveillance\shortcuts.json` |
| Debug logs | Local files | `%ProgramData%\Linksoft\VideoSurveillance\logs\` |
| Cached snapshots | Local files | `%ProgramData%\Linksoft\VideoSurveillance\snapshots\` |
| Camera config | **Server** (API) | `GET/PUT /api/cameras` |
| Layouts | **Server** (API) | `GET/PUT /api/layouts` |
| Settings | **Server** (API) | `GET/PUT /api/settings` |
| Recordings | **Server** (API) | `GET /api/recordings`, streamed via HTTP |

## 🌍 API Requirements for Future Phases

Some planned features require new or extended API endpoints that do not yet exist:

| Phase | Feature | Required API Change |
|-------|---------|-------------------|
| **9.1** | Server profiles | Client-only (no API change) |
| **9.2** | Notification settings | Client-only (SignalR events already exist) |
| **10.2** | Camera groups/tags | New: `GET/PUT /api/cameras/{id}/tags`, `GET /api/tags` |
| **10.3** | Event log viewer | New: `GET /api/events` or new SignalR event stream |
| **10.3** | Camera health | New: `GET /api/cameras/{id}/health` (uptime, quality metrics) |
| **10.4** | Timelapse schedules | New: `GET/PUT /api/cameras/{id}/timelapse` |
| **10.4** | Timelapse browser | New: `GET /api/timelapses`, `/timelapse-files/{path}` |

## ⚠️ Migration Risks & Mitigations (Phase 7)

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Namespace changes break CameraWall.Wpf | Build failures | Move files in batches, build after each batch |
| XAML resource dictionaries not found | Runtime crashes | Add `Generic.xaml` or merged dictionaries in Wpf.Core |
| DI registration order changes | Service resolution failures | Test both apps after each service move |
| Translations not loading from new assembly | Missing UI text | Ensure `.resx` files have correct Build Action and namespace |
| `ApplicationPaths` folder mismatch | Wrong file paths | Parameterize base folder name ("CameraWall" vs "VideoSurveillance") |
| Source generators not running in new project | Missing generated code | Verify `[ObservableProperty]`, `[Registration]` attributes work in Wpf.Core |
