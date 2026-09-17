// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
namespace Linksoft.VideoSurveillance.Wpf.Core.Windows;

/// <summary>
/// ViewModel for the fullscreen recording playback window.
/// Uses WPF MediaElement for local file playback.
/// </summary>
public sealed partial class FullScreenRecordingWindowViewModel : ViewModelDialogBase, IDisposable
{
    private static readonly double[] SpeedOptions = [1.0, 2.0, 4.0, 8.0, 16.0];

    private readonly string filePath;
    private readonly DateTime? recordingStartTime;
    private readonly PlaybackOverlaySettings overlaySettings;
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
    /// Initializes a new instance of the <see cref="FullScreenRecordingWindowViewModel"/> class.
    /// </summary>
    /// <param name="filePath">The path to the recording file.</param>
    /// <param name="fileName">The display name of the recording file.</param>
    /// <param name="overlaySettings">The playback overlay settings.</param>
    public FullScreenRecordingWindowViewModel(
        string filePath,
        string fileName,
        PlaybackOverlaySettings? overlaySettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        this.filePath = filePath;
        this.overlaySettings = overlaySettings ?? new PlaybackOverlaySettings();
        FileName = fileName;
        recordingStartTime = ParseRecordingStartTime(fileName);

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
    {
        // Filename format: {CameraName}_{yyyyMMdd_HHmmss}.ext
        // Example: FrontDoor_20240126_143215.mp4
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        // Find the timestamp pattern at the end: _yyyyMMdd_HHmmss
        var lastUnderscore = nameWithoutExtension.LastIndexOf('_');
        if (lastUnderscore < 9)
        {
            return null;
        }

        // Get the timestamp part (should be like "20240126_143215")
        var secondLastUnderscore = nameWithoutExtension.LastIndexOf('_', lastUnderscore - 1);
        if (secondLastUnderscore < 0)
        {
            return null;
        }

        var timestampPart = nameWithoutExtension[(secondLastUnderscore + 1)..];

        if (DateTime.TryParseExact(
            timestampPart,
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            return result;
        }

        return null;
    }

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
        }
    }

    private void OnMediaEnded(
        object? sender,
        RoutedEventArgs e)
    {
        IsPlaying = false;
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