namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers;

/// <summary>
/// Starts a recording someone asked for on a camera that has one pipeline
/// for everything (see <see cref="ICameraPipelineHost"/>). The REST handler
/// and the SignalR hub both start recordings; this is the one way they do
/// it for such a camera.
/// </summary>
public static class HostedCameraRecording
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets the text that tells a user why the camera could not be recorded.
    /// </summary>
    /// <param name="outcome">How the start went.</param>
    /// <param name="timeout">How long the camera was given to deliver a picture.</param>
    /// <returns>The text; empty when the recording started.</returns>
    public static string Describe(
        HostedCameraRecordingOutcome outcome,
        TimeSpan timeout)
        => outcome switch
        {
            HostedCameraRecordingOutcome.CameraUnavailable =>
                "This USB camera cannot be opened right now: it is unplugged, or the server is still connecting to it. Try again in a moment.",
            HostedCameraRecordingOutcome.NotConnected =>
                string.Create(CultureInfo.InvariantCulture, $"The camera delivered no picture within {timeout.TotalSeconds:N0}s."),
            HostedCameraRecordingOutcome.NotStarted =>
                "The recording could not be started: the camera is recording already, or there is no room on the disk.",
            _ => string.Empty,
        };

    /// <summary>
    /// Starts the recording on the camera's shared pipeline, opening the
    /// camera when nobody has it open yet.
    /// </summary>
    /// <param name="host">The host of the camera's pipeline.</param>
    /// <param name="recordingService">The recording service.</param>
    /// <param name="camera">The camera to record.</param>
    /// <param name="timeout">How long the camera gets to deliver a picture.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>How it went.</returns>
    public static async Task<HostedCameraRecordingOutcome> StartAsync(
        ICameraPipelineHost host,
        IRecordingService recordingService,
        AtcSoft.VideoSurveillance.Models.CameraConfiguration camera,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recordingService);
        ArgumentNullException.ThrowIfNull(camera);

        var pipeline = host.Acquire(camera, AtcSoft.VideoSurveillance.Enums.CameraPipelineUse.Recording);
        if (pipeline is null)
        {
            return HostedCameraRecordingOutcome.CameraUnavailable;
        }

        // From here on the pipeline is given back, never closed: someone
        // may be watching the camera on it.
        var started = false;
        try
        {
            if (!await WaitForPicturesAsync(pipeline, timeout, cancellationToken).ConfigureAwait(false))
            {
                return HostedCameraRecordingOutcome.NotConnected;
            }

            started = recordingService.StartRecordingOnSharedPipeline(camera, pipeline);
            return started
                ? HostedCameraRecordingOutcome.Started
                : HostedCameraRecordingOutcome.NotStarted;
        }
        finally
        {
            if (!started)
            {
                host.Release(camera.Id, AtcSoft.VideoSurveillance.Enums.CameraPipelineUse.Recording);
            }
        }
    }

    // The pipeline may have been playing for hours, so its "connected"
    // event may be long past: it is asked whether it delivers pictures.
    private static async Task<bool> WaitForPicturesAsync(
        IMediaPipeline pipeline,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var failed = false;

        void OnStateChanged(
            object? sender,
            AtcSoft.VideoSurveillance.Events.ConnectionStateChangedEventArgs e)
        {
            if (e.NewState == AtcSoft.VideoSurveillance.Enums.ConnectionState.Error)
            {
                Volatile.Write(ref failed, true);
            }
        }

        pipeline.ConnectionStateChanged += OnStateChanged;
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (pipeline.FramesDecoded > 0)
                {
                    return true;
                }

                if (Volatile.Read(ref failed) || DateTime.UtcNow >= deadline || cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                await Task.Delay(PollInterval, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // Its host closed it meanwhile.
            return false;
        }
        finally
        {
            pipeline.ConnectionStateChanged -= OnStateChanged;
        }
    }
}