namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers.Cameras;

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

        // Applied to a copy first: a refused update leaves the stored camera as it was.
        var updated = camera.Clone();
        updated.ApplyUpdate(parameters.Request);
        if (CameraSourceCheck.Problem(updated) is { } problem)
        {
            return Task.FromResult(UpdateCameraResult.BadRequest(problem));
        }

        camera.CopyFrom(updated);
        storage.AddOrUpdateCamera(camera);
        storage.Save();

        return Task.FromResult(UpdateCameraResult.Ok(camera.ToApiModel()));
    }
}