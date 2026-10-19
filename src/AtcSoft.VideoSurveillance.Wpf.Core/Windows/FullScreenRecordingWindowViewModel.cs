// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
namespace AtcSoft.VideoSurveillance.Wpf.Core.Windows;

/// <summary>
/// ViewModel for the fullscreen recording playback window.
/// Uses WPF MediaElement for local file playback.
/// </summary>
public sealed partial class FullScreenRecordingWindowViewModel : ViewModelDialogBase, IDisposable
{
    /// <summary>
    /// How many stretches the activity timeline shows: one per pixel of the seek bar under it.
    /// </summary>
    public const int ActivityBucketCount = 400;

    private static readonly double[] SpeedOptions = [1.0, 2.0, 4.0, 8.0, 16.0];

    // A jump to an event lands this much before it, so its start is seen; and "previous" right
    // after a jump goes one further back.
    private static readonly TimeSpan EventLeadIn = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PreviousEventSlack = TimeSpan.FromSeconds(2);

    private readonly PlaybackOverlaySettings overlaySettings;
    private readonly Func<RecordingSegment?>? nextSegment;
    private readonly CancellationTokenSource activityCancellation = new();
    private string filePath;
    private DateTime? recordingStartTime;
    private RecordingActivityLoader? activityLoader;
    private TimeSpan startPosition;

    // Counts the recordings played in this window: an activity that arrives for an earlier one is dropped.
    private int segment;
    private IReadOnlyList<ActivityEvent> activityEvents = [];
    private MediaElement? mediaElement;
    private DispatcherTimer? overlayHideTimer;
    private DispatcherTimer? positionUpdateTimer;
    private bool disposed;
    private bool isSeeking;
    private bool isUpdatingPositionFromPlayer;
    private int currentSpeedIndex;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private bool isOverlayVisible = true;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string positionText = "00:00";

    [ObservableProperty]
    private string durationText = "00:00";

    [ObservableProperty]
    private double seekPosition;

    [ObservableProperty]
    private double seekMaximum = 100;

    [ObservableProperty]
    private bool canSeek;

    [ObservableProperty]
    private double playbackSpeed = 1.0;

    [ObservableProperty]
    private string speedText = "1x";

    [ObservableProperty]
    private string recordingTimeText = string.Empty;

    [ObservableProperty]
    private bool showFilename = true;

    [ObservableProperty]
    private SolidColorBrush filenameColor = Brushes.White;

    [ObservableProperty]
    private bool showTimestamp = true;

    [ObservableProperty]
    private SolidColorBrush timestampColor = Brushes.White;

    /// <summary>
    /// The clockwise angle the picture is turned by, read from the
    /// recording's rotation flag. MediaElement ignores that flag, so the
    /// window applies it.
    /// </summary>
    [ObservableProperty]
    private double videoRotationAngle;

    /// <summary>
    /// The activity of the recording for its timeline, or <see langword="null"/> while there is none.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<ActivityBucket>? activityBuckets;

    [ObservableProperty]
    private bool hasActivity;

    /// <summary>
    /// Where the player is, as a fraction of the recording, for the timeline.
    /// </summary>
    [ObservableProperty]
    private double activityPosition;

