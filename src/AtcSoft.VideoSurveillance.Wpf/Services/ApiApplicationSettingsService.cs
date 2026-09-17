// ReSharper disable RedundantArgumentDefaultValue
using CameraOverrides = Linksoft.VideoSurveillance.Models.CameraOverrides;
using CameraProtocol = Linksoft.VideoSurveillance.Enums.CameraProtocol;
using MediaCleanupSchedule = Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule;
using OverlayPosition = Linksoft.VideoSurveillance.Enums.OverlayPosition;

namespace Linksoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// API-backed implementation of <see cref="IApplicationSettingsService"/>
/// for the WPF surveillance client. The bulk of settings — camera-display
/// defaults, connection / performance / recording / motion-detection /
/// advanced — live on the API server and are fetched / mutated via
/// <see cref="GatewayService"/>. A small set of per-user-machine
/// preferences (theme, language, window state) is the exception: those
/// genuinely belong to *this* Windows account on *this* PC and would be
/// wrong to share across every client connected to the same server, so
/// they're persisted to a tiny local file under
/// <c>%LocalAppData%\Linksoft\VideoSurveillance.Client\client-prefs.json</c>.
/// <para>
/// The service holds an in-memory <see cref="ApplicationSettings"/> cache
/// so the synchronous <c>General</c> / <c>CameraDisplay</c> / etc. property
/// readers work without an HTTP round-trip on every access. <see cref="LoadAsync"/>
/// must be called once at app startup before any consumer reads the cache.
/// Save calls update the cache, persist client prefs locally, and push the
/// full settings tree to the API in the background; an API failure logs a
/// warning but doesn't lose the in-session change.
/// </para>
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1031:Do not catch general exception types",
    Justification = "Settings round-tripping must never tear down the app — log and continue.")]
