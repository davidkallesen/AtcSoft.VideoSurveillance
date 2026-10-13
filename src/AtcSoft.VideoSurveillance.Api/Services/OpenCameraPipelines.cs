namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// <see cref="ICameraPipelineLookup"/> over the two places a camera's
/// pipeline can be: with the connection service (a camera recorded on
/// connect, or watched for motion) and with the recording service (a
/// recording someone started).
/// </summary>
public sealed class OpenCameraPipelines(
    CameraConnectionService connectionService,
    ServerRecordingService recordingService) : ICameraPipelineLookup
{
    /// <inheritdoc />
    public IMediaPipeline? Find(Guid cameraId)
        => connectionService.FindPipeline(cameraId) ?? recordingService.FindPipeline(cameraId);
}