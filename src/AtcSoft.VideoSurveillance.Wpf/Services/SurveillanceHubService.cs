namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Service for connecting to the SignalR surveillance hub.
/// Provides real-time camera events: connection state, recording, motion, and streaming.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1034:Nested types should not be visible",
    Justification = "Event payload records are local to this service.")]
[SuppressMessage(
    "Design",
    "CA1054:URI parameters should not be strings",
    Justification = "Hub URLs are configured strings, not constructed Uri values; matches SignalR HubConnectionBuilder API.")]
[SuppressMessage(
    "Design",
    "CA1056:URI properties should not be strings",
    Justification = "Hub URLs are configured strings, not constructed Uri values; matches SignalR HubConnectionBuilder API.")]
[SuppressMessage(
    "Design",
    "CA1003:Use generic event handler instances",
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
[SuppressMessage(
    "Design",
    "MA0046:Use EventHandler<T> to declare events",
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
public sealed class SurveillanceHubService : IAsyncDisposable
{
    private HubConnection? hubConnection;
    private Func<Task>? stopConnecting;

    public event Action<ConnectionStateEvent>? OnConnectionStateChanged;

    public event Action<RecordingStateEvent>? OnRecordingStateChanged;

    public event Action<MotionDetectedEvent>? OnMotionDetected;

    public event Action<StreamStartedEvent>? OnStreamStarted;

    public event Action<UsbCameraLifecycleEvent>? OnUsbCameraLifecycleChanged;

    public event Action<string>? OnHubConnectionStateChanged;

    /// <summary>
    /// Raised when the connection is there again after it was lost, or
    /// after the server was not there at the start. The server has
    /// forgotten this client meanwhile (its streams were stopped), and what
    /// the client shows may be out of date: whoever shows it loads it again.
    /// </summary>
    public event Action? OnReconnected;

    public bool IsConnected
        => hubConnection?.State == HubConnectionState.Connected;

    public string ConnectionState
        => hubConnection?.State.ToString() ?? "Disconnected";

    public string ApiBaseUrl { get; }

    public SurveillanceHubService(string apiBaseUrl)
    {
        ApiBaseUrl = apiBaseUrl;
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The token source is disposed by stopConnecting when the client disconnects.")]
    public async Task ConnectAsync()
    {
        if (hubConnection is not null)
        {
            return;
        }

        var hubUrl = $"{ApiBaseUrl}/hubs/surveillance";

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new HubReconnectPolicy())
            .Build();

        hubConnection.On<ConnectionStateEvent>("ConnectionStateChanged", e =>
        {
            OnConnectionStateChanged?.Invoke(e);
        });

        hubConnection.On<RecordingStateEvent>("RecordingStateChanged", e =>
        {
            OnRecordingStateChanged?.Invoke(e);
        });

        hubConnection.On<MotionDetectedEvent>("MotionDetected", e =>
        {
            OnMotionDetected?.Invoke(e);
        });

        hubConnection.On<StreamStartedEvent>("StreamStarted", e =>
        {
            OnStreamStarted?.Invoke(e);
        });

        hubConnection.On<UsbCameraLifecycleEvent>("UsbCameraLifecycleChanged", e =>
        {
            OnUsbCameraLifecycleChanged?.Invoke(e);
        });

        hubConnection.Reconnecting += _ =>
        {
            OnHubConnectionStateChanged?.Invoke("Reconnecting...");
            return Task.CompletedTask;
        };

        hubConnection.Reconnected += _ =>
        {
            OnHubConnectionStateChanged?.Invoke("Connected");
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };

        hubConnection.Closed += _ =>
        {
            OnHubConnectionStateChanged?.Invoke("Disconnected");
            return Task.CompletedTask;
        };

        // Ends the trying below when the client disconnects on purpose.
        var closing = new CancellationTokenSource();
        stopConnecting = async () =>
        {
            await closing.CancelAsync().ConfigureAwait(false);
            closing.Dispose();
        };

        var connection = hubConnection;
        var cancellationToken = closing.Token;

        if (await TryStartAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            OnHubConnectionStateChanged?.Invoke("Connected");
            return;
        }

        // The server is not there (yet). SignalR only reconnects a
        // connection that was established once, so this one is tried here
        // until it is.
        OnHubConnectionStateChanged?.Invoke("Reconnecting...");
        _ = KeepConnectingAsync(connection, cancellationToken);
    }

    private static async Task<bool> TryStartAsync(
        HubConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException or System.Net.Sockets.SocketException or System.Net.WebSockets.WebSocketException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task KeepConnectingAsync(
        HubConnection connection,
        CancellationToken cancellationToken)
    {
        var connected = await HubReconnectPolicy
            .KeepTryingAsync(
                token => TryStartAsync(connection, token),
                Task.Delay,
                cancellationToken)
            .ConfigureAwait(false);

        if (connected)
        {
            OnHubConnectionStateChanged?.Invoke("Connected");
            OnReconnected?.Invoke();
        }
    }

    public async Task StartRecordingAsync(Guid cameraId)
    {
        if (hubConnection?.State == HubConnectionState.Connected)
        {
            await hubConnection
                .InvokeAsync("StartRecording", cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task StopRecordingAsync(Guid cameraId)
    {
        if (hubConnection?.State == HubConnectionState.Connected)
        {
            await hubConnection
                .InvokeAsync("StopRecording", cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task StartStreamAsync(Guid cameraId)
    {
        if (hubConnection?.State == HubConnectionState.Connected)
        {
            await hubConnection
                .InvokeAsync("StartStream", cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task StopStreamAsync(Guid cameraId)
    {
        if (hubConnection?.State == HubConnectionState.Connected)
        {
            await hubConnection
                .InvokeAsync("StopStream", cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task StreamHeartbeatAsync(Guid cameraId)
    {
        if (hubConnection?.State == HubConnectionState.Connected)
        {
            await hubConnection
                .InvokeAsync("StreamHeartbeat", cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task DisconnectAsync()
    {
        if (hubConnection is not null)
        {
            if (stopConnecting is not null)
            {
                await stopConnecting().ConfigureAwait(false);
                stopConnecting = null;
            }

            await hubConnection.StopAsync().ConfigureAwait(false);
            await hubConnection.DisposeAsync().ConfigureAwait(false);
            hubConnection = null;
            OnHubConnectionStateChanged?.Invoke("Disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    public sealed record ConnectionStateEvent(Guid CameraId, string NewState, DateTimeOffset Timestamp);

    public sealed record RecordingStateEvent(Guid CameraId, string NewState, string OldState, string? FilePath);

    public sealed record MotionDetectedEvent(
        Guid CameraId,
        bool IsMotionActive,
        double ChangePercentage,
        IReadOnlyList<MotionBoundingBox> BoundingBoxes,
        int AnalysisWidth,
        int AnalysisHeight);

    public sealed record MotionBoundingBox(double X, double Y, double Width, double Height);

    public sealed record StreamStartedEvent(Guid CameraId, string PlaylistUrl);

    /// <summary>
    /// Server-broadcast notification that a USB camera attached to the
    /// API host transitioned between <c>Unplugged</c> and
    /// <c>Replugged</c>. <see cref="Phase"/> matches the
    /// <c>UsbCameraLifecyclePhase</c> enum (<c>"Unplugged"</c> /
    /// <c>"Replugged"</c>) — string-typed so the contract survives
    /// deliberate enum extensions without forcing client redeploys.
    /// </summary>
    public sealed record UsbCameraLifecycleEvent(
        Guid CameraId,
        string Phase,
        string DeviceId,
        string FriendlyName,
        DateTimeOffset Timestamp);
}