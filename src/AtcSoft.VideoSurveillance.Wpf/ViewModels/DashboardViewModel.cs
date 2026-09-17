namespace Linksoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// View model for the dashboard view showing live server stats.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly GatewayService gatewayService;
    private readonly SurveillanceHubService hubService;

    [ObservableProperty]
    private int totalCameras;

    [ObservableProperty]
    private int connectedCameras;

    [ObservableProperty]
    private int totalLayouts;

    [ObservableProperty]
    private int activeRecordings;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardViewModel"/> class.
    /// </summary>
    public DashboardViewModel(
        GatewayService gatewayService,
        SurveillanceHubService hubService)
    {
        ArgumentNullException.ThrowIfNull(gatewayService);
        ArgumentNullException.ThrowIfNull(hubService);

        this.gatewayService = gatewayService;
        this.hubService = hubService;

        this.hubService.OnConnectionStateChanged += OnConnectionStateChanged;
        this.hubService.OnRecordingStateChanged += OnRecordingStateChanged;
    }

    [RelayCommand("Load")]
    private async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var cameras = await gatewayService
                .GetCamerasAsync()
                .ConfigureAwait(false);

            var layouts = await gatewayService
                .GetLayoutsAsync()
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (cameras is not null)
                {
                    TotalCameras = cameras.Length;
                    ConnectedCameras = cameras.Count(c =>
                        c.ConnectionState == CameraConnectionState.Connected);
                    ActiveRecordings = cameras.Count(c => c.IsRecording);
                }
                else
                {
                    TotalCameras = 0;
                    ConnectedCameras = 0;
                    ActiveRecordings = 0;
                }

                TotalLayouts = layouts?.Length ?? 0;
            });
        }
        catch (HttpRequestException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TotalCameras = 0;
                ConnectedCameras = 0;
                TotalLayouts = 0;
                ActiveRecordings = 0;
            });
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    private void OnConnectionStateChanged(
        SurveillanceHubService.ConnectionStateEvent e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (string.Equals(e.NewState, "connected", StringComparison.OrdinalIgnoreCase))
            {
                ConnectedCameras++;
            }
            else
            {
                ConnectedCameras = Math.Max(0, ConnectedCameras - 1);
            }
        });
    }

    private void OnRecordingStateChanged(
        SurveillanceHubService.RecordingStateEvent e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (string.Equals(e.NewState, "recording", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.NewState, "recordingMotion", StringComparison.OrdinalIgnoreCase))
            {
                ActiveRecordings++;
            }
            else if (string.Equals(e.OldState, "recording", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(e.OldState, "recordingMotion", StringComparison.OrdinalIgnoreCase))
            {
                ActiveRecordings = Math.Max(0, ActiveRecordings - 1);
            }
        });
    }
}