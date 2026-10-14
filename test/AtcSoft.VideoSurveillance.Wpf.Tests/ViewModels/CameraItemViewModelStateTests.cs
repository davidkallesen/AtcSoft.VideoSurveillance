namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// The camera list shows the state as the hub writes it (<c>Connected</c>),
/// so a state from the camera list and one from a hub event look the same.
/// </summary>
public sealed class CameraItemViewModelStateTests
{
    [Theory]
    [InlineData(global::VideoSurveillance.Generated.Models.CameraConnectionState.Names.Connected, "Connected")]
    [InlineData(global::VideoSurveillance.Generated.Models.CameraConnectionState.Names.DeviceUnplugged, "DeviceUnplugged")]
    [InlineData("rebooting", "Rebooting")]
    public void The_State_Is_Written_As_The_Hub_Writes_It(
        string state,
        string expected)
    {
        var camera = TestCameras.Network() with
        {
            ConnectionState = new global::VideoSurveillance.Generated.Models.CameraConnectionState(state),
        };

        CameraItemViewModel.FromCamera(camera).ConnectionState.Should().Be(expected);
    }

    [Fact]
    public void A_Camera_Without_A_State_Is_Disconnected()
        => CameraItemViewModel.FromCamera(TestCameras.Network()).ConnectionState.Should().Be("Disconnected");
}