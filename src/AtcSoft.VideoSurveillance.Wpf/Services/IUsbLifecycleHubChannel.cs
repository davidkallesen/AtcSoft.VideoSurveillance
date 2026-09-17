namespace Linksoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Test seam over <see cref="SurveillanceHubService"/>'s USB-lifecycle
/// event stream. Lets <see cref="RemoteUsbCameraWatcher"/> be unit
/// tested without a live SignalR connection.
/// </summary>
public interface IUsbLifecycleHubChannel
{
    event Action<SurveillanceHubService.UsbCameraLifecycleEvent>? UsbCameraLifecycleChanged;
}

/// <summary>
/// Adapter that forwards the SurveillanceHubService event onto the
/// <see cref="IUsbLifecycleHubChannel"/> contract. The hub service
/// itself stays free of new interfaces — adapters keep production DI
/// readable while still letting tests substitute a fake channel.
/// </summary>
public sealed class SurveillanceHubLifecycleChannel : IUsbLifecycleHubChannel
{
    public SurveillanceHubLifecycleChannel(SurveillanceHubService hubService)
    {
        ArgumentNullException.ThrowIfNull(hubService);

        // The hub service outlives this adapter (DI singleton); keeping
        // the subscription for the process lifetime is intentional —
        // there's nowhere meaningful to unhook it in a singleton-driven
        // container, and unsubscribing would just race with shutdown.
        hubService.OnUsbCameraLifecycleChanged += Forward;
    }

    public event Action<SurveillanceHubService.UsbCameraLifecycleEvent>? UsbCameraLifecycleChanged;

    private void Forward(SurveillanceHubService.UsbCameraLifecycleEvent e)
        => UsbCameraLifecycleChanged?.Invoke(e);
}