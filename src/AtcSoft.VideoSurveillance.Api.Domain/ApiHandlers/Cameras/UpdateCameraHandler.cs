namespace Linksoft.VideoSurveillance.Api.Domain.ApiHandlers.Cameras;

/// <summary>
/// Handler business logic for the UpdateCamera operation.
/// </summary>
public sealed class UpdateCameraHandler(
    ICameraStorageService storage) : IUpdateCameraHandler
{
    public Task<UpdateCameraResult> ExecuteAsync(
        UpdateCameraParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var camera = storage.GetCameraById(parameters.CameraId);
        if (camera is null)
        {
            return Task.FromResult(UpdateCameraResult.NotFound($"Camera {parameters.CameraId} not found."));
        }

        camera.ApplyUpdate(parameters.Request);
        storage.AddOrUpdateCamera(camera);
        storage.Save();

        return Task.FromResult(UpdateCameraResult.Ok(camera.ToApiModel()));
    }
}