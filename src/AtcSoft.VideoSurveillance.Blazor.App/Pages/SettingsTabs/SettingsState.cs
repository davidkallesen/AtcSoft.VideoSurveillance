namespace Linksoft.VideoSurveillance.Blazor.App.Pages.SettingsTabs;

public sealed class SettingsState
{
    public AppSettingsThemeBase ThemeBase { get; set; } = AppSettingsThemeBase.Dark;

    public string ThemeAccent { get; set; } = "Blue";

    public string Language { get; set; } = "1033";

    public bool ConnectOnStartup { get; set; }

    public bool StartMaximized { get; set; }

    public bool ShowOverlayTitle { get; set; } = true;

    public bool ShowOverlayDescription { get; set; } = true;

    public bool ShowOverlayTime { get; set; }

    public bool ShowOverlayConnectionStatus { get; set; } = true;

    public double OverlayOpacity { get; set; } = 0.7;

    public AppSettingsOverlayPosition OverlayPosition { get; set; } = AppSettingsOverlayPosition.TopLeft;

    public bool AllowDragAndDropReorder { get; set; } = true;

    public bool AutoSaveLayoutChanges { get; set; } = true;

    public string SnapshotPath { get; set; } = string.Empty;

    public AppSettingsDefaultProtocol DefaultProtocol { get; set; } = AppSettingsDefaultProtocol.Rtsp;

    public int DefaultPort { get; set; } = 554;

    public int ConnectionTimeoutSeconds { get; set; } = 10;

    public int ReconnectDelaySeconds { get; set; } = 5;

    public bool AutoReconnectOnFailure { get; set; } = true;

    public bool ShowNotificationOnDisconnect { get; set; } = true;

    public bool ShowNotificationOnReconnect { get; set; } = true;

    public bool PlayNotificationSound { get; set; }

    public AppSettingsVideoQuality VideoQuality { get; set; } = AppSettingsVideoQuality.Auto;

    public bool HardwareAcceleration { get; set; } = true;

    public bool LowLatencyMode { get; set; }

    public int BufferDurationMs { get; set; } = 500;

    public AppSettingsRtspTransport RtspTransport { get; set; } = AppSettingsRtspTransport.Tcp;

    public int MaxLatencyMs { get; set; } = 1000;

    public int MotionSensitivity { get; set; } = 30;

    public double MinimumChangePercent { get; set; } = 0.5;

    public int AnalysisFrameRate { get; set; } = 5;

    public int AnalysisWidth { get; set; } = 320;

    public int AnalysisHeight { get; set; } = 240;

    public int PostMotionDurationSeconds { get; set; } = 5;

    public int CooldownSeconds { get; set; } = 3;

    public bool BoundingBoxShowInGrid { get; set; }

    public bool BoundingBoxShowInFullScreen { get; set; } = true;

    public string BoundingBoxColor { get; set; } = "#FF0000";

    public int BoundingBoxThickness { get; set; } = 2;

    public int BoundingBoxMinArea { get; set; } = 500;

    public int BoundingBoxPadding { get; set; } = 10;

    public double BoundingBoxSmoothing { get; set; } = 0.3;

    public string RecordingPath { get; set; } = string.Empty;

    public AppSettingsRecordingFormat RecordingFormat { get; set; } = AppSettingsRecordingFormat.Mp4;

    public bool EnableRecordingOnMotion { get; set; }

    public bool EnableRecordingOnConnect { get; set; }

    public bool EnableHourlySegmentation { get; set; }

    public int MaxRecordingDurationMinutes { get; set; } = 60;

    public int ThumbnailTileCount { get; set; } = 4;

    public bool EnableTimelapse { get; set; }

    public string TimelapseInterval { get; set; } = "5m";

    public AppSettingsCleanupSchedule CleanupSchedule { get; set; } = AppSettingsCleanupSchedule.Disabled;

    public int RecordingRetentionDays { get; set; } = 30;

    public bool CleanupIncludeSnapshots { get; set; }

    public int SnapshotRetentionDays { get; set; } = 30;

    public bool PlaybackShowFilename { get; set; } = true;

    public string PlaybackFilenameColor { get; set; } = "#FFFFFF";

