using CoreCameraConfiguration = Linksoft.VideoSurveillance.Models.CameraConfiguration;
using CoreCameraProtocol = Linksoft.VideoSurveillance.Enums.CameraProtocol;
using CoreCameraSource = Linksoft.VideoSurveillance.Enums.CameraSource;
using CoreConnectionState = Linksoft.VideoSurveillance.Enums.ConnectionState;
using CoreOverlayPosition = Linksoft.VideoSurveillance.Enums.OverlayPosition;
using CoreRecordingState = Linksoft.VideoSurveillance.Enums.RecordingState;
using CoreUsbConnectionSettings = Linksoft.VideoSurveillance.Models.Settings.UsbConnectionSettings;
using CoreUsbStreamFormat = Linksoft.VideoSurveillance.Models.UsbStreamFormat;

namespace Linksoft.VideoSurveillance.Api.Domain.Mapping;

internal static class CameraMappingExtensions
{
    public static Camera ToApiModel(
        this CoreCameraConfiguration core,
        CoreConnectionState? connectionState = null,
        bool isRecording = false)
    {
        var usb = core.Connection.Usb;
        var format = usb?.Format;

        return new Camera(
            Id: core.Id,
            DisplayName: core.Display.DisplayName,
            Description: core.Display.Description ?? string.Empty,
            Source: core.Connection.Source.ToApiSource(),
            IpAddress: core.Connection.IpAddress,
            Port: core.Connection.Port,
            Protocol: core.Connection.Protocol.ToApiProtocol(),
            Path: core.Connection.Path ?? string.Empty,
            Username: core.Authentication.UserName ?? string.Empty,
            UsbDeviceId: usb?.DeviceId ?? string.Empty,
            UsbFriendlyName: usb?.FriendlyName ?? string.Empty,
            UsbWidth: format?.Width ?? 0,
            UsbHeight: format?.Height ?? 0,
            UsbFrameRate: format?.FrameRate ?? 0,
            UsbPixelFormat: format?.PixelFormat ?? string.Empty,
            UsbCaptureAudio: usb?.PreferAudio ?? false,
            UsbAudioDeviceName: usb?.AudioDeviceName ?? string.Empty,
            OverlayPosition: ToApiOverlayPosition(core.Display.OverlayPosition),
            StreamUseLowLatencyMode: core.Stream.UseLowLatencyMode,
            StreamMaxLatencyMs: core.Stream.MaxLatencyMs,
            StreamRtspTransport: ToApiRtspTransport(core.Stream.RtspTransport),
            StreamBufferDurationMs: core.Stream.BufferDurationMs,
            ConnectionState: connectionState?.ToApiConnectionState(),
            IsRecording: isRecording);
    }

    public static CoreCameraConfiguration ToCoreModel(
        this CreateCameraRequest request)
    {
        var source = request.Source?.ToCoreSource() ?? CoreCameraSource.Network;

        var camera = new CoreCameraConfiguration
        {
            Connection =
            {
                Source = source,
                IpAddress = request.IpAddress ?? string.Empty,
                Port = request.Port,
                Protocol = request.Protocol?.ToCoreProtocol() ?? CoreCameraProtocol.Rtsp,
                Path = request.Path,
                Usb = source == CoreCameraSource.Usb ? BuildUsbSettings(request) : null,
            },
            Authentication =
            {
                UserName = request.Username,
                Password = request.Password,
            },
            Display =
            {
                DisplayName = request.DisplayName,
                Description = request.Description,
            },
        };

        if (request.OverlayPosition is not null)
        {
            camera.Display.OverlayPosition = ToCoreOverlayPosition(request.OverlayPosition.Value);
        }

        camera.Stream.UseLowLatencyMode = request.StreamUseLowLatencyMode;

        if (request.StreamMaxLatencyMs > 0)
        {
            camera.Stream.MaxLatencyMs = request.StreamMaxLatencyMs;
        }

        if (request.StreamRtspTransport is not null)
        {
            camera.Stream.RtspTransport = request.StreamRtspTransport.Value.ToString().ToLowerInvariant();
        }

        if (request.StreamBufferDurationMs > 0)
        {
            camera.Stream.BufferDurationMs = request.StreamBufferDurationMs;
        }

        return camera;
    }

