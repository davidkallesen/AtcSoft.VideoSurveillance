namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

public sealed class CameraStateTextTests
{
    [Fact]
    public void A_State_From_The_Hub_Is_The_Camera_State()
    {
        // The hub writes the state as the server's C# name.
        CameraStateText.Parse("Connected").Should().Be(CameraConnectionState.Connected);
        CameraStateText.Parse("DeviceUnplugged").Should().Be(CameraConnectionState.DeviceUnplugged);
    }

    [Fact]
    public void A_State_In_The_Wire_Spelling_Is_Read_Too()
        => CameraStateText.Parse("reconnecting").Should().Be(CameraConnectionState.Reconnecting);

    [Fact]
    public void A_State_The_Client_Does_Not_Know_Yet_Is_Kept()
    {
        var state = CameraStateText.Parse("Rebooting");

        state!.Value.IsKnown.Should().BeFalse();
        state.Value.Value.Should().Be("Rebooting");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_State_Is_No_Change(string? newState)
        => CameraStateText.Parse(newState).Should().BeNull();
}