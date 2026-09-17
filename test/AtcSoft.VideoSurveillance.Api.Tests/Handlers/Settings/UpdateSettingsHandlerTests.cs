namespace Linksoft.VideoSurveillance.Api.Handlers.Settings;

public class UpdateSettingsHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_AppliesSettingsAndReturnsOk()
    {
        // Arrange
        var settingsService = Substitute.For<IApplicationSettingsService>();
        var general = new GeneralSettings();
        var cameraDisplay = new CameraDisplayAppSettings();
        var connection = new ConnectionAppSettings();
        var performance = new PerformanceSettings();
        var motionDetection = new MotionDetectionSettings();
        var recording = new RecordingSettings();
        var advanced = new AdvancedSettings();
        settingsService.General.Returns(general);
        settingsService.CameraDisplay.Returns(cameraDisplay);
        settingsService.Connection.Returns(connection);
        settingsService.Performance.Returns(performance);
        settingsService.MotionDetection.Returns(motionDetection);
        settingsService.Recording.Returns(recording);
        settingsService.Advanced.Returns(advanced);
        var handler = new UpdateSettingsHandler(settingsService);

        var apiSettings = CreateFullAppSettings(
            themeBase: AppSettingsThemeBase.Light,
            language: "1030",
            enableDebugLogging: true);

        // Act
        var result = await handler.ExecuteAsync(
            new UpdateSettingsParameters(apiSettings),
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<Ok<AppSettings>>();
        settingsService.Received(1).SaveGeneral(general);
        settingsService.Received(1).SaveCameraDisplay(cameraDisplay);
        settingsService.Received(1).SaveConnection(connection);
        settingsService.Received(1).SavePerformance(performance);
        settingsService.Received(1).SaveMotionDetection(motionDetection);
        settingsService.Received(1).SaveRecording(recording);
        settingsService.Received(1).SaveAdvanced(advanced);
    }

    [Fact]
    public async Task ExecuteAsync_SavesAllSections()
    {
        // Arrange
        var settingsService = Substitute.For<IApplicationSettingsService>();
        settingsService.General.Returns(new GeneralSettings());
        settingsService.CameraDisplay.Returns(new CameraDisplayAppSettings());
        settingsService.Connection.Returns(new ConnectionAppSettings());
        settingsService.Performance.Returns(new PerformanceSettings());
        settingsService.MotionDetection.Returns(new MotionDetectionSettings());
        settingsService.Recording.Returns(new RecordingSettings());
        settingsService.Advanced.Returns(new AdvancedSettings());
        var handler = new UpdateSettingsHandler(settingsService);

        var apiSettings = CreateFullAppSettings();

        // Act
        await handler.ExecuteAsync(
            new UpdateSettingsParameters(apiSettings),
            CancellationToken.None);

        // Assert
        settingsService.Received(1).SaveGeneral(Arg.Any<GeneralSettings>());
        settingsService.Received(1).SaveCameraDisplay(Arg.Any<CameraDisplayAppSettings>());
        settingsService.Received(1).SaveConnection(Arg.Any<ConnectionAppSettings>());
        settingsService.Received(1).SavePerformance(Arg.Any<PerformanceSettings>());
        settingsService.Received(1).SaveMotionDetection(Arg.Any<MotionDetectionSettings>());
        settingsService.Received(1).SaveRecording(Arg.Any<RecordingSettings>());
        settingsService.Received(1).SaveAdvanced(Arg.Any<AdvancedSettings>());
    }

    private static AppSettings CreateFullAppSettings(
        AppSettingsThemeBase? themeBase = AppSettingsThemeBase.Dark,
        string language = "1033",
        bool enableDebugLogging = false)
        => new(
            ThemeBase: themeBase,
            ThemeAccent: "Blue",
            Language: language,
            ConnectOnStartup: true,
            StartMaximized: false,
            ShowOverlayTitle: true,
            ShowOverlayDescription: true,
            ShowOverlayTime: false,
            ShowOverlayConnectionStatus: true,
            OverlayOpacity: 0.7,
            OverlayPosition: AppSettingsOverlayPosition.TopLeft,
            AllowDragAndDropReorder: true,
            AutoSaveLayoutChanges: true,
            SnapshotPath: string.Empty,
            DefaultProtocol: AppSettingsDefaultProtocol.Rtsp,
            DefaultPort: 554,
            ConnectionTimeoutSeconds: 10,
            ReconnectDelaySeconds: 5,
            AutoReconnectOnFailure: true,
            ShowNotificationOnDisconnect: true,
            ShowNotificationOnReconnect: true,
            PlayNotificationSound: false,
            VideoQuality: AppSettingsVideoQuality.Auto,
            HardwareAcceleration: true,
            LowLatencyMode: false,
            BufferDurationMs: 500,
            RtspTransport: AppSettingsRtspTransport.Tcp,
            MaxLatencyMs: 1000,
            MotionSensitivity: 30,
            MinimumChangePercent: 0.5,
            AnalysisFrameRate: 5,
            AnalysisWidth: 320,
            AnalysisHeight: 240,
            PostMotionDurationSeconds: 5,
            CooldownSeconds: 3,
            BoundingBoxShowInGrid: false,
            BoundingBoxShowInFullScreen: true,
            BoundingBoxColor: "#FF0000",
            BoundingBoxThickness: 2,
            BoundingBoxMinArea: 500,
            BoundingBoxPadding: 10,
            BoundingBoxSmoothing: 0.3,
            RecordingPath: @"C:\recordings",
            RecordingFormat: AppSettingsRecordingFormat.Mp4,
            EnableRecordingOnMotion: false,
            EnableRecordingOnConnect: false,
            EnableHourlySegmentation: false,
            MaxRecordingDurationMinutes: 60,
            ThumbnailTileCount: 4,
            EnableTimelapse: false,
            TimelapseInterval: "5m",
            CleanupSchedule: AppSettingsCleanupSchedule.Disabled,
            RecordingRetentionDays: 30,
            CleanupIncludeSnapshots: false,
            SnapshotRetentionDays: 30,
            PlaybackShowFilename: true,
            PlaybackFilenameColor: "#FFFFFF",
            PlaybackShowTimestamp: true,
            PlaybackTimestampColor: "#FFFFFF",
            EnableDebugLogging: enableDebugLogging,
            LogPath: @"C:\logs");
}