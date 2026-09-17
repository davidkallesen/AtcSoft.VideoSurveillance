namespace Linksoft.VideoSurveillance.Api.Handlers.Cameras;

public class CreateCameraHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsCreatedWithMappedCamera()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var handler = new CreateCameraHandler(storage);
        var request = NetworkRequest(
            displayName: "Test Camera",
            description: "A test camera",
            ipAddress: "10.0.0.1",
            port: 554,
            protocol: ApiCameraProtocol.Rtsp,
            path: "stream1",
            username: "admin",
            password: "pass");
        var parameters = new CreateCameraParameters(request);

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        var created = result.Result.Should().BeOfType<Created<Camera>>().Subject;
        created.Value.Should().NotBeNull();
        created.Value!.DisplayName.Should().Be("Test Camera");
        created.Value.IpAddress.Should().Be("10.0.0.1");
        created.Value.Port.Should().Be(554);
        created.Value.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_CallsStorageAddAndSave()
    {
        // Arrange
        var storage = Substitute.For<ICameraStorageService>();
        var handler = new CreateCameraHandler(storage);
        var request = NetworkRequest(
            displayName: "New Cam",
            ipAddress: "192.168.1.50",
            port: 554,
            protocol: null);
        var parameters = new CreateCameraParameters(request);

        // Act
        await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        storage.Received(1).AddOrUpdateCamera(Arg.Is<CameraConfiguration>(c =>
            c.Display.DisplayName == "New Cam" &&
            c.Connection.IpAddress == "192.168.1.50"));
        storage.Received(1).Save();
    }

    [Fact]
    public async Task ExecuteAsync_UsbRequest_StoresUsbConfiguration()
    {
        var storage = Substitute.For<ICameraStorageService>();
        var handler = new CreateCameraHandler(storage);
        var request = new CreateCameraRequest(
            DisplayName: "USB Cam",
            Description: string.Empty,
            Source: ApiCameraSource.Usb,
            IpAddress: string.Empty,
            Protocol: null,
            Path: string.Empty,
            Username: string.Empty,
            Password: string.Empty,
            UsbDeviceId: @"\\?\usb#vid_046d&pid_085e",
            UsbFriendlyName: "Logitech BRIO",
            UsbWidth: 1920,
            UsbHeight: 1080,
            UsbFrameRate: 30,
            UsbPixelFormat: "nv12",
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: null,
            StreamUseLowLatencyMode: true,
            StreamMaxLatencyMs: 500,
            StreamRtspTransport: null,
            StreamBufferDurationMs: 0,
            Port: 0);
        var parameters = new CreateCameraParameters(request);

        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        result.Result.Should().BeOfType<Created<Camera>>();
        storage.Received(1).AddOrUpdateCamera(Arg.Is<CameraConfiguration>(c =>
            c.Connection.Source == CoreCameraSource.Usb &&
            c.Connection.Usb != null &&
            c.Connection.Usb.DeviceId == @"\\?\usb#vid_046d&pid_085e"));
    }

    private static CreateCameraRequest NetworkRequest(
        string displayName,
        string description = "",
        string ipAddress = "",
        int port = 554,
        ApiCameraProtocol? protocol = ApiCameraProtocol.Rtsp,
        string path = "",
        string username = "",
        string password = "")
        => new(
            DisplayName: displayName,
            Description: description,
            Source: ApiCameraSource.Network,
            IpAddress: ipAddress,
            Protocol: protocol,
            Path: path,
            Username: username,
            Password: password,
            UsbDeviceId: string.Empty,
            UsbFriendlyName: string.Empty,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: string.Empty,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: null,
            StreamUseLowLatencyMode: true,
            StreamMaxLatencyMs: 500,
            StreamRtspTransport: null,
            StreamBufferDurationMs: 0,
            Port: port);
}