public sealed partial class ApiApplicationSettingsService : IApplicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly GatewayService gatewayService;
    private readonly ILogger<ApiApplicationSettingsService> logger;
    private readonly string clientPrefsPath;

    private ApplicationSettings cache = new();

    public ApiApplicationSettingsService(
        GatewayService gatewayService,
        ILogger<ApiApplicationSettingsService> logger)
    {
        this.gatewayService = gatewayService ?? throw new ArgumentNullException(nameof(gatewayService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        clientPrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Linksoft",
            "VideoSurveillance.Client",
            "client-prefs.json");
    }

    /// <inheritdoc/>
    public GeneralSettings General => cache.General;

    /// <inheritdoc/>
    public CameraDisplayAppSettings CameraDisplay => cache.CameraDisplay;

    /// <inheritdoc/>
    public ConnectionAppSettings Connection => cache.Connection;

    /// <inheritdoc/>
    public PerformanceSettings Performance => cache.Performance;

    /// <inheritdoc/>
    public MotionDetectionSettings MotionDetection => cache.MotionDetection;

    /// <inheritdoc/>
    public RecordingSettings Recording => cache.Recording;

    /// <inheritdoc/>
    public AdvancedSettings Advanced => cache.Advanced;

    /// <inheritdoc/>
    public void Load() => LoadAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task LoadAsync()
    {
        var clientPrefs = LoadClientPrefs();
        AppSettings? api = null;

        try
        {
            api = await gatewayService.GetSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLoadFromApiFailed(ex);
        }

        cache = BuildCacheFrom(clientPrefs, api);
    }

    /// <inheritdoc/>
    public void SaveGeneral(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.General = settings;

        SaveClientPrefs(new ClientPreferences
        {
            ThemeBase = settings.ThemeBase,
            ThemeAccent = settings.ThemeAccent,
            Language = settings.Language,
            StartMaximized = settings.StartMaximized,
            StartRibbonCollapsed = settings.StartRibbonCollapsed,
        });

        PushToApi();
    }

    /// <inheritdoc/>
    public void SaveCameraDisplay(CameraDisplayAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.CameraDisplay = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void SaveConnection(ConnectionAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.Connection = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void SavePerformance(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.Performance = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void SaveMotionDetection(MotionDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.MotionDetection = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void SaveRecording(RecordingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.Recording = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void SaveAdvanced(AdvancedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cache.Advanced = settings;
        PushToApi();
    }

    /// <inheritdoc/>
    public void Save()
    {
        SaveClientPrefs(new ClientPreferences
        {
            ThemeBase = cache.General.ThemeBase,
            ThemeAccent = cache.General.ThemeAccent,
            Language = cache.General.Language,
            StartMaximized = cache.General.StartMaximized,
            StartRibbonCollapsed = cache.General.StartRibbonCollapsed,
        });

        PushToApi();
    }

    /// <inheritdoc/>
    public void ApplyDefaultsToCamera(CameraConfiguration camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        camera.Connection.Protocol = Connection.DefaultProtocol;
        camera.Connection.Port = Connection.DefaultPort;

        camera.Display.OverlayPosition = CameraDisplay.OverlayPosition;

        camera.Stream.UseLowLatencyMode = Performance.LowLatencyMode;
        camera.Stream.MaxLatencyMs = Performance.MaxLatencyMs;
        camera.Stream.RtspTransport = Performance.RtspTransport;
        camera.Stream.BufferDurationMs = Performance.BufferDurationMs;
    }

    /// <inheritdoc/>
    public T GetEffectiveValue<T>(
        CameraConfiguration camera,
        T appDefault,
        Func<CameraOverrides?, T?> overrideSelector)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(overrideSelector);

        var overrideValue = overrideSelector(camera.Overrides);
        return overrideValue ?? appDefault;
    }

    /// <inheritdoc/>
    public string? GetEffectiveStringValue(
        CameraConfiguration camera,
        string? appDefault,
        Func<CameraOverrides?, string?> overrideSelector)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(overrideSelector);

        var overrideValue = overrideSelector(camera.Overrides);
        return overrideValue ?? appDefault;
    }

    private ClientPreferences LoadClientPrefs()
    {
        if (!File.Exists(clientPrefsPath))
        {
            return new ClientPreferences();
        }

        try
        {
            var json = File.ReadAllText(clientPrefsPath);
            return JsonSerializer.Deserialize<ClientPreferences>(json, JsonOptions)
                   ?? new ClientPreferences();
        }
        catch (Exception ex)
        {
            LogClientPrefsReadFailed(ex, clientPrefsPath);
            return new ClientPreferences();
        }
    }

    private void SaveClientPrefs(ClientPreferences prefs)
    {
        try
        {
            var dir = Path.GetDirectoryName(clientPrefsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(prefs, JsonOptions);
            File.WriteAllText(clientPrefsPath, json);
        }
        catch (Exception ex)
        {
            LogClientPrefsWriteFailed(ex, clientPrefsPath);
        }
    }

    private void PushToApi()
    {
        // Fire-and-forget: cache and local prefs are already updated, so
        // the in-session UX is consistent. A failed push leaves the API
        // out of sync until the next successful save — surface it via the
        // logger rather than blocking the UI thread on HTTP. Snapshot the
        // cache so the captured value is the one that was just saved.
        var snapshot = CloneCache();
        _ = Task.Run(async () =>
        {
            try
            {
                var apiSettings = BuildApiSettings(snapshot);
                await gatewayService.UpdateSettingsAsync(apiSettings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPushToApiFailed(ex);
            }
        });
    }

    private ApplicationSettings CloneCache()
        => new()
        {
            General = cache.General,
            CameraDisplay = cache.CameraDisplay,
            Connection = cache.Connection,
            Performance = cache.Performance,
            MotionDetection = cache.MotionDetection,
            Recording = cache.Recording,
            Advanced = cache.Advanced,
        };

    /// <summary>
    /// Builds the in-memory <see cref="ApplicationSettings"/> cache by
    /// merging the local client preferences (theme / language / window
    /// state) with the server-side <see cref="AppSettings"/>. When the API
    /// is unreachable, falls back to constructed defaults so the app can
    /// still start.
    /// </summary>
    private static ApplicationSettings BuildCacheFrom(
        ClientPreferences clientPrefs,
        AppSettings? api)
    {
        var result = new ApplicationSettings
        {
            General =
            {
                ThemeBase = clientPrefs.ThemeBase,
                ThemeAccent = clientPrefs.ThemeAccent,
                Language = clientPrefs.Language,
                StartMaximized = clientPrefs.StartMaximized,
                StartRibbonCollapsed = clientPrefs.StartRibbonCollapsed,
                ConnectCamerasOnStartup = api?.ConnectOnStartup ?? true,
            },
        };

        if (api is null)
        {
            return result;
        }

        result.CameraDisplay = new CameraDisplayAppSettings
        {
            ShowOverlayTitle = api.ShowOverlayTitle,
            ShowOverlayDescription = api.ShowOverlayDescription,
            ShowOverlayTime = api.ShowOverlayTime,
            ShowOverlayConnectionStatus = api.ShowOverlayConnectionStatus,
            OverlayOpacity = api.OverlayOpacity,
            OverlayPosition = ParseEnumOrDefault(api.OverlayPosition?.ToString(), OverlayPosition.TopLeft),
            AllowDragAndDropReorder = api.AllowDragAndDropReorder,
            AutoSaveLayoutChanges = api.AutoSaveLayoutChanges,
            SnapshotPath = api.SnapshotPath,
        };

        result.Connection = new ConnectionAppSettings
        {
            DefaultProtocol = ParseEnumOrDefault(api.DefaultProtocol?.ToString(), CameraProtocol.Rtsp),
            DefaultPort = api.DefaultPort,
            ConnectionTimeoutSeconds = api.ConnectionTimeoutSeconds,
            ReconnectDelaySeconds = api.ReconnectDelaySeconds,
            AutoReconnectOnFailure = api.AutoReconnectOnFailure,
            ShowNotificationOnDisconnect = api.ShowNotificationOnDisconnect,
            ShowNotificationOnReconnect = api.ShowNotificationOnReconnect,
            PlayNotificationSound = api.PlayNotificationSound,
        };

        result.Performance = new PerformanceSettings
        {
            VideoQuality = api.VideoQuality?.ToString() ?? "Auto",
            HardwareAcceleration = api.HardwareAcceleration,
            LowLatencyMode = api.LowLatencyMode,
            BufferDurationMs = api.BufferDurationMs,
            RtspTransport = api.RtspTransport?.ToString() ?? "tcp",
            MaxLatencyMs = api.MaxLatencyMs,
        };

        result.MotionDetection = new MotionDetectionSettings
        {
            Sensitivity = api.MotionSensitivity,
            MinimumChangePercent = api.MinimumChangePercent,
            AnalysisFrameRate = api.AnalysisFrameRate,
            AnalysisWidth = api.AnalysisWidth,
            AnalysisHeight = api.AnalysisHeight,
            PostMotionDurationSeconds = api.PostMotionDurationSeconds,
            CooldownSeconds = api.CooldownSeconds,
            BoundingBox = new BoundingBoxSettings
            {
                ShowInGrid = api.BoundingBoxShowInGrid,
                ShowInFullScreen = api.BoundingBoxShowInFullScreen,
                Color = api.BoundingBoxColor,
                Thickness = api.BoundingBoxThickness,
                MinArea = api.BoundingBoxMinArea,
                Padding = api.BoundingBoxPadding,
                Smoothing = api.BoundingBoxSmoothing,
            },
        };

        result.Recording = new RecordingSettings
        {
            RecordingPath = api.RecordingPath,
            RecordingFormat = api.RecordingFormat?.ToString() ?? "mp4",
            EnableRecordingOnMotion = api.EnableRecordingOnMotion,
            EnableRecordingOnConnect = api.EnableRecordingOnConnect,
            EnableHourlySegmentation = api.EnableHourlySegmentation,
            MaxRecordingDurationMinutes = api.MaxRecordingDurationMinutes,
            ThumbnailTileCount = api.ThumbnailTileCount,
            EnableTimelapse = api.EnableTimelapse,
            TimelapseInterval = api.TimelapseInterval,
            Cleanup = new MediaCleanupSettings
            {
                Schedule = ParseEnumOrDefault(api.CleanupSchedule?.ToString(), MediaCleanupSchedule.Disabled),
                RecordingRetentionDays = api.RecordingRetentionDays,
                IncludeSnapshots = api.CleanupIncludeSnapshots,
                SnapshotRetentionDays = api.SnapshotRetentionDays,
            },
            PlaybackOverlay = new PlaybackOverlaySettings
            {
                ShowFilename = api.PlaybackShowFilename,
                FilenameColor = api.PlaybackFilenameColor,
                ShowTimestamp = api.PlaybackShowTimestamp,
                TimestampColor = api.PlaybackTimestampColor,
            },
        };

        result.Advanced = new AdvancedSettings
        {
            EnableDebugLogging = api.EnableDebugLogging,
            LogPath = api.LogPath,
        };

        return result;
    }

    /// <summary>
    /// Builds the flat API DTO from the in-memory cache. Theme / language /
    /// window-state fields *are* sent so the API server has a record of the
    /// last values written, but the WPF client never reads them back —
    /// the local <see cref="ClientPreferences"/> file is authoritative for
    /// those.
    /// </summary>
    private static AppSettings BuildApiSettings(ApplicationSettings settings)
    {
        _ = Enum.TryParse<AppSettingsThemeBase>(settings.General.ThemeBase, out var themeBase);
        _ = Enum.TryParse<AppSettingsOverlayPosition>(settings.CameraDisplay.OverlayPosition.ToString(), out var overlayPosition);
        _ = Enum.TryParse<AppSettingsDefaultProtocol>(settings.Connection.DefaultProtocol.ToString(), out var protocol);
        _ = Enum.TryParse<AppSettingsVideoQuality>(settings.Performance.VideoQuality, out var videoQuality);
        _ = Enum.TryParse<AppSettingsRtspTransport>(settings.Performance.RtspTransport, true, out var rtspTransport);
        _ = Enum.TryParse<AppSettingsRecordingFormat>(settings.Recording.RecordingFormat, true, out var recordingFormat);
        _ = Enum.TryParse<AppSettingsCleanupSchedule>(settings.Recording.Cleanup.Schedule.ToString(), out var cleanupSchedule);

        return new AppSettings(
            ThemeBase: themeBase,
            ThemeAccent: settings.General.ThemeAccent,
            Language: settings.General.Language,
            ConnectOnStartup: settings.General.ConnectCamerasOnStartup,
            StartMaximized: settings.General.StartMaximized,
            ShowOverlayTitle: settings.CameraDisplay.ShowOverlayTitle,
            ShowOverlayDescription: settings.CameraDisplay.ShowOverlayDescription,
            ShowOverlayTime: settings.CameraDisplay.ShowOverlayTime,
            ShowOverlayConnectionStatus: settings.CameraDisplay.ShowOverlayConnectionStatus,
            OverlayOpacity: settings.CameraDisplay.OverlayOpacity,
            OverlayPosition: overlayPosition,
            AllowDragAndDropReorder: settings.CameraDisplay.AllowDragAndDropReorder,
            AutoSaveLayoutChanges: settings.CameraDisplay.AutoSaveLayoutChanges,
            SnapshotPath: settings.CameraDisplay.SnapshotPath,
            DefaultProtocol: protocol,
            DefaultPort: settings.Connection.DefaultPort,
            ConnectionTimeoutSeconds: settings.Connection.ConnectionTimeoutSeconds,
            ReconnectDelaySeconds: settings.Connection.ReconnectDelaySeconds,
            AutoReconnectOnFailure: settings.Connection.AutoReconnectOnFailure,
            ShowNotificationOnDisconnect: settings.Connection.ShowNotificationOnDisconnect,
            ShowNotificationOnReconnect: settings.Connection.ShowNotificationOnReconnect,
            PlayNotificationSound: settings.Connection.PlayNotificationSound,
            VideoQuality: videoQuality,
            HardwareAcceleration: settings.Performance.HardwareAcceleration,
            LowLatencyMode: settings.Performance.LowLatencyMode,
            BufferDurationMs: settings.Performance.BufferDurationMs,
            RtspTransport: rtspTransport,
            MaxLatencyMs: settings.Performance.MaxLatencyMs,
            MotionSensitivity: settings.MotionDetection.Sensitivity,
            MinimumChangePercent: settings.MotionDetection.MinimumChangePercent,
            AnalysisFrameRate: settings.MotionDetection.AnalysisFrameRate,
            AnalysisWidth: settings.MotionDetection.AnalysisWidth,
            AnalysisHeight: settings.MotionDetection.AnalysisHeight,
            PostMotionDurationSeconds: settings.MotionDetection.PostMotionDurationSeconds,
            CooldownSeconds: settings.MotionDetection.CooldownSeconds,
            BoundingBoxShowInGrid: settings.MotionDetection.BoundingBox.ShowInGrid,
            BoundingBoxShowInFullScreen: settings.MotionDetection.BoundingBox.ShowInFullScreen,
            BoundingBoxColor: settings.MotionDetection.BoundingBox.Color,
            BoundingBoxThickness: settings.MotionDetection.BoundingBox.Thickness,
            BoundingBoxMinArea: settings.MotionDetection.BoundingBox.MinArea,
            BoundingBoxPadding: settings.MotionDetection.BoundingBox.Padding,
            BoundingBoxSmoothing: settings.MotionDetection.BoundingBox.Smoothing,
            RecordingPath: settings.Recording.RecordingPath,
            RecordingFormat: recordingFormat,
            EnableRecordingOnMotion: settings.Recording.EnableRecordingOnMotion,
            EnableRecordingOnConnect: settings.Recording.EnableRecordingOnConnect,
            EnableHourlySegmentation: settings.Recording.EnableHourlySegmentation,
            MaxRecordingDurationMinutes: settings.Recording.MaxRecordingDurationMinutes,
            ThumbnailTileCount: settings.Recording.ThumbnailTileCount,
            EnableTimelapse: settings.Recording.EnableTimelapse,
            TimelapseInterval: settings.Recording.TimelapseInterval,
            CleanupSchedule: cleanupSchedule,
            RecordingRetentionDays: settings.Recording.Cleanup.RecordingRetentionDays,
            CleanupIncludeSnapshots: settings.Recording.Cleanup.IncludeSnapshots,
            SnapshotRetentionDays: settings.Recording.Cleanup.SnapshotRetentionDays,
            PlaybackShowFilename: settings.Recording.PlaybackOverlay.ShowFilename,
            PlaybackFilenameColor: settings.Recording.PlaybackOverlay.FilenameColor,
            PlaybackShowTimestamp: settings.Recording.PlaybackOverlay.ShowTimestamp,
            PlaybackTimestampColor: settings.Recording.PlaybackOverlay.TimestampColor,
            EnableDebugLogging: settings.Advanced.EnableDebugLogging,
            LogPath: settings.Advanced.LogPath);
    }

    private static T ParseEnumOrDefault<T>(
        string? source,
        T fallback)
        where T : struct, Enum
        => Enum.TryParse<T>(source, ignoreCase: true, out var v) ? v : fallback;
}