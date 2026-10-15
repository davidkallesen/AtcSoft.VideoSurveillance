namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Reads the server's camera events (GET /events) for as long as the app runs,
/// and raises each part of an event as its own event. When the stream drops, it
/// asks again with the id of the last event it read (Last-Event-ID), so the
/// server sends what came meanwhile; while the server cannot be reached, it
/// waits a little longer each time.
/// </summary>
/// <remarks>
/// The events are raised on the thread that reads the stream, not the UI thread.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1003:Use generic event handler instances",
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
[SuppressMessage(
    "Design",
    "MA0046:Use EventHandler<T> to declare events",
    Justification = "Action<T> events are intentional for terse WPF consumption.")]
public sealed class SurveillanceEventService : BackgroundServiceBase<SurveillanceEventService>
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private readonly IListEventsEndpoint endpoint;

    public SurveillanceEventService(
        IListEventsEndpoint endpoint,
        ILogger<SurveillanceEventService> logger,
        SurveillanceEventServiceOptions options)
        : base(logger, options)
    {
        this.endpoint = endpoint;
    }

    public event Action<ConnectionStateChangedEvent>? OnConnectionStateChanged;

    public event Action<RecordingStateChangedEvent>? OnRecordingStateChanged;

    public event Action<MotionDetectedEvent>? OnMotionDetected;

    public event Action<UsbCameraLifecycleEvent>? OnUsbCameraLifecycleChanged;

    /// <summary>
    /// Raised when <see cref="IsReading"/> changes.
    /// </summary>
    public event Action? ReadingChanged;

    /// <summary>
    /// Gets a value indicating whether the stream is open now.
    /// </summary>
    public bool IsReading { get; private set; }

    /// <summary>
    /// Gets or sets how the service waits before it asks again (tests replace it).
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
        => RunAsync(stoppingToken);

    /// <summary>
    /// Reads the events until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string? lastEventId = null;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = false;
            try
            {
                using var result = await endpoint
                    .ExecuteAsync(new ListEventsParameters(CameraId: null, HeartbeatSeconds: null, LastEventId: lastEventId), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsOk)
                {
                    SetReading(true);
                    try
                    {
                        await foreach (var surveillanceEvent in result.Content!.WithCancellation(cancellationToken).ConfigureAwait(false))
                        {
                            read = true;
                            if (surveillanceEvent is not null)
                            {
                                Raise(surveillanceEvent);
                            }
                        }
                    }
                    finally
                    {
                        SetReading(false);
                        lastEventId = result.LastEventId is { Length: > 0 } id ? id : lastEventId;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
            {
                // The stream dropped: asked again below, after the last event read.
            }

            // A stream that gave events is asked again at once; one that gave none waits.
            failures = read ? 0 : failures + 1;
            if (failures == 0)
            {
                continue;
            }

            try
            {
                await Delay(Backoff[Math.Min(failures, Backoff.Length) - 1], cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // The part that is filled says what happened; an event of a type this
    // client does not know has none of them and is read past.
    private void Raise(SurveillanceEvent surveillanceEvent)
    {
        if (surveillanceEvent.Connection is { } connection)
        {
            OnConnectionStateChanged?.Invoke(connection);
        }

        if (surveillanceEvent.Recording is { } recording)
        {
            OnRecordingStateChanged?.Invoke(recording);
        }

        if (surveillanceEvent.Motion is { } motion)
        {
            OnMotionDetected?.Invoke(motion);
        }

        if (surveillanceEvent.Usb is { } usb)
        {
            OnUsbCameraLifecycleChanged?.Invoke(usb);
        }
    }

    private void SetReading(bool reading)
    {
        IsReading = reading;
        ReadingChanged?.Invoke();
    }
}