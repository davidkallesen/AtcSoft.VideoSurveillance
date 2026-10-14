namespace AtcSoft.VideoSurveillance.Wpf.Extensions;

/// <summary>
/// A camera sent to the server keeps what the client did not change.
/// </summary>
public sealed class CameraModelMappingExtensionsTests
{
    [Fact]
    public void ToUpdateCameraRequest_Sends_The_Stream_Path()
    {
        var config = Camera().ToCameraConfiguration();

        config.ToUpdateCameraRequest().Path.Should().Be("/stream1");
    }

    [Fact]
    public void ToCreateCameraRequest_Sends_The_Stream_Path()
    {
        var config = Camera().ToCameraConfiguration();

        config.ToCreateCameraRequest().Path.Should().Be("/stream1");
    }

    [Fact]
    public void ToUpdateCameraRequest_Without_A_Password_Leaves_The_Password_Out()
    {
        var config = Camera().ToCameraConfiguration();

        config.ToUpdateCameraRequest().Password.Should().BeNull();
    }

    [Fact]
    public void ToUpdateCameraRequest_Leaves_Out_The_Source_And_The_Usb_Settings()
    {
        var request = Camera().ToCameraConfiguration().ToUpdateCameraRequest();

        request.Source.Should().BeNull();
        request.UsbDeviceId.Should().BeNull();
        request.UsbCaptureAudio.Should().BeNull();
    }

    private static global::VideoSurveillance.Generated.Cameras.Models.Camera Camera()
        => new(
            Id: Guid.NewGuid(),
            DisplayName: "Gate",
            IpAddress: "192.0.2.10",
            Port: 554,
            Path: "/stream1");
}