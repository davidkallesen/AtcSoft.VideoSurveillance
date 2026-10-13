namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Finds the pipeline the server has open for a camera, whoever opened it.
/// </summary>
public interface ICameraPipelineLookup
{
    /// <summary>
    /// Gets the open pipeline of a camera.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <returns>The pipeline, or <see langword="null"/> when the server does not have the camera open.</returns>
    IMediaPipeline? Find(Guid cameraId);
}