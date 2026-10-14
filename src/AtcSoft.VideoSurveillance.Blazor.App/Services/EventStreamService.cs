namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Reads the server's camera events (GET /events) for as long as it runs. When
/// the stream drops, it asks again with the id of the last event it read
/// (Last-Event-ID), so the server sends what came meanwhile; while the server
/// cannot be reached, it waits a little longer each time.
/// </summary>
public sealed class EventStreamService(IListEventsEndpoint endpoint)
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>
    /// Gets how the service waits before it asks again (tests replace it).
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>
    /// Raised when <see cref="IsReading"/> changes, so a page can show it.
    /// </summary>
    public event Action? ReadingChanged;

    /// <summary>
    /// Gets a value indicating whether the stream is open now.
    /// </summary>
    public bool IsReading { get; private set; }

    /// <summary>
    /// Reads the events until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(
        Func<SurveillanceEvent, Task> onEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

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
                                await onEvent(surveillanceEvent).ConfigureAwait(false);
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
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException)
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

    private void SetReading(bool reading)
    {
        IsReading = reading;
        ReadingChanged?.Invoke();
    }
}