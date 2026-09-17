# AtcSoft.VideoSurveillance - Claude Code Guidelines

## Project Overview
A multi-assembly video surveillance platform with a WPF desktop app and a headless REST API + SignalR server, sharing a common Core library. Uses RTSP/HTTP protocols with AtcSoft.VideoEngine (in-process FFmpeg) for video on both WPF and server.

## Solution Structure
- `AtcSoft.VideoSurveillance.Core` - Shared library: models, enums, events, service interfaces, helpers (net10.0, no WPF)
- `AtcSoft.VideoEngine` - Cross-platform video engine (net10.0): demux, decode, record, capture via in-process FFmpeg
- `AtcSoft.VideoEngine.DirectX` - D3D11VA GPU acceleration, Video Processor, swap chain (net10.0-windows)
- `AtcSoft.VideoEngine.Windows` - Windows-only USB camera enumeration (Media Foundation) + hot-plug watcher (WMI), `IUsbCameraEnumerator` / `IUsbCameraWatcher` implementations (net10.0-windows)
- `AtcSoft.VideoPlayer.Wpf` - WPF VideoHost control with DComp surface + XAML overlay (net10.0-windows)
- `AtcSoft.CameraWall.Wpf` - Reusable WPF library (NuGet package) with UI, dialogs, VideoEngine integration
- `AtcSoft.CameraWall.Wpf.App` - Thin shell WPF application using the library (standalone, connects directly to cameras)
- `AtcSoft.VideoSurveillance.Wpf` - WPF library for the VideoSurveillance API client (GatewayService, SurveillanceHubService, OpenAPI-generated client)
- `AtcSoft.VideoSurveillance.Wpf.App` - WPF management application for the API server (Fluent.Ribbon, Serilog, splash screen)
- `AtcSoft.VideoSurveillance.Api.Contracts` - Generated API models, endpoints, handler interfaces (OpenAPI-first via atc-rest-api-source-generator)
- `AtcSoft.VideoSurveillance.Api.Domain` - Handler implementations calling Core services
- `AtcSoft.VideoSurveillance.Api` - ASP.NET Core host with SignalR hub
- `AtcSoft.VideoSurveillance.Aspire` - Aspire AppHost for orchestrated startup and developer dashboard
- `AtcSoft.VideoSurveillance.Core.Tests` - xUnit v3 tests for Core library

## Architecture
The solution follows a layered architecture with Core at the base:
- **Core** contains all shared models, enums, events, service interfaces, and helpers (zero UI dependencies)
- **VideoEngine** provides cross-platform video: demuxing, decoding (CPU/D3D11VA), recording, snapshots. DirectX project adds GPU rendering for WPF.
- **VideoEngine.Windows** provides USB / DirectShow source support: Media Foundation device enumeration, WMI hot-plug events, and the dshow demuxer wiring needed by the engine to open `video=Friendly Name` sources.
- **CameraWall WPF library** references Core and VideoEngine, adds UI controls, dialogs, VideoHost-based media pipeline
- **CameraWall WPF App** is a standalone thin shell with Ribbon UI delegating to `ICameraWallManager` (connects directly to cameras)
- **VideoSurveillance WPF library** references Core and VideoPlayer, provides `GatewayService` (OpenAPI REST client) and `SurveillanceHubService` (SignalR client)
- **VideoSurveillance WPF App** is an API client thin shell with Ribbon UI, connects to the REST API + SignalR hub (not directly to cameras)
- **API** references Core and provides REST endpoints + SignalR real-time events
- **Aspire** orchestrates the API, Blazor.App, and Wpf.App with a developer dashboard

## Server Edition
The REST API + SignalR server enables headless video surveillance:
- **OpenAPI-first**: `src/VideoSurveillance.yaml` defines all endpoints; `Atc.Rest.Api.SourceGenerator` generates contracts
- **Endpoints**: Cameras CRUD, Layouts CRUD, Recordings list, Settings get/update, Snapshot capture, Recording start/stop
- **SignalR hub**: `/hubs/surveillance` broadcasts connection state, motion, and recording events
- **Run with Aspire**: `dotnet run --project src/AtcSoft.VideoSurveillance.Aspire` starts the API with the Aspire dashboard

