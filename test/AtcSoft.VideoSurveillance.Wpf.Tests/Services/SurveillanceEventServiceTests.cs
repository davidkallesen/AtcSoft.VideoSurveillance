namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// The client reads GET /events over the real generated endpoint, raises each
/// part of an event as its own event, and goes on after the last event it read
/// when the stream drops.
/// </summary>
public sealed class SurveillanceEventServiceTests
{
    private static readonly Guid Front = Guid.NewGuid();

    [Fact]
    public async Task A_Stream_That_Drops_Is_Read_On_After_The_Last_Event()
    {
        var requests = new List<string?>();
        using var server = new GatewayTestServer(request =>
        {
            var lastEventId = request.Headers.TryGetValues("Last-Event-ID", out var values) ? values.Single() : null;
            requests.Add(lastEventId);

            // The first stream ends after two events (as a dropped connection does), the second goes on after them.
            return lastEventId is null
                ? Stream("event: ping\ndata:\n\n" + ConnectionEvent(1) + ConnectionEvent(2))
                : Stream(ConnectionEvent(3));
        });
        using var sut = CreateSut(server);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new List<ConnectionStateChangedEvent>();
        sut.OnConnectionStateChanged += e =>
        {
            received.Add(e);
            if (received.Count == 3)
            {
                done.Cancel();
            }
        };

        await sut.RunAsync(done.Token);

        received.Select(e => e.NewState).Should().Equal(
            CameraConnectionState.Connecting,
            CameraConnectionState.Connected,
            CameraConnectionState.Disconnected);
        requests.Take(2).Should().Equal(null, "2");
    }

    [Fact]
    public async Task Each_Part_Of_An_Event_Raises_Its_Own_Event_And_An_Unknown_Type_Is_Read_Past()
    {
        using var server = new GatewayTestServer(_ => Stream(
            Event(1, "somethingNew", string.Empty) +
            Event(2, "recordingStateChanged", "\"recording\":{\"cameraId\":\"" + Front + "\",\"newState\":\"recording\",\"oldState\":\"idle\",\"timestamp\":\"2026-10-15T09:00:00Z\"}") +
            Event(3, "motionDetected", "\"motion\":{\"cameraId\":\"" + Front + "\",\"isMotionActive\":true,\"changePercentage\":4.5,\"boundingBoxes\":[],\"analysisWidth\":320,\"analysisHeight\":180,\"timestamp\":\"2026-10-15T09:00:00Z\"}") +
            Event(4, "usbCameraLifecycleChanged", "\"usb\":{\"cameraId\":\"" + Front + "\",\"phase\":\"unplugged\",\"deviceId\":\"usb-1\",\"friendlyName\":\"Desk camera\",\"timestamp\":\"2026-10-15T09:00:00Z\"}")));
        using var sut = CreateSut(server);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new List<string>();
        sut.OnConnectionStateChanged += _ => received.Add("connection");
        sut.OnRecordingStateChanged += e => received.Add($"recording {e.NewState}");
        sut.OnMotionDetected += e => received.Add($"motion {e.IsMotionActive}");
        sut.OnUsbCameraLifecycleChanged += e =>
        {
            received.Add($"usb {e.Phase}");
            done.Cancel();
        };

        await sut.RunAsync(done.Token);

        received.Should().Equal("recording recording", "motion True", "usb unplugged");
    }

    [Fact]
    public async Task A_Server_That_Is_Away_Is_Asked_Again_A_Little_Later()
    {
        var calls = 0;
        using var server = new GatewayTestServer(_ =>
            ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("""{"status":503,"detail":"Starting."}""", System.Text.Encoding.UTF8, "application/problem+json"),
                }
                : Stream(ConnectionEvent(1)));
        var waits = new List<TimeSpan>();
        using var sut = CreateSut(server);
        sut.Delay = (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        };
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        sut.OnConnectionStateChanged += _ => done.Cancel();

        await sut.RunAsync(done.Token);

        calls.Should().Be(2);
        waits.Should().ContainSingle().Which.Should().BePositive();
    }

    [Fact]
    public async Task It_Says_When_It_Starts_And_Stops_Reading()
    {
        using var server = new GatewayTestServer(_ => Stream(ConnectionEvent(1)));
        using var sut = CreateSut(server);
        var changes = new List<bool>();
        sut.ReadingChanged += () => changes.Add(sut.IsReading);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        sut.OnConnectionStateChanged += _ => done.Cancel();

        await sut.RunAsync(done.Token);

        // Open before the first event, closed when it ends.
        changes.Should().Equal(true, false);
    }

    private static SurveillanceEventService CreateSut(GatewayTestServer server)
        => new(
            server.Get<IListEventsEndpoint>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SurveillanceEventService>.Instance,
            new SurveillanceEventServiceOptions())
        {
            Delay = (_, _) => Task.CompletedTask,
        };

    private static HttpResponseMessage Stream(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream"),
        };

    private static string ConnectionEvent(int id)
    {
        var state = id switch
        {
            1 => "connecting",
            2 => "connected",
            _ => "disconnected",
        };

        return Event(
            id,
            "connectionStateChanged",
            "\"connection\":{\"cameraId\":\"" + Front + "\",\"newState\":\"" + state + "\",\"timestamp\":\"2026-10-15T09:00:00Z\"}");
    }

    private static string Event(
        int id,
        string type,
        string part)
    {
        var json =
            "{\"type\":\"" + type + "\",\"cameraId\":\"" + Front + "\",\"timestamp\":\"2026-10-15T09:00:00Z\"" +
            (part.Length > 0 ? "," + part : string.Empty) + "}";

        return $"id: {id}\ndata: {json}\n\n";
    }
}