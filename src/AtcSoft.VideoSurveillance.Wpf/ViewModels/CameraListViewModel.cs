namespace Linksoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// View model for the camera list view with full CRUD, recording, and snapshot operations.
/// </summary>
public partial class CameraListViewModel : ViewModelBase
{
    private readonly GatewayService gatewayService;
    private readonly SurveillanceHubService hubService;

    [ObservableProperty]
    private ObservableCollection<CameraItemViewModel> cameras = [];

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraListViewModel"/> class.
    /// </summary>
    public CameraListViewModel(
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
            var result = await gatewayService
                .GetCamerasAsync()
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Cameras.Clear();

                if (result is not null)
                {
                    foreach (var camera in result)
                    {
                        Cameras.Add(CameraItemViewModel.FromCamera(camera));
                    }
                }
            });
        }
        catch (HttpRequestException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Cameras.Clear());
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    [RelayCommand("AddCamera")]
    private async Task AddCameraAsync()
    {
        var viewModel = new CameraEditDialogViewModel();
        var dialog = new CameraEditDialog(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await gatewayService
                .CreateCameraAsync(viewModel.BuildCreateRequest())
                .ConfigureAwait(false);

            await LoadAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToCreateCamera1,
                    ex.Message)));
        }
    }

    [RelayCommand("EditCamera")]
    private async Task EditCameraAsync(CameraItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Camera? camera;
        try
        {
            camera = await gatewayService
                .GetCameraByIdAsync(item.Id)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(Translations.FailedToLoadCameraDetails));
            return;
        }

        if (camera is null)
        {
            return;
        }

        var result = false;
        CameraEditDialogViewModel? viewModel = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            viewModel = new CameraEditDialogViewModel(camera);
            var dialog = new CameraEditDialog(viewModel)
            {
                Owner = Application.Current.MainWindow,
            };

            result = dialog.ShowDialog() == true;
        });

        if (!result || viewModel?.CameraId is null)
        {
            return;
        }

        try
        {
            await gatewayService
                .UpdateCameraAsync(viewModel.CameraId.Value, viewModel.BuildUpdateRequest())
                .ConfigureAwait(false);

            await LoadAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToUpdateCamera1,
                    ex.Message)));
        }
    }

    [RelayCommand("DeleteCamera")]
    private async Task DeleteCameraAsync(CameraItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (!UserDialog.Confirm(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.ConfirmDeleteCamera1,
                    item.DisplayName),
                Translations.DeleteCamera))
        {
            return;
        }

        try
        {
            await gatewayService
                .DeleteCameraAsync(item.Id)
                .ConfigureAwait(false);

            await LoadAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToDeleteCamera1,
                    ex.Message)));
        }
    }

    [RelayCommand("StartRecording")]
    private async Task StartRecordingAsync(CameraItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await gatewayService
                .StartRecordingAsync(item.Id)
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() => item.IsRecording = true);
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToStartRecording1,
                    ex.Message)));
        }
    }

    [RelayCommand("StopRecording")]
    private async Task StopRecordingAsync(CameraItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await gatewayService
                .StopRecordingAsync(item.Id)
                .ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() => item.IsRecording = false);
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToStopRecording1,
                    ex.Message)));
        }
    }

    [RelayCommand("CaptureSnapshot")]
    private async Task CaptureSnapshotAsync(CameraItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var snapshotData = await gatewayService
                .CaptureSnapshotAsync(item.Id)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(snapshotData))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    UserDialog.ShowWarning(Translations.NoSnapshotData, Translations.Snapshot));
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"snapshot_{item.DisplayName}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    Filter = "PNG Image|*.png",
                    Title = "Save Snapshot",
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var bytes = Convert.FromBase64String(snapshotData);
                    System.IO.File.WriteAllBytes(saveDialog.FileName, bytes);

                    UserDialog.ShowInfo(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Translations.SnapshotSavedTo1,
                            saveDialog.FileName),
                        Translations.SnapshotSaved);
                }
            });
        }
        catch (HttpRequestException ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                UserDialog.ShowError(string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.FailedToCaptureSnapshot1,
                    ex.Message)));
        }
    }

    private void OnConnectionStateChanged(
        SurveillanceHubService.ConnectionStateEvent e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var camera = Cameras.FirstOrDefault(c => c.Id == e.CameraId);
            if (camera is not null)
            {
                camera.ConnectionState = e.NewState;
            }
        });
    }

    private void OnRecordingStateChanged(
        SurveillanceHubService.RecordingStateEvent e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var camera = Cameras.FirstOrDefault(c => c.Id == e.CameraId);
            if (camera is not null)
            {
                camera.IsRecording =
                    string.Equals(e.NewState, "recording", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.NewState, "recordingMotion", StringComparison.OrdinalIgnoreCase);
            }
        });
    }
}