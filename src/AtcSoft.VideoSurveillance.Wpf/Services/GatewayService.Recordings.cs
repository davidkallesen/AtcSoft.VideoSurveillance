namespace AtcSoft.VideoSurveillance.Wpf.Services;

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

    /// <summary>
    /// Gets a recording's activity for its timeline.
    /// </summary>
    /// <param name="filePath">The recording's path as the listing gives it.</param>
    /// <param name="buckets">How many stretches.</param>
    /// <param name="durationSeconds">How long the recording is, in seconds.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The activity, or <see langword="null"/> when the server did not give it.</returns>
    public async Task<RecordingActivity?> GetRecordingActivityAsync(
        string filePath,
        int buckets,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var parameters = new GetRecordingActivityParameters(FilePath: filePath, Buckets: buckets, DurationSeconds: durationSeconds);
        var result = await getRecordingActivityEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public string GetRecordingFileUrl(string filePath)
        => $"{ApiBaseUrl}/recordings-files/{AtcSoft.VideoSurveillance.Helpers.RecordingPathHelper.EscapePathSegments(filePath)}";
}