namespace Linksoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Gateway service - Recordings operations using generated endpoints.
/// CA1055 suppression applied at the type level via [SuppressMessage] on
/// the GatewayService.cs partial.
/// </summary>
public sealed partial class GatewayService
{
    public async Task<Recording[]?> GetRecordingsAsync(
        Guid? cameraId = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new ListRecordingsParameters(CameraId: cameraId);
        var result = await listRecordingsEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent.ToArray()
            : null;
    }

    public string GetRecordingFileUrl(string filePath)
        => $"{ApiBaseUrl}/recordings-files/{filePath}";
}