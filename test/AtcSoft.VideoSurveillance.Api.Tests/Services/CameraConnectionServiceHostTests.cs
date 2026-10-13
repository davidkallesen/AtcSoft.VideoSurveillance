namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Windows gives a USB camera to one program at a time. The connection
/// service therefore keeps the one pipeline such a camera has: watching it
/// live and recording it both use that pipeline, in either order, and it is
/// closed when nothing needs it any more.
/// </summary>
public sealed class CameraConnectionServiceHostTests : IDisposable
{
    private readonly CameraConfiguration camera = new() { Id = Guid.NewGuid() };
    private readonly ICameraStorageService storage = Substitute.For<ICameraStorageService>();
    private readonly IApplicationSettingsService settings = Substitute.For<IApplicationSettingsService>();
    private readonly IRecordingService recordingService = Substitute.For<IRecordingService>();
    private readonly IMediaPipelineFactory pipelineFactory = Substitute.For<IMediaPipelineFactory>();
    private readonly IMediaPipeline pipeline = Substitute.For<IMediaPipeline>();
    private readonly IMotionDetectionService motionDetection = Substitute.For<IMotionDetectionService>();
    private readonly IUsbCameraLifecycleCoordinator usbLifecycle = Substitute.For<IUsbCameraLifecycleCoordinator>();
    private readonly ICameraConnectionStateSink connectionStates = Substitute.For<ICameraConnectionStateSink>();
    private readonly RecordingSettings recordingSettings = new();
    private readonly CameraConnectionService sut;

    public CameraConnectionServiceHostTests()
    {
        camera.Display.DisplayName = "Webcam";
        camera.Connection.Source = CoreCameraSource.Usb;
        camera.Connection.Usb = new UsbConnectionSettings { DeviceId = "usb#1", FriendlyName = "Webcam" };
        storage.GetAllCameras().Returns([camera]);
        storage.GetCameraById(camera.Id).Returns(camera);
        settings.Recording.Returns(recordingSettings);
        settings.MotionDetection.Returns(new MotionDetectionSettings());
        pipelineFactory.Create(camera).Returns(pipeline);

        sut = new CameraConnectionService(
            NullLogger<CameraConnectionService>.Instance,
            new CameraConnectionServiceOptions(),
            Substitute.For<IBackgroundServiceHealthService>(),
            storage,
            settings,
            recordingService,
            pipelineFactory,
            usbLifecycle,
            motionDetection,
            connectionTypeAutoDetector: null,
            connectionStates);
    }

    public void Dispose()
    {
        sut.Dispose();
        pipeline.Dispose();
        usbLifecycle.Dispose();
    }

    [Fact]
    public void A_Usb_Camera_Has_One_Pipeline_For_Everything_And_A_Network_Camera_Has_Not()
    {
        var network = new CameraConfiguration { Id = Guid.NewGuid() };

        sut.Hosts(camera).Should().BeTrue();
        sut.Hosts(network).Should().BeFalse("it can be opened as often as needed");
        sut.Acquire(network, CameraPipelineUse.Viewing).Should().BeNull();
    }

