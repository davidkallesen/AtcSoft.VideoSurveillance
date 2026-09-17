namespace Linksoft.VideoSurveillance.Api.Handlers.Cameras;

public class UpdateCameraHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_CameraExists_UpdatesAndReturnsOk()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        camera.Display.DisplayName = "Old Name";
        camera.Connection.IpAddress = "10.0.0.1";
        storage.GetCameraById(cameraId).Returns(camera);
        var handler = new UpdateCameraHandler(storage);

        var request = MinimalUpdate("New Name");

        // Act
        var result = await handler.ExecuteAsync(
            new UpdateCameraParameters(cameraId, request),
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<Camera>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.DisplayName.Should().Be("New Name");
        storage.Received(1).AddOrUpdateCamera(camera);
        storage.Received(1).Save();
    }

    [Fact]
    public async Task ExecuteAsync_CameraNotFound_ReturnsNotFound()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var cameraId = Guid.NewGuid();
        storage.GetCameraById(cameraId).Returns((CameraConfiguration?)null);
        var handler = new UpdateCameraHandler(storage);

        var request = MinimalUpdate("Name");

        // Act
        var result = await handler.ExecuteAsync(
            new UpdateCameraParameters(cameraId, request),
            CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task ExecuteAsync_PartialUpdate_OnlyChangesProvidedFields()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var cameraId = Guid.NewGuid();
        var camera = new CameraConfiguration { Id = cameraId };
        camera.Display.DisplayName = "Keep This";
        camera.Connection.IpAddress = "192.168.1.1";
        camera.Connection.Port = 554;
        storage.GetCameraById(cameraId).Returns(camera);
        var handler = new UpdateCameraHandler(storage);

        var request = new UpdateCameraRequest(
            DisplayName: null!,
            Description: "New description",
            Source: null,
            IpAddress: null!,
            Port: 0,
            Protocol: null,
            Path: null!,
            Username: null!,
            Password: null!,
            UsbDeviceId: null!,
            UsbFriendlyName: null!,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: null!,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: null!,
            OverlayPosition: null,
            StreamUseLowLatencyMode: true,
            StreamMaxLatencyMs: 500,
            StreamRtspTransport: null,
            StreamBufferDurationMs: 0);

        // Act
        var result = await handler.ExecuteAsync(
            new UpdateCameraParameters(cameraId, request),
            CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<Camera>>().Subject;
        okResult.Value!.DisplayName.Should().Be("Keep This");
        okResult.Value.Description.Should().Be("New description");
        okResult.Value.IpAddress.Should().Be("192.168.1.1");
    }

    private static UpdateCameraRequest MinimalUpdate(string displayName)
        => new(
            DisplayName: displayName,
            Description: null!,
            Source: null,
            IpAddress: null!,
            Port: 0,
            Protocol: null,
            Path: null!,
            Username: null!,
            Password: null!,
            UsbDeviceId: null!,
            UsbFriendlyName: null!,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: null!,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: null!,
            OverlayPosition: null,
            StreamUseLowLatencyMode: true,
            StreamMaxLatencyMs: 500,
            StreamRtspTransport: null,
            StreamBufferDurationMs: 0);
}