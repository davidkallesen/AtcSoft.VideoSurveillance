namespace AtcSoft.CameraWall.Wpf.Timeline;

using ActivitySummaryCache = AtcSoft.VideoSurveillance.Helpers.Activity.ActivitySummaryCache;
using IActiveRecordings = AtcSoft.VideoSurveillance.Services.IActiveRecordings;
using LocalTimeline = AtcSoft.VideoSurveillance.Helpers.Activity.LocalTimeline;
using TimelineBuilder = AtcSoft.VideoSurveillance.Helpers.Activity.TimelineBuilder;
using TimelineCamera = AtcSoft.VideoSurveillance.Models.TimelineCamera;
using TimelineDays = AtcSoft.VideoSurveillance.Helpers.Activity.TimelineDays;
using TimelineMark = AtcSoft.VideoSurveillance.Models.TimelineMark;
using TimelineMarks = AtcSoft.VideoSurveillance.Helpers.Activity.TimelineMarks;
using TimelineRecording = AtcSoft.VideoSurveillance.Models.TimelineRecording;
using TimelineZoom = AtcSoft.VideoSurveillance.Helpers.Activity.TimelineZoom;

/// <summary>
/// CameraWall's timeline of every camera, from its own recording folders: the last 24 hours or a
/// chosen day, zoom and move, previous and next event, a click that plays from that moment on
/// across the camera's recordings, and *play all from here*. It grows while it shows now.
/// </summary>
/// <remarks>
/// CameraWall keeps no history of when a camera could be recorded, so a gap has no reason.
/// </remarks>
[SuppressMessage("", "S2325:Make properties static", Justification = "XAML binding requires instance properties")]
public sealed partial class LocalTimelineViewModel : ViewModelBase, IDisposable
{
    // About one stretch per two pixels of a wide window.
    private const int BucketCount = 720;

    // An event plays from a second before it, so its start is seen.
    private static readonly TimeSpan EventLeadIn = TimeSpan.FromSeconds(1);

    // The window grows by itself while it shows a span up to now, at most this often.
    private static readonly TimeSpan LiveReloadInterval = TimeSpan.FromSeconds(5);

    private readonly Func<IReadOnlyList<TimelineCamera>> cameras;
    private readonly Func<IReadOnlyList<string?>> folders;
    private readonly IActiveRecordings recordings;
    private readonly ActivitySummaryCache cache;
    private readonly TimeZoneInfo zone;
    private DateTimeOffset from;
    private DateTimeOffset to;
    private DateTimeOffset lastLoad;
    private bool zoomed;
    private int reloadPending;
    private DateTimeOffset? cursor;

    [ObservableProperty]
    private ObservableCollection<LocalTimelineLaneViewModel> lanes = [];

