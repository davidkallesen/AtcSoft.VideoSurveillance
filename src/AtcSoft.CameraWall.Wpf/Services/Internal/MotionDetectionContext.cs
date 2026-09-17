// ReSharper disable InconsistentNaming
namespace Linksoft.CameraWall.Wpf.Services.Internal;

/// <summary>
/// Internal context for tracking motion detection state per camera.
/// </summary>
internal sealed class MotionDetectionContext : IDisposable
{
    public MotionDetectionContext(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings settings)
    {
        CameraId = cameraId;
        Pipeline = pipeline;
        Settings = settings;
    }

    public Guid CameraId { get; }

    public IMediaPipeline Pipeline { get; }

    public MotionDetectionSettings Settings { get; }

    public byte[]? PreviousFrame { get; set; }

    public bool IsMotionDetected { get; set; }

    public DateTime? LastMotionTime { get; set; }

    public IReadOnlyList<BoundingBox> LastBoundingBoxes { get; set; } = [];

    [SuppressMessage("", "SA1401: Volatile requires a field, not a property", Justification = "OK")]
    public volatile bool IsAnalyzing;

    public int ConsecutiveFails { get; set; }

    public void Dispose()
    {
        PreviousFrame = null;
        LastBoundingBoxes = [];
        ConsecutiveFails = 0;
    }
}