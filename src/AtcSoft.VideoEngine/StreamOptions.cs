namespace Linksoft.VideoEngine;

/// <summary>
/// Options for opening a video stream.
/// </summary>
public sealed class StreamOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to use low-latency mode.
    /// When enabled, reduces buffer sizes and enables low-delay decoding.
    /// </summary>
    public bool UseLowLatencyMode { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum allowed latency in milliseconds.
    /// The player will speed up to catch up if latency exceeds this value.
    /// A value of 0 disables latency management.
    /// </summary>
    public int MaxLatencyMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets the RTSP transport protocol ("tcp" or "udp").
    /// Ignored when <see cref="InputFormat"/> is not
    /// <see cref="InputFormatKind.Auto"/>.
    /// </summary>
    public string RtspTransport { get; set; } = "tcp";

    /// <summary>
    /// Gets or sets the demuxer buffer duration in milliseconds.
    /// </summary>
    public int BufferDurationMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether hardware-accelerated decoding is enabled.
    /// </summary>
    public bool HardwareAcceleration { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum vertical resolution (0 = no limit).
    /// Used to limit decoding resolution for performance.
    /// </summary>
    public int MaxVerticalResolution { get; set; }

    /// <summary>
    /// Gets or sets a human-readable label (typically the camera display name)
    /// included in player and demuxer log messages so failures can be traced
    /// back to a specific camera. Empty when not set.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the FFmpeg input format. <see cref="InputFormatKind.Auto"/>
    /// (the default) preserves the legacy URL-sniffing behaviour for
    /// network sources; any other value selects a local-device demuxer
    /// and switches the open path onto <see cref="RawDeviceSpec"/>.
    /// </summary>
    public InputFormatKind InputFormat { get; set; } = InputFormatKind.Auto;

    /// <summary>
    /// Gets or sets the raw device specifier passed verbatim to
    /// <c>avformat_open_input</c> when <see cref="InputFormat"/> is
    /// not <see cref="InputFormatKind.Auto"/>. For DirectShow this
    /// looks like <c>video=Logitech BRIO</c> or
    /// <c>video=A:audio=B</c>.
    /// </summary>
    public string? RawDeviceSpec { get; set; }

    /// <summary>
    /// Gets or sets the FFmpeg <c>video_size</c> option
    /// (e.g. <c>1920x1080</c>). Only honoured when
    /// <see cref="InputFormat"/> is not <see cref="InputFormatKind.Auto"/>.
    /// </summary>
    public string? VideoSize { get; set; }

    /// <summary>
    /// Gets or sets the FFmpeg <c>framerate</c> option (e.g. <c>30</c>,
    /// <c>29.97</c>). Only honoured when <see cref="InputFormat"/> is
    /// not <see cref="InputFormatKind.Auto"/>.
    /// </summary>
    public string? FrameRate { get; set; }

    /// <summary>
    /// Gets or sets the FFmpeg <c>pixel_format</c> option (e.g.
    /// <c>nv12</c>). Only honoured when <see cref="InputFormat"/> is
    /// not <see cref="InputFormatKind.Auto"/>.
    /// </summary>
    public string? PixelFormat { get; set; }

    /// <summary>
    /// The FFmpeg input-format string corresponding to
    /// <see cref="InputFormat"/>, or <see langword="null"/> when set
    /// to <see cref="InputFormatKind.Auto"/> (let FFmpeg sniff).
    /// </summary>
    public string? InputFormatName
        => InputFormat switch
        {
            InputFormatKind.Dshow => "dshow",
            InputFormatKind.V4l2 => "v4l2",
            InputFormatKind.AVFoundation => "avfoundation",
            _ => null,
        };

    /// <inheritdoc />
    public override string ToString()
        => InputFormat == InputFormatKind.Auto
            ? $"StreamOptions {{ Source='{Source}', Transport='{RtspTransport}', LowLatency={UseLowLatencyMode}, MaxLatency={MaxLatencyMs}ms, HwAccel={HardwareAcceleration} }}"
            : $"StreamOptions {{ Source='{Source}', InputFormat={InputFormat}, Device='{RawDeviceSpec}', VideoSize='{VideoSize}', FrameRate='{FrameRate}', PixelFormat='{PixelFormat}' }}";
}