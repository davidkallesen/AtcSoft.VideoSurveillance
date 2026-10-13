namespace AtcSoft.VideoSurveillance.Wpf.Dialogs;

/// <summary>
/// View model for the settings dialog.
/// Loads/saves settings via REST API (flat AppSettings record).
/// General settings are stored locally via IApplicationSettingsService.
/// </summary>
[SuppressMessage("", "SA1124: Do not use regions", Justification = "OK")]
[SuppressMessage("", "S2325:Make properties static", Justification = "XAML binding requires instance properties")]
public partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly GatewayService gatewayService;
    private readonly AtcSoft.VideoSurveillance.Wpf.Core.Services.IApplicationSettingsService settingsService;

    // Snapshot of full API settings for preserving server-only fields on save
    private AppSettings? originalApiSettings;

    // Original theme/language values for restoration on cancel
    private string originalThemeBase = "Dark";
    private string originalThemeAccent = "Blue";
    private string originalLanguage = "1033";

    public SettingsDialogViewModel(
        GatewayService gatewayService,
        AtcSoft.VideoSurveillance.Wpf.Core.Services.IApplicationSettingsService settingsService)
    {
        this.gatewayService = gatewayService ?? throw new ArgumentNullException(nameof(gatewayService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public event EventHandler<DialogClosedEventArgs>? CloseRequested;

    #region General Tab

    [ObservableProperty]
    private string selectedLanguage = "1033";

    [ObservableProperty]
    private bool connectCamerasOnStartup = true;

    [ObservableProperty]
    private bool startMaximized;

    [ObservableProperty]
    private bool startRibbonCollapsed;

    #endregion

    #region Camera Display Tab

    [ObservableProperty]
    private bool showCameraOverlayTitle = true;

    [ObservableProperty]
    private bool showCameraOverlayDescription = true;

    [ObservableProperty]
    private bool showCameraOverlayTime;

    [ObservableProperty]
    private bool showCameraOverlayConnectionStatus = true;

    [ObservableProperty]
    private bool showCameraOverlayQuickActions = true;

    [ObservableProperty]
    private string selectedOverlayOpacity = DropDownItemsFactory.DefaultOverlayOpacity;

    public IDictionary<string, string> OverlayOpacityItems
        => DropDownItemsFactory.OverlayOpacityItems;

    [ObservableProperty(DependentPropertyNames = [nameof(ConnectionTypeIconPlacementItems)])]
    private string selectedOverlayPosition = DropDownItemsFactory.DefaultOverlayPosition;

    public IDictionary<string, string> OverlayPositionItems
        => DropDownItemsFactory.OverlayPositionItems;

    [ObservableProperty]
    private bool showCameraOverlayConnectionTypeIcon;

    [ObservableProperty]
    private string selectedConnectionTypeIconPlacement = DropDownItemsFactory.DefaultConnectionTypeIconPlacement;

    /// <summary>
    /// Gets the icon placements, labelled with the concrete corner they resolve
    /// to for the currently selected default overlay position.
    /// </summary>
    public IDictionary<string, string> ConnectionTypeIconPlacementItems
        => DropDownItemsFactory.GetConnectionTypeIconPlacementItems(
            Enum.TryParse<AtcSoft.VideoSurveillance.Enums.OverlayPosition>(SelectedOverlayPosition, out var position)
                ? position
                : AtcSoft.VideoSurveillance.Enums.OverlayPosition.TopLeft);

    [ObservableProperty]
    private bool allowDragAndDropReorder = true;

    [ObservableProperty]
    private bool autoSaveLayoutChanges = true;

    [ObservableProperty]
    private DirectoryInfo? snapshotPath;

    #endregion

    #region Connection Tab

    [ObservableProperty]
    private string selectedDefaultProtocol = DropDownItemsFactory.DefaultProtocol;

    public IDictionary<string, string> DefaultProtocolItems
        => DropDownItemsFactory.ProtocolItems;

    [ObservableProperty]
    private int defaultPort = 554;

    [ObservableProperty]
    private int connectionTimeoutSeconds = 10;

    [ObservableProperty]
    private int reconnectDelaySeconds = 10;

    [ObservableProperty]
    private bool autoReconnectOnFailure = true;

    [ObservableProperty]
    private bool showNotificationOnDisconnect = true;

    [ObservableProperty]
    private bool showNotificationOnReconnect;

    [ObservableProperty]
    private bool playNotificationSound;

    #endregion

    #region Performance Tab

    [ObservableProperty]
    private string selectedVideoQuality = DropDownItemsFactory.DefaultVideoQuality;

    public IDictionary<string, string> VideoQualityItems
        => DropDownItemsFactory.VideoQualityItems;

    [ObservableProperty]
    private bool hardwareAcceleration = true;

    [ObservableProperty]
    private bool lowLatencyMode;

    [ObservableProperty]
    private int bufferDurationMs = 500;

    [ObservableProperty]
    private string selectedRtspTransport = DropDownItemsFactory.DefaultRtspTransport;

    public IDictionary<string, string> RtspTransportItems
        => DropDownItemsFactory.RtspTransportItems;

    [ObservableProperty]
    private int maxLatencyMs = 500;

    #endregion

    #region Motion Detection Tab

    [ObservableProperty]
    private int motionSensitivity = DropDownItemsFactory.DefaultMotionSensitivity;

    [ObservableProperty]
    private int postMotionDurationSeconds = DropDownItemsFactory.DefaultPostMotionDuration;

    [ObservableProperty]
    private int analysisFrameRate = 2;

    [ObservableProperty]
    private string selectedAnalysisResolution = DropDownItemsFactory.DefaultMotionAnalysisResolution;

    public IDictionary<string, string> AnalysisResolutionItems
        => DropDownItemsFactory.MotionAnalysisResolutionItems;

    [ObservableProperty]
    private int cooldownSeconds = 5;

    [ObservableProperty]
    private bool showBoundingBoxInGrid;

    [ObservableProperty]
    private bool showBoundingBoxInFullScreen;

    [ObservableProperty]
    private string selectedBoundingBoxColor = DropDownItemsFactory.DefaultBoundingBoxColor;

    [ObservableProperty]
    private string selectedBoundingBoxThickness = DropDownItemsFactory.DefaultBoundingBoxThickness.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> BoundingBoxThicknessItems
        => DropDownItemsFactory.BoundingBoxThicknessItems;

    [ObservableProperty]
    private string selectedBoundingBoxMinArea = DropDownItemsFactory.DefaultBoundingBoxMinArea.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> BoundingBoxMinAreaItems
        => DropDownItemsFactory.BoundingBoxMinAreaItems;

    #endregion

    #region Capture Tab

    [ObservableProperty]
    private DirectoryInfo? recordingPath;

    [ObservableProperty]
    private string selectedRecordingFormat = DropDownItemsFactory.DefaultRecordingFormat;

    public IDictionary<string, string> RecordingFormatItems
        => DropDownItemsFactory.RecordingFormatItems;

    // Whether recordings are made as H.264 whatever the camera sends.
    [ObservableProperty]
    private string selectedTranscodeVideoCodec = AtcSoft.VideoSurveillance.Factories.DropDownItemsFactory.DefaultTranscodeVideoCodec;

    public IDictionary<string, string> TranscodeVideoCodecItems
        => AtcSoft.VideoSurveillance.Factories.DropDownItemsFactory.TranscodeVideoCodecItems;

    [ObservableProperty]
    private bool enableRecordingOnMotion;

    [ObservableProperty]
    private bool enableRecordingOnConnect;

    // Storage layout
    [ObservableProperty(DependentPropertyNames = [nameof(RecordingLayoutExampleSummary), nameof(SeparateThumbnailFolderHint)])]
    private bool useDailySubfolders = true;

    [ObservableProperty(DependentPropertyNames = [nameof(RecordingLayoutExampleSummary), nameof(UseDailySubfoldersHint)])]
    private bool separateThumbnailFolder = true;

    /// <summary>
    /// Gets the example shown under the two layout switches. The server
    /// moves its recordings when the settings are saved.
    /// </summary>
    public string RecordingLayoutExampleSummary
        => RecordingLayoutExamples.Summary(UseDailySubfolders, SeparateThumbnailFolder, SelectedRecordingFormat);

    /// <summary>
    /// Gets the hover help of the "folder per day" switch.
    /// </summary>
    public string UseDailySubfoldersHint
        => RecordingLayoutExamples.DailySubfoldersHint(SeparateThumbnailFolder, SelectedRecordingFormat);

    /// <summary>
    /// Gets the hover help of the "separate thumbnail folder" switch.
    /// </summary>
    public string SeparateThumbnailFolderHint
        => RecordingLayoutExamples.SeparateThumbnailFolderHint(UseDailySubfolders, SelectedRecordingFormat);

    // Segmentation
    [ObservableProperty]
    private bool enableHourlySegmentation = true;

    [ObservableProperty]
    private string selectedMaxRecordingDuration = DropDownItemsFactory.DefaultMaxRecordingDuration.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> MaxRecordingDurationItems
        => DropDownItemsFactory.MaxRecordingDurationItems;

    // Thumbnails
    [ObservableProperty]
    private string selectedThumbnailTileCount = DropDownItemsFactory.DefaultThumbnailTileCount.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> ThumbnailTileCountItems
        => DropDownItemsFactory.ThumbnailTileCountItems;

    #endregion

    #region Timelapse Tab

    [ObservableProperty(DependentPropertyNames = [nameof(IsTimelapseIntervalEnabled)])]
    private bool enableTimelapse;

    [ObservableProperty]
    private string selectedTimelapseInterval = DropDownItemsFactory.DefaultTimelapseInterval;

    public IDictionary<string, string> TimelapseIntervalItems
        => DropDownItemsFactory.TimelapseIntervalItems;

    /// <summary>
    /// Gets a value indicating whether the timelapse interval dropdown should be enabled.
    /// </summary>
    public bool IsTimelapseIntervalEnabled
        => EnableTimelapse;

    #endregion

    #region Storage Tab

    // Media Cleanup
    [ObservableProperty(DependentPropertyNames = [nameof(IsCleanupEnabled), nameof(IsSnapshotRetentionEnabled), nameof(IsDiskSpaceGuardControlEnabled)])]
    private string selectedCleanupSchedule = DropDownItemsFactory.DefaultMediaCleanupSchedule;

    public IDictionary<string, string> CleanupScheduleItems
        => DropDownItemsFactory.MediaCleanupScheduleItems;

    [ObservableProperty]
    private string selectedRecordingRetention = DropDownItemsFactory.DefaultRecordingRetentionDays.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> RecordingRetentionItems
        => DropDownItemsFactory.MediaRetentionPeriodItems;

    [ObservableProperty(DependentPropertyNames = [nameof(IsSnapshotRetentionEnabled)])]
    private bool includeSnapshotsInCleanup;

    [ObservableProperty]
    private string selectedSnapshotRetention = DropDownItemsFactory.DefaultSnapshotRetentionDays.ToString(CultureInfo.InvariantCulture);

    public IDictionary<string, string> SnapshotRetentionItems
        => DropDownItemsFactory.MediaRetentionPeriodItems;

    /// <summary>
    /// Gets a value indicating whether cleanup is enabled (schedule is not Disabled).
    /// </summary>
    public bool IsCleanupEnabled
        => !string.Equals(SelectedCleanupSchedule, "Disabled", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether snapshot retention dropdown should be enabled.
    /// </summary>
    public bool IsSnapshotRetentionEnabled
        => IsCleanupEnabled && IncludeSnapshotsInCleanup;

    [ObservableProperty(DependentPropertyNames = [nameof(IsDiskSpaceGuardControlEnabled)])]
    private bool isDiskSpaceGuardEnabled = true;

    [ObservableProperty]
    private string selectedMinFreeSpace = DropDownItemsFactory.DefaultMinFreeSpaceMb;

    public IDictionary<string, string> MinFreeSpaceItems
        => DropDownItemsFactory.MinFreeSpaceItems;

    /// <summary>
    /// Gets a value indicating whether the minimum free space dropdown
    /// should be enabled (cleanup is on AND disk guard is on).
    /// </summary>
    public bool IsDiskSpaceGuardControlEnabled
        => IsCleanupEnabled && IsDiskSpaceGuardEnabled;

    // Playback Overlay
    [ObservableProperty]
    private bool showPlaybackFilename = true;

    [ObservableProperty]
    private string selectedPlaybackFilenameColor = "White";

    [ObservableProperty]
    private bool showPlaybackTimestamp = true;

    [ObservableProperty]
    private string selectedPlaybackTimestampColor = "White";

    #endregion

    #region Advanced Tab

    [ObservableProperty]
    private bool enableDebugLogging;

    [ObservableProperty]
    private DirectoryInfo? logPath;

    #endregion

    #region Load / Save

    /// <summary>
    /// Loads settings from the local service (General) and API server (everything else).
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        // Load general settings from local service
        var general = settingsService.General;
        originalThemeBase = general.ThemeBase;
        originalThemeAccent = general.ThemeAccent;
        originalLanguage = general.Language;
        SelectedLanguage = general.Language;
        ConnectCamerasOnStartup = general.ConnectCamerasOnStartup;
        StartMaximized = general.StartMaximized;
        StartRibbonCollapsed = general.StartRibbonCollapsed;

        try
        {
            var settings = await gatewayService
                .GetSettingsAsync()
                .ConfigureAwait(true);

            if (settings is null)
            {
                return;
            }

            originalApiSettings = settings;

            // Camera Display
            ShowCameraOverlayTitle = settings.ShowOverlayTitle;
            ShowCameraOverlayDescription = settings.ShowOverlayDescription;
            ShowCameraOverlayTime = settings.ShowOverlayTime;
            ShowCameraOverlayConnectionStatus = settings.ShowOverlayConnectionStatus;
            ShowCameraOverlayQuickActions = settings.ShowOverlayQuickActions ?? true;
            ShowCameraOverlayConnectionTypeIcon = settings.ShowOverlayConnectionTypeIcon;
            SelectedConnectionTypeIconPlacement = settings.ConnectionTypeIconPlacement?.ToString() ?? DropDownItemsFactory.DefaultConnectionTypeIconPlacement;
            SelectedOverlayOpacity = settings.OverlayOpacity.ToString("F1", CultureInfo.InvariantCulture);
            SelectedOverlayPosition = settings.OverlayPosition?.ToString() ?? "TopLeft";
            AllowDragAndDropReorder = settings.AllowDragAndDropReorder;
            AutoSaveLayoutChanges = settings.AutoSaveLayoutChanges;
            SnapshotPath = !string.IsNullOrEmpty(settings.SnapshotPath) ? new DirectoryInfo(settings.SnapshotPath) : null;

            // Connection
            SelectedDefaultProtocol = settings.DefaultProtocol?.ToString() ?? "Rtsp";
            DefaultPort = settings.DefaultPort;
            ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds;
            ReconnectDelaySeconds = settings.ReconnectDelaySeconds;
            AutoReconnectOnFailure = settings.AutoReconnectOnFailure;
            ShowNotificationOnDisconnect = settings.ShowNotificationOnDisconnect;
            ShowNotificationOnReconnect = settings.ShowNotificationOnReconnect;
            PlayNotificationSound = settings.PlayNotificationSound;

            // Performance
            SelectedVideoQuality = AtcSoft.VideoSurveillance.Helpers.VideoQualityHelper.Normalize(settings.VideoQuality) ?? DropDownItemsFactory.DefaultVideoQuality;
            HardwareAcceleration = settings.HardwareAcceleration;
            LowLatencyMode = settings.LowLatencyMode;
            BufferDurationMs = settings.BufferDurationMs;
            SelectedRtspTransport = settings.RtspTransport?.ToString() ?? "Tcp";
            MaxLatencyMs = settings.MaxLatencyMs;

            // Motion Detection
            MotionSensitivity = settings.MotionSensitivity;
            PostMotionDurationSeconds = settings.PostMotionDurationSeconds;
            AnalysisFrameRate = settings.AnalysisFrameRate;
            SelectedAnalysisResolution = DropDownItemsFactory.FormatAnalysisResolution(settings.AnalysisWidth, settings.AnalysisHeight);
            CooldownSeconds = settings.CooldownSeconds;
            ShowBoundingBoxInGrid = settings.BoundingBoxShowInGrid;
            ShowBoundingBoxInFullScreen = settings.BoundingBoxShowInFullScreen;
            SelectedBoundingBoxColor = settings.BoundingBoxColor ?? DropDownItemsFactory.DefaultBoundingBoxColor;
            SelectedBoundingBoxThickness = settings.BoundingBoxThickness.ToString(CultureInfo.InvariantCulture);
            SelectedBoundingBoxMinArea = settings.BoundingBoxMinArea.ToString(CultureInfo.InvariantCulture);

            // Capture
            RecordingPath = !string.IsNullOrEmpty(settings.RecordingPath) ? new DirectoryInfo(settings.RecordingPath) : null;
            SelectedRecordingFormat = settings.RecordingFormat?.ToString() ?? "Mp4";
            SelectedTranscodeVideoCodec = settings.RecordAsH264 ? "H264" : "None";
            EnableRecordingOnMotion = settings.EnableRecordingOnMotion;
            EnableRecordingOnConnect = settings.EnableRecordingOnConnect;
            EnableHourlySegmentation = settings.EnableHourlySegmentation;
            UseDailySubfolders = settings.UseDailySubfolders ?? true;
            SeparateThumbnailFolder = settings.SeparateThumbnailFolder ?? true;
            SelectedMaxRecordingDuration = settings.MaxRecordingDurationMinutes.ToString(CultureInfo.InvariantCulture);
            SelectedThumbnailTileCount = settings.ThumbnailTileCount.ToString(CultureInfo.InvariantCulture);

            // Timelapse
            EnableTimelapse = settings.EnableTimelapse;
            SelectedTimelapseInterval = settings.TimelapseInterval ?? DropDownItemsFactory.DefaultTimelapseInterval;

            // Storage
            SelectedCleanupSchedule = settings.CleanupSchedule?.ToString() ?? DropDownItemsFactory.DefaultMediaCleanupSchedule;
            SelectedRecordingRetention = settings.RecordingRetentionDays.ToString(CultureInfo.InvariantCulture);
            IncludeSnapshotsInCleanup = settings.CleanupIncludeSnapshots;
            SelectedSnapshotRetention = settings.SnapshotRetentionDays.ToString(CultureInfo.InvariantCulture);
            IsDiskSpaceGuardEnabled = settings.EnableDiskSpaceGuard ?? true;
            SelectedMinFreeSpace = settings.MinFreeSpaceMb > 0
                ? settings.MinFreeSpaceMb.ToString(CultureInfo.InvariantCulture)
                : DropDownItemsFactory.DefaultMinFreeSpaceMb;
            ShowPlaybackFilename = settings.PlaybackShowFilename;
            SelectedPlaybackFilenameColor = settings.PlaybackFilenameColor ?? "White";
            ShowPlaybackTimestamp = settings.PlaybackShowTimestamp;
            SelectedPlaybackTimestampColor = settings.PlaybackTimestampColor ?? "White";

            // Advanced
            EnableDebugLogging = settings.EnableDebugLogging;
            LogPath = !string.IsNullOrEmpty(settings.LogPath) ? new DirectoryInfo(settings.LogPath) : null;
        }
        catch
        {
            // Failed to load settings - use defaults
        }
    }

    #endregion

    #region Commands

    [RelayCommand("Save")]
    private async Task SaveAsync()
    {
        try
        {
            // Save general settings locally
            SaveGeneralSettings();

            // Save remaining settings to API
            var settings = BuildApiSettings();
            await gatewayService
                .UpdateSettingsAsync(settings)
                .ConfigureAwait(true);
        }
        catch
        {
            // Save failed - could show error but settings dialog will close
        }

        CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: true));
    }

    [RelayCommand]
    private void Cancel()
    {
        RestoreOriginalThemeAndLanguage();
        CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: false));
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        // General Tab
        SelectedLanguage = "1033";
        ConnectCamerasOnStartup = true;
        StartMaximized = false;
        StartRibbonCollapsed = false;

        ThemeManager.Current.ChangeThemeBaseColor(Application.Current, "Dark");
        ThemeManager.Current.ChangeThemeColorScheme(Application.Current, "Blue");
        CultureManager.UiCulture = new CultureInfo(1033);

        // Camera Display Tab
        ShowCameraOverlayTitle = true;
        ShowCameraOverlayDescription = true;
        ShowCameraOverlayTime = false;
        ShowCameraOverlayConnectionStatus = true;
        ShowCameraOverlayQuickActions = true;
        SelectedOverlayOpacity = "0.7";
        SelectedOverlayPosition = "TopLeft";
        ShowCameraOverlayConnectionTypeIcon = false;
        SelectedConnectionTypeIconPlacement = DropDownItemsFactory.DefaultConnectionTypeIconPlacement;
        AllowDragAndDropReorder = true;
        AutoSaveLayoutChanges = true;
        SnapshotPath = new DirectoryInfo(ApplicationPaths.DefaultSnapshotsPath);

        // Connection Tab
        SelectedDefaultProtocol = "Rtsp";
        DefaultPort = 554;
        ConnectionTimeoutSeconds = 10;
        ReconnectDelaySeconds = 10;
        AutoReconnectOnFailure = true;
        ShowNotificationOnDisconnect = true;
        ShowNotificationOnReconnect = false;
        PlayNotificationSound = false;

        // Performance Tab
        SelectedVideoQuality = "Auto";
        HardwareAcceleration = true;
        LowLatencyMode = false;
        BufferDurationMs = 500;
        SelectedRtspTransport = "tcp";
        MaxLatencyMs = 500;

        // Motion Detection Tab
        MotionSensitivity = DropDownItemsFactory.DefaultMotionSensitivity;
        PostMotionDurationSeconds = DropDownItemsFactory.DefaultPostMotionDuration;
        AnalysisFrameRate = 15;
        SelectedAnalysisResolution = DropDownItemsFactory.DefaultMotionAnalysisResolution;
        CooldownSeconds = 5;
        ShowBoundingBoxInGrid = false;
        ShowBoundingBoxInFullScreen = false;
        SelectedBoundingBoxColor = DropDownItemsFactory.DefaultBoundingBoxColor;
        SelectedBoundingBoxThickness = DropDownItemsFactory.DefaultBoundingBoxThickness.ToString(CultureInfo.InvariantCulture);
        SelectedBoundingBoxMinArea = DropDownItemsFactory.DefaultBoundingBoxMinArea.ToString(CultureInfo.InvariantCulture);

        // Capture Tab
        RecordingPath = new DirectoryInfo(ApplicationPaths.DefaultRecordingsPath);
        SelectedRecordingFormat = "mp4";
        SelectedTranscodeVideoCodec = "None";
        EnableRecordingOnMotion = false;
        EnableRecordingOnConnect = false;
        EnableHourlySegmentation = true;
        UseDailySubfolders = true;
        SeparateThumbnailFolder = true;
        SelectedMaxRecordingDuration = DropDownItemsFactory.DefaultMaxRecordingDuration.ToString(CultureInfo.InvariantCulture);
        SelectedThumbnailTileCount = DropDownItemsFactory.DefaultThumbnailTileCount.ToString(CultureInfo.InvariantCulture);

        // Timelapse Tab
        EnableTimelapse = false;
        SelectedTimelapseInterval = DropDownItemsFactory.DefaultTimelapseInterval;

        // Storage Tab
        SelectedCleanupSchedule = DropDownItemsFactory.DefaultMediaCleanupSchedule;
        SelectedRecordingRetention = DropDownItemsFactory.DefaultRecordingRetentionDays.ToString(CultureInfo.InvariantCulture);
        IncludeSnapshotsInCleanup = false;
        SelectedSnapshotRetention = DropDownItemsFactory.DefaultSnapshotRetentionDays.ToString(CultureInfo.InvariantCulture);
        IsDiskSpaceGuardEnabled = true;
        SelectedMinFreeSpace = DropDownItemsFactory.DefaultMinFreeSpaceMb;
        ShowPlaybackFilename = true;
        SelectedPlaybackFilenameColor = "White";
        ShowPlaybackTimestamp = true;
        SelectedPlaybackTimestampColor = "White";

        // Advanced Tab
        EnableDebugLogging = false;
        LogPath = new DirectoryInfo(ApplicationPaths.DefaultLogsPath);
    }

    #endregion

    #region General Settings

    private void SaveGeneralSettings()
    {
        var currentTheme = ThemeManager.Current.DetectTheme(Application.Current);
        var themeBase = currentTheme?.BaseColorScheme ?? "Dark";
        var themeAccent = currentTheme?.ColorScheme ?? "Blue";
        var language = CultureManager.UiCulture.LCID.ToString(CultureInfo.InvariantCulture);

        settingsService.SaveGeneral(new GeneralSettings
        {
            ThemeBase = themeBase,
            ThemeAccent = themeAccent,
            Language = language,
            ConnectCamerasOnStartup = ConnectCamerasOnStartup,
            StartMaximized = StartMaximized,
            StartRibbonCollapsed = StartRibbonCollapsed,
        });
    }

    private void RestoreOriginalThemeAndLanguage()
    {
        try
        {
            ThemeManager.Current.ChangeThemeBaseColor(Application.Current, originalThemeBase);
            ThemeManager.Current.ChangeThemeColorScheme(Application.Current, originalThemeAccent);

            if (int.TryParse(originalLanguage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lcid))
            {
                CultureManager.UiCulture = new CultureInfo(lcid);
            }
        }
        catch
        {
            // Restore failed - ignore
        }
    }

    #endregion

    #region Private Helpers

    private AppSettings BuildApiSettings()
    {
        // Get current theme/accent/language from live state (Atc controls update these directly)
        var currentTheme = ThemeManager.Current.DetectTheme(Application.Current);
        var apiThemeBase = currentTheme?.BaseColorScheme ?? "Dark";
        var apiThemeAccent = currentTheme?.ColorScheme ?? "Blue";
        var apiLanguage = CultureManager.UiCulture.LCID.ToString(CultureInfo.InvariantCulture);

        // Parse enum values from string selections
        _ = Enum.TryParse<AppSettingsThemeBase>(apiThemeBase, out var themeBase);
        _ = Enum.TryParse<AppSettingsOverlayPosition>(SelectedOverlayPosition, out var overlayPosition);
        _ = Enum.TryParse<AppSettingsConnectionTypeIconPlacement>(SelectedConnectionTypeIconPlacement, out var iconPlacement);
        _ = Enum.TryParse<AppSettingsDefaultProtocol>(SelectedDefaultProtocol, out var protocol);
        _ = Enum.TryParse<AppSettingsRtspTransport>(SelectedRtspTransport, out var rtspTransport);
        _ = Enum.TryParse<AppSettingsRecordingFormat>(SelectedRecordingFormat, out var recordingFormat);
        _ = Enum.TryParse<AppSettingsCleanupSchedule>(SelectedCleanupSchedule, out var cleanupSchedule);
        _ = double.TryParse(SelectedOverlayOpacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity);
        _ = int.TryParse(SelectedMaxRecordingDuration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRecordingDuration);
        _ = int.TryParse(SelectedThumbnailTileCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thumbnailTileCount);
        _ = int.TryParse(SelectedRecordingRetention, NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordingRetention);
        _ = int.TryParse(SelectedSnapshotRetention, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snapshotRetention);
        if (!int.TryParse(SelectedMinFreeSpace, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minFreeSpace) || minFreeSpace <= 0)
        {
            minFreeSpace = int.Parse(DropDownItemsFactory.DefaultMinFreeSpaceMb, CultureInfo.InvariantCulture);
        }

        _ = int.TryParse(SelectedBoundingBoxThickness, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boundingBoxThickness);
        _ = int.TryParse(SelectedBoundingBoxMinArea, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boundingBoxMinArea);

        var (analysisWidth, analysisHeight) = DropDownItemsFactory.ParseAnalysisResolution(SelectedAnalysisResolution);

        return new AppSettings(
            ThemeBase: themeBase,
            ThemeAccent: apiThemeAccent,
            Language: apiLanguage,
            ConnectOnStartup: ConnectCamerasOnStartup,
            StartMaximized: StartMaximized,
            ShowOverlayTitle: ShowCameraOverlayTitle,
            ShowOverlayDescription: ShowCameraOverlayDescription,
            ShowOverlayTime: ShowCameraOverlayTime,
            ShowOverlayConnectionStatus: ShowCameraOverlayConnectionStatus,
            ShowOverlayQuickActions: ShowCameraOverlayQuickActions,
            ShowOverlayConnectionTypeIcon: ShowCameraOverlayConnectionTypeIcon,
            ConnectionTypeIconPlacement: iconPlacement,
            OverlayOpacity: opacity,
            OverlayPosition: overlayPosition,
            AllowDragAndDropReorder: AllowDragAndDropReorder,
            AutoSaveLayoutChanges: AutoSaveLayoutChanges,
            SnapshotPath: SnapshotPath?.FullName ?? string.Empty,
            DefaultProtocol: protocol,
            DefaultPort: DefaultPort,
            ConnectionTimeoutSeconds: ConnectionTimeoutSeconds,
            ReconnectDelaySeconds: ReconnectDelaySeconds,
            AutoReconnectOnFailure: AutoReconnectOnFailure,
            ShowNotificationOnDisconnect: ShowNotificationOnDisconnect,
            ShowNotificationOnReconnect: ShowNotificationOnReconnect,
            PlayNotificationSound: PlayNotificationSound,
            VideoQuality: SelectedVideoQuality,
            HardwareAcceleration: HardwareAcceleration,
            LowLatencyMode: LowLatencyMode,
            BufferDurationMs: BufferDurationMs,
            RtspTransport: rtspTransport,
            MaxLatencyMs: MaxLatencyMs,
            MotionSensitivity: MotionSensitivity,
            MinimumChangePercent: originalApiSettings?.MinimumChangePercent ?? 0.5,
            AnalysisFrameRate: AnalysisFrameRate,
            AnalysisWidth: analysisWidth,
            AnalysisHeight: analysisHeight,
            PostMotionDurationSeconds: PostMotionDurationSeconds,
            CooldownSeconds: CooldownSeconds,
            BoundingBoxShowInGrid: ShowBoundingBoxInGrid,
            BoundingBoxShowInFullScreen: ShowBoundingBoxInFullScreen,
            BoundingBoxColor: SelectedBoundingBoxColor,
            BoundingBoxThickness: boundingBoxThickness,
            BoundingBoxMinArea: boundingBoxMinArea,
            BoundingBoxPadding: originalApiSettings?.BoundingBoxPadding ?? 10,
            BoundingBoxSmoothing: originalApiSettings?.BoundingBoxSmoothing ?? 0.3,
            RecordingPath: RecordingPath?.FullName ?? string.Empty,
            RecordingFormat: recordingFormat,
            RecordAsH264: string.Equals(SelectedTranscodeVideoCodec, "H264", StringComparison.OrdinalIgnoreCase),
            EnableRecordingOnMotion: EnableRecordingOnMotion,
            EnableRecordingOnConnect: EnableRecordingOnConnect,
            EnableHourlySegmentation: EnableHourlySegmentation,
            UseDailySubfolders: UseDailySubfolders,
            SeparateThumbnailFolder: SeparateThumbnailFolder,
            MaxRecordingDurationMinutes: maxRecordingDuration > 0 ? maxRecordingDuration : DropDownItemsFactory.DefaultMaxRecordingDuration,
            ThumbnailTileCount: thumbnailTileCount == 1 || thumbnailTileCount == 4 ? thumbnailTileCount : DropDownItemsFactory.DefaultThumbnailTileCount,
            EnableTimelapse: EnableTimelapse,
            TimelapseInterval: SelectedTimelapseInterval,
            CleanupSchedule: cleanupSchedule,
            RecordingRetentionDays: recordingRetention > 0 ? recordingRetention : DropDownItemsFactory.DefaultRecordingRetentionDays,
            CleanupIncludeSnapshots: IncludeSnapshotsInCleanup,
            SnapshotRetentionDays: snapshotRetention > 0 ? snapshotRetention : DropDownItemsFactory.DefaultSnapshotRetentionDays,
            EnableDiskSpaceGuard: IsDiskSpaceGuardEnabled,
            MinFreeSpaceMb: minFreeSpace,
            PlaybackShowFilename: ShowPlaybackFilename,
            PlaybackFilenameColor: SelectedPlaybackFilenameColor,
            PlaybackShowTimestamp: ShowPlaybackTimestamp,
            PlaybackTimestampColor: SelectedPlaybackTimestampColor,
            EnableDebugLogging: EnableDebugLogging,
            LogPath: LogPath?.FullName ?? string.Empty);
    }

    #endregion
}