namespace BoundingBoxWpfApp;

/// <summary>
/// The sample's window: a run's pictures one at a time, with the boxes the apps would show,
/// the truth, and the score. The clock is deterministic: every picture was analysed by its time
/// before it is shown, so stepping, playing or opening the same file again shows the same boxes.
/// A recording can be reviewed: stretches marked with a verdict are saved beside it as
/// <c>{video}.review.json</c> and score the run by count.
/// </summary>
[SuppressMessage("", "S2325:Make properties static", Justification = "XAML binding requires instance properties")]
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly DispatcherTimer playTimer;
    private SampleRun? current;
    private string? openPath;
    private bool openIsStorybook;
    private bool settingRate;
    private RecordingReview? review;
    private double? markFrom;

    [ObservableProperty]
    private string title = "Bounding box sample";

    [ObservableProperty]
    private string status = "Open a storybook or a recording.";

    [ObservableProperty]
    private WriteableBitmap? picture;

    [ObservableProperty(AfterChangedCallback = nameof(OnFrameIndexChanged))]
    private int frameIndex;

    [ObservableProperty]
    private int lastFrameIndex;

    [ObservableProperty]
    private string frameInfo = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BoundingBox>? detectedBoxes;

    [ObservableProperty]
    private IReadOnlyList<BoundingBox>? truthBoxes;

    [ObservableProperty]
    private IReadOnlyList<BoundingBox>? mustNotBoxes;

    [ObservableProperty]
    private int videoWidth = 1280;

    [ObservableProperty]
    private int videoHeight = 720;

    [ObservableProperty]
    private string scoreSummary = string.Empty;

    [ObservableProperty]
    private string scoreJson = string.Empty;

    [ObservableProperty(AfterChangedCallback = nameof(OnAnalysesPerSecondChanged))]
    private double analysesPerSecond = StartupOptions.DefaultAnalysesPerSecond;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isReviewing;

    [ObservableProperty]
    private string reviewInfo = string.Empty;

    [ObservableProperty]
    private int selectedSpanIndex = -1;

    [ObservableProperty]
    private string selectedVerdict = ReviewVerdicts.Correct;

    [ObservableProperty]
    private int spanCount = 1;

    [ObservableProperty]
    private string spanLabel = string.Empty;

    public MainWindowViewModel()
    {
        playTimer = new DispatcherTimer(DispatcherPriority.Normal);
        playTimer.Tick += OnPlayTick;
    }

    /// <summary>
    /// Gets the analysis rates to choose from: the server's and CameraWall's.
    /// </summary>
    public IReadOnlyList<double> Rates { get; } = [4, 15];

    /// <summary>
    /// Gets the verdicts a marked stretch can have.
    /// </summary>
    public IReadOnlyList<string> Verdicts { get; } = ReviewVerdicts.All;

    /// <summary>
    /// Gets the review's marked stretches, as shown in the list.
    /// </summary>
    public ObservableCollection<string> ReviewSpans { get; } = [];

    /// <summary>
    /// Opens a storybook and runs it.
    /// </summary>
    /// <param name="path">The storybook file.</param>
    /// <param name="rate">How many pictures a second are analysed.</param>
    public Task OpenStorybookAsync(
        string path,
        double rate)
    {
        openPath = path;
        openIsStorybook = true;
        SetRateQuietly(rate);
        return LoadAsync();
    }

    /// <summary>
    /// Opens a recording and runs it.
    /// </summary>
    /// <param name="path">The recording.</param>
    /// <param name="rate">How many pictures a second are analysed.</param>
    public Task OpenRecordingAsync(
        string path,
        double rate)
    {
        openPath = path;
        openIsStorybook = false;
        SetRateQuietly(rate);
        return LoadAsync();
    }

    [RelayCommand("OpenStorybook", CanExecute = nameof(CanOpen))]
    private async Task OpenStorybookFromDialogAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Storybook (*.storybook.json)|*.storybook.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            await OpenStorybookAsync(dialog.FileName, AnalysesPerSecond).ConfigureAwait(true);
        }
    }

    [RelayCommand("OpenRecording", CanExecute = nameof(CanOpen))]
    private async Task OpenRecordingFromDialogAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Video (*.mp4;*.mkv)|*.mp4;*.mkv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            await OpenRecordingAsync(dialog.FileName, AnalysesPerSecond).ConfigureAwait(true);
        }
    }

    private bool CanOpen()
        => !IsBusy;

    /// <summary>
    /// Sweeps the motion settings over the storybooks in a folder (all of them, at 4 and 15 pictures
    /// a second) and the reviewed recordings in a corpus folder, writes the report to
    /// <c>{data}\sweeps</c> and shows it in the score panel.
    /// </summary>
    /// <param name="scenesFolder">The folder of storybooks.</param>
    /// <param name="corpusFolder">The folder of recordings with their reviews, or <see langword="null"/>.</param>
    public async Task SweepAsync(
        string scenesFolder,
        string? corpusFolder)
    {
        Stop();
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Sweeping…";
        try
        {
            SweepScene[] scenes = [.. Directory.GetFiles(scenesFolder, "*.storybook.json").Order(StringComparer.Ordinal)
                .Select(f => new SweepScene(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), StorybookReader.ReadFile(f)))];
            var corpus = CorpusIn(corpusFolder);
            if (corpus.Count > 0 && !EnsureEngine())
            {
                Status = "FFmpeg is needed to read the corpus.";
                return;
            }

            var progress = new Progress<double>(share => Status = string.Create(CultureInfo.InvariantCulture, $"Sweeping… {share:0%}"));
            var ranked = await Task.Run(() => StorybookSweep.Run(scenes, corpus, progress: progress)).ConfigureAwait(true);
            var report = StorybookSweep.Report(ranked);
            var folder = Path.Combine(App.DataFolder, "sweeps");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"sweep-{DateTime.Now:yyyyMMdd-HHmmss}.md"));
            await File.WriteAllTextAsync(path, report).ConfigureAwait(true);

            current = null;
            IsReviewing = false;
            Title = "Bounding box sample - sweep";
            ScoreSummary = report.Split('\n').Skip(4).FirstOrDefault()?.Trim() ?? string.Empty;
            ScoreJson = report;
            Log.Information("Swept {Combinations} combinations over {Scenes} scenes and {Recordings} reviewed recordings: {Report}", ranked.Count, scenes.Length, corpus.Count, path);
            Status = "Ready";
        }
        catch (Exception ex) when (ex is StorybookException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or AtcSoft.VideoEngine.FFmpeg.FFmpegException)
        {
            Log.Error(ex, "Sweeping {Folder} failed", scenesFolder);
            Status = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    [RelayCommand("Sweep", CanExecute = nameof(CanOpen))]
    private async Task SweepFromDialogAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder of storybooks to sweep" };
        if (dialog.ShowDialog() == true)
        {
            await SweepAsync(dialog.FolderName, Environment.GetEnvironmentVariable(StartupOptions.CorpusVariable)).ConfigureAwait(true);
        }
    }

    // The recordings under a folder with their reviews beside them; one without its recording is left out.
    private static List<SweepRecording> CorpusIn(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var corpus = new List<SweepRecording>();
        foreach (var reviewPath in Directory.GetFiles(folder, "*" + RecordingPathHelper.ReviewExtension, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var reviewed = RecordingReviewFile.Read(reviewPath);
            var video = Path.Combine(Path.GetDirectoryName(reviewPath)!, reviewed.Recording.File);
            if (File.Exists(video))
            {
                corpus.Add(new SweepRecording(video, reviewed));
            }
        }

        return corpus;
    }

    [RelayCommand(CanExecute = nameof(HasRun))]
    private void PlayPause()
    {
        if (IsPlaying)
        {
            Stop();
            return;
        }

        if (FrameIndex >= LastFrameIndex)
        {
            FrameIndex = 0;
        }

        playTimer.Interval = TimeSpan.FromSeconds(1 / AnalysesPerSecond);
        playTimer.Start();
        IsPlaying = true;
    }

    [RelayCommand(CanExecute = nameof(HasRun))]
    private void PreviousFrame()
    {
        Stop();
        FrameIndex = Math.Max(0, FrameIndex - 1);
    }

    [RelayCommand(CanExecute = nameof(HasRun))]
    private void NextFrame()
    {
        Stop();
        FrameIndex = Math.Min(LastFrameIndex, FrameIndex + 1);
    }

    private bool HasRun()
        => current is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanReview))]
    private void MarkStart()
    {
        Stop();
        markFrom = CurrentTime();
        ReviewInfo = string.Create(CultureInfo.InvariantCulture, $"Stretch from {markFrom:0.00} s: go to its last picture and add it.");
        CommandManager.InvalidateRequerySuggested();
    }

    [RelayCommand(CanExecute = nameof(CanAddSpan))]
    private void AddSpan()
    {
        Stop();
        var from = markFrom!.Value;

        // To the end of the picture shown, so the last picture marked is in the stretch.
        var to = CurrentTime() + (1 / AnalysesPerSecond);
        if (to <= from)
        {
            ReviewInfo = "The stretch must end after it starts.";
            return;
        }

        review!.Spans.Add(new ReviewSpan
        {
            From = TimeSpan.FromSeconds(from),
            To = TimeSpan.FromSeconds(to),
            Verdict = SelectedVerdict,
            Count = SelectedVerdict == ReviewVerdicts.FalseBox ? null : Math.Max(1, SpanCount),
            Label = string.IsNullOrWhiteSpace(SpanLabel) ? null : SpanLabel.Trim(),
        });
        markFrom = null;
        ReviewInfo = "Stretch added; save the review to keep it.";
        RefreshReview();
    }

    private bool CanAddSpan()
        => CanReview() && markFrom is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSpan))]
    private void RemoveSpan()
    {
        review!.Spans.RemoveAt(SelectedSpanIndex);
        RefreshReview();
    }

    private bool CanRemoveSpan()
        => CanReview() && SelectedSpanIndex >= 0 && SelectedSpanIndex < review!.Spans.Count;

    [RelayCommand(CanExecute = nameof(CanReview))]
    private void SaveReview()
    {
        var path = RecordingPathHelper.BuildReviewPath(openPath!);
        try
        {
            RecordingReviewFile.Write(path, review!);
            ReviewInfo = $"Saved {Path.GetFileName(path)}.";
            Log.Information("Saved the review {Path} with {Count} stretches", path, review!.Spans.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error(ex, "Saving the review {Path} failed", path);
            ReviewInfo = $"Not saved: {ex.Message}";
        }
    }

    private bool CanReview()
        => IsReviewing && review is not null && HasRun();

    private double CurrentTime()
        => current is { } run && run.Run.Frames.Count > 0
            ? run.Run.Frames[Math.Clamp(FrameIndex, 0, run.Run.Frames.Count - 1)].Time
            : 0;

    private async Task LoadAsync()
    {
        if (openPath is not { } path)
        {
            return;
        }

        Stop();
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Analysing…";
        try
        {
            var rate = AnalysesPerSecond;
            SampleRun run;
            if (openIsStorybook)
            {
                var scene = StorybookReader.ReadFile(path);
                if (SampleRunner.NeedsEngine(scene) && !EnsureEngine())
                {
                    Status = "FFmpeg is needed for a storybook with a bitrate.";
                    return;
                }

                run = await Task.Run(() => SampleRunner.RunStorybook(path, scene, rate, Path.Combine(App.DataFolder, "videos"))).ConfigureAwait(true);
            }
            else
            {
                if (!EnsureEngine())
                {
                    Status = "FFmpeg is needed to read a recording.";
                    return;
                }

                var (found, note, isSaved) = await Task.Run(() => ReviewOf(path)).ConfigureAwait(true);
                run = await Task.Run(() => SampleRunner.RunRecording(path, rate, isSaved ? found : null)).ConfigureAwait(true);
                review = found;
                ReviewInfo = note;
            }

            if (openIsStorybook)
            {
                review = null;
                ReviewInfo = string.Empty;
            }

            markFrom = null;
            IsReviewing = !openIsStorybook;
            Show(run);
            RefreshReview();
            Log.Information("Opened {Name} ({Way}, {Rate}/s): {Score}", run.Name, run.Way, rate, run.Run.Score.Summary);
            Status = "Ready";
        }
        catch (Exception ex) when (ex is StorybookException or IOException or InvalidOperationException or InvalidDataException or AtcSoft.VideoEngine.FFmpeg.FFmpegException)
        {
            Log.Error(ex, "Opening {Path} failed", path);
            Status = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // The recording's review beside it (saved), or a new one (not saved); one of another file is not used.
    private static (RecordingReview Review, string Note, bool IsSaved) ReviewOf(
        string path)
    {
        var sha256 = RecordingReviewFile.Sha256Of(path);
        var fresh = new RecordingReview { Recording = new ReviewRecording { File = Path.GetFileName(path), Sha256 = sha256 } };
        var reviewPath = RecordingPathHelper.BuildReviewPath(path);
        if (!File.Exists(reviewPath))
        {
            return (fresh, "No review yet: mark stretches and save.", false);
        }

        try
        {
            var read = RecordingReviewFile.Read(reviewPath);
            return string.Equals(read.Recording.Sha256, sha256, StringComparison.Ordinal)
                ? (read, $"Review with {read.Spans.Count} stretches.", true)
                : (fresh, "The review beside it is of another file; starting a new one.", false);
        }
        catch (InvalidDataException ex)
        {
            return (fresh, $"The review beside it cannot be read ({ex.Message}); starting a new one.", false);
        }
    }

    // Shows the review's stretches and scores the run by them.
    private void RefreshReview()
    {
        ReviewSpans.Clear();
        foreach (var span in review?.Spans ?? [])
        {
            var count = span.Count is { } c ? $" x{c}" : string.Empty;
            var label = span.Label is { } l ? " " + l : string.Empty;
            ReviewSpans.Add(string.Create(CultureInfo.InvariantCulture, $"{span.From.TotalSeconds:0.00}-{span.To.TotalSeconds:0.00} s  {span.Verdict}{count}{label}"));
        }

        if (current is { HasTruth: false } run)
        {
            var score = review is { Spans.Count: > 0 } reviewed ? ReviewScorer.Score(run.Run.Frames, reviewed) : null;
            ScoreSummary = score is not null
                ? "Review: " + score.Summary
                : $"{run.Run.Frames.Count(f => f.Detected.Count > 0)} of {run.Run.Frames.Count} pictures with boxes (no truth)";
            ScoreJson = SampleRunner.ScoreJson(run, score);
        }

        ShowFrame();
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool EnsureEngine()
    {
        if (VideoEngineBootstrap.IsInitialized)
        {
            return true;
        }

        if (FFmpegSetup.EnsureReady() is not { } folder)
        {
            return false;
        }

        VideoEngineBootstrap.Initialize(new VideoEngineConfig { FFmpegPath = folder });
        return true;
    }

    private void Show(SampleRun run)
    {
        current = run;
        Title = $"Bounding box sample - {run.Name}";
        VideoWidth = run.Run.VideoWidth;
        VideoHeight = run.Run.VideoHeight;
        Picture = new WriteableBitmap(run.Run.AnalysisWidth, run.Run.AnalysisHeight, 96, 96, PixelFormats.Gray8, palette: null);
        ScoreJson = run.ScoreJson;
        ScoreSummary = run.HasTruth ? run.Run.Score.Summary : $"{run.Run.Frames.Count(f => f.Detected.Count > 0)} of {run.Run.Frames.Count} pictures with boxes (no truth)";
        LastFrameIndex = Math.Max(0, run.Run.Frames.Count - 1);

        // Always draw, also when the index does not change.
        if (FrameIndex == 0)
        {
            ShowFrame();
        }
        else
        {
            FrameIndex = 0;
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowFrame()
    {
        if (current is null || current.Run.Frames.Count == 0 || Picture is not { } bitmap)
        {
            return;
        }

        var index = Math.Clamp(FrameIndex, 0, current.Run.Frames.Count - 1);
        var frame = current.Run.Frames[index];
        if (frame.Pixels is { } pixels)
        {
            bitmap.WritePixels(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), pixels, bitmap.PixelWidth, 0);
        }

        DetectedBoxes = frame.Detected.Select(d => ToBox(d.Box, d.TrackId)).ToList();
        TruthBoxes = frame.Truth.Where(t => !t.IsDistractor && t.State == TruthState.MustBox).Select(t => ToBox(t.Box, trackId: null)).ToList();
        MustNotBoxes = frame.Truth.Where(t => t.State == TruthState.MustNotBox).Select(t => ToBox(t.Box, trackId: null)).ToList();
        var marked = review?.Spans.Where(s => s.From.TotalSeconds <= frame.Time && frame.Time < s.To.TotalSeconds).Select(s => s.Verdict + (s.Count is { } c ? $" x{c}" : string.Empty)).ToList() ?? [];
        FrameInfo = string.Create(
            CultureInfo.InvariantCulture,
            $"{frame.Time:0.00} s · picture {index + 1} of {current.Run.Frames.Count} · {frame.Detected.Count} boxes · {TruthBoxes.Count} objects to box{(marked.Count > 0 ? " · marked: " + string.Join(", ", marked) : string.Empty)}");
    }

    private static BoundingBox ToBox(
        PixelBox box,
        int? trackId)
        => new() { X = box.X, Y = box.Y, Width = box.Width, Height = box.Height, TrackId = trackId };

    private void OnFrameIndexChanged()
        => ShowFrame();

    private void OnAnalysesPerSecondChanged()
    {
        // A rate chosen in the window runs the file again; one set while opening a file does not.
        if (!settingRate)
        {
            _ = LoadAsync();
        }
    }

    private void SetRateQuietly(double rate)
    {
        settingRate = true;
        try
        {
            AnalysesPerSecond = rate;
        }
        finally
        {
            settingRate = false;
        }
    }

    private void OnPlayTick(
        object? sender,
        EventArgs e)
    {
        if (FrameIndex >= LastFrameIndex)
        {
            Stop();
            return;
        }

        FrameIndex++;
    }

    private void Stop()
    {
        playTimer.Stop();
        IsPlaying = false;
    }
}