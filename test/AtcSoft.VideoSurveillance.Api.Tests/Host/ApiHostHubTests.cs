namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// The hub speaks as the specification declares it (<c>x-signalr-hubs</c>):
/// the names on the wire, the arguments in their order, and the values as
/// the REST API writes them.
/// </summary>
[Collection(ApiHostTestGroup.Name)]
public sealed class ApiHostHubTests(ApiHostFixture host)
{
    [Fact]
    public async Task A_Swap_Request_Reaches_Every_Client_With_The_Direction_As_The_Specification_Writes_It()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = host.CreateHubConnection();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("CameraSwapRequested", e => received.TrySetResult(e));
        await hub.StartAsync(cancellationToken);
        var cameraId = Guid.NewGuid();

        // Two arguments on the wire: the camera, then the direction.
        await hub.InvokeAsync("SwapCameras", cameraId, "right", cancellationToken);
        var swap = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        swap.GetProperty("cameraId").GetGuid().Should().Be(cameraId);
        swap.GetProperty("direction").GetString().Should().Be("right");
    }

    [Fact]
    public async Task Starting_A_Recording_Of_A_Camera_The_Server_Does_Not_Know_Is_An_Error_Event_With_The_Reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hub = host.CreateHubConnection();
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string>("Error", e => error.TrySetResult(e));
        await hub.StartAsync(cancellationToken);
        var cameraId = Guid.NewGuid();

        await hub.InvokeAsync("StartRecording", cameraId, cancellationToken);
        var reason = await error.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        reason.Should().Be($"Camera {cameraId} not found.");
    }
}