## Hosted Background Services (Atc.Hosting)
Long-running periodic work uses `Atc.Hosting` base classes instead of hand-rolled `System.Threading.Timer` loops. The base class supplies the loop, sequential (no-overlap) ticking, exception logging via `OnExceptionAsync`, and optional health tracking.

- **`BackgroundServiceBase<T>`** — fixed-interval work. Override `DoWorkAsync(CancellationToken)`. Cadence comes from `IBackgroundServiceOptions` (`StartupDelaySeconds`, then `RepeatIntervalSeconds` *after* each tick completes — it is delay-after-completion, not fixed-rate).
- **`BackgroundScheduleServiceBase<T>`** — cron-scheduled work via `IBackgroundScheduleServiceOptions.CronExpression` (5-field, evaluated against **UTC**). Use for wall-clock schedules ("daily at 03:00"); use `BackgroundServiceBase` for plain intervals.
- **Do not use a plain `IHostedService`.** Every hosted worker derives from an `Atc.Hosting` `*ServiceBase`, even pure event-wiring services with no recurring work. Subscribe in an overridden `StartAsync` (then call `base.StartAsync`), unsubscribe in an overridden `StopAsync` (then call `base.StopAsync`), and make `DoWorkAsync` a no-op with `RepeatIntervalSeconds = ushort.MaxValue` so the idle loop parks on a long delay rather than spinning (see `SurveillanceEventBroadcaster`).

### Per-service typed options (avoid the shared-singleton footgun)
`BackgroundServiceBase` takes the *interface* `IBackgroundServiceOptions`, so registering one `DefaultBackgroundServiceOptions` as that interface makes **every** worker share one interval. Give each worker its **own** typed options class and register it by that concrete type:
```csharp
public sealed class CameraConnectionServiceOptions : IBackgroundServiceOptions
{
    public ushort StartupDelaySeconds { get; set; } = 3;
    public ushort RepeatIntervalSeconds { get; set; } = 30;
    public string? ServiceName { get; set; } = nameof(CameraConnectionService);
}

// Program.cs
builder.Services.AddSingleton(new CameraConnectionServiceOptions());
builder.Services.AddHostedService<CameraConnectionService>();
```

### Health tracking
Register the tracker once and let each worker report staleness; the API's `/health` endpoint returns 503 when an always-on worker goes stale:
```csharp
builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton<IBackgroundServiceHealthService, BackgroundServiceHealthService>();
```
Each worker passes `healthService` to the 3-arg base ctor and sets its own window in its constructor:
```csharp
: base(logger, options, healthService)
{
    healthService.SetMaxStalenessInSeconds(ServiceName, BackgroundServiceHealthHelper.StalenessFor(options));
}
```

### WPF hosting (shared singleton + interface)
WPF services that are both UI-facing (events, `IsRunning`, manual triggers) and periodic derive from `BackgroundServiceBase<T>` **and** implement their interface. They are NOT `[Registration]`-auto-registered; the App registers one singleton exposed both ways so the same instance serves the UI and the host:
```csharp
services.AddSingleton<MediaCleanupService>();
services.AddSingleton<IMediaCleanupService>(sp => sp.GetRequiredService<MediaCleanupService>());
services.AddHostedService(sp => sp.GetRequiredService<MediaCleanupService>());
```
These start on `host.StartAsync()` and stop on `host.StopAsync()` — no explicit `Initialize()`/`StopService()`.

