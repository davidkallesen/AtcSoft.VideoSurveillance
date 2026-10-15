namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Service for connecting to the SignalR surveillance hub.
/// Provides real-time camera events: connection state, recording, motion, and streaming.
/// The events, the methods and what they carry are the generated
/// <see cref="SurveillanceHubClient"/>, from the hub in the specification.
/// </summary>
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
    Justification = "Action<T> events are intentional for terse Blazor consumption.")]
[SuppressMessage(
    "Design",
    "MA0046:Use EventHandler<T> to declare events",
    Justification = "Action<T> events are intentional for terse Blazor consumption.")]
public sealed class SurveillanceHubService : IAsyncDisposable
{
    private HubConnection? hubConnection;
    private SurveillanceHubClient? hub;

    public event Action<ConnectionStateChangedEvent>? OnConnectionStateChanged;

    public event Action<RecordingStateChangedEvent>? OnRecordingStateChanged;

    public event Action<MotionDetectedEvent>? OnMotionDetected;

    public event Action<StreamStartedEvent>? OnStreamStarted;

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

        var hubUrl = $"{ApiBaseUrl.TrimEnd('/')}{SurveillanceHubClient.Url}";

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // The subscriptions live as long as the connection: both are disposed together.
        hub = new SurveillanceHubClient(hubConnection);
        hub.OnConnectionStateChanged(e => OnConnectionStateChanged?.Invoke(e));
        hub.OnRecordingStateChanged(e => OnRecordingStateChanged?.Invoke(e));
        hub.OnMotionDetected(e => OnMotionDetected?.Invoke(e));
        hub.OnStreamStarted(e => OnStreamStarted?.Invoke(e));

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

    public async Task StartStreamAsync(Guid cameraId)
    {
        if (hub is not null && hubConnection?.State == HubConnectionState.Connected)
        {
            await hub
                .StartStreamAsync(cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task StopStreamAsync(Guid cameraId)
    {
        if (hub is not null && hubConnection?.State == HubConnectionState.Connected)
        {
            await hub
                .StopStreamAsync(cameraId)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells the server a stream is still being watched. The server stops a
    /// stream it has not heard about for a minute.
    /// </summary>
    public async Task StreamHeartbeatAsync(Guid cameraId)
    {
        if (hub is not null && hubConnection?.State == HubConnectionState.Connected)
        {
            await hub
                .StreamHeartbeatAsync(cameraId)
                .ConfigureAwait(false);
        }
    }

    public async Task DisconnectAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.StopAsync().ConfigureAwait(false);
            if (hub is not null)
            {
                await hub.DisposeAsync().ConfigureAwait(false);
                hub = null;
            }

            await hubConnection.DisposeAsync().ConfigureAwait(false);
            hubConnection = null;
            OnHubConnectionStateChanged?.Invoke("Disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}