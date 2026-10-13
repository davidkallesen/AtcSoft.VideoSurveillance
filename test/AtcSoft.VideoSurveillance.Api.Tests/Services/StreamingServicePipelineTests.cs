namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// A USB camera the server already has open (it is recording it, or
/// watching it for motion) is streamed to browsers from that open pipeline:
/// Windows would not let a second program open the camera.
/// </summary>
public sealed class StreamingServicePipelineTests : IDisposable
{
    private readonly CameraConfiguration camera = new() { Id = Guid.NewGuid() };
    private readonly ICameraStorageService storage = Substitute.For<ICameraStorageService>();
    private readonly ICameraPipelineLookup pipelines = Substitute.For<ICameraPipelineLookup>();
    private readonly IMediaPipeline pipeline = Substitute.For<IMediaPipeline>();
    private readonly StreamingService sut;

    public StreamingServicePipelineTests()
    {
        camera.Connection.Source = CoreCameraSource.Usb;
        camera.Connection.Usb = new UsbConnectionSettings { DeviceId = "usb#1", FriendlyName = "Webcam" };
        storage.GetCameraById(camera.Id).Returns(camera);
        pipelines.Find(camera.Id).Returns(pipeline);
        pipeline.IsLiveStreamActive.Returns(true);

        sut = new StreamingService(storage, NullLogger<StreamingService>.Instance, pipelines);
    }

    public void Dispose()
    {
        sut.Dispose();
        pipeline.Dispose();
    }

    [Fact]
    public void A_Usb_Camera_The_Server_Has_Open_Is_Streamed_From_That_Pipeline()
    {
        var playlist = sut.StartStream(camera.Id);

        playlist.Should().Be(Path.Combine(sut.HlsOutputRoot, camera.Id.ToString("N"), "stream.m3u8"));
        pipeline.Received(1).StartLiveStream(playlist);
        Directory.Exists(Path.GetDirectoryName(playlist)).Should().BeTrue("the pipeline writes the segments there");
        sut.IsStreaming(camera.Id).Should().BeTrue();
        sut.HasProcessExited(camera.Id).Should().BeFalse();
    }

    [Fact]
    public void A_Second_Viewer_Shares_The_Stream()
    {
        sut.StartStream(camera.Id);
        sut.StartStream(camera.Id);

        pipeline.Received(1).StartLiveStream(Arg.Any<string>());
        sut.GetViewerCount(camera.Id).Should().Be(2);
    }

    [Fact]
    public void The_Stream_Stops_With_Its_Last_Viewer_And_The_Pipeline_Stays_Open()
    {
        var playlist = sut.StartStream(camera.Id);
        sut.StartStream(camera.Id);

        sut.StopStream(camera.Id);
        pipeline.DidNotReceive().StopLiveStream();

        sut.StopStream(camera.Id);

        pipeline.Received(1).StopLiveStream();
        pipeline.DidNotReceive().Dispose();
        sut.IsStreaming(camera.Id).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(playlist)).Should().BeFalse();
    }

    [Fact]
    public void A_Stream_Whose_Pipeline_Was_Closed_Counts_As_Ended()
    {
        // The recording was stopped, or the camera unplugged.
        sut.StartStream(camera.Id);

        pipeline.IsLiveStreamActive.Returns(false);

        sut.HasProcessExited(camera.Id).Should().BeTrue();
    }

    [Fact]
    public void A_Stream_From_The_Servers_Pipeline_Does_Not_Hold_The_Camera_Itself()
    {
        // Whoever wants to open the camera asks whether a stream has it.
        // This one does not: the server's own pipeline has.
        sut.StartStream(camera.Id);

        ((ILiveStreamStatus)sut).IsStreaming(camera.Id).Should().BeFalse();
        sut.CanStreamFromServerPipeline(camera).Should().BeTrue();
    }

    [Fact]
    public void A_Network_Camera_Is_Not_Streamed_From_The_Servers_Pipeline()
    {
        // It can be opened a second time, and the stream of its own carries the sound.
        camera.Connection.Source = CoreCameraSource.Network;

        sut.CanStreamFromServerPipeline(camera).Should().BeFalse();
    }

    [Fact]
    public void A_Usb_Camera_The_Server_Does_Not_Have_Open_Is_Not_Streamed_From_A_Pipeline()
    {
        pipelines.Find(camera.Id).Returns((IMediaPipeline?)null);

        sut.CanStreamFromServerPipeline(camera).Should().BeFalse();
    }
}