namespace Linksoft.VideoSurveillance.Api.Handlers.Cameras;

public class CaptureSnapshotHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_CameraNotFound_ReturnsNotFound()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var settingsService = Substitute.For<IApplicationSettingsService>();
        var cameraId = Guid.NewGuid();
        storage.GetCameraById(cameraId).Returns((CameraConfiguration?)null);
        var handler = new CaptureSnapshotHandler(storage, pipelineFactory, settingsService);

        // Act
        var result = await handler.ExecuteAsync(
            new CaptureSnapshotParameters(cameraId),
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task ExecuteAsync_CaptureReturnsNull_ReturnsNotFound()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var settingsService = Substitute.For<IApplicationSettingsService>();
        var pipeline = Substitute.For<IMediaPipeline>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        storage.GetCameraById(cameraId).Returns(camera);
        pipelineFactory.Create(camera).Returns(pipeline);
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var handler = new CaptureSnapshotHandler(storage, pipelineFactory, settingsService);

        // Act — kick off ExecuteAsync, then raise Connected so the handler
        // proceeds past the connection wait and into CaptureFrameAsync.
        var executeTask = handler.ExecuteAsync(
            new CaptureSnapshotParameters(cameraId),
            CancellationToken.None);

        pipeline.ConnectionStateChanged += Raise.EventWith(
            new Linksoft.VideoSurveillance.Events.ConnectionStateChangedEventArgs(
                Linksoft.VideoSurveillance.Enums.ConnectionState.Connecting,
                Linksoft.VideoSurveillance.Enums.ConnectionState.Connected));

        var result = await executeTask;

        // Assert
        pipelineFactory.Received(1).Create(camera);
        await pipeline.Received(1).CaptureFrameAsync(Arg.Any<CancellationToken>());
        result.Result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task ExecuteAsync_PipelineEntersErrorState_ReturnsNotFoundWithoutCalling_CaptureFrameAsync()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var pipelineFactory = Substitute.For<IMediaPipelineFactory>();
        var settingsService = Substitute.For<IApplicationSettingsService>();
        var pipeline = Substitute.For<IMediaPipeline>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        storage.GetCameraById(cameraId).Returns(camera);
        pipelineFactory.Create(camera).Returns(pipeline);
        var handler = new CaptureSnapshotHandler(storage, pipelineFactory, settingsService);

        // Act — fire an Error transition; the connection waiter should
        // complete with `false` and the handler should short-circuit before
        // ever calling CaptureFrameAsync.
        var executeTask = handler.ExecuteAsync(
            new CaptureSnapshotParameters(cameraId),
            CancellationToken.None);

        pipeline.ConnectionStateChanged += Raise.EventWith(
            new Linksoft.VideoSurveillance.Events.ConnectionStateChangedEventArgs(
                Linksoft.VideoSurveillance.Enums.ConnectionState.Connecting,
                Linksoft.VideoSurveillance.Enums.ConnectionState.Error));

        var result = await executeTask;

        // Assert
        await pipeline.DidNotReceive().CaptureFrameAsync(Arg.Any<CancellationToken>());
        result.Result.Should().BeOfType<NotFound<string>>();
    }
}