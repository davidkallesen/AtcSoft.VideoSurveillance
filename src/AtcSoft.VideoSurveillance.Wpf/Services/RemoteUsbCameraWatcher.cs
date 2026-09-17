namespace Linksoft.VideoSurveillance.Wpf.Services;

using IUsbCameraWatcher = Linksoft.VideoSurveillance.Services.IUsbCameraWatcher;
using UsbCameraEventArgs = Linksoft.VideoSurveillance.Events.UsbCameraEventArgs;
using UsbDeviceDescriptor = Linksoft.VideoSurveillance.Models.UsbDeviceDescriptor;

/// <summary>
/// API-client implementation of <see cref="IUsbCameraWatcher"/>.
/// Subscribes to <see cref="IUsbLifecycleHubChannel"/> (which fronts
/// the SignalR <c>UsbCameraLifecycleChanged</c> message) and translates
/// server-side <c>Replugged</c> / <c>Unplugged</c> phases into the
/// arrival / removal events local consumers already understand.
/// </summary>
/// <remarks>
/// Phase mapping:
/// <list type="bullet">
///   <item><c>Replugged</c> → <see cref="DeviceArrived"/></item>
///   <item><c>Unplugged</c> → <see cref="DeviceRemoved"/></item>
/// </list>
/// Unknown phase strings are ignored; this keeps forward compatibility
/// when the server adds future lifecycle phases without forcing a
/// client redeploy.
/// </remarks>
public sealed class RemoteUsbCameraWatcher : IUsbCameraWatcher
{
    private readonly IUsbLifecycleHubChannel hubChannel;
    private readonly Lock syncRoot = new();

    private bool started;
    private bool disposed;

    public RemoteUsbCameraWatcher(IUsbLifecycleHubChannel hubChannel)
    {
        ArgumentNullException.ThrowIfNull(hubChannel);
        this.hubChannel = hubChannel;
    }

    public event EventHandler<UsbCameraEventArgs>? DeviceArrived;

    public event EventHandler<UsbCameraEventArgs>? DeviceRemoved;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (syncRoot)
        {
            if (started)
            {
                return;
            }

            hubChannel.UsbCameraLifecycleChanged += OnLifecycleChanged;
            started = true;
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            if (!started)
            {
                return;
            }

            hubChannel.UsbCameraLifecycleChanged -= OnLifecycleChanged;
            started = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
    }

    private void OnLifecycleChanged(
        SurveillanceHubService.UsbCameraLifecycleEvent e)
    {
        // Synthesize a descriptor from the SignalR payload so consumers
        // get a fully-qualified UsbDeviceDescriptor matching the same
        // shape the local Windows watcher would produce. The descriptor
        // intentionally has no Capabilities — the API client never
        // inspects them; capability discovery happens server-side.
        var descriptor = new UsbDeviceDescriptor(
            deviceId: string.IsNullOrEmpty(e.DeviceId) ? Guid.NewGuid().ToString() : e.DeviceId,
            friendlyName: e.FriendlyName ?? string.Empty,
            isPresent: string.Equals(e.Phase, "Replugged", StringComparison.OrdinalIgnoreCase));

        var args = new UsbCameraEventArgs(descriptor);

        if (string.Equals(e.Phase, "Replugged", StringComparison.OrdinalIgnoreCase))
        {
            DeviceArrived?.Invoke(this, args);
        }
        else if (string.Equals(e.Phase, "Unplugged", StringComparison.OrdinalIgnoreCase))
        {
            DeviceRemoved?.Invoke(this, args);
        }

        // Unknown phase → no-op (forward compatibility).
    }
}