    [ObservableProperty]
    private IReadOnlyList<TimelineMark> marks = [];

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedDayChanged))]
    private DateTime? selectedDay;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalTimelineViewModel"/> class.
    /// </summary>
    /// <param name="cameras">The cameras, each a lane.</param>
    /// <param name="folders">The recording folders: the global one and each camera's own.</param>
    /// <param name="recordings">The recordings in progress; the window grows when they change.</param>
    /// <param name="cache">The summaries of the activity indexes already read.</param>
    /// <param name="zone">The viewer's zone, which is also the zone of the files' names.</param>
    public LocalTimelineViewModel(
        Func<IReadOnlyList<TimelineCamera>> cameras,
        Func<IReadOnlyList<string?>> folders,
        IActiveRecordings recordings,
        ActivitySummaryCache cache,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(cache);

        this.cameras = cameras;
        this.folders = folders;
        this.recordings = recordings;
        this.cache = cache;
        this.zone = zone ?? TimeZoneInfo.Local;
        recordings.RecordingStateChanged += OnRecordingStateChanged;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window is open; only then does it grow live.
    /// </summary>
    public bool IsShown { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        IsShown = false;
        recordings.RecordingStateChanged -= OnRecordingStateChanged;
    }

    /// <summary>
    /// Gets the tiles of *play all from here*: every lane with a recording ending after the moment.
    /// </summary>
    /// <param name="lanes">The lanes shown.</param>
    /// <param name="moment">The moment every tile plays from.</param>
    /// <returns>One tile per lane that has something to play.</returns>
    public static IReadOnlyList<PlayAllTileViewModel> PlayAllTiles(
        IEnumerable<LocalTimelineLaneViewModel> lanes,
        DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        return [.. lanes
            .Select(l => (Lane: l, Segments: l.Coverage
                .Where(c => c.End > moment)
                .OrderBy(c => c.Start)
                .Select(c => new PlayAllSegment(new Uri(c.VideoPath), c.Start, c.RecordingStart))
                .ToList()))
            .Where(l => l.Segments.Count > 0)
            .Select(l => new PlayAllTileViewModel(l.Lane.CameraName, l.Segments))];
    }

    /// <summary>
    /// Gets what plays after a recording of a lane: the lane's following recordings, one after
    /// another, gaps skipped.
    /// </summary>
    /// <param name="lane">The camera's lane.</param>
    /// <param name="videoPath">The recording that plays first.</param>
    /// <returns>A function that gives the next recording each time, then <see langword="null"/>.</returns>
    public static Func<RecordingSegment?> NextSegments(
        LocalTimelineLaneViewModel lane,
        string videoPath)
    {
        ArgumentNullException.ThrowIfNull(lane);

        var following = lane.Coverage
            .OrderBy(c => c.Start)
            .SkipWhile(c => !string.Equals(c.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
            .Skip(1)
            .ToList();
        var next = 0;
        return () =>
        {
            if (next >= following.Count)
            {
                return null;
            }

            var coverage = following[next++];
            return new RecordingSegment(
                coverage.VideoPath,
                Title(lane.CameraName, coverage.RecordingStart),
                coverage.RecordingStart.LocalDateTime,
                RecordingActivityLoaders.FromFile(coverage.VideoPath));
        };
    }

    [RelayCommand("Load")]
    private async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        lastLoad = now;
        if (!zoomed)
        {
            (from, to) = TimelineDays.Span(SelectedDay, now, zone);
        }

        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var (span, start, end) = (BucketCount, from, to);
            var all = cameras();
            var built = await Task.Run(() =>
            {
                var found = Find(start, end, now, all);
                return all
                    .Select(c => (Camera: c, Timeline: TimelineBuilder.Build(found.GetValueOrDefault(c.Id) ?? [], start, end, span)))
                    .ToList();
            }).ConfigureAwait(true);

            Lanes = [.. built.Select(b => new LocalTimelineLaneViewModel(b.Camera.Id, b.Camera.Name, b.Timeline, start, end, now, PlayAt))];
            Marks = TimelineMarks.For(start, end, zone, CultureInfo.CurrentCulture);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ZoomIn()
        => Zoom(zoomIn: true);

    [RelayCommand]
    private void ZoomOut()
        => Zoom(zoomIn: false);

    [RelayCommand]
    private void Earlier()
        => Move(later: false);

    [RelayCommand]
    private void Later()
        => Move(later: true);

    [RelayCommand]
    private void ShowLast24Hours()
    {
        zoomed = false;
        if (SelectedDay is null)
        {
            LoadCommand.Execute(parameter: null);
            return;
        }

        SelectedDay = null;
    }

    [RelayCommand("NextEvent")]
    private Task NextEventAsync()
        => JumpAsync(forward: true);

    [RelayCommand("PreviousEvent")]
    private Task PreviousEventAsync()
        => JumpAsync(forward: false);

    // From the place last clicked or played, else from the first recording in the span.
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Window disposes its view model on close via the Closed event handler.")]
    [RelayCommand]
    private void PlayAll()
    {
        var moment = cursor ?? Lanes
            .SelectMany(l => l.Coverage)
            .Select(c => c.Start < from ? from : c.Start)
            .DefaultIfEmpty(from)
            .Min();
        var tiles = PlayAllTiles(Lanes, moment);
        if (tiles.Count == 0)
        {
            StatusMessage = Translations.NothingRecordedThen;
            return;
        }

        StatusMessage = string.Empty;
        new PlayAllWindow(new PlayAllWindowViewModel(moment, tiles)).Show();
    }

    // Around the place last played, or the middle; the span stays put until the next zoom or move.
    private void Zoom(bool zoomIn)
    {
        var now = DateTimeOffset.UtcNow;
        var focus = cursor ?? from + ((to - from) / 2);
        (from, to) = zoomIn ? TimelineZoom.In(from, to, focus, now) : TimelineZoom.Out(from, to, focus, now);
        zoomed = true;
        LoadCommand.Execute(parameter: null);
    }

    private void Move(bool later)
    {
        (from, to) = TimelineZoom.Move(from, to, later, DateTimeOffset.UtcNow);
        zoomed = true;
        LoadCommand.Execute(parameter: null);
    }

    private void OnSelectedDayChanged()
    {
        zoomed = false;
        cursor = null;
        LoadCommand.Execute(parameter: null);
    }

    private async Task JumpAsync(bool forward)
    {
        var moment = cursor ?? (forward ? from : to);
        var all = cameras();
        var now = DateTimeOffset.UtcNow;
        var found = await Task.Run(() =>
        {
            var around = forward
                ? Find(moment, moment + LocalTimeline.MaximumSpan, now, all)
                : Find(moment - LocalTimeline.MaximumSpan, moment, now, all);
            return LocalTimeline.FindEvent(around, moment, forward);
        }).ConfigureAwait(true);

        if (found is not { } hit)
        {
            StatusMessage = Translations.NoEventWithinAWeek;
            return;
        }

        cursor = hit.Event.Start;
        var lane = Lanes.FirstOrDefault(l => l.CameraId == hit.CameraId);
        var offset = TimeSpan.FromSeconds(hit.Event.OffsetSeconds) - EventLeadIn;
        Play(lane, lane?.CameraName ?? string.Empty, hit.Event.VideoPath, offset > TimeSpan.Zero ? offset : TimeSpan.Zero, hit.Event.Start);
    }

    private void PlayAt(
        LocalTimelineLaneViewModel lane,
        double fraction)
    {
        var moment = from + ((to - from) * Math.Clamp(fraction, 0, 1));
        cursor = moment;
        if (lane.Coverage.FirstOrDefault(c => c.Start <= moment && moment < c.End) is not { } covering)
        {
            StatusMessage = Translations.NothingRecordedThen;
            return;
        }

        StatusMessage = string.Empty;
        var offset = moment - covering.RecordingStart;
        Play(lane, lane.CameraName, covering.VideoPath, offset > TimeSpan.Zero ? offset : TimeSpan.Zero, moment);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Window self-disposes on close via the Closed event handler.")]
    private static void Play(
        LocalTimelineLaneViewModel? lane,
        string cameraName,
        string videoPath,
        TimeSpan offset,
        DateTimeOffset moment)
    {
        var viewModel = new FullScreenRecordingWindowViewModel(
            videoPath,
            string.IsNullOrEmpty(cameraName) ? Path.GetFileName(videoPath) : Title(cameraName, moment),
            recordingStartTime: (moment - offset).LocalDateTime,
            activityLoader: RecordingActivityLoaders.FromFile(videoPath),
            startPosition: offset,
            nextSegment: lane is null ? null : NextSegments(lane, videoPath));

        new FullScreenRecordingWindow(viewModel).Show();
    }

    private static string Title(
        string cameraName,
        DateTimeOffset moment)
        => $"{cameraName} - {moment.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}";

    private IReadOnlyDictionary<Guid, IReadOnlyList<TimelineRecording>> Find(
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        IReadOnlyList<TimelineCamera> all)
        => LocalTimeline.FindRecordings(folders(), all, recordings.GetActiveSessions(), start, end, zone, now, cache);

    private void OnRecordingStateChanged(
        object? sender,
        RecordingStateChangedEventArgs e)
        => ReloadLive();

    private void ReloadLive()
    {
        if (!IsShown || SelectedDay is not null || zoomed || Interlocked.Exchange(ref reloadPending, 1) == 1)
        {
            return;
        }

        var wait = lastLoad + LiveReloadInterval - DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait).ConfigureAwait(false);
                }

                if (IsShown && Application.Current?.Dispatcher is { } dispatcher)
                {
                    await dispatcher.InvokeAsync(() => LoadCommand.Execute(parameter: null));
                }
            }
            finally
            {
                Interlocked.Exchange(ref reloadPending, 0);
            }
        });
    }
}