# 📹 Linksoft.VideoSurveillance

A professional video surveillance platform for live monitoring of multiple RTSP/HTTP camera streams. Includes a WPF desktop application with an intuitive ribbon interface, a headless REST API + SignalR server edition, and Aspire orchestration.

[![Release](https://img.shields.io/github/v/release/davidkallesen/Linksoft.VideoSurveillance?include_prereleases)](https://github.com/davidkallesen/Linksoft.VideoSurveillance/releases)
[![NuGet](https://img.shields.io/nuget/v/Linksoft.CameraWall.Wpf)](https://www.nuget.org/packages/Linksoft.CameraWall.Wpf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## ✨ Features

### 🖥️ Camera Display
- **Dynamic Grid Layout** - Auto-calculates optimal grid based on camera count (optimized for 4:3 aspect ratio)
- **Camera Tile Overlay** - Configurable overlay showing title, description, timestamp, and connection status
- **Overlay Configuration** - Choose corner position (TopLeft, TopRight, BottomLeft, BottomRight) and opacity (0-100%)
- **Stream Rotation** - Per-camera 0°/90°/180°/270° rotation applied at the GPU video processor stage; live display, snapshots, and recordings are all rotated. Recordings carry an MP4 `rotate` metadata tag so any standard player honours it.
- **Full Screen Mode** - Double-click or use context menu to view any camera in full screen (Escape to exit)
- **Connection State Indicators** - Visual indicators for Connected, Connecting, and Error states

### 🔍 Zoom & Pan
- **Scroll Zoom** - Ctrl+Scroll to zoom in/out on any camera tile or full screen view, centered on mouse pointer
- **Rectangle Selection Zoom** - Ctrl+Left-drag to draw a selection rectangle, zooms to the selected region on release
- **Pan** - Ctrl+Left-drag while zoomed to pan the viewport
- **Zoom Indicator** - "Zoom: 150%" badge displayed on camera tile while zoomed
- **Reset Zoom** - Double-click or ESC to reset back to fit-to-view
- **Single Zoom Policy** - Only one camera tile can be zoomed at a time in the grid layout; zooming another resets the previous
- **Motion Overlay Pause** - Bounding box overlays are hidden while zoomed to avoid visual clutter, restored on reset
- **GPU-Accelerated** - Zoom operates at the DirectComposition transform level for smooth, low-latency rendering

### 📷 Camera Management
- **Context Menu** - Edit, Delete, Full Screen, Swap Left/Right, Snapshot, Reconnect, Start/Stop Recording, Reset Zoom, Rotate (0°/90°/180°/270°)
- **Drag-and-Drop** - Reorder cameras by dragging tiles within the grid
- **Network Scanner** - Auto-discover cameras on the local network (integrated in Add Camera dialog)
- **Test Connection** - Validate camera connectivity before saving
- **Multiple Protocols** - RTSP, HTTP, and HTTPS support with configurable ports and paths
- **USB / Webcam Support** - Mix DirectShow / UVC USB cameras with IP cameras in the same grid (Windows). Devices are enumerated via Media Foundation; hot-plug events surface via WMI so unplugged devices transition cleanly to a `DeviceUnplugged` state instead of thrashing in a reconnect loop. See [`docs/usb-cameras.md`](docs/usb-cameras.md).
- **Per-Camera Overrides** - Override any application-level setting on a per-camera basis (connection, display, performance, recording, motion detection)

### 🔴 Recording
- **Manual Recording** - Start/stop recording per camera via context menu
- **Auto-Record on Connect** - Automatically start recording when a camera connects
- **Motion-Triggered Recording** - Automatically record when motion is detected, with configurable post-motion duration
- **Recording Segmentation** - Clock-aligned automatic file segmentation (e.g., every 15 minutes at :00, :15, :30, :45)
- **Recording Format** - Configurable format (MP4, MKV, AVI) with per-camera override
- **Recording Indicator** - Visual indicator when recording is active
- **Thumbnail Generation** - Auto-generates thumbnails from recording frames (single image or 2x2 grid)
- **Recordings Browser** - Browse, filter, and playback recorded videos with playback overlay

### 🏃 Motion Detection
- **Frame-Based Detection** - Compares consecutive frames using grayscale pixel differencing
- **Multi-Bounding Box** - Detects and highlights multiple motion regions simultaneously using grid-based clustering
- **Configurable Sensitivity** - 0-100 scale (higher = more motion required to trigger)
- **Analysis Resolution** - Configurable analysis width/height for performance tuning
- **Bounding Box Visualization** - Real-time colored bounding boxes in both grid and full-screen views with configurable color, thickness, smoothing, and minimum area
- **Smart Scheduling** - Staggered analysis across cameras to prevent CPU spikes
- **Post-Motion Duration** - Continue recording for configurable seconds after motion stops
- **Cooldown** - Configurable delay before motion can trigger a new recording

### ⏱️ Timelapse
- **Interval Capture** - Automatically capture snapshots at configurable intervals (10s, 1m, 5m, 1h)
- **Per-Camera Configuration** - Enable/disable and configure interval per camera
- **Organized Storage** - Snapshots stored in camera-named subdirectories

### 📐 Layout Management
- **Named Layouts** - Create, save, and switch between named layouts
- **Startup Layout** - Designate a layout to load on application start
- **Auto-Save** - Automatic persistence of camera positions (configurable)
- **Camera Assignment** - Dual-list dialog for assigning cameras to layouts

### 🧹 Media Cleanup
- **Automatic Cleanup** - Remove old recordings and snapshots based on retention policy
- **Flexible Scheduling** - On startup, hourly, daily, or weekly cleanup
- **Configurable Retention** - Set maximum age for recordings (default 30 days) and snapshots (default 7 days)

### 🔌 Connectivity
- **Auto-Reconnect** - Automatic reconnection on failure with configurable delay
- **Connection Timeout** - Configurable timeout per camera or globally
- **Disconnect Notifications** - Optional notifications on camera disconnect/reconnect
- **Notification Sounds** - Optional audio alert on connectivity events

### 🎨 User Interface
- **Fluent Ribbon UI** - Modern ribbon interface with Layouts, Cameras, View, and Help tabs
- **Theme Support** - Dark/Light theme switching with configurable accent colors
- **Multi-Language** - Localization support via LCID-based language selection
- **Settings Dialog** - Comprehensive settings organized into General, Camera Display, Connection, Performance, Recording, Motion Detection, Timelapse, and Advanced sections
- **Recordings Browser** - Browse and playback recorded videos with filtering
- **About Dialog** - Application version and information
- **Check for Updates** - GitHub-based update checking with download link

### 📦 Installation
- **MSI Installer** - Windows installer with Start Menu and Desktop shortcuts
- **Per-Machine Install** - Installs for all users on the machine

## 🛠️ Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 10.0 / .NET 10.0-windows |
| Desktop UI | WPF with Fluent.Ribbon, Atc.Wpf.Controls |
| Server | ASP.NET Core, SignalR |
| API Definition | OpenAPI 3.2 (atc-rest-api-source-generator) |
| Video Engine | Linksoft.VideoEngine (desktop + server, in-process FFmpeg) |
| Orchestration | .NET Aspire |
| MVVM | Atc.XamlToolkit |
| Source Generators | Atc.SourceGenerators |
| Theming | Atc.Wpf.Theming |
| Logging | Serilog (file sink) |
| Versioning | release-please (Conventional Commits) |
| Installer | WiX Toolset v5 |

## 🏗️ Solution Structure

```
Linksoft.VideoSurveillance/
├── src/
│   ├── Linksoft.VideoSurveillance.Core/        # 📚 Shared library: models, enums, events, service interfaces
│   ├── Linksoft.VideoEngine/                   # 🎬 Cross-platform video engine (FFmpeg in-process)
│   ├── Linksoft.VideoEngine.DirectX/           # 🖥️ D3D11VA GPU acceleration for WPF
│   ├── Linksoft.VideoPlayer.Wpf/               # 🎞️ WPF VideoHost control (DComp surface + overlay)
│   ├── Linksoft.VideoSurveillance.Wpf.Core/    # 🧩 Shared WPF library (dialogs, controls, services)
│   ├── Linksoft.CameraWall.Wpf/                # 📦 Reusable CameraWall WPF library (NuGet package)
│   ├── Linksoft.CameraWall.Wpf.App/            # 🚀 Standalone WPF desktop app (Fluent.Ribbon)
│   ├── Linksoft.VideoSurveillance.Wpf/         # 🌐 WPF library for API client (GatewayService, SignalR)
│   ├── Linksoft.VideoSurveillance.Wpf.App/     # 🚀 WPF API client app (Fluent.Ribbon)
│   ├── VideoSurveillance.yaml                  # 📝 OpenAPI 3.2 spec (shared contract)
│   ├── Linksoft.VideoSurveillance.Api.Contracts/ # 📋 Generated: endpoints, models, handler interfaces
│   ├── Linksoft.VideoSurveillance.Api.Domain/  # ⚙️ Handler implementations calling Core services
│   ├── Linksoft.VideoSurveillance.Api/         # 🌍 ASP.NET Core host with SignalR hub
│   ├── Linksoft.VideoSurveillance.Blazor.App/  # 🌐 Blazor WebAssembly UI (MudBlazor)
│   └── Linksoft.VideoSurveillance.Aspire/      # ☁️ Aspire AppHost (orchestration + dashboard)
│
├── setup/
│   ├── Linksoft.CameraWall.Installer/          # 📦 WiX MSI installer (CameraWall)
│   └── Linksoft.VideoSurveillance.Installer/   # 📦 WiX MSI installer (VideoSurveillance)
│
├── test/
│   ├── Linksoft.VideoSurveillance.Core.Tests/      # ✅ xUnit v3 tests for Core
│   ├── Linksoft.VideoSurveillance.Wpf.Core.Tests/  # ✅ xUnit v3 tests for Wpf.Core
│   ├── Linksoft.VideoSurveillance.Api.Tests/        # ✅ xUnit v3 tests for API
│   └── Linksoft.VideoEngine.Tests/                  # ✅ xUnit v3 tests for VideoEngine
│
└── docs/
    ├── architecture.md          # 🏛️ Architecture diagrams and assembly details
    ├── roadmap.md               # 🗺️ Overall project roadmap
    ├── roadmap_VSWpfApp.md      # 🗺️ VideoSurveillance WPF App roadmap
    ├── motion-detection.md      # 🏃 Motion detection setup guide
    └── settings.md              # ⚙️ Complete settings reference
```

### 🔗 Dependency Graph

Arrows point from a project to the projects it depends on (compile-time `ProjectReference`).

```mermaid
flowchart TD
    subgraph Foundation["📚 Foundation (cross-platform, net10.0)"]
        Core[Linksoft.VideoSurveillance.Core]
        VE[Linksoft.VideoEngine]
    end

    subgraph Server["☁️ Server (REST + SignalR)"]
        Contracts[Linksoft.VideoSurveillance.Api.Contracts]
        Domain[Linksoft.VideoSurveillance.Api.Domain]
        Api[Linksoft.VideoSurveillance.Api]
        Blazor[Linksoft.VideoSurveillance.Blazor.App]
    end

    subgraph Windows["🪟 Windows-only (net10.0-windows)"]
        VEDx[Linksoft.VideoEngine.DirectX]
        VPWpf[Linksoft.VideoPlayer.Wpf]
        WpfCore[Linksoft.VideoSurveillance.Wpf.Core]
        CamWall[Linksoft.CameraWall.Wpf]
        VsWpf[Linksoft.VideoSurveillance.Wpf]
    end

    subgraph Apps["🚀 Applications"]
        CamApp[Linksoft.CameraWall.Wpf.App]
        VsApp[Linksoft.VideoSurveillance.Wpf.App]
        Aspire[Linksoft.VideoSurveillance.Aspire]
    end

    %% Server stack
    Contracts --> Core
    Domain --> Contracts
    Domain --> Core
    Api --> Contracts
    Api --> Domain
    Api --> Core
    Api --> VE

    %% Windows / WPF stack
    VEDx --> VE
    VPWpf --> VEDx
    WpfCore --> Core
    WpfCore --> VPWpf
    CamWall --> WpfCore
    VsWpf --> Core
    VsWpf --> WpfCore

    %% App composition
    CamApp --> CamWall
    VsApp --> VsWpf
    Aspire --> Api
    Aspire --> Blazor
    Aspire --> CamApp
    Aspire --> VsApp
```

## 🔧 Services

Services are split between Core (shared) and WPF (UI-specific), all auto-registered via `[Registration]` attributes.

### 📚 Core Services (Linksoft.VideoSurveillance.Core)

| Service | Description |
|---------|-------------|
| `ICameraStorageService` | 💾 Persistence for cameras and layouts |
| `IApplicationSettingsService` | ⚙️ Application settings with per-camera override support |
| `IRecordingService` | 🔴 Manual and motion-triggered recording management |
| `IRecordingSegmentationService` | ✂️ Clock-aligned recording file segmentation |
| `IMotionDetectionService` | 🏃 Frame-based motion detection with multi-bounding box support |
| `ITimelapseService` | ⏱️ Interval-based timelapse snapshot capture |
| `IThumbnailGeneratorService` | 🖼️ Recording thumbnail generation |
| `IMediaCleanupService` | 🧹 Automatic cleanup of old recordings and snapshots |
| `IGitHubReleaseService` | 🔄 GitHub-based update checking |
| `IMediaPipeline` | 🎬 Abstraction for video stream operations (record, capture frame) |
| `IMediaPipelineFactory` | 🏭 Creates `IMediaPipeline` instances per camera |

### 🖥️ WPF Services (Linksoft.CameraWall.Wpf)

| Service | Description |
|---------|-------------|
| `ICameraWallManager` | 🎛️ Main facade for all camera wall operations |
| `IDialogService` | 💬 Dialog abstraction for camera config, settings, input, confirmations |

### 🔀 Per-Camera Override System

Every application-level setting can be overridden per camera. Override models use nullable properties -- `null` means "use application default":

```csharp
// Get effective value: per-camera override wins over app default
var timeout = settingsService.GetEffectiveValue(
    camera,
    settingsService.Connection.ConnectionTimeoutSeconds,
    o => o?.Connection.ConnectionTimeoutSeconds);
```

Override categories: Connection, CameraDisplay, Performance, Recording, MotionDetection (with nested BoundingBox).

## 🚀 Getting Started

### 📋 Prerequisites

- Windows 10/11
- .NET 10 SDK (for building from source)

### 📥 Install

Download the latest MSI installer from [GitHub Releases](https://github.com/davidkallesen/Linksoft.VideoSurveillance/releases).

### 🔨 Build from Source

```bash
dotnet build
dotnet run --project src/Linksoft.CameraWall.Wpf.App
```

### ☁️ Run Server Edition with Aspire

```bash
dotnet run --project src/Linksoft.VideoSurveillance.Aspire
```

This starts the Aspire dashboard and the REST API server. The dashboard provides monitoring, logs, and traces for all orchestrated services.

### 🌍 Run Server Edition Standalone

```bash
dotnet run --project src/Linksoft.VideoSurveillance.Api
```

### 📁 User Data

Application data is stored in:
```
%ProgramData%/Linksoft/CameraWall/
├── cameras.json        # 📷 Camera configurations and layouts
├── settings.json       # ⚙️ Application settings
├── logs/               # 📝 Debug log files (when enabled)
├── snapshots/          # 🖼️ Camera snapshots and timelapse frames
└── recordings/         # 🎥 Camera recordings with thumbnails
```

## 📖 Usage

1. **➕ Add Camera** - Click "Add Camera" in the Cameras tab to open the configuration dialog
2. **🔍 Network Scanner** - Use the built-in scanner in the Add Camera dialog to discover cameras
3. **📐 Layouts** - Use the Layouts tab to create, delete, or set startup layouts
4. **🖱️ Context Menu** - Right-click any camera tile for actions (Edit, Delete, Full Screen, Swap, Snapshot, Recording, Reconnect)
5. **✋ Drag-and-Drop** - Drag camera tiles to reorder them in the grid
6. **🔲 Full Screen** - Double-click a camera tile or use context menu for full screen view
7. **⚙️ Settings** - Click the Settings button in the View tab to configure all application settings
8. **🎥 Recordings** - Use the View tab to browse and playback recorded videos
9. **🔄 Check for Updates** - Use Help tab to check for new versions on GitHub

## 📄 License

MIT