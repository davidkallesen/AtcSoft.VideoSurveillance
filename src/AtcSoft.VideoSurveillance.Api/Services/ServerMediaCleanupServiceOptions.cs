namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Options for <see cref="ServerMediaCleanupService"/>. <see cref="StartupDelaySeconds"/>
/// is 0 so the first cleanup runs at startup (mirroring the previous
/// OnStartup behavior), and the 4-hour repeat drives the periodic passes.
/// </summary>
public sealed class ServerMediaCleanupServiceOptions : IBackgroundServiceOptions
{
    /// <inheritdoc />
    public ushort StartupDelaySeconds { get; set; }

    /// <inheritdoc />
    public ushort RepeatIntervalSeconds { get; set; } = 4 * 60 * 60;

    /// <inheritdoc />
    public string? ServiceName { get; set; } = nameof(ServerMediaCleanupService);
}