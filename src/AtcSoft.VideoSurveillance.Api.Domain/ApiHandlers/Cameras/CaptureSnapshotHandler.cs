namespace Linksoft.VideoSurveillance.Api.Domain.ApiHandlers.Cameras;

/// <summary>
/// Handler business logic for the CaptureSnapshot operation.
/// Creates a transient media pipeline to capture a single frame from the camera stream.
/// </summary>
public sealed class CaptureSnapshotHandler(
    ICameraStorageService storage,
    IMediaPipelineFactory pipelineFactory,
    IApplicationSettingsService settingsService) : ICaptureSnapshotHandler
{
    // Same envelope as StartRecording — long enough for a typical RTSP open
    // (1-3s), short enough that a stuck client gets a definite answer.
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

    public async Task<CaptureSnapshotResult> ExecuteAsync(
        CaptureSnapshotParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var camera = storage.GetCameraById(parameters.CameraId);
        if (camera is null)
        {
            return CaptureSnapshotResult.NotFound($"Camera {parameters.CameraId} not found.");
        }

        using var pipeline = pipelineFactory.Create(camera);

        // pipelineFactory.Create returns before RTSP open completes; the
        // player is in Opening state. CaptureFrameAsync returns null when
        // state != Playing, so without this wait the handler reports
        // "Failed to capture frame" on practically every cold call.
        var connected = await PipelineConnectionWaiter
            .WaitForConnectedAsync(pipeline, ConnectionTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!connected)
        {
            return CaptureSnapshotResult.NotFound(
                $"Camera {parameters.CameraId} did not reach Connected within {ConnectionTimeout.TotalSeconds:N0}s.");
        }

        var frame = await pipeline.CaptureFrameAsync(cancellationToken);
        if (frame is null)
        {
            return CaptureSnapshotResult.NotFound("Failed to capture frame from camera stream.");
        }

        var snapshotPath = settingsService.CameraDisplay.SnapshotPath;
        Directory.CreateDirectory(snapshotPath);

        var safeName = string.IsNullOrWhiteSpace(camera.Display.DisplayName)
            ? camera.Id.ToString("N")[..8]
            : string.Join("_", camera.Display.DisplayName.Split(Path.GetInvalidFileNameChars()));
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"{safeName}_{timestamp}.png";
        var filePath = Path.Combine(snapshotPath, fileName);

        await File.WriteAllBytesAsync(filePath, frame, cancellationToken);

        return CaptureSnapshotResult.Ok(frame, "image/png", fileName);
    }
}