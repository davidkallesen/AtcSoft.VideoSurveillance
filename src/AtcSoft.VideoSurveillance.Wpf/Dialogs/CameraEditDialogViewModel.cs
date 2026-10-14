namespace AtcSoft.VideoSurveillance.Wpf.Dialogs;

/// <summary>
/// View model for the camera edit dialog.
/// </summary>
public partial class CameraEditDialogViewModel : ViewModelBase
{
    private readonly Guid? cameraId;
    private readonly Func<Guid, Task<CameraConnectionType?>>? detectConnectionType;

    public event EventHandler<DialogClosedEventArgs>? CloseRequested;

    [ObservableProperty(AfterChangedCallback = nameof(OnFormChanged))]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty(AfterChangedCallback = nameof(OnFormChanged))]
    private string ipAddress = string.Empty;

    [ObservableProperty]
    private int port = 554;

    [ObservableProperty]
    private CameraProtocol selectedProtocol = CameraProtocol.Rtsp;

    [ObservableProperty]
    private string path = string.Empty;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private CameraConnectionType selectedConnectionType = CameraConnectionType.None;

    [ObservableProperty]
    private CameraRotation selectedRotation = CameraRotation.None;

    [ObservableProperty]
    private CameraOverlayPosition selectedOverlayPosition = CameraOverlayPosition.TopLeft;

    [ObservableProperty]
    private bool streamUseLowLatencyMode;

    [ObservableProperty]
    private int streamMaxLatencyMs;

    [ObservableProperty]
    private CameraStreamRtspTransport selectedRtspTransport = CameraStreamRtspTransport.Tcp;

    [ObservableProperty]
    private int streamBufferDurationMs;

    /// <summary>
    /// Gets whether this is an edit operation.
    /// </summary>
    public bool IsEdit { get; }

    /// <summary>
    /// Gets the dialog title.
    /// </summary>
    public string DialogTitle => IsEdit ? "Edit Camera" : "Add Camera";

    /// <summary>
    /// Gets the protocol items for the combobox.
    /// </summary>
    public IDictionary<string, string> ProtocolItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CameraProtocol.Rtsp)] = "RTSP",
        [nameof(CameraProtocol.Http)] = "HTTP",
        [nameof(CameraProtocol.Https)] = "HTTPS",
    };

    /// <summary>
    /// Gets the connection types offered for a network camera.
    /// </summary>
    public IDictionary<string, string> ConnectionTypeItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CameraConnectionType.None)] = "Not set",
        [nameof(CameraConnectionType.Lan)] = "Wired (LAN)",
        [nameof(CameraConnectionType.Wifi)] = "Wi-Fi",
    };

    /// <summary>
    /// Gets the clockwise rotations offered for the camera picture.
    /// </summary>
    public IDictionary<string, string> RotationItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CameraRotation.None)] = "0\u00B0",
        [nameof(CameraRotation.Rotate90)] = "90\u00B0",
        [nameof(CameraRotation.Rotate180)] = "180\u00B0",
        [nameof(CameraRotation.Rotate270)] = "270\u00B0",
    };

    /// <summary>
    /// Gets the overlay position items for the combobox.
    /// </summary>
    public IDictionary<string, string> OverlayPositionItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CameraOverlayPosition.TopLeft)] = "Top Left",
        [nameof(CameraOverlayPosition.TopRight)] = "Top Right",
        [nameof(CameraOverlayPosition.BottomLeft)] = "Bottom Left",
        [nameof(CameraOverlayPosition.BottomRight)] = "Bottom Right",
    };

    /// <summary>
    /// Gets the RTSP transport items for the combobox.
    /// </summary>
    public IDictionary<string, string> RtspTransportItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CameraStreamRtspTransport.Tcp)] = "TCP",
        [nameof(CameraStreamRtspTransport.Udp)] = "UDP",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraEditDialogViewModel"/> class for adding a new camera.
    /// </summary>
    public CameraEditDialogViewModel()
    {
        IsEdit = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraEditDialogViewModel"/> class for editing an existing camera.
    /// </summary>
    public CameraEditDialogViewModel(
        Camera camera,
        Func<Guid, Task<CameraConnectionType?>>? detectConnectionType = null)
    {
        ArgumentNullException.ThrowIfNull(camera);

        this.detectConnectionType = detectConnectionType;

        IsEdit = true;
        cameraId = camera.Id;
        displayName = camera.DisplayName;
        description = camera.Description ?? string.Empty;
        ipAddress = camera.IpAddress ?? string.Empty;
        port = camera.Port ?? 0;
        selectedProtocol = camera.Protocol ?? CameraProtocol.Rtsp;
        path = camera.Path ?? string.Empty;
        username = camera.Username ?? string.Empty;
        selectedOverlayPosition = camera.OverlayPosition ?? CameraOverlayPosition.TopLeft;
        selectedConnectionType = camera.ConnectionType ?? CameraConnectionType.None;
        selectedRotation = camera.Rotation ?? CameraRotation.None;
        streamUseLowLatencyMode = camera.StreamUseLowLatencyMode ?? false;
        streamMaxLatencyMs = camera.StreamMaxLatencyMs ?? 0;
        selectedRtspTransport = camera.StreamRtspTransport ?? CameraStreamRtspTransport.Tcp;
        streamBufferDurationMs = camera.StreamBufferDurationMs ?? 0;
    }

    /// <summary>
    /// Builds a <see cref="CreateCameraRequest"/> from the form fields.
    /// </summary>
    public CreateCameraRequest BuildCreateRequest()
        => new(
            DisplayName: DisplayName,
            Description: Description,
            Source: CameraSource.Network,
            ConnectionType: SelectedConnectionType,
            Rotation: SelectedRotation,
            IpAddress: IpAddress,
            Protocol: SelectedProtocol,
            Path: Path,
            Username: Username,
            Password: string.IsNullOrEmpty(Password) ? null! : Password,
            UsbDeviceId: string.Empty,
            UsbFriendlyName: string.Empty,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: string.Empty,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: SelectedOverlayPosition,
            StreamUseLowLatencyMode: StreamUseLowLatencyMode,
            StreamMaxLatencyMs: StreamMaxLatencyMs,
            StreamRtspTransport: SelectedRtspTransport,
            StreamBufferDurationMs: StreamBufferDurationMs,
            Port: Port);

    /// <summary>
    /// Builds an <see cref="UpdateCameraRequest"/> from the form fields.
    /// </summary>
    public UpdateCameraRequest BuildUpdateRequest()
        => new(
            DisplayName: DisplayName,
            Description: Description,
            Source: CameraSource.Network,
            ConnectionType: SelectedConnectionType,
            Rotation: SelectedRotation,
            IpAddress: IpAddress,
            Port: Port,
            Protocol: SelectedProtocol,
            Path: Path,
            Username: Username,
            Password: string.IsNullOrEmpty(Password) ? null! : Password,
            UsbDeviceId: string.Empty,
            UsbFriendlyName: string.Empty,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: string.Empty,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: SelectedOverlayPosition,
            StreamUseLowLatencyMode: StreamUseLowLatencyMode,
            StreamMaxLatencyMs: StreamMaxLatencyMs,
            StreamRtspTransport: SelectedRtspTransport,
            StreamBufferDurationMs: StreamBufferDurationMs);

    /// <summary>
    /// Gets the camera ID for update operations.
    /// </summary>
    public Guid? CameraId => cameraId;

    /// <summary>
    /// Gets a value indicating whether server-side detection is available
    /// (edit mode only: the server probes with the stored credentials).
    /// </summary>
    public bool IsConnectionTypeDetectionAvailable
        => detectConnectionType is not null;

    [ObservableProperty]
    private string connectionTypeDetectionStatus = string.Empty;

    [ObservableProperty]
    private bool isDetectingConnectionType;

    /// <summary>
    /// Explicit detection: applies a detected LAN / Wi-Fi value to the picker;
    /// the user can still change it before saving.
    /// </summary>
    [RelayCommand("DetectConnectionType", CanExecute = nameof(CanDetectConnectionType))]
    private async Task DetectConnectionTypeAsync()
    {
        IsDetectingConnectionType = true;
        ConnectionTypeDetectionStatus = "Detecting…";

        try
        {
            var detected = await detectConnectionType!(cameraId!.Value).ConfigureAwait(true);
            if (detected is CameraConnectionType.Lan or CameraConnectionType.Wifi)
            {
                SelectedConnectionType = detected.Value;
                ConnectionTypeDetectionStatus = "Detected: " + ConnectionTypeItems[detected.Value.ToString()];
            }
            else
            {
                ConnectionTypeDetectionStatus = "Could not detect the connection type";
            }
        }
        finally
        {
            IsDetectingConnectionType = false;
        }
    }

    private bool CanDetectConnectionType()
        => detectConnectionType is not null && cameraId is not null && !IsDetectingConnectionType;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
        => CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: true));

    [RelayCommand]
    private void Cancel()
        => CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: false));

    private bool CanSave()
        => !string.IsNullOrWhiteSpace(DisplayName) &&
           !string.IsNullOrWhiteSpace(IpAddress);

    private static void OnFormChanged()
        => CommandManager.InvalidateRequerySuggested();
}