    /// <summary>
    /// Initializes a new instance of the <see cref="FullScreenRecordingWindowViewModel"/> class.
    /// </summary>
    /// <param name="filePath">The path to the recording file.</param>
    /// <param name="fileName">The display name of the recording file.</param>
    /// <param name="overlaySettings">The playback overlay settings.</param>
    /// <param name="recordingStartTime">
    /// When the recording started, in local time. When <see langword="null"/>
    /// it is read from the file name, which is local time for recordings made
    /// by the standalone app. A caller that knows the real start time (the
    /// server names its files in UTC) should pass it.
    /// </param>
    /// <param name="activityLoader">
    /// Gets the recording's activity for the timeline under the player; without one, or for a
    /// recording without an activity index, there is no timeline.
    /// </param>
    /// <param name="startPosition">Where to start in the recording: a timeline plays from a moment.</param>
    /// <param name="nextSegment">
    /// Gets the recording to move on to when this one ends (a timeline plays a camera's segments one
    /// after another); <see langword="null"/>, or a function that returns it, ends playback.
    /// </param>
    public FullScreenRecordingWindowViewModel(
        string filePath,
        string fileName,
        PlaybackOverlaySettings? overlaySettings = null,
        DateTime? recordingStartTime = null,
        RecordingActivityLoader? activityLoader = null,
        TimeSpan? startPosition = null,
        Func<RecordingSegment?>? nextSegment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        this.filePath = filePath;
        this.activityLoader = activityLoader;
        this.startPosition = startPosition ?? TimeSpan.Zero;
        this.nextSegment = nextSegment;
        this.overlaySettings = overlaySettings ?? new PlaybackOverlaySettings();
        FileName = fileName;
        this.recordingStartTime = recordingStartTime ?? ParseRecordingStartTime(fileName);

        // Apply overlay settings
        ShowFilename = this.overlaySettings.ShowFilename;
        FilenameColor = ParseColor(this.overlaySettings.FilenameColor, Brushes.White);
        ShowTimestamp = this.overlaySettings.ShowTimestamp;
        TimestampColor = ParseColor(this.overlaySettings.TimestampColor, new SolidColorBrush(Color.FromRgb(255, 215, 0)));

        UpdateRecordingTimeText(0);

        StartOverlayHideTimer();
        StartPositionUpdateTimer();
    }

    /// <summary>
    /// Occurs when the window requests to be closed.
    /// </summary>
    public event EventHandler<DialogClosedEventArgs>? CloseRequested;

    /// <summary>
    /// Sets the MediaElement for playback control.
    /// Must be called from code-behind after the window is created.
    /// </summary>
    /// <param name="element">The MediaElement control.</param>
    public void SetMediaElement(MediaElement element)
    {
        mediaElement = element;
        mediaElement.MediaOpened += OnMediaOpened;
        mediaElement.MediaEnded += OnMediaEnded;

        LoadVideoRotation();

        // Open and play the recording
        mediaElement.Source = new Uri(filePath);
        mediaElement.Play();
        IsPlaying = true;
    }

    /// <summary>
    /// Called when the mouse moves in the window.
    /// </summary>
    public void OnMouseMoved()
    {
        IsOverlayVisible = true;
        overlayHideTimer?.Stop();
        overlayHideTimer?.Start();
    }

    /// <summary>
    /// Called when the seek slider drag starts.
    /// </summary>
    public void OnSeekStarted()
    {
        isSeeking = true;
    }

    /// <summary>
    /// Called when the seek slider drag ends.
    /// </summary>
    public void OnSeekCompleted()
    {
        if (isSeeking && mediaElement is not null)
        {
            mediaElement.Position = TimeSpan.FromTicks((long)SeekPosition);
            isSeeking = false;
        }
    }

    /// <summary>
    /// Called when the seek slider value changes.
    /// </summary>
    public void OnSeekValueChanged()
    {
        // Only seek if the change came from user interaction, not from the timer
        if (!isUpdatingPositionFromPlayer && mediaElement is not null)
        {
            mediaElement.Position = TimeSpan.FromTicks((long)SeekPosition);
            UpdateRecordingTimeText((long)SeekPosition);
        }
    }

