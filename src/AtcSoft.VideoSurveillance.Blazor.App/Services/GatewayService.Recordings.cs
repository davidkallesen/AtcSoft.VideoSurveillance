namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Gateway service - Recordings operations using generated endpoints.
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
    /// Deletes a finished recording on the server.
    /// </summary>
    /// <param name="filePath">The recording's path, as the listing gave it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<RecordingDeletionOutcome> DeleteRecordingAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DeleteRecordingParameters(FilePath: filePath);
        var result = await deleteRecordingEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsNoContent)
        {
            return RecordingDeletionOutcome.Deleted;
        }

        if (result.IsConflict)
        {
            return RecordingDeletionOutcome.InProgress;
        }

        return result.IsNotFound
            ? RecordingDeletionOutcome.NotFound
            : RecordingDeletionOutcome.Failed;
    }
}