    public static void ApplyUpdate(
        this CoreCameraConfiguration core,
        UpdateCameraRequest request)
    {
        if (!string.IsNullOrEmpty(request.DisplayName))
        {
            core.Display.DisplayName = request.DisplayName;
        }

        if (request.Description is not null)
        {
            core.Display.Description = request.Description;
        }

        if (request.Source is not null)
        {
            core.Connection.Source = request.Source.Value.ToCoreSource();
        }

        if (!string.IsNullOrEmpty(request.IpAddress))
        {
            core.Connection.IpAddress = request.IpAddress;
        }

        if (request.Port > 0)
        {
            core.Connection.Port = request.Port;
        }

        if (request.Protocol is not null)
        {
            core.Connection.Protocol = request.Protocol.Value.ToCoreProtocol();
        }

        if (request.Path is not null)
        {
            core.Connection.Path = request.Path;
        }

        if (request.Username is not null)
        {
            core.Authentication.UserName = request.Username;
        }

        if (request.Password is not null)
        {
            core.Authentication.Password = request.Password;
        }

        ApplyUsbUpdate(core, request);

        if (request.OverlayPosition is not null)
        {
            core.Display.OverlayPosition = ToCoreOverlayPosition(request.OverlayPosition.Value);
        }

        core.Stream.UseLowLatencyMode = request.StreamUseLowLatencyMode;

        if (request.StreamMaxLatencyMs > 0)
        {
            core.Stream.MaxLatencyMs = request.StreamMaxLatencyMs;
        }

        if (request.StreamRtspTransport is not null)
        {
            core.Stream.RtspTransport = request.StreamRtspTransport.Value.ToString().ToLowerInvariant();
        }

        if (request.StreamBufferDurationMs > 0)
        {
            core.Stream.BufferDurationMs = request.StreamBufferDurationMs;
        }
    }

    public static CameraProtocol ToApiProtocol(this CoreCameraProtocol protocol)
        => protocol switch
        {
            CoreCameraProtocol.Http => CameraProtocol.Http,
            CoreCameraProtocol.Https => CameraProtocol.Https,
            _ => CameraProtocol.Rtsp,
        };

    public static CoreCameraProtocol ToCoreProtocol(
        this CameraProtocol protocol)
        => protocol switch
        {
            CameraProtocol.Http => CoreCameraProtocol.Http,
            CameraProtocol.Https => CoreCameraProtocol.Https,
            _ => CoreCameraProtocol.Rtsp,
        };

    public static CameraSource ToApiSource(this CoreCameraSource source)
        => source switch
        {
            CoreCameraSource.Usb => CameraSource.Usb,
            _ => CameraSource.Network,
        };

    public static CoreCameraSource ToCoreSource(this CameraSource source)
        => source switch
        {
            CameraSource.Usb => CoreCameraSource.Usb,
            _ => CoreCameraSource.Network,
        };

    public static CameraConnectionState ToApiConnectionState(
        this CoreConnectionState state)
        => state switch
        {
            CoreConnectionState.Connecting => CameraConnectionState.Connecting,
            CoreConnectionState.Connected => CameraConnectionState.Connected,
            CoreConnectionState.Reconnecting => CameraConnectionState.Reconnecting,
            CoreConnectionState.Error => CameraConnectionState.Error,
            CoreConnectionState.DeviceUnplugged => CameraConnectionState.DeviceUnplugged,
            _ => CameraConnectionState.Disconnected,
        };

    public static RecordingStatusState ToApiRecordingState(
        this CoreRecordingState state)
        => state switch
        {
            CoreRecordingState.Recording => RecordingStatusState.Recording,
            CoreRecordingState.RecordingMotion => RecordingStatusState.RecordingMotion,
            CoreRecordingState.RecordingPostMotion => RecordingStatusState.RecordingPostMotion,
            _ => RecordingStatusState.Idle,
        };

