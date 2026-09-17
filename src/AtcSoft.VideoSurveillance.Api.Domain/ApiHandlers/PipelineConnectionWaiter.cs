namespace Linksoft.VideoSurveillance.Api.Domain.ApiHandlers;

/// <summary>
/// Waits for an <see cref="IMediaPipeline"/> to reach
/// <see cref="Linksoft.VideoSurveillance.Enums.ConnectionState.Connected"/>.
/// Used by handlers (StartRecording, CaptureSnapshot) that need a definite
/// connect outcome before acting on the pipeline — pipelineFactory.Create
/// returns immediately while the actual RTSP open runs on a background thread,
/// so calling CaptureFrameAsync / StartRecording without waiting is racy.
/// </summary>
internal static class PipelineConnectionWaiter
{
    /// <summary>
    /// Returns true when the pipeline reaches Connected, false on Error,
    /// timeout, or external cancellation.
    /// </summary>
    public static async Task<bool> WaitForConnectedAsync(
        IMediaPipeline pipeline,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(
            object? sender,
            Linksoft.VideoSurveillance.Events.ConnectionStateChangedEventArgs e)
        {
            if (e.NewState == Linksoft.VideoSurveillance.Enums.ConnectionState.Connected)
            {
                tcs.TrySetResult(true);
            }
            else if (e.NewState == Linksoft.VideoSurveillance.Enums.ConnectionState.Error)
            {
                tcs.TrySetResult(false);
            }
        }

        pipeline.ConnectionStateChanged += Handler;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await using var reg = cts.Token.Register(() => tcs.TrySetResult(false));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            pipeline.ConnectionStateChanged -= Handler;
        }
    }
}