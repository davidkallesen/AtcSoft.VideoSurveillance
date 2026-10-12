namespace AtcSoft.VideoSurveillance.Api.Handlers.Recordings;

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

    [Fact]
    public async Task ExecuteAsync_FindsRecordingsInEveryLayout()
    {
        // Arrange — one flat recording, one in a day folder with its
        // thumbnail in the separate thumbnails folder
        var root = Path.Combine(Path.GetTempPath(), "test-recordings-" + Guid.NewGuid());
        try
        {
            var camera = new CameraConfiguration { Id = Guid.NewGuid() };
            camera.Display.DisplayName = "Front Door";

            var flat = Touch(Path.Combine(root, "Front Door", "Front Door_20261011_101500_000.mkv"));
            Touch(Path.ChangeExtension(flat, ".png"));
            Touch(Path.Combine(root, "Front Door", "2026-10-12", "Front Door_20261012_101500_000.mkv"));
            Touch(Path.Combine(root, "thumbnails", "Front Door", "2026-10-12", "Front Door_20261012_101500_000.png"));
            Touch(Path.Combine(root, "Front Door", "2026-10-13", "Front Door_20261013_101500_000.mkv"));

            var handler = CreateHandlerFor(root, camera);

            // Act
            var result = await handler.ExecuteAsync(
                new ListRecordingsParameters(CameraId: null),
                CancellationToken.None);

            // Assert
            var recordings = result.Result.Should().BeOfType<Ok<List<Recording>>>().Subject.Value!;
            recordings.Should().HaveCount(3);
            recordings.Should().OnlyContain(r => r.CameraId == camera.Id && r.CameraName == "Front Door");
            recordings.Select(r => r.FilePath).Should().BeEquivalentTo(
                "Front Door/Front Door_20261011_101500_000.mkv",
                "Front Door/2026-10-12/Front Door_20261012_101500_000.mkv",
                "Front Door/2026-10-13/Front Door_20261013_101500_000.mkv");
            recordings.Single(r => r.FilePath.Contains("20261011", StringComparison.Ordinal)).HasThumbnail.Should().BeTrue();
            recordings.Single(r => r.FilePath.Contains("20261012", StringComparison.Ordinal)).HasThumbnail.Should().BeTrue();
            recordings.Single(r => r.FilePath.Contains("20261013", StringComparison.Ordinal)).HasThumbnail.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_TwoCamerasWithTheSameName_DoesNotFail()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "test-recordings-" + Guid.NewGuid());
        try
        {
            var first = new CameraConfiguration { Id = Guid.NewGuid() };
            first.Display.DisplayName = "Gate";
            var second = new CameraConfiguration { Id = Guid.NewGuid() };
            second.Display.DisplayName = "Gate";
            Touch(Path.Combine(root, "Gate", "Gate_20261012_101500_000.mkv"));

            var handler = CreateHandlerFor(root, first, second);

            // Act
            var result = await handler.ExecuteAsync(
                new ListRecordingsParameters(CameraId: null),
                CancellationToken.None);

            // Assert
            var recordings = result.Result.Should().BeOfType<Ok<List<Recording>>>().Subject.Value!;
            recordings.Should().ContainSingle().Which.CameraId.Should().Be(first.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ListRecordingsHandler CreateHandlerFor(
        string recordingPath,
        params CameraConfiguration[] cameras)
    {
        var recordingService = Substitute.For<IRecordingService>();
        recordingService.GetActiveSessions().Returns([]);

        var settingsService = Substitute.For<IApplicationSettingsService>();
        settingsService.Recording.Returns(new RecordingSettings { RecordingPath = recordingPath });

        var cameraStorageService = Substitute.For<ICameraStorageService>();
        cameraStorageService.GetAllCameras().Returns([.. cameras]);

        return new ListRecordingsHandler(recordingService, settingsService, cameraStorageService);
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}