## Key Frameworks & Packages
- **Atc.XamlToolkit** - MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`, `[DependencyProperty]`)
- **Atc.SourceGenerators** - Source generators for DI registration (`[Registration]`), options binding (`[OptionsBinding]`)
- **Atc.Wpf.Controls** - UI controls (`LabelTextBox`, `LabelComboBox`, `LabelPasswordBox`, `GridEx`)
- **Atc.Wpf.NetworkControls** - `NetworkScannerView` for discovering cameras
- **AtcSoft.VideoEngine** - In-process FFmpeg video engine for RTSP/HTTP streams (replaces FlyleafLib)
- **Flyleaf.FFmpeg.Bindings** - FFmpeg P/Invoke bindings NuGet dependency
- **Fluent.Ribbon** - Ribbon UI (App only)
- **Serilog** - Structured logging with file sink (App only)

## Library Services

### ICameraWallManager
Main facade for all camera wall operations. Apps inject this and delegate business logic to it.
```csharp
public interface ICameraWallManager : INotifyPropertyChanged
{
    // Properties
    ObservableCollection<CameraLayout> Layouts { get; }
    CameraLayout? CurrentLayout { get; set; }
    CameraLayout? SelectedStartupLayout { get; }
    int CameraCount { get; }
    int ConnectedCount { get; }
    string StatusText { get; }
    AtcSoft.CameraWall.Wpf.UserControls.CameraGrid? CameraGrid { get; }

    // Initialization
    void Initialize(AtcSoft.CameraWall.Wpf.UserControls.CameraGrid cameraGridControl);

    // Camera operations
    void AddCamera();
    void EditCamera(CameraConfiguration camera);
    void DeleteCamera(CameraConfiguration camera);
    void ShowFullScreen(CameraConfiguration camera);
    void RefreshAll();

    // Layout operations
    void CreateNewLayout();
    void DeleteCurrentLayout();
    void SetCurrentAsStartup();
    void SaveCurrentLayout();

    // Event handlers
    void OnConnectionStateChanged(CameraConnectionChangedEventArgs e);
    void OnPositionChanged(CameraPositionChangedEventArgs e);

    // Dialog operations
    void ShowAboutDialog();
    void ShowCheckForUpdatesDialog();
    void ShowSettingsDialog();

    // CanExecute properties
    bool CanCreateNewLayout { get; }
    bool CanDeleteCurrentLayout { get; }
    bool CanSetCurrentAsStartup { get; }
    bool CanRefreshAll { get; }
}
```

### IDialogService
Abstraction for showing dialogs:
```csharp
public interface IDialogService
{
    CameraConfiguration? ShowCameraConfigurationDialog(CameraConfiguration? camera, bool isNew);
    string? ShowInputBox(string title, string prompt, string defaultText = "");
    bool ShowConfirmation(string message, string title);
    void ShowError(string message, string title = "Error");
    void ShowInfo(string message, string title = "Information");
    void ShowAboutDialog();
    void ShowCheckForUpdatesDialog();
    void ShowFullScreenCamera(CameraConfiguration camera);
    void ShowSettingsDialog();
}
```

### ICameraStorageService
Abstraction for camera/layout persistence (JSON file implementation provided):
```csharp
public interface ICameraStorageService
{
    List<CameraConfiguration> GetAllCameras();
    CameraConfiguration? GetCameraById(Guid id);
    List<CameraLayout> GetAllLayouts();
    CameraLayout? GetLayoutById(Guid id);
    void AddOrUpdateCamera(CameraConfiguration camera);
    void DeleteCamera(Guid id);
    void AddOrUpdateLayout(CameraLayout layout);
    void DeleteLayout(Guid id);
    Guid? StartupLayoutId { get; set; }
    void Load();
    void Save();
}
```

### IApplicationSettingsService
Abstraction for application settings (theme, language, display, recording options):
```csharp
public interface IApplicationSettingsService
{
    GeneralSettings General { get; }
    CameraDisplayAppSettings CameraDisplay { get; }
    ConnectionAppSettings Connection { get; }
    PerformanceSettings Performance { get; }
    RecordingSettings Recording { get; }
    AdvancedSettings Advanced { get; }
    void Load();
    void Save();
}
```

### IRecordingService
Service for managing camera recording sessions:
```csharp
public interface IRecordingService
{
    event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
    RecordingState GetRecordingState(Guid cameraId);
    bool StartRecording(CameraConfiguration camera, Player player);
    void StopRecording(Guid cameraId);
    void StopAllRecordings();
    bool IsRecording(Guid cameraId);
    bool TriggerMotionRecording(CameraConfiguration camera, Player player);
}
```

### IGitHubReleaseService
Service for checking GitHub releases for updates.

## MVVM Patterns

### Observable Properties
Use `[ObservableProperty]` attribute on private fields:
```csharp
[ObservableProperty]
private string displayName = string.Empty;
```

With callbacks:
```csharp
[ObservableProperty(AfterChangedCallback = nameof(OnSelectedCameraChanged))]
private CameraConfiguration? selectedCamera;

