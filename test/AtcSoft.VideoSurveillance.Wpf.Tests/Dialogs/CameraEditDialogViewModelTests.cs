namespace AtcSoft.VideoSurveillance.Wpf.Dialogs;

/// <summary>
/// The dialog edits a network camera's fields. An update leaves out what the
/// dialog does not edit, so the server keeps it.
/// </summary>
public sealed class CameraEditDialogViewModelTests
{
    [Fact]
    public void An_Update_Leaves_Out_The_Source_And_The_Usb_Settings()
    {
        var vm = new CameraEditDialogViewModel(Camera());

        var request = vm.BuildUpdateRequest();

        request.Source.Should().BeNull();
        request.UsbDeviceId.Should().BeNull();
        request.UsbFriendlyName.Should().BeNull();
        request.UsbWidth.Should().BeNull();
        request.UsbHeight.Should().BeNull();
        request.UsbFrameRate.Should().BeNull();
        request.UsbPixelFormat.Should().BeNull();
        request.UsbCaptureAudio.Should().BeNull();
        request.UsbAudioDeviceName.Should().BeNull();
    }

    [Fact]
    public void An_Update_Without_A_New_Password_Leaves_The_Password_Out()
    {
        var vm = new CameraEditDialogViewModel(Camera());

        vm.BuildUpdateRequest().Password.Should().BeNull();
    }

    [Fact]
    public void An_Update_Sends_The_Fields_The_Dialog_Edits()
    {
        var vm = new CameraEditDialogViewModel(Camera())
        {
            DisplayName = "Back door",
            Path = "/stream2",
            Password = "secret",
        };

        var request = vm.BuildUpdateRequest();

        request.DisplayName.Should().Be("Back door");
        request.Path.Should().Be("/stream2");
        request.Password.Should().Be("secret");
        request.IpAddress.Should().Be("192.0.2.10");
    }

    private static global::VideoSurveillance.Generated.Cameras.Models.Camera Camera()
        => new(
            Id: Guid.NewGuid(),
            DisplayName: "Gate",
            IpAddress: "192.0.2.10",
            Port: 554,
            Path: "/stream1");
}