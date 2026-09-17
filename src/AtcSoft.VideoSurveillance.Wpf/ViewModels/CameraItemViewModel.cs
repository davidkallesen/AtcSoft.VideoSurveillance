namespace Linksoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// Wraps a generated Camera model for DataGrid binding with in-place SignalR updates.
/// </summary>
public partial class CameraItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private Guid id;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string ipAddress = string.Empty;

    [ObservableProperty]
    private int port;

    [ObservableProperty]
    private CameraProtocol protocol;

    [ObservableProperty]
    private string connectionState = "disconnected";

    [ObservableProperty]
    private bool isRecording;

    /// <summary>
    /// Creates a <see cref="CameraItemViewModel"/> from a generated <see cref="Camera"/> model.
    /// </summary>
    public static CameraItemViewModel FromCamera(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        return new CameraItemViewModel
        {
            Id = camera.Id,
            DisplayName = camera.DisplayName,
            Description = camera.Description ?? string.Empty,
            IpAddress = camera.IpAddress,
            Port = camera.Port,
            Protocol = camera.Protocol ?? CameraProtocol.Rtsp,
            ConnectionState = camera.ConnectionState?.ToString() ?? "disconnected",
            IsRecording = camera.IsRecording,
        };
    }
}