    public bool PlaybackShowTimestamp { get; set; } = true;

    public string PlaybackTimestampColor { get; set; } = "#FFFFFF";

    public bool EnableDebugLogging { get; set; }

    public string LogPath { get; set; } = string.Empty;

    public void LoadFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ThemeBase = settings.ThemeBase ?? AppSettingsThemeBase.Dark;
        ThemeAccent = settings.ThemeAccent ?? "Blue";
        Language = settings.Language ?? "1033";
        ConnectOnStartup = settings.ConnectOnStartup;
        StartMaximized = settings.StartMaximized;

        ShowOverlayTitle = settings.ShowOverlayTitle;
        ShowOverlayDescription = settings.ShowOverlayDescription;
        ShowOverlayTime = settings.ShowOverlayTime;
        ShowOverlayConnectionStatus = settings.ShowOverlayConnectionStatus;
        OverlayOpacity = settings.OverlayOpacity;
        OverlayPosition = settings.OverlayPosition ?? AppSettingsOverlayPosition.TopLeft;
        AllowDragAndDropReorder = settings.AllowDragAndDropReorder;
        AutoSaveLayoutChanges = settings.AutoSaveLayoutChanges;
        SnapshotPath = settings.SnapshotPath ?? string.Empty;

        DefaultProtocol = settings.DefaultProtocol ?? AppSettingsDefaultProtocol.Rtsp;
        DefaultPort = settings.DefaultPort;
        ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds;
        ReconnectDelaySeconds = settings.ReconnectDelaySeconds;
        AutoReconnectOnFailure = settings.AutoReconnectOnFailure;
        ShowNotificationOnDisconnect = settings.ShowNotificationOnDisconnect;
        ShowNotificationOnReconnect = settings.ShowNotificationOnReconnect;
        PlayNotificationSound = settings.PlayNotificationSound;

        VideoQuality = settings.VideoQuality ?? AppSettingsVideoQuality.Auto;
        HardwareAcceleration = settings.HardwareAcceleration;
        LowLatencyMode = settings.LowLatencyMode;
        BufferDurationMs = settings.BufferDurationMs;
        RtspTransport = settings.RtspTransport ?? AppSettingsRtspTransport.Tcp;
        MaxLatencyMs = settings.MaxLatencyMs;

        MotionSensitivity = settings.MotionSensitivity;
        MinimumChangePercent = settings.MinimumChangePercent;
        AnalysisFrameRate = settings.AnalysisFrameRate;
        AnalysisWidth = settings.AnalysisWidth;
        AnalysisHeight = settings.AnalysisHeight;
        PostMotionDurationSeconds = settings.PostMotionDurationSeconds;
        CooldownSeconds = settings.CooldownSeconds;
        BoundingBoxShowInGrid = settings.BoundingBoxShowInGrid;
        BoundingBoxShowInFullScreen = settings.BoundingBoxShowInFullScreen;
        BoundingBoxColor = settings.BoundingBoxColor ?? "#FF0000";
        BoundingBoxThickness = settings.BoundingBoxThickness;
        BoundingBoxMinArea = settings.BoundingBoxMinArea;
        BoundingBoxPadding = settings.BoundingBoxPadding;
        BoundingBoxSmoothing = settings.BoundingBoxSmoothing;

        RecordingPath = settings.RecordingPath ?? string.Empty;
        RecordingFormat = settings.RecordingFormat ?? AppSettingsRecordingFormat.Mp4;
        EnableRecordingOnMotion = settings.EnableRecordingOnMotion;
        EnableRecordingOnConnect = settings.EnableRecordingOnConnect;
        EnableHourlySegmentation = settings.EnableHourlySegmentation;
        MaxRecordingDurationMinutes = settings.MaxRecordingDurationMinutes;
        ThumbnailTileCount = settings.ThumbnailTileCount;
        EnableTimelapse = settings.EnableTimelapse;
        TimelapseInterval = settings.TimelapseInterval ?? "5m";
        CleanupSchedule = settings.CleanupSchedule ?? AppSettingsCleanupSchedule.Disabled;
        RecordingRetentionDays = settings.RecordingRetentionDays;
        CleanupIncludeSnapshots = settings.CleanupIncludeSnapshots;
        SnapshotRetentionDays = settings.SnapshotRetentionDays;
        PlaybackShowFilename = settings.PlaybackShowFilename;
        PlaybackFilenameColor = settings.PlaybackFilenameColor ?? "#FFFFFF";
        PlaybackShowTimestamp = settings.PlaybackShowTimestamp;
        PlaybackTimestampColor = settings.PlaybackTimestampColor ?? "#FFFFFF";

