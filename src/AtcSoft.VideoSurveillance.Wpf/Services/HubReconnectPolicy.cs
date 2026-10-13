namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// How the client gets its connection to the server back: it tries again
/// soon at first, then every half minute, and never gives up. SignalR's own
/// policy stops after four attempts (about 40 seconds), which leaves a
/// client that runs for days disconnected after a server restart.
/// </summary>
public sealed class HubReconnectPolicy : Microsoft.AspNetCore.SignalR.Client.IRetryPolicy
{
    private static readonly TimeSpan[] FirstDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
    ];

    private static readonly TimeSpan SteadyDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long to wait before the next attempt.
    /// </summary>
    /// <param name="attemptsSoFar">How many attempts have failed since the connection was lost.</param>
    public static TimeSpan DelayFor(long attemptsSoFar)
        => attemptsSoFar >= 0 && attemptsSoFar < FirstDelays.Length
            ? FirstDelays[attemptsSoFar]
            : SteadyDelay;

    /// <summary>
    /// Tries to connect until it works. Used for a server that was not
    /// there when the client started: SignalR only reconnects a connection
    /// that was established once.
    /// </summary>
    /// <param name="tryConnect">One attempt; <see langword="true"/> when connected. An attempt that throws counts as failed.</param>
    /// <param name="wait">Waits between attempts.</param>
    /// <param name="cancellationToken">Ends the trying: the client is closing.</param>
    /// <returns><see langword="true"/> when connected; <see langword="false"/> when cancelled.</returns>
    public static async Task<bool> KeepTryingAsync(
        Func<CancellationToken, Task<bool>> tryConnect,
        Func<TimeSpan, CancellationToken, Task> wait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tryConnect);
        ArgumentNullException.ThrowIfNull(wait);

        // The attempt at the start, the one without a wait, has been made.
        var attemptsSoFar = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            attemptsSoFar++;

            try
            {
                await wait(DelayFor(attemptsSoFar), cancellationToken).ConfigureAwait(false);

                if (await tryConnect(cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Not there yet; the next attempt may find it.
            }
        }

        return false;
    }

    /// <inheritdoc />
    public TimeSpan? NextRetryDelay(
        Microsoft.AspNetCore.SignalR.Client.RetryContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(retryContext);
        return DelayFor(retryContext.PreviousRetryCount);
    }
}