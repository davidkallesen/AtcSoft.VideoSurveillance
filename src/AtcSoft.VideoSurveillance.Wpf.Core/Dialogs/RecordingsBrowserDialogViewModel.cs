namespace AtcSoft.VideoSurveillance.Wpf.Core.Dialogs;

/// <summary>
/// ViewModel for the recordings browser dialog.
/// </summary>
[SuppressMessage("", "S2325:Make properties static", Justification = "XAML binding requires instance properties")]
public partial class RecordingsBrowserDialogViewModel : ViewModelDialogBase
{
    private const string AllCamerasKey = "_ALL_";

    private readonly IApplicationSettingsService settingsService;

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedCameraFilterChanged))]
    private string selectedCameraFilter = AllCamerasKey;

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedDayFilterChanged))]
    private string selectedDayFilter = DropDownItemsFactory.DefaultDayFilter;

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedTimeFilterChanged))]
    private string selectedTimeFilter = DropDownItemsFactory.DefaultTimeFilter;

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedRecordingChanged))]
    private RecordingEntry? selectedRecording;

    [ObservableProperty]
    private ObservableCollection<RecordingEntry> recordings = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingsBrowserDialogViewModel"/> class.
    /// </summary>
    /// <param name="settingsService">The application settings service.</param>
    public RecordingsBrowserDialogViewModel(
        IApplicationSettingsService settingsService)
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        LoadRecordings();
    }

    /// <summary>
    /// Occurs when the dialog requests to be closed.
    /// </summary>
    public event EventHandler<DialogClosedEventArgs>? CloseRequested;

    /// <summary>
    /// Occurs when a recording should be played.
    /// </summary>
    public event EventHandler<RecordingEntry>? PlayRecordingRequested;

    /// <summary>
    /// Occurs when a thumbnail preview is requested.
    /// </summary>
    public event EventHandler<RecordingEntry>? ThumbnailPreviewRequested;

    /// <summary>
    /// Gets the dialog title.
    /// </summary>
    public static string DialogTitle => Translations.BrowseRecordings;

    /// <summary>
    /// Gets the available camera filters.
    /// </summary>
    public ObservableCollection<KeyValuePair<string, string>> CameraFilters { get; } = [];

    /// <summary>
    /// Gets the day filter items for the dropdown.
    /// </summary>
    public IDictionary<string, string> DayFilterItems
        => DropDownItemsFactory.DayFilterItems;

    /// <summary>
    /// Gets the time filter items for the dropdown.
    /// </summary>
    public IDictionary<string, string> TimeFilterItems
        => DropDownItemsFactory.TimeFilterItems;

    /// <summary>
    /// Gets the filtered recordings based on the selected filters.
    /// </summary>
    public IEnumerable<RecordingEntry> FilteredRecordings
    {
        get
        {
            var result = Recordings.AsEnumerable();

            // Camera filter
            if (!string.IsNullOrEmpty(SelectedCameraFilter) && SelectedCameraFilter != AllCamerasKey)
            {
                result = result.Where(r =>
                    string.Equals(r.CameraName, SelectedCameraFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Day filter
            result = ApplyDayFilter(result);

            // Time filter
            result = ApplyTimeFilter(result);

            return result;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a recording can be played.
    /// </summary>
    public bool CanPlay => SelectedRecording is not null;

    /// <summary>
    /// Gets a value indicating whether a recording can be deleted.
    /// </summary>
    public bool CanDelete => SelectedRecording is not null;

    /// <summary>
    /// Gets the playback overlay settings from the application settings.
    /// </summary>
    public PlaybackOverlaySettings PlaybackOverlaySettings
        => settingsService.Recording.PlaybackOverlay;

    /// <summary>
    /// Gets a value indicating whether the browser closes when a recording is played.
    /// </summary>
    public bool CloseOnPlay
        => settingsService.Recording.CloseRecordingsBrowserOnPlay;

    /// <summary>
    /// Gets the filters and the selection, to open the browser the same way again.
    /// </summary>
    /// <returns>The current state.</returns>
    public RecordingsBrowserState CaptureState()
        => new(
            SelectedCameraFilter,
            SelectedDayFilter,
            SelectedTimeFilter,
            SelectedRecording?.FilePath);

    /// <summary>
    /// Applies filters and a selection from an earlier browser. Whatever no
    /// longer exists is skipped: a camera without recordings falls back to
    /// all cameras, and a deleted recording leaves nothing selected.
    /// </summary>
    /// <param name="state">The state to apply.</param>
    public void RestoreState(RecordingsBrowserState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (CameraFilters.Any(f => f.Key == state.CameraFilter))
        {
            SelectedCameraFilter = state.CameraFilter;
        }

        if (DayFilterItems.ContainsKey(state.DayFilter))
        {
            SelectedDayFilter = state.DayFilter;
        }

        if (TimeFilterItems.ContainsKey(state.TimeFilter))
        {
            SelectedTimeFilter = state.TimeFilter;
        }

        SelectedRecording = state.SelectedFilePath is null
            ? null
            : FilteredRecordings.FirstOrDefault(r =>
                string.Equals(r.FilePath, state.SelectedFilePath, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadRecordings();
    }

    [RelayCommand]
    private void PreviewThumbnail(RecordingEntry? recording)
    {
        if (recording is null || !recording.HasThumbnail)
        {
            return;
        }

        ThumbnailPreviewRequested?.Invoke(this, recording);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        PlayRecordingRequested?.Invoke(this, SelectedRecording);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        try
        {
            // Delete the video file
            File.Delete(SelectedRecording.FilePath);

            // Also delete the thumbnail, wherever its layout put it
            var basePath = GetRecordingsBasePath();
            foreach (var thumbnailPath in RecordingPathHelper.GetThumbnailCandidates(SelectedRecording.FilePath, basePath))
            {
                try
                {
                    File.Delete(thumbnailPath);
                }
                catch
                {
                    // Ignore thumbnail deletion errors
                }
            }

            // The last recording of a day leaves an empty day folder behind
            _ = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(basePath, MediaCleanupRunner.EmptyDirectoryGracePeriod);

            Recordings.Remove(SelectedRecording);
            SelectedRecording = null;
            UpdateCameraFilters();
            OnPropertyChanged(nameof(FilteredRecordings));
            UpdateStatusMessage();
        }
        catch (IOException ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Translations.FailedWithStatus1, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Translations.FailedWithStatus1, ex.Message);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var basePath = GetRecordingsBasePath();
        if (Directory.Exists(basePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", basePath);
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: true));
    }

    private void OnSelectedCameraFilterChanged()
        => OnFilterChanged();

    private void OnSelectedDayFilterChanged()
        => OnFilterChanged();

    private void OnSelectedTimeFilterChanged()
        => OnFilterChanged();

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(FilteredRecordings));
        UpdateStatusMessage();
    }

    private void OnSelectedRecordingChanged()
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanDelete));
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadRecordings()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var basePath = GetRecordingsBasePath();

            if (!Directory.Exists(basePath))
            {
                Recordings.Clear();
                UpdateCameraFilters();
                StatusMessage = Translations.NoRecordingsFound;
                return;
            }

            var entries = new List<RecordingEntry>();

            // Each folder below the root is a camera. Its recordings may sit
            // directly in it or in a folder per day, so search the whole
            // camera folder. The thumbnails folder holds no recordings.
            foreach (var cameraFolder in Directory.GetDirectories(basePath))
            {
                var cameraName = Path.GetFileName(cameraFolder);
                if (cameraName.Equals(RecordingPathHelper.ThumbnailsFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(cameraFolder, "*.*", SearchOption.AllDirectories)
                    .Where(IsVideoFile))
                {
                    var info = new FileInfo(file);
                    var duration = GetVideoDuration(file);

                    entries.Add(new RecordingEntry
                    {
                        FilePath = file,
                        CameraName = cameraName,
                        RecordingRoot = basePath,
                        RecordingTime = RecordingPathHelper.TryParseTimestamp(info.Name, out var timestamp)
                            ? timestamp
                            : info.CreationTime,
                        FileSizeBytes = info.Length,
                        Duration = duration,
                    });
                }
            }

            // Sort by date descending (newest first)
            Recordings = new ObservableCollection<RecordingEntry>(
                entries.OrderByDescending(e => e.RecordingTime));

            UpdateCameraFilters();
            UpdateStatusMessage();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetRecordingsBasePath()
        => !string.IsNullOrEmpty(settingsService.Recording.RecordingPath)
            ? settingsService.Recording.RecordingPath
            : ApplicationPaths.DefaultRecordingsPath;

    private void UpdateCameraFilters()
    {
        var currentSelection = SelectedCameraFilter;

        CameraFilters.Clear();
        CameraFilters.Add(new KeyValuePair<string, string>(AllCamerasKey, Translations.AllCameras));

        var cameraNames = Recordings
            .Select(r => r.CameraName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var cameraName in cameraNames)
        {
            CameraFilters.Add(new KeyValuePair<string, string>(cameraName, cameraName));
        }

        // Restore selection if still valid, otherwise default to All
        if (CameraFilters.Any(f => f.Key == currentSelection))
        {
            SelectedCameraFilter = currentSelection;
        }
        else
        {
            SelectedCameraFilter = AllCamerasKey;
        }

        OnPropertyChanged(nameof(FilteredRecordings));
    }

    private void UpdateStatusMessage()
    {
        var count = FilteredRecordings.Count();
        if (count == 0)
        {
            StatusMessage = Translations.NoRecordingsFound;
        }
        else
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                Translations.RecordingsFound1,
                count);
        }
    }

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return ext is ".MP4" or ".MKV" or ".AVI";
    }

    /// <summary>
    /// Gets the length of a recording. A recording that is still being made
    /// has no length yet, and probing it only makes FFmpeg report a broken
    /// file, so it is left at zero.
    /// </summary>
    private static TimeSpan GetVideoDuration(string filePath)
        => RecordingPathHelper.IsBeingWritten(filePath)
            ? TimeSpan.Zero
            : MediaProbe.GetDuration(filePath);

    private IEnumerable<RecordingEntry> ApplyDayFilter(
        IEnumerable<RecordingEntry> recordings)
    {
        var today = DateTime.Today;

        return SelectedDayFilter switch
        {
            "_ALL_" => recordings,
            "TODAY" => recordings.Where(r => r.RecordingTime.Date == today),
            "YESTERDAY" => recordings.Where(r => r.RecordingTime.Date == today.AddDays(-1)),
            "LAST7" => recordings.Where(r => r.RecordingTime.Date >= today.AddDays(-6)),
            "LAST30" => recordings.Where(r => r.RecordingTime.Date >= today.AddDays(-29)),
            "THISWEEK" => recordings.Where(r => IsThisWeek(r.RecordingTime, today)),
            "THISMONTH" => recordings.Where(r =>
                r.RecordingTime.Year == today.Year && r.RecordingTime.Month == today.Month),
            _ => recordings,
        };
    }

    private IEnumerable<RecordingEntry> ApplyTimeFilter(
        IEnumerable<RecordingEntry> recordings)
        => SelectedTimeFilter switch
        {
            "_ALL_" => recordings,
            "0_6" => recordings.Where(r => r.RecordingTime.Hour >= 0 && r.RecordingTime.Hour < 6),
            "6_12" => recordings.Where(r => r.RecordingTime.Hour >= 6 && r.RecordingTime.Hour < 12),
            "12_18" => recordings.Where(r => r.RecordingTime.Hour >= 12 && r.RecordingTime.Hour < 18),
            "18_24" => recordings.Where(r => r.RecordingTime.Hour >= 18 && r.RecordingTime.Hour < 24),
            _ => recordings,
        };

    private static bool IsThisWeek(
        DateTime date,
        DateTime today)
    {
        // Get Monday of the current week (using Monday as start of week)
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(6);

        return date.Date >= monday && date.Date <= sunday;
    }
}