    [Fact]
    public void Watching_A_Usb_Camera_Nobody_Has_Open_Opens_It()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing).Should().BeSameAs(pipeline);

        pipelineFactory.Received(1).Create(camera);
    }

    [Fact]
    public void Recording_A_Usb_Camera_That_Is_Being_Watched_Uses_The_Pipeline_It_Is_Watched_On()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);

        sut.Acquire(camera, CameraPipelineUse.Recording).Should().BeSameAs(pipeline);

        pipelineFactory.Received(1).Create(camera);
    }

    [Fact]
    public void A_Camera_That_Connects_For_A_Viewer_Is_Reported_Connected_And_Not_Recorded()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);

        RaiseConnected();

        connectionStates.Received(1).Report(camera.Id, ConnectionState.Connected);
        recordingService.DidNotReceiveWithAnyArgs().StartRecording(default!, default!);
        motionDetection.DidNotReceiveWithAnyArgs().StartDetection(Guid.Empty, default!, default!);
    }

    [Fact]
    public async Task A_Camera_Someone_Is_Watching_Is_Not_Closed_By_The_Periodic_Check()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        pipeline.DidNotReceive().Dispose();
        pipelineFactory.Received(1).Create(camera);
    }

    [Fact]
    public async Task The_Camera_Is_Closed_When_Its_Last_Viewer_Leaves()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);

        sut.Release(camera.Id, CameraPipelineUse.Viewing);

        await WaitForAsync(() => pipeline.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IDisposable.Dispose)));
        connectionStates.Received(1).Report(camera.Id, ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Closing_The_Live_View_Leaves_A_Running_Recording_Alone()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);
        sut.Acquire(camera, CameraPipelineUse.Recording);
        recordingService.IsRecording(camera.Id).Returns(true);

        sut.Release(camera.Id, CameraPipelineUse.Viewing);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        pipeline.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task Stopping_The_Recording_Leaves_The_Live_View_Alone()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);
        sut.Acquire(camera, CameraPipelineUse.Recording);

        RaiseRecordingStopped();
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        pipeline.DidNotReceive().Dispose();
    }

    [Fact]
    public Task The_Camera_Is_Closed_When_A_Recording_Nobody_Watches_Stops()
    {
        sut.Acquire(camera, CameraPipelineUse.Recording);

        RaiseRecordingStopped();

        return WaitForAsync(() => pipeline.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IDisposable.Dispose)));
    }

    [Fact]
    public async Task A_Recording_That_Goes_On_In_A_New_File_Keeps_The_Camera()
    {
        // A new segment is reported as a stop and a start; the camera is
        // recording throughout.
        sut.Acquire(camera, CameraPipelineUse.Recording);
        recordingService.IsRecording(camera.Id).Returns(true);

        recordingService.RecordingStateChanged += Raise.EventWith(
            new RecordingStateChangedEventArgs(camera.Id, RecordingState.Recording, RecordingState.Idle));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        pipeline.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task A_Recording_Of_Another_Kind_Of_Camera_That_Stops_Is_None_Of_The_Hosts_Business()
    {
        RaiseRecordingStopped();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        connectionStates.DidNotReceiveWithAnyArgs().Report(Guid.Empty, default);
    }

    [Fact]
    public void An_Unplugged_Camera_Cannot_Be_Opened()
    {
        usbLifecycle.IsUnplugged(camera.Id).Returns(true);

        sut.Acquire(camera, CameraPipelineUse.Viewing).Should().BeNull();

        pipelineFactory.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task A_Camera_The_Server_Watches_For_Motion_Is_Watched_Live_On_That_Pipeline_And_Stays_Open_Afterwards()
    {
        recordingSettings.EnableRecordingOnMotion = true;
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        sut.Acquire(camera, CameraPipelineUse.Viewing).Should().BeSameAs(pipeline);
        sut.Release(camera.Id, CameraPipelineUse.Viewing);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        pipelineFactory.Received(1).Create(camera);
        pipeline.DidNotReceive().Dispose();
    }

    [Fact]
    public void A_Camera_The_Server_Keeps_Open_Itself_Is_Not_Opened_Behind_Its_Back()
    {
        // The server opens it on its own schedule (it may be waiting to
        // retry). A second attempt from here could open the camera twice.
        recordingSettings.EnableRecordingOnConnect = true;

        sut.Acquire(camera, CameraPipelineUse.Viewing).Should().BeNull();

        pipelineFactory.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task A_Watched_Camera_That_Went_Silent_Is_Closed()
    {
        sut.Acquire(camera, CameraPipelineUse.Viewing);
        pipeline.LastPacketUtc.Returns(DateTime.UtcNow.AddMinutes(-2));

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => pipeline.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IDisposable.Dispose)));
    }

    [Fact]
    public async Task A_Camera_That_Is_Switched_To_Record_On_Connect_While_Watched_Is_Recorded_On_The_Same_Pipeline()
    {
        // Dropping the pipeline and opening the camera again would leave
        // two openers of a camera that only takes one.
        sut.Acquire(camera, CameraPipelineUse.Viewing);
        pipeline.FramesDecoded.Returns(10);
        recordingService.StartRecording(camera, pipeline).Returns(true);
        recordingSettings.EnableRecordingOnConnect = true;

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        recordingService.Received(1).StartRecording(camera, pipeline);
        pipelineFactory.Received(1).Create(camera);
        pipeline.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task A_Recorded_Camera_Whose_Pipeline_Never_Came_Up_Is_Closed_Before_It_Is_Opened_Again()
    {
        recordingSettings.EnableRecordingOnConnect = true;
        var second = Substitute.For<IMediaPipeline>();
        pipelineFactory.Create(camera).Returns(pipeline, second);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await WaitForAsync(() => pipeline.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IDisposable.Dispose)));
        pipelineFactory.Received(2).Create(camera);
        second.DidNotReceive().Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task A_Recorded_Camera_Whose_Recording_Died_Is_Opened_Again()
    {
        recordingSettings.EnableRecordingOnConnect = true;
        var second = Substitute.For<IMediaPipeline>();
        pipelineFactory.Create(camera).Returns(pipeline, second);
        recordingService.StartRecording(camera, pipeline).Returns(true);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);
        RaiseConnected();
        recordingService.IsRecording(camera.Id).Returns(true);
        recordingService.When(r => r.StopRecording(camera.Id)).Do(_ => recordingService.IsRecording(camera.Id).Returns(false));

        // The pipeline stops producing; the recording service still has the session.
        pipeline.IsRecordingActive.Returns(false);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        recordingService.Received(1).StopRecording(camera.Id);
        pipelineFactory.Received(2).Create(camera);
        second.Dispose();
    }

    private void RaiseConnected()
        => pipeline.ConnectionStateChanged += Raise.EventWith(
            new ConnectionStateChangedEventArgs(ConnectionState.Connecting, ConnectionState.Connected));

    private void RaiseRecordingStopped()
        => recordingService.RecordingStateChanged += Raise.EventWith(
            new RecordingStateChangedEventArgs(camera.Id, RecordingState.Recording, RecordingState.Idle));

    // Pipelines are closed on a thread-pool thread.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The pipeline was not closed.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}