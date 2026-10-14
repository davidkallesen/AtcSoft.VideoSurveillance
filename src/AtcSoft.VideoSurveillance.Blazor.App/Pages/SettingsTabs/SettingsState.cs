namespace AtcSoft.VideoSurveillance.Blazor.App.Pages.SettingsTabs;

/// <summary>
/// The settings as they are edited on the settings page. The starting
/// values are the server's defaults; a test keeps them equal.
/// </summary>
public sealed class SettingsState
{
    private bool loadedUseDailySubfolders = true;
    private bool loadedSeparateThumbnailFolder = true;

    public AppSettingsThemeBase ThemeBase { get; set; } = AppSettingsThemeBase.Dark;

    public string ThemeAccent { get; set; } = "Blue";

    public string Language { get; set; } = "1033";

    public bool ConnectOnStartup { get; set; } = true;

    public bool StartMaximized { get; set; }

    public bool ShowOverlayTitle { get; set; } = true;

    public bool ShowOverlayDescription { get; set; } = true;

    public bool ShowOverlayTime { get; set; }

    public bool ShowOverlayConnectionStatus { get; set; } = true;

    public bool ShowOverlayQuickActions { get; set; } = true;

    public double OverlayOpacity { get; set; } = 0.7;

    public AppSettingsOverlayPosition OverlayPosition { get; set; } = AppSettingsOverlayPosition.TopLeft;

    public bool ShowOverlayConnectionTypeIcon { get; set; }

    public AppSettingsConnectionTypeIconPlacement ConnectionTypeIconPlacement { get; set; } = AppSettingsConnectionTypeIconPlacement.SameRow;

    public bool AllowDragAndDropReorder { get; set; } = true;

    public bool AutoSaveLayoutChanges { get; set; } = true;

    public string SnapshotPath { get; set; } = string.Empty;

    public AppSettingsDefaultProtocol DefaultProtocol { get; set; } = AppSettingsDefaultProtocol.Rtsp;

    public int DefaultPort { get; set; } = 554;

    public int ConnectionTimeoutSeconds { get; set; } = 10;

    public int ReconnectDelaySeconds { get; set; } = 10;

    public bool AutoReconnectOnFailure { get; set; } = true;

    public bool ShowNotificationOnDisconnect { get; set; } = true;

    public bool ShowNotificationOnReconnect { get; set; }

    public bool PlayNotificationSound { get; set; }

    public string VideoQuality { get; set; } = "Auto";

    public bool HardwareAcceleration { get; set; } = true;

    public bool LowLatencyMode { get; set; }

    public int BufferDurationMs { get; set; } = 500;

    public AppSettingsRtspTransport RtspTransport { get; set; } = AppSettingsRtspTransport.Tcp;

    public int MaxLatencyMs { get; set; } = 500;

    public int MotionSensitivity { get; set; } = 30;

    public double MinimumChangePercent { get; set; } = 0.5;

    public int AnalysisFrameRate { get; set; } = 15;

    public int AnalysisWidth { get; set; } = 800;

    public int AnalysisHeight { get; set; } = 600;