        EnableDebugLogging = settings.EnableDebugLogging;
        LogPath = settings.LogPath ?? string.Empty;
    }

    public AppSettings ToApiModel()
        => new(
            ThemeBase: ThemeBase,
            ThemeAccent: ThemeAccent,
            Language: Language,
            ConnectOnStartup: ConnectOnStartup,
            StartMaximized: StartMaximized,
            ShowOverlayTitle: ShowOverlayTitle,
            ShowOverlayDescription: ShowOverlayDescription,
            ShowOverlayTime: ShowOverlayTime,
            ShowOverlayConnectionStatus: ShowOverlayConnectionStatus,
            OverlayOpacity: OverlayOpacity,
            OverlayPosition: OverlayPosition,
            AllowDragAndDropReorder: AllowDragAndDropReorder,
            AutoSaveLayoutChanges: AutoSaveLayoutChanges,
            SnapshotPath: SnapshotPath,
            DefaultProtocol: DefaultProtocol,
            DefaultPort: DefaultPort,
            ConnectionTimeoutSeconds: ConnectionTimeoutSeconds,
            ReconnectDelaySeconds: ReconnectDelaySeconds,
            AutoReconnectOnFailure: AutoReconnectOnFailure,
            ShowNotificationOnDisconnect: ShowNotificationOnDisconnect,
            ShowNotificationOnReconnect: ShowNotificationOnReconnect,
            PlayNotificationSound: PlayNotificationSound,
            VideoQuality: VideoQuality,
            HardwareAcceleration: HardwareAcceleration,
            LowLatencyMode: LowLatencyMode,
            BufferDurationMs: BufferDurationMs,
            RtspTransport: RtspTransport,
            MaxLatencyMs: MaxLatencyMs,
            MotionSensitivity: MotionSensitivity,
            MinimumChangePercent: MinimumChangePercent,
            AnalysisFrameRate: AnalysisFrameRate,
            AnalysisWidth: AnalysisWidth,
            AnalysisHeight: AnalysisHeight,
            PostMotionDurationSeconds: PostMotionDurationSeconds,
            CooldownSeconds: CooldownSeconds,
            BoundingBoxShowInGrid: BoundingBoxShowInGrid,
            BoundingBoxShowInFullScreen: BoundingBoxShowInFullScreen,
            BoundingBoxColor: BoundingBoxColor,
            BoundingBoxThickness: BoundingBoxThickness,
            BoundingBoxMinArea: BoundingBoxMinArea,
            BoundingBoxPadding: BoundingBoxPadding,
            BoundingBoxSmoothing: BoundingBoxSmoothing,
            RecordingPath: RecordingPath,
            RecordingFormat: RecordingFormat,
            EnableRecordingOnMotion: EnableRecordingOnMotion,
            EnableRecordingOnConnect: EnableRecordingOnConnect,
            EnableHourlySegmentation: EnableHourlySegmentation,
            MaxRecordingDurationMinutes: MaxRecordingDurationMinutes,
            ThumbnailTileCount: ThumbnailTileCount,
            EnableTimelapse: EnableTimelapse,
            TimelapseInterval: TimelapseInterval,
            CleanupSchedule: CleanupSchedule,
            RecordingRetentionDays: RecordingRetentionDays,
            CleanupIncludeSnapshots: CleanupIncludeSnapshots,
            SnapshotRetentionDays: SnapshotRetentionDays,
            PlaybackShowFilename: PlaybackShowFilename,
            PlaybackFilenameColor: PlaybackFilenameColor,
            PlaybackShowTimestamp: PlaybackShowTimestamp,
            PlaybackTimestampColor: PlaybackTimestampColor,
            EnableDebugLogging: EnableDebugLogging,
            LogPath: LogPath);
}