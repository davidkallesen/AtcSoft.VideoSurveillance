// ReSharper disable InconsistentNaming
namespace AtcSoft.CameraWall.Wpf.Services.Internal;

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
        Detector = new BuiltInMotionDetector(settings);
    }

    public Guid CameraId { get; }

    public IMediaPipeline Pipeline { get; }

    public MotionDetectionSettings Settings { get; }

    // The same detector as the server's, behind the shared seam.
    public IMotionDetector Detector { get; }

    public bool IsMotionDetected { get; set; }

    public DateTime? LastMotionTime { get; set; }

    public IReadOnlyList<BoundingBox> LastBoundingBoxes { get; set; } = [];

    [SuppressMessage("", "SA1401: Volatile requires a field, not a property", Justification = "OK")]
    public volatile bool IsAnalyzing;

    public int ConsecutiveFails { get; set; }

    public void Dispose()
    {
        Detector.Reset();
        LastBoundingBoxes = [];
        ConsecutiveFails = 0;
    }
}