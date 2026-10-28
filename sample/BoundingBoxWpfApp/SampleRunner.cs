namespace BoundingBoxWpfApp;

/// <summary>
/// Runs what the sample opens through the shipped detector, the way the storybook tests do, and
/// keeps every picture to show. A storybook without a bitrate is drawn and fed straight to the
/// detector (its score is the baseline's); one with a bitrate is encoded by the FFmpeg tool once
/// (kept by the storybook's hash) and read back as a recording; a recording is read as
/// <i>Analyse recording</i> reads it.
/// </summary>
public static class SampleRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Gets whether opening the storybook needs the engine (FFmpeg): only one with a bitrate.
    /// </summary>
    /// <param name="scene">The storybook.</param>
    public static bool NeedsEngine(StorybookScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Video.BitrateKbps is not null;
    }

    /// <summary>
    /// Runs a storybook. One with a bitrate needs the engine started.
    /// </summary>
    /// <param name="path">The storybook file.</param>
    /// <param name="scene">The storybook, as read from it.</param>
    /// <param name="analysesPerSecond">How many pictures a second are analysed.</param>
    /// <param name="videoFolder">Where an encoded storybook is kept.</param>
    /// <param name="cancellationToken">Stops the encoding.</param>
    /// <returns>The run.</returns>
    public static SampleRun RunStorybook(
        string path,
        StorybookScene scene,
        double analysesPerSecond,
        string videoFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(scene);

        var detector = new BuiltInMotionDetector(scene.Detector.Settings());
        var options = new StorybookRunOptions(AnalysesPerSecond: analysesPerSecond, KeepPictures: true);
        if (!NeedsEngine(scene))
        {
            return Result(scene.Name, "fast", analysesPerSecond, StorybookRunner.Run(scene, detector, options), hasTruth: true);
        }

        var (video, truth) = Encoded(path, scene, videoFolder, cancellationToken);
        return Result(scene.Name, "encoded", analysesPerSecond, StorybookRunner.RunEncoded(scene, detector, video, truth, options), hasTruth: true);
    }

    /// <summary>
    /// Runs a recording as <i>Analyse recording</i> does: the first seconds only teach the scene,
    /// with the review's settings and zones when it has one, else the default settings. Needs the
    /// engine started.
    /// </summary>
    /// <param name="path">The recording.</param>
    /// <param name="analysesPerSecond">How many pictures a second are analysed.</param>
    /// <param name="review">The recording's review, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The run; it has no truth (a review is scored by count, see <see cref="ReviewScorer"/>).</returns>
    public static SampleRun RunRecording(
        string path,
        double analysesPerSecond,
        RecordingReview? review = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var settings = review?.Detector.Settings ?? new MotionDetectionSettings();
        var detector = new BuiltInMotionDetector(settings, zones: review is null ? null : [.. review.Detector.Zones])
        {
            WarmUpSeconds = RecordingAnalysis.WarmUpSeconds,
        };
        var options = new StorybookRunOptions(
            AnalysisWidth: settings.AnalysisWidth > 0 ? settings.AnalysisWidth : MotionAnalysisSize.DefaultLongerSide,
            AnalysesPerSecond: analysesPerSecond,
            KeepPictures: true);
        var run = StorybookRunner.RunRecording(path, detector, options, cancellationToken);
        return Result(Path.GetFileName(path), "recording", analysesPerSecond, run, hasTruth: false);
    }

    /// <summary>
    /// Gets the file an encoded storybook is kept in: named by the storybook and the hash of its
    /// text, so a changed storybook is encoded again.
    /// </summary>
    /// <param name="path">The storybook file.</param>
    /// <param name="videoFolder">The folder.</param>
    /// <returns>The video's path.</returns>
    public static string EncodedVideoPath(
        string path,
        string videoFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))[..16];
        var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
        return Path.Combine(videoFolder, $"{name}-{hash}.mkv");
    }

    private static (string Video, IReadOnlyList<FrameTruth> Truth) Encoded(
        string path,
        StorybookScene scene,
        string videoFolder,
        CancellationToken cancellationToken)
    {
        var video = EncodedVideoPath(path, videoFolder);
        var truthPath = TruthFile.PathFor(video);
        if (File.Exists(video) && File.Exists(truthPath))
        {
            return (video, TruthFile.Read(truthPath));
        }

        var folder = VideoEngineBootstrap.FFmpegFolder
                     ?? throw new InvalidOperationException("The engine is not started.");
        var tool = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(tool))
        {
            throw new StorybookException($"The FFmpeg command-line tool is not in '{folder}'; it encodes a storybook with a bitrate.");
        }

        Directory.CreateDirectory(videoFolder);
        return (video, StorybookEncoder.Encode(scene, tool, video, cancellationToken));
    }

    /// <summary>
    /// Gets the score panel's JSON: how the run was made and its score. A recording has no truth;
    /// with a review it is scored by the review instead.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="review">The recording's review score, or <see langword="null"/>.</param>
    /// <returns>The JSON.</returns>
    public static string ScoreJson(
        SampleRun run,
        ReviewScore? review)
    {
        ArgumentNullException.ThrowIfNull(run);

        object score = review is not null
            ? review
            : run.HasTruth
                ? run.Run.Score
                : new { picturesWithBoxes = run.Run.Frames.Count(f => f.Detected.Count > 0), pictures = run.Run.Frames.Count, trackCount = run.Run.Score.TrackCount };
        return JsonSerializer.Serialize(
            new
            {
                name = run.Name,
                way = run.Way,
                analysesPerSecond = run.AnalysesPerSecond,
                detector = "builtIn",
                analysisWidth = run.Run.AnalysisWidth,
                analysisHeight = run.Run.AnalysisHeight,
                scoredBy = review is not null ? "review" : run.HasTruth ? "truth" : "nothing",
                score,
            },
            JsonOptions);
    }

    private static SampleRun Result(
        string name,
        string way,
        double analysesPerSecond,
        StorybookRun run,
        bool hasTruth)
    {
        var result = new SampleRun(name, way, analysesPerSecond, run, hasTruth, string.Empty);
        return result with { ScoreJson = ScoreJson(result, review: null) };
    }
}