    private static CameraOverlayPosition? ToApiOverlayPosition(
        CoreOverlayPosition position)
        => position switch
        {
            CoreOverlayPosition.TopRight => CameraOverlayPosition.TopRight,
            CoreOverlayPosition.BottomLeft => CameraOverlayPosition.BottomLeft,
            CoreOverlayPosition.BottomRight => CameraOverlayPosition.BottomRight,
            _ => CameraOverlayPosition.TopLeft,
        };

    private static CoreOverlayPosition ToCoreOverlayPosition(
        CameraOverlayPosition position)
        => position switch
        {
            CameraOverlayPosition.TopRight => CoreOverlayPosition.TopRight,
            CameraOverlayPosition.BottomLeft => CoreOverlayPosition.BottomLeft,
            CameraOverlayPosition.BottomRight => CoreOverlayPosition.BottomRight,
            _ => CoreOverlayPosition.TopLeft,
        };

    private static CameraStreamRtspTransport? ToApiRtspTransport(
        string? transport)
    {
        if (string.IsNullOrEmpty(transport))
        {
            return null;
        }

        return string.Equals(transport, "udp", StringComparison.OrdinalIgnoreCase)
            ? CameraStreamRtspTransport.Udp
            : CameraStreamRtspTransport.Tcp;
    }

    private static CoreUsbConnectionSettings BuildUsbSettings(
        CreateCameraRequest request)
        => new()
        {
            DeviceId = request.UsbDeviceId ?? string.Empty,
            FriendlyName = request.UsbFriendlyName ?? string.Empty,
            PreferAudio = request.UsbCaptureAudio,
            AudioDeviceName = request.UsbAudioDeviceName ?? string.Empty,
            Format = (request.UsbWidth > 0 && request.UsbHeight > 0)
                ? new CoreUsbStreamFormat
                {
                    Width = request.UsbWidth,
                    Height = request.UsbHeight,
                    FrameRate = request.UsbFrameRate,
                    PixelFormat = request.UsbPixelFormat ?? string.Empty,
                }
                : null,
        };

    private static void ApplyUsbUpdate(
        CoreCameraConfiguration core,
        UpdateCameraRequest request)
    {
        var hasUsbField = request.UsbDeviceId is not null
            || request.UsbFriendlyName is not null
            || request.UsbWidth > 0
            || request.UsbHeight > 0
            || request.UsbFrameRate > 0
            || request.UsbPixelFormat is not null
            || request.UsbCaptureAudio
            || request.UsbAudioDeviceName is not null;

        if (!hasUsbField && core.Connection.Source != CoreCameraSource.Usb)
        {
            return;
        }

        core.Connection.Usb ??= new CoreUsbConnectionSettings();

        if (request.UsbDeviceId is not null)
        {
            core.Connection.Usb.DeviceId = request.UsbDeviceId;
        }

        if (request.UsbFriendlyName is not null)
        {
            core.Connection.Usb.FriendlyName = request.UsbFriendlyName;
        }

        // PreferAudio is a non-nullable bool — only overwrite when the
        // request explicitly carries `true` so existing-camera updates
        // that omit the field don't silently disable audio.
        if (request.UsbCaptureAudio)
        {
            core.Connection.Usb.PreferAudio = true;
        }

        if (request.UsbAudioDeviceName is not null)
        {
            core.Connection.Usb.AudioDeviceName = request.UsbAudioDeviceName;
        }

        if (request.UsbWidth > 0 || request.UsbHeight > 0 ||
            request.UsbFrameRate > 0 || request.UsbPixelFormat is not null)
        {
            core.Connection.Usb.Format ??= new CoreUsbStreamFormat();

            if (request.UsbWidth > 0)
            {
                core.Connection.Usb.Format.Width = request.UsbWidth;
            }

            if (request.UsbHeight > 0)
            {
                core.Connection.Usb.Format.Height = request.UsbHeight;
            }

            if (request.UsbFrameRate > 0)
            {
                core.Connection.Usb.Format.FrameRate = request.UsbFrameRate;
            }

            if (request.UsbPixelFormat is not null)
            {
                core.Connection.Usb.Format.PixelFormat = request.UsbPixelFormat;
            }
        }
    }
}