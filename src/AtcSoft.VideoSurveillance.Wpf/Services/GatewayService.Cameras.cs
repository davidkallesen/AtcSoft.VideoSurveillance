namespace Linksoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Gateway service - Cameras operations using generated endpoints.
/// </summary>
public sealed partial class GatewayService
{
    public async Task<Camera[]?> GetCamerasAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await listCamerasEndpoint
            .ExecuteAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent.ToArray()
            : null;
    }

    public async Task<Camera?> CreateCameraAsync(
        CreateCameraRequest request,
        CancellationToken cancellationToken = default)
    {
        var parameters = new CreateCameraParameters(Request: request);
        var result = await createCameraEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsCreated
            ? result.CreatedContent
            : null;
    }

    public async Task<Camera?> GetCameraByIdAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new GetCameraByIdParameters(CameraId: cameraId);
        var result = await getCameraByIdEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public async Task<Camera?> UpdateCameraAsync(
        Guid cameraId,
        UpdateCameraRequest request,
        CancellationToken cancellationToken = default)
    {
        var parameters = new UpdateCameraParameters(CameraId: cameraId, Request: request);
        var result = await updateCameraEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public async Task DeleteCameraAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DeleteCameraParameters(CameraId: cameraId);
        var result = await deleteCameraEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsNoContent)
        {
            throw new HttpRequestException($"Failed to delete camera: {result.StatusCode}");
        }
    }

    public async Task<RecordingStatus?> StartRecordingAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new StartRecordingParameters(CameraId: cameraId);
        var result = await startRecordingEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public async Task<RecordingStatus?> StopRecordingAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new StopRecordingParameters(CameraId: cameraId);
        var result = await stopRecordingEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public async Task<string?> CaptureSnapshotAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new CaptureSnapshotParameters(CameraId: cameraId);
        var result = await captureSnapshotEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? result.Content
            : null;
    }
}