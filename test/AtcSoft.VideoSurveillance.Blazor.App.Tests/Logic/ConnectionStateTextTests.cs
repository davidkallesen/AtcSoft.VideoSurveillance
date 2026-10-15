namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

public sealed class ConnectionStateTextTests
{
    [Theory]
    [InlineData(CameraConnectionState.Names.Connected, "Connected")]
    [InlineData(CameraConnectionState.Names.Connecting, "Connecting...")]
    [InlineData(CameraConnectionState.Names.DeviceUnplugged, "Device unplugged")]
    [InlineData("rebooting", "Rebooting")]
    public void A_Camera_State_Is_Shown_In_Words(
        string state,
        string expected)
        => ConnectionStateText.ForCamera(new CameraConnectionState(state)).Should().Be(expected);

    [Fact]
    public void No_Camera_State_Is_Unknown()
        => ConnectionStateText.ForCamera(state: null).Should().Be("Unknown");

    [Theory]
    [InlineData("Connected", "Connected")]
    [InlineData("Reconnecting", "Reconnecting...")]
    [InlineData("Disconnected", "Disconnected")]
    [InlineData("Unknown", "Unknown")]
    public void The_Hub_State_Is_Shown_In_Words(
        string state,
        string expected)
        => ConnectionStateText.ForHub(state).Should().Be(expected);

    [Fact]
    public void A_State_Is_In_The_Chosen_Language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");

            ConnectionStateText.ForCamera(CameraConnectionState.Disconnected).Should().Be("Afbrudt");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}