private static void OnSelectedCameraChanged()
    => CommandManager.InvalidateRequerySuggested();
```

With dependent properties:
```csharp
[ObservableProperty(DependentPropertyNames = [nameof(ScanButtonText)])]
private bool isScanning;
```

### Dependency Properties
Use `[DependencyProperty]` attribute on private fields for UserControls:
```csharp
[DependencyProperty(PropertyChangedCallback = nameof(OnCameraChanged))]
private CameraConfiguration? camera;

[DependencyProperty(DefaultValue = true)]
private bool autoSave = true;
```

### Relay Commands
Use `[RelayCommand]` attribute on methods:
```csharp
[RelayCommand(CanExecute = nameof(CanSave))]
private void Save() { ... }

private bool CanSave() => !string.IsNullOrWhiteSpace(Camera.DisplayName);
```

For async commands without "Async" suffix in command name:
```csharp
[RelayCommand("TestConnection", CanExecute = nameof(CanTestConnection))]
private async Task TestConnectionAsync() { ... }
```

### Command CanExecute Invalidation
When a property affects command CanExecute, use `AfterChangedCallback`:
```csharp
[ObservableProperty(AfterChangedCallback = nameof(OnIsTestingChanged))]
private bool isTesting;

private static void OnIsTestingChanged()
    => CommandManager.InvalidateRequerySuggested();
```

## UI Controls (Atc.Wpf)
Use Atc controls instead of standard WPF controls:
- `atc:GridEx` instead of `Grid`
- `atc:LabelTextBox` for labeled text inputs
- `atc:LabelComboBox` for labeled dropdowns (uses `IDictionary<string, string>` Items and `SelectedKey`)
- `atc:LabelPasswordBox` for password inputs
- `atc:LabelIntegerBox` for numeric inputs

## Models
Models that need UI binding should inherit from `ObservableObject`:
```csharp
public partial class CameraConfiguration : ObservableObject
{
    [ObservableProperty]
    private string ipAddress = string.Empty;
}
```

## Enums
Use extension methods for enum conversions:
```csharp
public static string ToScheme(this CameraProtocol protocol)
    => protocol switch
    {
        CameraProtocol.Rtsp => CameraProtocol.Rtsp.ToStringLowerCase(),
        // ...
    };
