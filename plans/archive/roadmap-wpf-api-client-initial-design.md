# 🗺️ AtcSoft.VideoSurveillance Roadmap

## 🖥️ AtcSoft.VideoSurveillance.WpfApp

A new WPF management application for the `AtcSoft.VideoSurveillance.Api` server. Combines the management features of the existing Blazor WebAssembly UI with live multi-camera streaming and recording playback over HTTP.

### 💡 Motivation

The existing `AtcSoft.Wpf.CameraWall.App` is a standalone desktop application that connects directly to cameras via RTSP/HTTP. The new `WpfApp` is a **client** for the REST API server, enabling:

- Centralized server-side camera management and recording
- Live camera streaming over HTTP/HLS from the server
- Remote management from any Windows machine on the network
- Recording playback streamed from the server's recording archive

### 🏛️ Architecture

```mermaid
graph TB
    subgraph WpfApp["AtcSoft.VideoSurveillance.WpfApp"]
        UI["WPF UI<br/><i>Fluent.Ribbon</i>"]
        Gateway["GatewayService<br/><i>REST Client</i>"]
        HubService["SurveillanceHubService<br/><i>SignalR Client</i>"]
        HLS["HLS Player<br/><i>VideoEngine or MediaElement</i>"]
    end

    subgraph Server["AtcSoft.VideoSurveillance.Api"]
        API["REST Endpoints"]
        Hub["SignalR Hub"]
        Streams["/streams/{cameraId}/playlist.m3u8"]
        Recordings["/recordings-files/{path}"]
    end

    Gateway -- "HTTP" --> API
    HubService -- "WebSocket" --> Hub
    HLS -- "HLS" --> Streams
    HLS -- "HTTP" --> Recordings
```

### 📂 Project Structure

```
src/
└── AtcSoft.VideoSurveillance.WpfApp/
    ├── AtcSoft.VideoSurveillance.WpfApp.csproj  (net10.0-windows, WPF)
    ├── App.xaml / App.xaml.cs                     (Host, DI, Serilog)
    ├── MainWindow.xaml / MainWindow.xaml.cs        (Fluent.Ribbon shell)
    ├── Services/
    │   ├── GatewayService.cs                      (REST API client)
    │   ├── GatewayService.Cameras.cs
    │   ├── GatewayService.Layouts.cs
    │   ├── GatewayService.Recordings.cs
    │   ├── GatewayService.Settings.cs
    │   └── SurveillanceHubService.cs              (SignalR client)
    ├── ViewModels/
    │   ├── DashboardViewModel.cs
    │   ├── CamerasViewModel.cs
    │   ├── CameraFormViewModel.cs
    │   ├── LayoutsViewModel.cs
    │   ├── LiveViewModel.cs
    │   ├── RecordingsViewModel.cs
    │   └── SettingsViewModel.cs
    ├── Views/
    │   ├── DashboardView.xaml
    │   ├── CamerasView.xaml
    │   ├── CameraFormDialog.xaml
    │   ├── LayoutsView.xaml
    │   ├── LayoutEditorDialog.xaml
    │   ├── LiveView.xaml
    │   ├── RecordingsView.xaml
    │   └── SettingsDialog.xaml
    └── UserControls/
        ├── LiveCameraTile.xaml                     (HLS stream tile)
        ├── MotionBoundingBoxOverlay.xaml           (bounding box overlay)
        └── RecordingPlayer.xaml                    (HTTP playback)
```

### 📦 Dependencies

```
AtcSoft.VideoSurveillance.WpfApp
├── AtcSoft.VideoSurveillance.Core          (models, enums, settings)
├── AtcSoft.VideoSurveillance.Api.Contracts (generated API client types)
├── AtcSoft.VideoEngine                     (HLS playback via FFmpeg)
├── AtcSoft.VideoEngine.DirectX             (GPU-accelerated rendering)
├── AtcSoft.Wpf.VideoPlayer                 (VideoHost control for HLS)
├── Fluent.Ribbon                            (Ribbon UI)
├── Atc.XamlToolkit                          (MVVM source generators)
├── Atc.Wpf.Controls                         (UI controls)
├── Microsoft.AspNetCore.SignalR.Client       (real-time events)
├── Microsoft.Extensions.Hosting              (DI, configuration)
└── Serilog                                   (logging)
```

Phases 1-6 of the VideoSurveillance.Wpf.App implementation are **complete**. All remaining and future phases (7+) are tracked in [`roadmap_VSWpfApp.md`](roadmap_VSWpfApp.md).

## 🏗️ Phase 1: Foundation

Core infrastructure and basic camera management.

### 1.1 Project Setup

