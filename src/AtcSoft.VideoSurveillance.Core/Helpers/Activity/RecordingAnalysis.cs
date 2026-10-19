namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// *Analyse recording*: gives a recording made without an activity index (before the index
/// existed, or with it turned off) the same index as a recording analysed while it was made, by
/// running the motion detector over its pictures. The pictures come from the video engine; this
/// part knows nothing of video.
/// </summary>
public static class RecordingAnalysis
{
    /// <summary>
    /// How many pictures a second are analysed: as many as for the index of a recording in progress.
    /// </summary>
    public const double FramesPerSecond = 4;

    /// <summary>
    /// Gets when a recording started: the time in its name, read in the zone it was written in,
    /// else when the file was made.
    /// </summary>
    /// <param name="videoPath">The recording.</param>
    /// <param name="nameZone">The zone of the time in the name: CameraWall writes local time, the server UTC.</param>
    public static DateTimeOffset StartOf(
        string videoPath,
        TimeZoneInfo nameZone)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoPath);
        ArgumentNullException.ThrowIfNull(nameZone);

        return RecordingPathHelper.TryParseTimestamp(Path.GetFileName(videoPath), out var named)
            ? LocalTimeline.InZone(named, nameZone)
            : new DateTimeOffset(File.GetCreationTimeUtc(videoPath), TimeSpan.Zero);
    }

    /// <summary>
    /// Analyses a recording's pictures and writes its activity index beside it. The index is
    /// written to a temporary file first, so a recording is never left with half an index.
    /// </summary>
    /// <param name="pictures">The recording's pictures from its start, grey at the analysis size, with how far in each is.</param>
    /// <param name="width">The analysis width.</param>
    /// <param name="height">The analysis height.</param>
    /// <param name="detector">The motion detector, with the camera's settings.</param>
    /// <param name="videoPath">The recording.</param>
    /// <param name="start">When the recording started.</param>
    /// <param name="cancellationToken">Stops the analysis; nothing is written then.</param>
    /// <returns>How many pictures were analysed.</returns>
    public static int Run(
        IEnumerable<(TimeSpan Position, byte[] Pixels)> pictures,
        int width,
        int height,
        IMotionDetector detector,
        string videoPath,
        DateTimeOffset start,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentException.ThrowIfNullOrEmpty(videoPath);

        var indexPath = RecordingPathHelper.BuildActivityPath(videoPath);
        var partPath = indexPath + ".part";
        var count = 0;
        try
        {
            using (var writer = ActivityLogWriter.Create(partPath, start))
            {
                detector.Reset();
                foreach (var (position, pixels) in pictures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var time = start + position;
                    writer.Add(time, detector.Process(new MotionFrame(pixels, width, height, time)));
                    count++;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partPath, indexPath, overwrite: true);
            return count;
        }
        catch
        {
            File.Delete(partPath);
            throw;
        }
    }
}