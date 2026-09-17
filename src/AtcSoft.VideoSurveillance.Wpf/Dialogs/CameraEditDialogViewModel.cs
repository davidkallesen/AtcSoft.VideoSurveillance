namespace Linksoft.VideoSurveillance.Wpf.Dialogs;

/// <summary>
/// View model for the camera edit dialog.
/// </summary>
public partial class CameraEditDialogViewModel : ViewModelBase
{
    private readonly Guid? cameraId;

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
    public CameraEditDialogViewModel(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        IsEdit = true;
        cameraId = camera.Id;
        displayName = camera.DisplayName;
        description = camera.Description ?? string.Empty;
        ipAddress = camera.IpAddress;
        port = camera.Port;
        selectedProtocol = camera.Protocol ?? CameraProtocol.Rtsp;
        path = camera.Path ?? string.Empty;
        username = camera.Username ?? string.Empty;
        selectedOverlayPosition = camera.OverlayPosition ?? CameraOverlayPosition.TopLeft;
        streamUseLowLatencyMode = camera.StreamUseLowLatencyMode;
        streamMaxLatencyMs = camera.StreamMaxLatencyMs;
        selectedRtspTransport = camera.StreamRtspTransport ?? CameraStreamRtspTransport.Tcp;
        streamBufferDurationMs = camera.StreamBufferDurationMs;
    }

    /// <summary>
    /// Builds a <see cref="CreateCameraRequest"/> from the form fields.
    /// </summary>
    public CreateCameraRequest BuildCreateRequest()
        => new(
            DisplayName: DisplayName,
            Description: Description,
            Source: CameraSource.Network,
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