namespace Linksoft.VideoSurveillance.Api.Handlers.Cameras;

public class StartRecordingHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_CameraNotFound_ReturnsNotFound()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var recordingService = Substitute.For<IRecordingService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var cameraId = Guid.NewGuid();
        storage.GetCameraById(cameraId).Returns((CameraConfiguration?)null);
        var handler = new StartRecordingHandler(storage, recordingService, pipelineFactory);

        // Act
        var result = await handler.ExecuteAsync(
            new StartRecordingParameters(cameraId),
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyRecording_ReturnsConflict()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var recordingService = Substitute.For<IRecordingService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        storage.GetCameraById(cameraId).Returns(camera);
        recordingService.IsRecording(cameraId).Returns(true);
        var handler = new StartRecordingHandler(storage, recordingService, pipelineFactory);

        // Act
        var result = await handler.ExecuteAsync(
            new StartRecordingParameters(cameraId),
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<Conflict<string>>();
    }

    [Fact]
    public async Task ExecuteAsync_CameraExistsNotRecording_CreatesPipelineAndStartsRecording()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var recordingService = Substitute.For<IRecordingService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var pipeline = Substitute.For<IMediaPipeline>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        storage.GetCameraById(cameraId).Returns(camera);
        recordingService.IsRecording(cameraId).Returns(false);
        pipelineFactory.Create(camera).Returns(pipeline);
        recordingService.StartRecording(camera, pipeline).Returns(true);
        recordingService.GetRecordingState(cameraId).Returns(RecordingState.Recording);
        recordingService.GetSession(cameraId).Returns((RecordingSession?)null);
        var handler = new StartRecordingHandler(storage, recordingService, pipelineFactory);

        // Act — kick off ExecuteAsync; it will subscribe to
        // pipeline.ConnectionStateChanged and yield at the await on the
        // TaskCompletionSource. We then raise the Connected event so it
        // can complete and proceed to call StartRecording.
        var executeTask = handler.ExecuteAsync(
            new StartRecordingParameters(cameraId),
            CancellationToken.None);

        pipeline.ConnectionStateChanged += Raise.EventWith(
            new Linksoft.VideoSurveillance.Events.ConnectionStateChangedEventArgs(
                Linksoft.VideoSurveillance.Enums.ConnectionState.Connecting,
                Linksoft.VideoSurveillance.Enums.ConnectionState.Connected));

        var result = await executeTask;

        // Assert
        pipelineFactory.Received(1).Create(camera);
        recordingService.Received(1).StartRecording(camera, pipeline);
        var okResult = result.Result.Should().BeOfType<Ok<RecordingStatus>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.CameraId.Should().Be(cameraId);
        okResult.Value.State.Should().Be(RecordingStatusState.Recording);
    }
}