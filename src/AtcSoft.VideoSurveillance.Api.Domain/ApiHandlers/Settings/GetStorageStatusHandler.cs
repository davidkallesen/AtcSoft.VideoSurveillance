namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers.Settings;

/// <summary>
/// Handler business logic for the GetStorageStatus operation.
/// </summary>
public sealed class GetStorageStatusHandler(
    IApplicationSettingsService settingsService) : IGetStorageStatusHandler
{
    public Task<GetStorageStatusResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var recording = settingsService.Recording;
        var status = DiskSpaceStatus.Read(
            recording.RecordingPath,
            recording.Cleanup.MinFreeSpaceMb,
            recording.Cleanup.EnableDiskSpaceGuard);

        return Task.FromResult(status is null
            ? GetStorageStatusResult.ServiceUnavailable("The disk of the recording folder cannot be read.")
            : GetStorageStatusResult.Ok(new StorageStatus(
                FreeBytes: status.FreeBytes,
                TotalBytes: status.TotalBytes,
                MinFreeBytes: status.MinFreeBytes,
                IsLow: status.IsLow,
                UsedPercent: status.UsedPercent,
                Text: status.Text)));
    }
}