- [x] Create `AtcSoft.VideoSurveillance.WpfApp` project (net10.0-windows, WPF)
- [x] Configure `Microsoft.Extensions.Hosting` with DI container
- [x] Configure Serilog with file sink (matching existing App patterns)
- [x] Add Fluent.Ribbon `MainWindow` shell with tab structure
- [x] Add configurable API base URL (via appsettings.json or connection dialog)
- [x] Add project to `AtcSoft.VideoSurveillance.slnx` solution

### 1.2 API Client Layer

- [x] Implement `GatewayService` using `Atc.Rest.Client` and generated API contracts
- [x] Camera endpoints: List, Create, Get, Update, Delete, Snapshot, Start/Stop Recording
- [x] Layout endpoints: List, Create, Update, Delete, Apply
- [x] Recording endpoints: List (with optional camera filter)
- [x] Settings endpoints: Get, Update
- [x] Add HTTP resilience handler (retry, timeout)
- [x] Add error handling with user-friendly error messages

### 1.3 SignalR Client Layer

- [x] Implement `SurveillanceHubService` connecting to `/hubs/surveillance`
- [x] Handle `ConnectionStateChanged` events
- [x] Handle `RecordingStateChanged` events
- [x] Handle `MotionDetected` events with bounding box data
- [x] Handle `StreamStarted` events with HLS playlist URLs
- [x] Auto-reconnect with connection state tracking
- [x] Connection status indicator in the Ribbon status bar

### 1.4 Dashboard View

- [x] Real-time statistics: total cameras, connected count, active recordings, layout count
- [x] Quick action buttons: Add Camera, Create Layout, Open Settings
- [x] Server connection status indicator
- [x] Auto-refresh via SignalR events

---

## 📷 Phase 2: Camera and Layout Management

Full CRUD operations matching BlazorApp feature parity.

### 2.1 Cameras View

- [x] DataGrid listing all cameras (Name, IP, Port, Protocol, State, Recording)
- [x] Connection state with color-coded indicators (connected = green, error = red, etc.)
- [x] Real-time state updates via SignalR
- [x] Toolbar buttons: Add, Edit, Delete, Snapshot, Start/Stop Recording
- [x] Context menu on camera rows with all actions
- [x] Snapshot capture with save-to-file dialog

### 2.2 Camera Form Dialog

- [x] Expandable sections: Connection, Authentication, Display, Stream Settings
- [x] Validation with error indicators (required fields, port range, etc.)
- [x] Test Connection button (via API)
- [x] Per-camera override tabs: Connection, Display, Performance, Motion Detection, Recording
- [x] Override toggle pattern: "Use Master Settings" / "Override Locally"

### 2.3 Layouts View

- [x] DataGrid listing all layouts (Name, Grid Size, Camera Count)
- [x] Toolbar buttons: Create, Edit, Delete, Apply
- [x] Set Startup Layout action

### 2.4 Layout Editor Dialog

- [x] Grid configuration: Rows (1-8) and Columns (1-8)
- [x] Dual-panel drag-drop editor:
  - Left panel: Available (unassigned) cameras
  - Right panel: Grid positions with assigned cameras
- [x] Drag cameras between available and grid positions
- [x] Save layout via API

---

## 🎥 Phase 3: Live Multi-Camera Streaming

The key differentiating feature: live camera streaming from the server over HTTP/HLS.

### 3.1 HLS Stream Infrastructure

- [x] Integrate `AtcSoft.VideoEngine` for HLS playback (FFmpeg demux + decode)
- [x]Use `AtcSoft.Wpf.VideoPlayer.VideoHost` for GPU-accelerated rendering
- [x] Handle `StreamStarted` SignalR events to receive HLS playlist URLs
- [x] Start/Stop stream via SignalR hub invocation (`StartStream`, `StopStream`)
- [x] Fallback to `MediaElement` for HLS if VideoEngine unavailable

### 3.2 Live View

- [x] Layout selector dropdown (populated from API)
- [x] Dynamic camera grid based on selected layout (rows x columns)
- [x] `LiveCameraTile` UserControl per camera:
  - Camera name overlay
  - Connection status indicator
  - Start/Stop stream button
  - HLS video playback via VideoHost
  - Recording indicator
- [x] Auto-start streams when layout is selected
- [x] Grid resize handling with aspect ratio preservation

### 3.3 Motion Detection Visualization

- [x] `MotionBoundingBoxOverlay` UserControl on each live tile
- [x] Real-time bounding box rendering from SignalR `MotionDetected` events
- [x] Coordinate mapping from analysis resolution to display resolution
- [x] Smoothing algorithm for bounding box transitions (matching existing WPF implementation)
- [x] Configurable color, thickness, and visibility per bounding box settings
- [x] Motion indicator badge on camera tiles

### 3.4 Live View Context Menu

- [x] Full screen (single camera, Escape to exit)
- [x] Snapshot capture
- [x] Start/Stop recording
- [x] Start/Stop stream
- [x] Edit camera
- [x] Swap left/right positions

---

## 🎬 Phase 4: Recordings Management