```

### Camera Source vs. Protocol
`CameraProtocol` is the wire protocol for **network** cameras (RTSP / HTTP / HTTPS). `CameraSource` is the higher-level discriminator and selects between configuration shapes:

```csharp
public enum CameraSource
{
    Network = 0,  // uses Connection.IpAddress / Port / Protocol / Path
    Usb = 1,      // uses Connection.Usb (UsbConnectionSettings)
}
```

`ConnectionSettings.Source` defaults to `Network` so existing JSON-stored cameras keep working without migration. USB cameras populate `Connection.Usb` with `DeviceId` (the stable Media Foundation symbolic link), `FriendlyName`, optional `Format` (`UsbStreamFormat` — width × height × FPS × pixel-format), and `PreferAudio`. Use `CameraUriHelper.BuildSourceLocator(camera)` instead of `camera.BuildUri()` whenever the code path needs to handle both source kinds — `BuildUri()` throws on USB cameras to fail fast.

## ViewModels
- Inherit from `ViewModelDialogBase` for dialog ViewModels
- Use `CloseRequested` event with `DialogClosedEventArgs` for dialog closing
- Subscribe to model `PropertyChanged` to react to changes

## Dialogs (in Library)
- `CameraConfigurationDialog` - Add/Edit camera with integrated network scanner
- `InputBox` - Simple text input dialog (for layout names)
- `SettingsDialog` - Configure general and display settings (uses extracted UserControls from `Dialogs/Parts/Settings/`)
- `CheckForUpdatesDialog` - Check for and display available updates from GitHub
- `RecordingsBrowserDialog` - Browse and play back recorded videos

## Windows (in Library)
- `FullScreenCameraWindow` - Display single camera in full screen mode
- `FullScreenRecordingWindow` - Play back recorded video in full screen mode

## Naming Conventions
- ViewModels: `{Name}ViewModel` (e.g., `CameraConfigurationDialogViewModel`)
- Dialogs: `{Name}Dialog` (e.g., `CameraConfigurationDialog`)
- Extensions: `{Type}Extensions` (e.g., `CameraProtocolExtensions`)
- UserControls: PascalCase (e.g., `CameraTile`, `CameraGrid`, `CameraOverlay`)
- Services: `I{Name}Service` / `{Name}Service` (e.g., `ICameraWallManager`, `CameraWallManager`)

## Dependency Injection
Library services are auto-registered using the source-generated extension method:
```csharp
// Auto-registers all services marked with [Registration] attribute
services.AddDependencyRegistrationsFromWpf();
```

For transitive registration of referenced assemblies:
```csharp
services.AddDependencyRegistrationsFromWpf(includeReferencedAssemblies: true);
```

## Atc.Source.Generators

### Service Registration Attribute
Services annotated with `[Registration]` are auto-registered via source-generated extension methods:
```csharp
[Registration(Lifetime.Singleton)]
public class CameraStorageService : ICameraStorageService { }

[Registration(Lifetime.Scoped)]
public class UserService : IUserService { }

[Registration(Lifetime.Transient)]
public class LoggerService { }
```

The generator creates `AddDependencyRegistrationsFrom{AssemblySuffix}()` extension methods. For `AtcSoft.CameraWall.Wpf`, this generates `AddDependencyRegistrationsFromWpf()`.

Advanced options:
```csharp
// Register as specific interface
[Registration(As = typeof(INotificationService))]
public class EmailService : INotificationService, IEmailService { }

// Register as both interface and self
[Registration(AsSelf = true)]
public class CacheService : ICacheService { }

// TryAdd pattern (only register if not already present)
[Registration(TryAdd = true)]
public class DefaultLogger : ILogger { }
```

### Options Binding
Use `[OptionsBinding]` for configuration classes with validation:
```csharp
[OptionsBinding("CameraWall", ValidateDataAnnotations = true, ValidateOnStart = true)]
public partial class CameraWallOptions
{
    [Required]
    public string ThemeBase { get; set; } = "Dark";
}
```

### DataAnnotations on Models
Use DataAnnotations for validation metadata:
```csharp
public partial class CameraConfiguration : ObservableObject
{
    [ObservableProperty]
    [Required(ErrorMessage = "IP Address is required")]
    private string ipAddress = string.Empty;

    [ObservableProperty]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
    private int port = 554;

