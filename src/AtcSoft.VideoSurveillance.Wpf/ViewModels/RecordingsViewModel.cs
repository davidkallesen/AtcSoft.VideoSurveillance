namespace Linksoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// View model for the recordings browser view.
/// </summary>
[SuppressMessage("", "S2325:Make properties static", Justification = "XAML binding requires instance properties")]
public sealed partial class RecordingsViewModel : ViewModelBase
{
    private const string AllCamerasKey = "_ALL_";

    private readonly GatewayService gatewayService;
    private readonly Uri apiBaseUri;

    [ObservableProperty(AfterChangedCallback = nameof(OnFilterChanged))]
    private string selectedCameraFilter = AllCamerasKey;

    [ObservableProperty(AfterChangedCallback = nameof(OnFilterChanged))]
    private string selectedDayFilter = "_ALL_";

    [ObservableProperty(AfterChangedCallback = nameof(OnFilterChanged))]
    private string selectedTimeFilter = "_ALL_";

    [ObservableProperty(AfterChangedCallback = nameof(OnSelectedRecordingChanged))]
    private RecordingEntryViewModel? selectedRecording;

    [ObservableProperty]
    private ObservableCollection<RecordingEntryViewModel> recordings = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingsViewModel"/> class.
    /// </summary>
    public RecordingsViewModel(
        GatewayService gatewayService,
        Uri apiBaseUri)
    {
        ArgumentNullException.ThrowIfNull(gatewayService);
        ArgumentNullException.ThrowIfNull(apiBaseUri);

        this.gatewayService = gatewayService;
        this.apiBaseUri = apiBaseUri;
    }

    /// <summary>
    /// Gets the available camera filters.
    /// </summary>
    public ObservableCollection<KeyValuePair<string, string>> CameraFilters { get; } = [];

    /// <summary>
    /// Gets the day filter items.
    /// </summary>
    public IDictionary<string, string> DayFilterItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["_ALL_"] = "All Days",
        ["TODAY"] = "Today",
        ["YESTERDAY"] = "Yesterday",
        ["LAST7"] = "Last 7 Days",
        ["LAST30"] = "Last 30 Days",
        ["THISWEEK"] = "This Week",
        ["THISMONTH"] = "This Month",
    };

    /// <summary>
    /// Gets the time filter items.
    /// </summary>
    public IDictionary<string, string> TimeFilterItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["_ALL_"] = "All Times",
        ["0_6"] = "00:00 - 06:00",
        ["6_12"] = "06:00 - 12:00",
        ["12_18"] = "12:00 - 18:00",
        ["18_24"] = "18:00 - 00:00",
    };

    /// <summary>
    /// Gets the filtered recordings based on selected filters.
    /// </summary>
    public IEnumerable<RecordingEntryViewModel> FilteredRecordings
    {
        get
        {
            var result = Recordings.AsEnumerable();

            if (!string.IsNullOrEmpty(SelectedCameraFilter) && SelectedCameraFilter != AllCamerasKey)
            {
                result = result.Where(r =>
                    string.Equals(r.CameraName, SelectedCameraFilter, StringComparison.OrdinalIgnoreCase));
            }

            result = ApplyDayFilter(result);
            result = ApplyTimeFilter(result);

            return result;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a recording can be played.
    /// </summary>
    public bool CanPlay => SelectedRecording is not null;

    [RelayCommand("Load")]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var apiRecordings = await gatewayService
                .GetRecordingsAsync()
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (apiRecordings is not null && apiRecordings.Length > 0)
                {
                    Recordings = new ObservableCollection<RecordingEntryViewModel>(
                        apiRecordings
                            .Select(r => new RecordingEntryViewModel(r, apiBaseUri))
                            .OrderByDescending(r => r.RecordingTime));
                }
                else
                {
                    Recordings = [];
                }

                UpdateCameraFilters();
                UpdateStatusMessage();
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Recordings = [];
                UpdateCameraFilters();
                StatusMessage = $"Failed to load recordings: {ex.Message}";
            });
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsLoading = false;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Window self-disposes on close via the Closed event handler.")]
    private void Play()
    {
        if (SelectedRecording is null)
        {
            return;
        }

        var vm = new Linksoft.VideoSurveillance.Wpf.Core.Windows.FullScreenRecordingWindowViewModel(
            SelectedRecording.PlaybackUrl.AbsoluteUri,
            SelectedRecording.FileName);

        var window = new Linksoft.VideoSurveillance.Wpf.Core.Windows.FullScreenRecordingWindow(vm);
        window.Show();
    }

    [RelayCommand]
    private Task Refresh()
        => LoadAsync();

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(FilteredRecordings));
        UpdateStatusMessage();
    }

    private void OnSelectedRecordingChanged()
    {
        OnPropertyChanged(nameof(CanPlay));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateCameraFilters()
    {
        var currentSelection = SelectedCameraFilter;

        CameraFilters.Clear();
        CameraFilters.Add(new KeyValuePair<string, string>(AllCamerasKey, "All Cameras"));

        var cameraNames = Recordings
            .Select(r => r.CameraName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var cameraName in cameraNames)
        {
            CameraFilters.Add(new KeyValuePair<string, string>(cameraName, cameraName));
        }

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
        StatusMessage = count == 0
            ? "No recordings found"
            : $"{count} recording{(count == 1 ? string.Empty : "s")} found";
    }

    private IEnumerable<RecordingEntryViewModel> ApplyDayFilter(
        IEnumerable<RecordingEntryViewModel> source)
    {
        var today = DateTime.Today;

        return SelectedDayFilter switch
        {
            "_ALL_" => source,
            "TODAY" => source.Where(r => r.RecordingTime.LocalDateTime.Date == today),
            "YESTERDAY" => source.Where(r => r.RecordingTime.LocalDateTime.Date == today.AddDays(-1)),
            "LAST7" => source.Where(r => r.RecordingTime.LocalDateTime.Date >= today.AddDays(-6)),
            "LAST30" => source.Where(r => r.RecordingTime.LocalDateTime.Date >= today.AddDays(-29)),
            "THISWEEK" => source.Where(r => IsThisWeek(r.RecordingTime.LocalDateTime, today)),
            "THISMONTH" => source.Where(r =>
                r.RecordingTime.LocalDateTime.Year == today.Year &&
                r.RecordingTime.LocalDateTime.Month == today.Month),
            _ => source,
        };
    }

    private IEnumerable<RecordingEntryViewModel> ApplyTimeFilter(
        IEnumerable<RecordingEntryViewModel> source)
        => SelectedTimeFilter switch
        {
            "_ALL_" => source,
            "0_6" => source.Where(r => r.RecordingTime.LocalDateTime.Hour >= 0 && r.RecordingTime.LocalDateTime.Hour < 6),
            "6_12" => source.Where(r => r.RecordingTime.LocalDateTime.Hour >= 6 && r.RecordingTime.LocalDateTime.Hour < 12),
            "12_18" => source.Where(r => r.RecordingTime.LocalDateTime.Hour >= 12 && r.RecordingTime.LocalDateTime.Hour < 18),
            "18_24" => source.Where(r => r.RecordingTime.LocalDateTime.Hour >= 18 && r.RecordingTime.LocalDateTime.Hour < 24),
            _ => source,
        };

    private static bool IsThisWeek(
        DateTime date,
        DateTime today)
    {
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(6);

        return date.Date >= monday && date.Date <= sunday;
    }
}