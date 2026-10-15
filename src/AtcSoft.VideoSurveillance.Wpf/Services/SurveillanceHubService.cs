namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Service for connecting to the SignalR surveillance hub.
/// Starts and stops the live streams; the camera events come from
/// <see cref="SurveillanceEventService"/> (GET /events).
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
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
[SuppressMessage(
    "Design",
    "MA0046:Use EventHandler<T> to declare events",
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
public sealed class SurveillanceHubService : IAsyncDisposable
{
    private HubConnection? hubConnection;
    private SurveillanceHubClient? hub;
    private Func<Task>? stopConnecting;

    public event Action<StreamStartedEvent>? OnStreamStarted;

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

        var hubUrl = $"{ApiBaseUrl.TrimEnd('/')}{SurveillanceHubClient.Url}";

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new HubReconnectPolicy())
            .Build();

        // The subscriptions live as long as the connection: both are disposed together.
        hub = new SurveillanceHubClient(hubConnection);
        hub.OnStreamStarted(e => OnStreamStarted?.Invoke(e));

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
            if (stopConnecting is not null)
            {
                await stopConnecting().ConfigureAwait(false);
                stopConnecting = null;
            }

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