    [ObservableProperty]
    [Required(ErrorMessage = "Display Name is required")]
    [StringLength(256, ErrorMessage = "Display Name cannot exceed 256 characters")]
    private string displayName = string.Empty;
}
```

## Settings Models

### GeneralSettings
```csharp
public class GeneralSettings
{
    public string ThemeBase { get; set; } = "Dark";           // "Dark" or "Light"
    public string ThemeAccent { get; set; } = "Blue";         // Accent color name
    public string Language { get; set; } = "1033";            // LCID as string (1033 = en-US)
    public bool ConnectCamerasOnStartup { get; set; } = true;
    public bool StartMaximized { get; set; } = false;
    public bool StartRibbonCollapsed { get; set; } = false;
}
```

### CameraDisplayAppSettings
```csharp
public class CameraDisplayAppSettings
{
    public bool ShowOverlayTitle { get; set; } = true;
    public bool ShowOverlayDescription { get; set; } = true;
    public bool ShowOverlayTime { get; set; } = false;
    public bool ShowOverlayConnectionStatus { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.7;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.TopLeft;
    public bool AllowDragAndDropReorder { get; set; } = true;
    public bool AutoSaveLayoutChanges { get; set; } = true;
    public string SnapshotPath { get; set; } = ApplicationPaths.DefaultSnapshotsPath;
}
```

### RecordingSettings
```csharp
public class RecordingSettings
{
    public string RecordingPath { get; set; } = ApplicationPaths.DefaultRecordingsPath;
    public string RecordingFormat { get; set; } = "mp4";
    public bool EnableRecordingOnMotion { get; set; }
    public bool EnableRecordingOnConnect { get; set; }
    public MotionDetectionSettings MotionDetection { get; set; } = new();
}
```

### AdvancedSettings
```csharp
public class AdvancedSettings
{
    public bool EnableDebugLogging { get; set; }
    public string LogPath { get; set; } = ApplicationPaths.DefaultLogsPath;
}
```

## Threading
Use `SemaphoreSlim` for thread-safe operations with proper acquisition tracking:
```csharp
var acquired = false;
try
{
    semaphore.Wait();
    acquired = true;
    // work...
}
finally
{
    if (acquired)
    {
        semaphore.Release();
    }
}
```

## Converters
- `BoolToOpacityConverter` - Converts bool to opacity value (0.0-1.0)
- `ConnectionStateToColorConverter` - Converts ConnectionState enum to brush color
- `ConnectionStateToTextConverter` - Converts ConnectionState enum to localized text
- `OverrideOrDefaultMultiValueConverter` - Returns override value if non-null, otherwise default value

## Factories
Use `DropDownItemsFactory` for centralized dropdown items and defaults:
```csharp
// Dropdown items for comboboxes
public static IDictionary<string, string> VideoQualityItems { get; }
public static IDictionary<string, string> RtspTransportItems { get; }
public static IDictionary<string, string> RecordingFormatItems { get; }
public static IDictionary<string, string> OverlayPositionItems { get; }
public static IDictionary<string, string> OverlayOpacityItems { get; }  // 0%-100% in 10% increments
public static IDictionary<string, string> ProtocolItems { get; }

// Default values
public const string DefaultVideoQuality = "Auto";
public const string DefaultRtspTransport = "tcp";
public const string DefaultRecordingFormat = "mp4";
public const string DefaultOverlayPosition = "TopLeft";
public const string DefaultOverlayOpacity = "0.7";
public const string DefaultProtocol = "Rtsp";

// Utility methods
public static int GetMaxResolutionFromQuality(string videoQuality);
```

## Helpers

### ApplicationPaths
Provides default application paths using `Environment.SpecialFolder.CommonApplicationData`:
```csharp
public static class ApplicationPaths
{
    public static string DefaultLogsPath { get; }       // {ProgramData}\Linksoft\CameraWall\logs
    public static string DefaultSnapshotsPath { get; }  // {ProgramData}\Linksoft\CameraWall\snapshots
    public static string DefaultRecordingsPath { get; } // {ProgramData}\Linksoft\CameraWall\recordings
    public static string DefaultSettingsPath { get; }   // {ProgramData}\Linksoft\CameraWall\settings.json
    public static string DefaultCameraDataPath { get; } // {ProgramData}\Linksoft\CameraWall\cameras.json
}
```

## Debug Logging
Debug logging is configured via Serilog in `App.xaml.cs`:
- Controlled by `AdvancedSettings.EnableDebugLogging` and `AdvancedSettings.LogPath`
- When enabled, logs to `{LogPath}\camera-wall-{date}.log`
- Daily rolling with 7-day retention
- Settings loaded early (before Host builder) to configure logging at startup
- Logs include: layout loading, camera connection state changes, recording state changes

## Build
```bash
dotnet build
dotnet run --project src/AtcSoft.CameraWall.Wpf.App
```
