namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// *Analyse recording* on the server: analyses the queued recordings one at a time, each with its
/// camera's own motion settings, reading the pictures straight from the file
/// (<see cref="AtcSoft.VideoEngine.Capture.GreyFrameFileReader"/>) and writing the activity index beside it
/// (<see cref="RecordingAnalysis"/>). The server names its recordings in UTC.
/// </summary>
public sealed partial class RecordingAnalysisService : BackgroundServiceBase<RecordingAnalysisService>, IRecordingAnalyses
{
    private readonly ConcurrentQueue<string> queue = new();
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly IApplicationSettingsService settingsService;
    private readonly ICameraStorageService cameraStorageService;

    public RecordingAnalysisService(
        ILogger<RecordingAnalysisService> logger,
        RecordingAnalysisServiceOptions options,
        IApplicationSettingsService settingsService,
        ICameraStorageService cameraStorageService)
        : base(logger, options)
    {
        this.settingsService = settingsService;
        this.cameraStorageService = cameraStorageService;
    }

    /// <inheritdoc />
    public bool TryEnqueue(string videoPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoPath);

        if (!pending.TryAdd(videoPath, 0))
        {
            return false;
        }

        queue.Enqueue(videoPath);
        return true;
    }

    /// <inheritdoc />
    public bool Contains(string videoPath)
        => pending.ContainsKey(videoPath);

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && queue.TryDequeue(out var videoPath))
        {
            try
            {
                var settings = SettingsFor(videoPath);
                var watch = Stopwatch.StartNew();
                var pictures = Analyse(videoPath, settings, stoppingToken);
                LogAnalysed(videoPath, pictures, watch.Elapsed.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                // The server is stopping: nothing was written.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AtcSoft.VideoEngine.FFmpeg.FFmpegException)
            {
                LogAnalysisFailed(ex, videoPath);
            }
            finally
            {
                pending.TryRemove(videoPath, out _);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Analyses a recording file for its activity index.
    /// </summary>
    /// <param name="videoPath">The recording.</param>
    /// <param name="settings">The motion settings to analyse with.</param>
    /// <param name="cancellationToken">Stops the analysis; nothing is written then.</param>
    /// <returns>How many pictures were analysed.</returns>
    public static int Analyse(
        string videoPath,
        MotionDetectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var width = settings.AnalysisWidth > 0 ? settings.AnalysisWidth : 800;
        var height = settings.AnalysisHeight > 0 ? settings.AnalysisHeight : 600;
        using var reader = AtcSoft.VideoEngine.Capture.GreyFrameFileReader.Open(
            videoPath,
            width,
            height,
            (AtcSoft.VideoEngine.VideoRotation)MediaProbe.GetRotation(videoPath),
            RecordingAnalysis.DecoderThreads);

        return RecordingAnalysis.Run(
            reader.ReadFrames(RecordingAnalysis.FramesPerSecond, cancellationToken).Select(f => (f.Position, f.Pixels)),
            width,
            height,
            new BuiltInMotionDetector(settings),
            videoPath,
            RecordingAnalysis.StartOf(videoPath, TimeZoneInfo.Utc),
            cancellationToken);
    }

    // The camera whose folder the recording is in, with its own overrides; else the app's settings.
    private MotionDetectionSettings SettingsFor(string videoPath)
    {
        var root = settingsService.Recording.RecordingPath;
        var folder = string.IsNullOrEmpty(root) ? null : RecordingPathHelper.GetCameraFolderFromPath(videoPath, root);
        var camera = folder is null
            ? null
            : cameraStorageService.GetAllCameras().FirstOrDefault(c => string.Equals(
                RecordingPathHelper.GetCameraFolderName(c.Display.DisplayName, c.Id),
                folder,
                StringComparison.OrdinalIgnoreCase));
        return camera is null
            ? settingsService.MotionDetection
            : MotionDetectionPolicy.Resolve(camera, settingsService.MotionDetection);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysed {Path}: {Pictures} pictures in {Seconds:F1} s")]
    private partial void LogAnalysed(string path, int pictures, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not analyse {Path}")]
    private partial void LogAnalysisFailed(Exception ex, string path);
}