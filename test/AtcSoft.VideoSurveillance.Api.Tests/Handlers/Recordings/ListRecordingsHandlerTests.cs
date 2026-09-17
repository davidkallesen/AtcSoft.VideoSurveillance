namespace Linksoft.VideoSurveillance.Api.Handlers.Recordings;

public class ListRecordingsHandlerTests
{
    private static ListRecordingsHandler CreateHandler(
        IRecordingService? recordingService = null,
        IApplicationSettingsService? settingsService = null,
        ICameraStorageService? cameraStorageService = null)
    {
        recordingService ??= Substitute.For<IRecordingService>();
        settingsService ??= CreateDefaultSettingsService();
        cameraStorageService ??= Substitute.For<ICameraStorageService>();
        cameraStorageService.GetAllCameras().Returns([]);

        return new ListRecordingsHandler(recordingService, settingsService, cameraStorageService);
    }

    private static IApplicationSettingsService CreateDefaultSettingsService()
    {
        var settingsService = Substitute.For<IApplicationSettingsService>();
        settingsService.Recording.Returns(new RecordingSettings
        {
            RecordingPath = Path.Combine(Path.GetTempPath(), "test-recordings-" + Guid.NewGuid()),
        });
        return settingsService;
    }

    [Fact]
    public async Task ExecuteAsync_NoActiveSessions_ReturnsEmptyList()
    {
        // Arrange
        var recordingService = Substitute.For<IRecordingService>();
        recordingService.GetActiveSessions().Returns([]);
        var handler = CreateHandler(recordingService: recordingService);

        // Act
        var result = await handler.ExecuteAsync(
            new ListRecordingsParameters(CameraId: null),
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<List<Recording>>>().Subject;
        okResult.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveSessions_ReturnsMappedRecordings()
    {
        // Arrange
        var recordingService = Substitute.For<IRecordingService>();
        var cameraId = Guid.NewGuid();
        var session = new RecordingSession(cameraId, "TestCam", @"C:\recordings\cam1.mp4", isManualRecording: true);
        recordingService.GetActiveSessions().Returns([session]);
        var handler = CreateHandler(recordingService: recordingService);

        // Act
        var result = await handler.ExecuteAsync(
            new ListRecordingsParameters(CameraId: null),
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<List<Recording>>>().Subject;
        okResult.Value.Should().HaveCount(1);
        okResult.Value![0].CameraId.Should().Be(cameraId);
        okResult.Value[0].FilePath.Should().Be(@"C:\recordings\cam1.mp4");
    }

    [Fact]
    public async Task ExecuteAsync_FilterByCameraId_ReturnsOnlyMatchingSessions()
    {
        // Arrange
        var recordingService = Substitute.For<IRecordingService>();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var session1 = new RecordingSession(targetId, "Target", @"C:\rec\cam1.mp4", isManualRecording: true);
        var session2 = new RecordingSession(otherId, "Other", @"C:\rec\cam2.mp4", isManualRecording: true);
        recordingService.GetActiveSessions().Returns([session1, session2]);
        var handler = CreateHandler(recordingService: recordingService);

        // Act
        var result = await handler.ExecuteAsync(
            new ListRecordingsParameters(CameraId: targetId),
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<List<Recording>>>().Subject;
        okResult.Value.Should().HaveCount(1);
        okResult.Value![0].CameraId.Should().Be(targetId);
    }
}