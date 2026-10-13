namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

public sealed class StreamErrorTextTests
{
    [Fact]
    public void The_Reason_From_The_Server_Is_Shown_Without_The_Technical_Wrapping()
    {
        const string fromSignalR =
            "An unexpected error occurred invoking 'StartStream' on the server. " +
            "HubException: Failed to start stream: The server is recording this USB camera. Stop the recording to watch it live.";

        StreamErrorText.For(fromSignalR)
            .Should().Be("The server is recording this USB camera. Stop the recording to watch it live.");
    }

    [Fact]
    public void A_Reason_Without_Wrapping_Is_Shown_As_It_Is()
        => StreamErrorText.For("The connection was closed.").Should().Be("The connection was closed.");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("An unexpected error occurred invoking 'StartStream' on the server.")]
    [InlineData("An unexpected error occurred invoking 'StartStream' on the server. HubException: Failed to start stream: ")]
    public void Without_A_Reason_There_Is_Still_Something_To_Read(
        string? message)
        => StreamErrorText.For(message).Should().Be("The stream could not be started.");
}