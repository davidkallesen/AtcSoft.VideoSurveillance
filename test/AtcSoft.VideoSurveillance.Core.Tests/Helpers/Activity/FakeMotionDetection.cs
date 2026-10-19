namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// The analysis of the pictures: remembers which cameras it was asked to index, and sends the
/// pictures the test gives it.
/// </summary>
public sealed class FakeMotionDetection : IMotionDetectionService
{
    public event EventHandler<MotionDetectedEventArgs>? MotionDetected;

    public event EventHandler<PictureAnalyzedEventArgs>? PictureAnalyzed;

    public Dictionary<Guid, MotionDetectionSettings?> Indexing { get; } = [];

    public List<Guid> IndexingStopped { get; } = [];

    public void StartDetection(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
        => throw new NotSupportedException();

    public void StopDetection(Guid cameraId)
        => throw new NotSupportedException();

    public bool IsDetectionActive(Guid cameraId)
        => false;

    public void StartIndexing(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
        => Indexing[cameraId] = settings;

    public void StopIndexing(Guid cameraId)
    {
        Indexing.Remove(cameraId);
        IndexingStopped.Add(cameraId);
    }

    public bool IsIndexing(Guid cameraId)
        => Indexing.ContainsKey(cameraId);

    public bool IsMotionDetected(Guid cameraId)
        => false;

    public IReadOnlyList<BoundingBox> GetLastBoundingBoxes(Guid cameraId)
        => [];

    public (int Width, int Height) GetAnalysisResolution(Guid cameraId)
        => (320, 240);

    public void Picture(
        Guid cameraId,
        DateTimeOffset time,
        double level)
        => PictureAnalyzed?.Invoke(this, new PictureAnalyzedEventArgs(cameraId, time, new MotionDetectorResult(false, MotionHoldChange.None, level, [])));

    public void Motion(MotionDetectedEventArgs args)
        => MotionDetected?.Invoke(this, args);
}