    private void UpdateRecordingTimeText(long positionTicks)
    {
        if (recordingStartTime.HasValue)
        {
            var currentTime = recordingStartTime.Value.AddTicks(positionTicks);
            RecordingTimeText = currentTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        else
        {
            // Fallback to playback position if we couldn't parse the start time
            RecordingTimeText = FormatDuration(positionTicks);
        }
    }

    private static DateTime? ParseRecordingStartTime(string fileName)
        => RecordingPathHelper.TryParseTimestamp(fileName, out var startTime)
            ? startTime
            : null;

    private static SolidColorBrush ParseColor(
        string colorName,
        SolidColorBrush defaultBrush)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorName);
            return new SolidColorBrush(color);
        }
        catch
        {
            return defaultBrush;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (mediaElement is null)
        {
            return;
        }

        if (IsPlaying)
        {
            mediaElement.Pause();
            IsPlaying = false;
        }
        else
        {
            mediaElement.Play();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: true));
    }

    [RelayCommand]
    private void CycleSpeed()
    {
        currentSpeedIndex = (currentSpeedIndex + 1) % SpeedOptions.Length;
        PlaybackSpeed = SpeedOptions[currentSpeedIndex];
        SpeedText = $"{PlaybackSpeed:G}x";

        if (mediaElement is not null)
        {
            mediaElement.SpeedRatio = PlaybackSpeed;
        }
    }

    private void LoadVideoRotation()
    {
        // Probing opens the file (or URL), so keep it off the UI thread.
        var path = filePath;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = Task.Run(Probe, CancellationToken.None);

        void Probe()
        {
            int degrees;
            try
            {
                degrees = MediaProbe.GetRotation(path);
            }
            catch (Exception)
            {
                // No rotation flag is the same as an unreadable one: play upright.
                return;
            }

            if (degrees != 0)
            {
                _ = dispatcher.BeginInvoke(() => VideoRotationAngle = degrees);
            }
        }
    }

    private void OnMediaOpened(
        object? sender,
        RoutedEventArgs e)
    {
        if (mediaElement?.NaturalDuration.HasTimeSpan == true)
        {
            var duration = mediaElement.NaturalDuration.TimeSpan.Ticks;
            DurationText = FormatDuration(duration);
            SeekMaximum = duration > 0 ? duration : 100;
            CanSeek = duration > 0;

            if (startPosition > TimeSpan.Zero && duration > 0)
            {
                SeekTo(startPosition.Ticks / (double)duration);
            }

            if (activityLoader is not null && duration > 0)
            {
                _ = LoadActivityAsync(activityLoader, segment, TimeSpan.FromTicks(duration).TotalSeconds, Dispatcher.CurrentDispatcher);
            }
        }
    }

    private async Task LoadActivityAsync(
        RecordingActivityLoader loader,
        int forSegment,
        double durationSeconds,
        Dispatcher dispatcher)
    {
        IReadOnlyList<ActivityBucket>? buckets;
        try
        {
            buckets = await loader(ActivityBucketCount, durationSeconds, activityCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No activity is the same as an unreadable one: the recording plays without a timeline.
            return;
        }

        if (buckets is not { Count: > 0 })
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (disposed || forSegment != segment)
            {
                return;
            }

            activityEvents = ActivityTimelineLayout.FindEvents(buckets);
            ActivityBuckets = buckets;
            HasActivity = true;
            CommandManager.InvalidateRequerySuggested();
        });
    }

    [RelayCommand]
    private void SeekActivity(double fraction)
        => SeekTo(fraction);

    [RelayCommand(CanExecute = nameof(CanJumpToEvent))]
    private void NextEvent()
    {
        if (ActivityTimelineLayout.NextEventStart(activityEvents, CurrentFraction() + Fraction(EventLeadIn)) is { } start)
        {
            SeekTo(start - Fraction(EventLeadIn));
        }
    }

    [RelayCommand(CanExecute = nameof(CanJumpToEvent))]
    private void PreviousEvent()
    {
        var at = CurrentFraction() + Fraction(EventLeadIn);
        if (ActivityTimelineLayout.PreviousEventStart(activityEvents, at, Fraction(PreviousEventSlack)) is { } start)
        {
            SeekTo(start - Fraction(EventLeadIn));
        }
    }

    private bool CanJumpToEvent()
        => CanSeek && activityEvents.Count > 0;

    private double CurrentFraction()
        => mediaElement is not null && SeekMaximum > 0
            ? mediaElement.Position.Ticks / SeekMaximum
            : 0;

    private double Fraction(TimeSpan span)
        => SeekMaximum > 0 ? span.Ticks / SeekMaximum : 0;

    private void SeekTo(double fraction)
    {
        if (mediaElement is null || !CanSeek)
        {
            return;
        }

        var ticks = (long)(Math.Clamp(fraction, 0, 1) * SeekMaximum);
        mediaElement.Position = TimeSpan.FromTicks(ticks);

        isUpdatingPositionFromPlayer = true;
        try
        {
            SeekPosition = ticks;
            PositionText = FormatDuration(ticks);
            ActivityPosition = ticks / SeekMaximum;
        }
        finally
        {
            isUpdatingPositionFromPlayer = false;
        }

        UpdateRecordingTimeText(ticks);
    }

    private void OnMediaEnded(
        object? sender,
        RoutedEventArgs e)
    {
        if (mediaElement is not null && nextSegment?.Invoke() is { } next)
        {
            PlayNext(mediaElement, next);
            return;
        }

        IsPlaying = false;
    }

    // Moves on to the next recording in the same window, from its start.
    private void PlayNext(
        MediaElement element,
        RecordingSegment next)
    {
        segment++;
        filePath = next.FilePath;
        FileName = next.FileName;
        recordingStartTime = next.RecordingStartTime ?? ParseRecordingStartTime(next.FileName);
        activityLoader = next.ActivityLoader;
        startPosition = TimeSpan.Zero;

        activityEvents = [];
        ActivityBuckets = null;
        HasActivity = false;
        ActivityPosition = 0;
        CanSeek = false;
        isUpdatingPositionFromPlayer = true;
        try
        {
            SeekPosition = 0;
            PositionText = FormatDuration(0);
        }
        finally
        {
            isUpdatingPositionFromPlayer = false;
        }

        UpdateRecordingTimeText(0);
        VideoRotationAngle = 0;
        LoadVideoRotation();
        CommandManager.InvalidateRequerySuggested();

        element.Source = new Uri(filePath);
        element.Play();
        IsPlaying = true;
    }

    private void StartOverlayHideTimer()
    {
        overlayHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };

        overlayHideTimer.Tick += (_, _) =>
        {
            IsOverlayVisible = false;
            overlayHideTimer.Stop();
        };

        overlayHideTimer.Start();
    }

    private void StartPositionUpdateTimer()
    {
        positionUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };

        positionUpdateTimer.Tick += (_, _) =>
        {
            if (mediaElement is not null)
            {
                var position = mediaElement.Position.Ticks;

                // Always update the recording time text
                UpdateRecordingTimeText(position);

                if (!isSeeking)
                {
                    // Set flag to prevent OnSeekValueChanged from triggering a seek
                    isUpdatingPositionFromPlayer = true;
                    try
                    {
                        SeekPosition = position;
                        PositionText = FormatDuration(position);
                        ActivityPosition = SeekMaximum > 0 ? position / SeekMaximum : 0;
                    }
                    finally
                    {
                        isUpdatingPositionFromPlayer = false;
                    }
                }
            }
        };

        positionUpdateTimer.Start();
    }

    private static string FormatDuration(long ticks)
    {
        var timeSpan = TimeSpan.FromTicks(ticks);
        return timeSpan.Hours > 0
            ? $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
            : $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            activityCancellation.Cancel();
            activityCancellation.Dispose();

            overlayHideTimer?.Stop();
            overlayHideTimer = null;

            positionUpdateTimer?.Stop();
            positionUpdateTimer = null;

            if (mediaElement is not null)
            {
                mediaElement.MediaOpened -= OnMediaOpened;
                mediaElement.MediaEnded -= OnMediaEnded;
                mediaElement.Stop();
                mediaElement = null;
            }
        }

        disposed = true;
    }
}