using CoreRecordingEntry = Linksoft.VideoSurveillance.Models.RecordingEntry;

namespace Linksoft.VideoSurveillance.Api.Domain.Mapping;

internal static class RecordingMappingExtensions
{
    public static Recording ToApiModel(
        this CoreRecordingEntry core,
        Guid? cameraId = null)
        => new(
            Id: GenerateDeterministicId(core.FilePath),
            CameraId: cameraId ?? Guid.Empty,
            CameraName: core.CameraName,
            FilePath: core.FilePath,
            StartedAt: new DateTimeOffset(core.RecordingTime, TimeSpan.Zero),
            Duration: core.Duration.ToString("c"),
            FileSizeBytes: core.FileSizeBytes,
            HasThumbnail: core.HasThumbnail);

    private static Guid GenerateDeterministicId(string filePath)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(filePath));
        return new Guid(hash.AsSpan(0, 16));
    }
}