    /// <summary>
    /// Gets or sets the analysis size as one "width x height" choice.
    /// </summary>
    public string AnalysisResolution
    {
        get => string.Create(CultureInfo.InvariantCulture, $"{AnalysisWidth}x{AnalysisHeight}");
        set
        {
            var parts = (value ?? string.Empty).Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
                width > 0 &&
                height > 0)
            {
                AnalysisWidth = width;
                AnalysisHeight = height;
            }
        }
    }

    public int PostMotionDurationSeconds { get; set; } = 10;

    public int CooldownSeconds { get; set; } = 5;

    public bool BoundingBoxShowInGrid { get; set; }

    public bool BoundingBoxShowInFullScreen { get; set; }

    public string BoundingBoxColor { get; set; } = "Red";

    public int BoundingBoxThickness { get; set; } = 2;

    public int BoundingBoxMinArea { get; set; } = 10;

    public int BoundingBoxPadding { get; set; } = 4;

    public double BoundingBoxSmoothing { get; set; } = 0.3;

    public string RecordingPath { get; set; } = string.Empty;

    public AppSettingsRecordingFormat RecordingFormat { get; set; } = AppSettingsRecordingFormat.Mkv;

    public bool RecordAsH264 { get; set; }

    public bool EnableRecordingOnMotion { get; set; }

    public bool EnableRecordingOnConnect { get; set; }

    public bool EnableHourlySegmentation { get; set; } = true;

    public bool UseDailySubfolders { get; set; } = true;

    public bool SeparateThumbnailFolder { get; set; } = true;

    public int MaxRecordingDurationMinutes { get; set; } = 60;

    public int ThumbnailTileCount { get; set; } = 4;

    public bool EnableTimelapse { get; set; }

    public string TimelapseInterval { get; set; } = "5m";

    public AppSettingsCleanupSchedule CleanupSchedule { get; set; } = AppSettingsCleanupSchedule.OnStartupAndPeriodically;

    public int RecordingRetentionDays { get; set; } = 30;

    public bool CleanupIncludeSnapshots { get; set; }

    public int SnapshotRetentionDays { get; set; } = 7;

    public bool EnableDiskSpaceGuard { get; set; } = true;

    public int MinFreeSpaceMb { get; set; } = 2048;

    public bool PlaybackShowFilename { get; set; } = true;

    public string PlaybackFilenameColor { get; set; } = "White";

    public bool PlaybackShowTimestamp { get; set; } = true;

    public string PlaybackTimestampColor { get; set; } = "White";

    public bool EnableDebugLogging { get; set; }

    public string LogPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a folder layout switch differs from
    /// what the server has. Saving then moves the existing recordings.
    /// </summary>
    public bool HasRecordingLayoutChanged
        => UseDailySubfolders != loadedUseDailySubfolders ||
           SeparateThumbnailFolder != loadedSeparateThumbnailFolder;

    /// <summary>
    /// Gets where a recording goes with the current folder layout, below the recording folder.
    /// </summary>
    public string RecordingPathExample
        => RecordingLayoutExample.Build(RecordingFormat.ToString(), UseDailySubfolders, SeparateThumbnailFolder).Video;

    /// <summary>
    /// Gets where its thumbnail goes.
    /// </summary>
    public string ThumbnailPathExample
        => RecordingLayoutExample.Build(RecordingFormat.ToString(), UseDailySubfolders, SeparateThumbnailFolder).Thumbnail;

    /// <summary>
    /// Gets the help of the "folder per day" switch: the recording path with the switch on and off.
    /// </summary>
    public string UseDailySubfoldersHint
        => OnOffHint(
            RecordingLayoutExample.Build(RecordingFormat.ToString(), useDailySubfolders: true, SeparateThumbnailFolder).Video,
            RecordingLayoutExample.Build(RecordingFormat.ToString(), useDailySubfolders: false, SeparateThumbnailFolder).Video);

    /// <summary>
    /// Gets the help of the "separate thumbnail folder" switch: the thumbnail path with the switch on and off.
    /// </summary>
    public string SeparateThumbnailFolderHint
        => OnOffHint(
            RecordingLayoutExample.Build(RecordingFormat.ToString(), UseDailySubfolders, separateThumbnailFolder: true).Thumbnail,
            RecordingLayoutExample.Build(RecordingFormat.ToString(), UseDailySubfolders, separateThumbnailFolder: false).Thumbnail);

    public void LoadFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ThemeBase = settings.ThemeBase ?? AppSettingsThemeBase.Dark;
        ThemeAccent = settings.ThemeAccent ?? "Blue";
        Language = settings.Language ?? "1033";
        ConnectOnStartup = settings.ConnectOnStartup ?? false;
        StartMaximized = settings.StartMaximized ?? false;

        ShowOverlayTitle = settings.ShowOverlayTitle ?? false;
        ShowOverlayDescription = settings.ShowOverlayDescription ?? false;
        ShowOverlayTime = settings.ShowOverlayTime ?? false;
        ShowOverlayConnectionStatus = settings.ShowOverlayConnectionStatus ?? false;
        ShowOverlayQuickActions = settings.ShowOverlayQuickActions ?? true;
        OverlayOpacity = settings.OverlayOpacity ?? 0;
        OverlayPosition = settings.OverlayPosition ?? AppSettingsOverlayPosition.TopLeft;
        ShowOverlayConnectionTypeIcon = settings.ShowOverlayConnectionTypeIcon ?? false;
        ConnectionTypeIconPlacement = settings.ConnectionTypeIconPlacement ?? AppSettingsConnectionTypeIconPlacement.SameRow;
        AllowDragAndDropReorder = settings.AllowDragAndDropReorder ?? false;
        AutoSaveLayoutChanges = settings.AutoSaveLayoutChanges ?? false;
        SnapshotPath = settings.SnapshotPath ?? string.Empty;

        DefaultProtocol = settings.DefaultProtocol ?? AppSettingsDefaultProtocol.Rtsp;
        DefaultPort = settings.DefaultPort ?? 0;
        ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds ?? 0;
        ReconnectDelaySeconds = settings.ReconnectDelaySeconds ?? 0;
        AutoReconnectOnFailure = settings.AutoReconnectOnFailure ?? false;
        ShowNotificationOnDisconnect = settings.ShowNotificationOnDisconnect ?? false;
        ShowNotificationOnReconnect = settings.ShowNotificationOnReconnect ?? false;
        PlayNotificationSound = settings.PlayNotificationSound ?? false;

        VideoQuality = string.IsNullOrEmpty(settings.VideoQuality) ? "Auto" : settings.VideoQuality;
        HardwareAcceleration = settings.HardwareAcceleration ?? false;
        LowLatencyMode = settings.LowLatencyMode ?? false;
        BufferDurationMs = settings.BufferDurationMs ?? 0;
        RtspTransport = settings.RtspTransport ?? AppSettingsRtspTransport.Tcp;
        MaxLatencyMs = settings.MaxLatencyMs ?? 0;

        MotionSensitivity = settings.MotionSensitivity ?? 0;
        MinimumChangePercent = settings.MinimumChangePercent ?? 0;
        AnalysisFrameRate = settings.AnalysisFrameRate ?? 0;
        AnalysisWidth = settings.AnalysisWidth ?? 0;
        AnalysisHeight = settings.AnalysisHeight ?? 0;
        PostMotionDurationSeconds = settings.PostMotionDurationSeconds ?? 0;
        CooldownSeconds = settings.CooldownSeconds ?? 0;
        BoundingBoxShowInGrid = settings.BoundingBoxShowInGrid ?? false;
        BoundingBoxShowInFullScreen = settings.BoundingBoxShowInFullScreen ?? false;
        BoundingBoxColor = ColorChoices.Normalize(settings.BoundingBoxColor, "Red");
        BoundingBoxThickness = settings.BoundingBoxThickness ?? 0;
        BoundingBoxMinArea = settings.BoundingBoxMinArea ?? 0;
        BoundingBoxPadding = settings.BoundingBoxPadding ?? 0;
        BoundingBoxSmoothing = settings.BoundingBoxSmoothing ?? 0;

        RecordingPath = settings.RecordingPath ?? string.Empty;
        RecordingFormat = settings.RecordingFormat ?? AppSettingsRecordingFormat.Mp4;
        RecordAsH264 = settings.RecordAsH264 ?? false;
        EnableRecordingOnMotion = settings.EnableRecordingOnMotion ?? false;
        EnableRecordingOnConnect = settings.EnableRecordingOnConnect ?? false;
        EnableHourlySegmentation = settings.EnableHourlySegmentation ?? false;
        MaxRecordingDurationMinutes = settings.MaxRecordingDurationMinutes ?? 0;

        // The server makes a thumbnail of one tile or of four.
        ThumbnailTileCount = settings.ThumbnailTileCount == 1 ? 1 : 4;
        UseDailySubfolders = settings.UseDailySubfolders ?? true;
        SeparateThumbnailFolder = settings.SeparateThumbnailFolder ?? true;
        loadedUseDailySubfolders = UseDailySubfolders;
        loadedSeparateThumbnailFolder = SeparateThumbnailFolder;
        EnableTimelapse = settings.EnableTimelapse ?? false;
        TimelapseInterval = settings.TimelapseInterval ?? "5m";
        CleanupSchedule = settings.CleanupSchedule ?? AppSettingsCleanupSchedule.Disabled;
        RecordingRetentionDays = settings.RecordingRetentionDays ?? 0;
        CleanupIncludeSnapshots = settings.CleanupIncludeSnapshots ?? false;
        SnapshotRetentionDays = settings.SnapshotRetentionDays ?? 0;
        EnableDiskSpaceGuard = settings.EnableDiskSpaceGuard ?? true;
        MinFreeSpaceMb = settings.MinFreeSpaceMb > 0 ? settings.MinFreeSpaceMb.Value : 2048;
        PlaybackShowFilename = settings.PlaybackShowFilename ?? false;
        PlaybackFilenameColor = ColorChoices.Normalize(settings.PlaybackFilenameColor, "White");
        PlaybackShowTimestamp = settings.PlaybackShowTimestamp ?? false;
        PlaybackTimestampColor = ColorChoices.Normalize(settings.PlaybackTimestampColor, "White");

        EnableDebugLogging = settings.EnableDebugLogging ?? false;
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
            ShowOverlayQuickActions: ShowOverlayQuickActions,
            ShowOverlayConnectionTypeIcon: ShowOverlayConnectionTypeIcon,
            ConnectionTypeIconPlacement: ConnectionTypeIconPlacement,
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
            RecordAsH264: RecordAsH264,
            EnableRecordingOnMotion: EnableRecordingOnMotion,
            EnableRecordingOnConnect: EnableRecordingOnConnect,
            EnableHourlySegmentation: EnableHourlySegmentation,
            MaxRecordingDurationMinutes: MaxRecordingDurationMinutes,
            ThumbnailTileCount: ThumbnailTileCount,
            UseDailySubfolders: UseDailySubfolders,
            SeparateThumbnailFolder: SeparateThumbnailFolder,
            EnableTimelapse: EnableTimelapse,
            TimelapseInterval: TimelapseInterval,
            CleanupSchedule: CleanupSchedule,
            RecordingRetentionDays: RecordingRetentionDays,
            CleanupIncludeSnapshots: CleanupIncludeSnapshots,
            SnapshotRetentionDays: SnapshotRetentionDays,
            EnableDiskSpaceGuard: EnableDiskSpaceGuard,
            MinFreeSpaceMb: MinFreeSpaceMb,
            PlaybackShowFilename: PlaybackShowFilename,
            PlaybackFilenameColor: PlaybackFilenameColor,
            PlaybackShowTimestamp: PlaybackShowTimestamp,
            PlaybackTimestampColor: PlaybackTimestampColor,
            EnableDebugLogging: EnableDebugLogging,
            LogPath: LogPath);

    private static string OnOffHint(
        string on,
        string off)
        => $"On: {on}{Environment.NewLine}Off: {off}";

    /// <summary>
    /// Gets the concrete corner a placement resolves to for the current
    /// overlay position: the icon always uses a corner adjacent to the info box.
    /// </summary>
    public string GetConnectionTypeIconPlacementLabel(
        AppSettingsConnectionTypeIconPlacement placement)
    {
        var isTop = OverlayPosition is AppSettingsOverlayPosition.TopLeft or AppSettingsOverlayPosition.TopRight;
        var isLeft = OverlayPosition is AppSettingsOverlayPosition.TopLeft or AppSettingsOverlayPosition.BottomLeft;

        if (placement == AppSettingsConnectionTypeIconPlacement.SameColumn)
        {
            isTop = !isTop;
            return $"{(isTop ? "Top" : "Bottom")} {(isLeft ? "Left" : "Right")} (same column as info box)";
        }

        isLeft = !isLeft;
        return $"{(isTop ? "Top" : "Bottom")} {(isLeft ? "Left" : "Right")} (same row as info box)";
    }
}