Browse, play, and download recordings streamed from the server.

### 4.1 Recordings View

- [x] DataGrid listing recordings (Camera, File Path, Start Time, Duration, Size)
- [x] Camera filter dropdown
- [x] Date range filter
- [x] Refresh button
- [x] Real-time updates when `RecordingStateChanged` fires (new recording completed)

### 4.2 Recording Playback

- [x] `RecordingPlayer` UserControl with HTTP video playback
- [x] Stream recordings from server via `/recordings-files/{path}` endpoint
- [x] Playback controls: Play, Pause, Seek, Volume
- [x] Playback overlay showing filename and timestamp (per PlaybackOverlaySettings)
- [x] Full-screen playback mode (matching `FullScreenRecordingWindow` pattern)

### 4.3 Recording Actions

- [x] Play recording in embedded player
- [x] Full-screen playback
- [x] Download recording to local file (HTTP download with progress)
- [x] Delete recording (with confirmation)

---

## ⚙️ Phase 5: Settings and Configuration

Comprehensive settings management matching the 7-tab Settings dialog.

### 5.1 Settings Dialog

- [x] **General** tab: Theme (Dark/Light), Accent, Language, Startup behavior
- [x] **Camera Display** tab: Overlay settings, Grid layout, Snapshot path
- [x] **Connection** tab: Default protocol/port, Timeout, Reconnect, Notifications
- [x] **Performance** tab: Video quality, Hardware acceleration, Buffer, RTSP transport
- [x] **Motion Detection** tab: Sensitivity, Analysis resolution/FPS, Post-motion, Cooldown, Bounding box settings
- [x] **Recording** tab: Path, Format, Segmentation, Timelapse, Cleanup, Playback overlay
- [x] **Advanced** tab: Debug logging, Log path

### 5.2 Settings Persistence

- [x] Load settings from API on dialog open (`GET /settings`)
- [x] Save settings to API on dialog OK (`PUT /settings`)
- [x] Per-section save support
- [x] Revert/Cancel without saving

---

## ✨ Phase 6: Polish and Advanced Features

### 6.1 Connection Management

- [x] Server connection dialog on startup (URL, optional auth)
- [x] Remember last server URL
- [x] Auto-reconnect to API on network recovery
- [x] Connection status in Ribbon status bar
- [x] Multiple server profiles (save/switch between servers)

### 6.2 Notifications

- [x] Toast notifications for camera disconnect/reconnect events
- [x] Toast notifications for motion detection events
- [x] Toast notifications for recording start/stop
- [x] Optional notification sounds (matching existing app behavior)
- [x] Notification history panel

### 6.3 UI Enhancements

- [x] Dark/Light theme support (applied from server settings)
- [x] Multi-language support (loaded from server settings LCID)
- [x]Keyboard shortcuts for common actions
- [x] Ribbon quick access toolbar customization
- [x] Window state persistence (size, position, maximized)

### 6.4 Aspire Integration

- [x] Add `WpfApp` to Aspire orchestration as optional client
- [x] Service discovery for API base URL
- [x] Health check integration

---

## 📊 Phase Summary

| Phase | Focus | Key Deliverable |
|-------|-------|-----------------|
| **Phase 1** | Foundation | Project scaffold, API client, SignalR, Dashboard |
| **Phase 2** | Management | Camera CRUD, Layout editor, full BlazorApp parity |
| **Phase 3** | Live Streaming | Multi-camera HLS grid, motion visualization |
| **Phase 4** | Recordings | Browse, play, download server recordings |
| **Phase 5** | Settings | 7-tab settings dialog via API |
| **Phase 6** | Polish | Notifications, themes, connection management |

## 🔄 Feature Comparison

| Feature | BlazorApp | WpfApp (planned) |
|---------|-----------|-------------------|
| Camera CRUD | Yes | Phase 2 |
| Layout management | Yes | Phase 2 |
| Layout drag-drop editor | Yes | Phase 2 |
| Dashboard with stats | Yes | Phase 1 |
| Settings (7 tabs) | Yes | Phase 5 |
| Live multi-camera grid | Yes (HLS.js) | Phase 3 (VideoEngine) |
| Motion bounding boxes | Yes (SVG overlay) | Phase 3 (WPF overlay) |
| Recording browser | Yes | Phase 4 |
| Recording playback | Yes (HTML5 video) | Phase 4 (VideoHost) |
| Recording download | Yes (JS interop) | Phase 4 (HTTP download) |
| Real-time SignalR events | Yes | Phase 1 |
| Dark/Light theme | Yes | Phase 6 |
| GPU-accelerated rendering | No (browser) | Phase 3 (D3D11VA) |
| Low-latency playback | No (HLS latency) | Phase 3 (VideoEngine) |
| Notification sounds | No | Phase 6 |
| Multiple server profiles | No | Phase 6 |
