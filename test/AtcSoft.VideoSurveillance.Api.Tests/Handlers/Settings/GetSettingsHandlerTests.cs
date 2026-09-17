namespace Linksoft.VideoSurveillance.Api.Handlers.Settings;

public class GetSettingsHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsMappedSettings()
    {
        // Arrange
        var settingsService = Substitute.For<IApplicationSettingsService>();
        settingsService.General.Returns(new GeneralSettings
        {
            ThemeBase = "Dark",
            Language = "1033",
            ConnectCamerasOnStartup = true,
        });
        settingsService.CameraDisplay.Returns(new CameraDisplayAppSettings
        {
            SnapshotPath = @"C:\snapshots",
        });
        settingsService.Connection.Returns(new ConnectionAppSettings());
        settingsService.Performance.Returns(new PerformanceSettings());
        settingsService.MotionDetection.Returns(new MotionDetectionSettings());
        settingsService.Recording.Returns(new RecordingSettings
        {
            RecordingPath = @"C:\recordings",
            RecordingFormat = "mp4",
        });
        settingsService.Advanced.Returns(new AdvancedSettings
        {
            EnableDebugLogging = false,
            LogPath = @"C:\logs",
        });
        var handler = new GetSettingsHandler(settingsService);

        // Act
        var result = await handler.ExecuteAsync(CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<AppSettings>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.ThemeBase.Should().Be(AppSettingsThemeBase.Dark);
        okResult.Value.Language.Should().Be("1033");
        okResult.Value.ConnectOnStartup.Should().BeTrue();
        okResult.Value.RecordingPath.Should().Be(@"C:\recordings");
        okResult.Value.RecordingFormat.Should().Be(AppSettingsRecordingFormat.Mp4);
        okResult.Value.SnapshotPath.Should().Be(@"C:\snapshots");
        okResult.Value.EnableDebugLogging.Should().BeFalse();
        okResult.Value.LogPath.Should().Be(@"C:\logs");
    }
}