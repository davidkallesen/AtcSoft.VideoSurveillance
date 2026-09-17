namespace Linksoft.VideoSurveillance.Api.Mapping;

public class SettingsMappingExtensionsTests
{
    [Fact]
    public void ToApiModel_CombinesAllSettingsSources()
    {
        // Arrange
        var general = new GeneralSettings
        {
            ThemeBase = "Dark",
            ThemeAccent = "Blue",
            Language = "1033",
            ConnectCamerasOnStartup = true,
            StartMaximized = false,
        };
        var cameraDisplay = new CameraDisplayAppSettings
        {
            ShowOverlayTitle = true,
            SnapshotPath = @"C:\snapshots",
        };
        var connection = new ConnectionAppSettings { DefaultPort = 554 };
        var performance = new PerformanceSettings { VideoQuality = "Auto" };
        var motionDetection = new MotionDetectionSettings { Sensitivity = 30 };
        var recording = new RecordingSettings
        {
            RecordingPath = @"C:\recordings",
            RecordingFormat = "mp4",
        };
        var advanced = new AdvancedSettings
        {
            EnableDebugLogging = true,
            LogPath = @"C:\logs",
        };

        // Act
        var api = SettingsMappingExtensions.ToApiModel(
            general,
            cameraDisplay,
            connection,
            performance,
            motionDetection,
            recording,
            advanced);

        // Assert
        api.ThemeBase.Should().Be(AppSettingsThemeBase.Dark);
        api.ThemeAccent.Should().Be("Blue");
        api.Language.Should().Be("1033");
        api.ConnectOnStartup.Should().BeTrue();
        api.ShowOverlayTitle.Should().BeTrue();
        api.SnapshotPath.Should().Be(@"C:\snapshots");
        api.DefaultPort.Should().Be(554);
        api.VideoQuality.Should().Be(AppSettingsVideoQuality.Auto);
        api.MotionSensitivity.Should().Be(30);
        api.RecordingPath.Should().Be(@"C:\recordings");
        api.RecordingFormat.Should().Be(AppSettingsRecordingFormat.Mp4);
        api.EnableDebugLogging.Should().BeTrue();
        api.LogPath.Should().Be(@"C:\logs");
    }

    [Fact]
    public void ToApiModel_LightTheme_MapsCorrectly()
    {
        // Arrange
        var general = new GeneralSettings { ThemeBase = "Light" };

        // Act
        var api = SettingsMappingExtensions.ToApiModel(
            general,
            new CameraDisplayAppSettings(),
            new ConnectionAppSettings(),
            new PerformanceSettings(),
            new MotionDetectionSettings(),
            new RecordingSettings(),
            new AdvancedSettings());

        // Assert
        api.ThemeBase.Should().Be(AppSettingsThemeBase.Light);
    }

    [Theory]
    [InlineData("mp4", AppSettingsRecordingFormat.Mp4)]
    [InlineData("mkv", AppSettingsRecordingFormat.Mkv)]
    [InlineData("avi", AppSettingsRecordingFormat.Avi)]
    [InlineData("MKV", AppSettingsRecordingFormat.Mkv)]
    public void ToApiModel_RecordingFormat_ParsesCorrectly(
        string format,
        AppSettingsRecordingFormat expected)
    {
        // Arrange
        var recording = new RecordingSettings { RecordingFormat = format };

        // Act
        var api = SettingsMappingExtensions.ToApiModel(
            new GeneralSettings(),
            new CameraDisplayAppSettings(),
            new ConnectionAppSettings(),
            new PerformanceSettings(),
            new MotionDetectionSettings(),
            recording,
            new AdvancedSettings());

        // Assert
        api.RecordingFormat.Should().Be(expected);
    }

    [Fact]
    public void ApplyToCore_AppliesAllFields()
    {
        // Arrange
        var general = new GeneralSettings();
        var cameraDisplay = new CameraDisplayAppSettings();
        var connection = new ConnectionAppSettings();
        var performance = new PerformanceSettings();
        var motionDetection = new MotionDetectionSettings();
        var recording = new RecordingSettings();
        var advanced = new AdvancedSettings();

        var api = CreateFullAppSettings(
            themeBase: AppSettingsThemeBase.Light,
            language: "1030",
            connectOnStartup: false,
            recordingPath: @"D:\rec",
            recordingFormat: AppSettingsRecordingFormat.Mkv,
            enableDebugLogging: true,
            logPath: @"D:\logs");

        // Act
        api.ApplyToCore(general, cameraDisplay, connection, performance, motionDetection, recording, advanced);

        // Assert
        general.ThemeBase.Should().Be("Light");
        general.Language.Should().Be("1030");
        general.ConnectCamerasOnStartup.Should().BeFalse();
        recording.RecordingPath.Should().Be(@"D:\rec");
        recording.RecordingFormat.Should().Be("mkv");
        advanced.EnableDebugLogging.Should().BeTrue();
        advanced.LogPath.Should().Be(@"D:\logs");
    }

    [Fact]
    public void ApplyToCore_NullTheme_DoesNotChangeTheme()
    {
        // Arrange
        var general = new GeneralSettings { ThemeBase = "Dark" };

        var api = CreateFullAppSettings(themeBase: null);

        // Act
        api.ApplyToCore(
            general,
            new CameraDisplayAppSettings(),
            new ConnectionAppSettings(),
            new PerformanceSettings(),
            new MotionDetectionSettings(),
            new RecordingSettings(),
            new AdvancedSettings());

        // Assert
        general.ThemeBase.Should().Be("Dark");
    }

    private static AppSettings CreateFullAppSettings(
        AppSettingsThemeBase? themeBase = AppSettingsThemeBase.Dark,
        string language = "1033",
        bool connectOnStartup = true,
        string recordingPath = "",
        AppSettingsRecordingFormat? recordingFormat = AppSettingsRecordingFormat.Mp4,
        bool enableDebugLogging = false,
        string logPath = "")
        => new(
            ThemeBase: themeBase,
            ThemeAccent: "Blue",
            Language: language,
            ConnectOnStartup: connectOnStartup,
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
            RecordingPath: recordingPath,
            RecordingFormat: recordingFormat,
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
            LogPath: logPath);
}