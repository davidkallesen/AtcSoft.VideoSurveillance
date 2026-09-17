namespace Linksoft.VideoSurveillance.Wpf.Services;

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

    public event Action<ConnectionStateEvent>? OnConnectionStateChanged;

    public event Action<RecordingStateEvent>? OnRecordingStateChanged;

    public event Action<MotionDetectedEvent>? OnMotionDetected;

    public event Action<StreamStartedEvent>? OnStreamStarted;

    public event Action<UsbCameraLifecycleEvent>? OnUsbCameraLifecycleChanged;

    public event Action<string>? OnHubConnectionStateChanged;

    public bool IsConnected
        => hubConnection?.State == HubConnectionState.Connected;

    public string ConnectionState
        => hubConnection?.State.ToString() ?? "Disconnected";

    public string ApiBaseUrl { get; }

    public SurveillanceHubService(string apiBaseUrl)
    {
        ApiBaseUrl = apiBaseUrl;
    }

    public async Task ConnectAsync()
    {
        if (hubConnection is not null)
        {
            return;
        }

        var hubUrl = $"{ApiBaseUrl}/hubs/surveillance";

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
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
            return Task.CompletedTask;
        };

        hubConnection.Closed += _ =>
        {
            OnHubConnectionStateChanged?.Invoke("Disconnected");
            return Task.CompletedTask;
        };

        try
        {
            await hubConnection.StartAsync().ConfigureAwait(false);
            OnHubConnectionStateChanged?.Invoke("Connected");
        }
        catch (HttpRequestException)
        {
            OnHubConnectionStateChanged?.Invoke("Disconnected");
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