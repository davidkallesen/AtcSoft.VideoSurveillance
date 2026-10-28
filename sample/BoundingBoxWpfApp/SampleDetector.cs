namespace BoundingBoxWpfApp;

/// <summary>
/// How the sample makes a detector: the settings and inner options from the tuning panel, the
/// zones of a recording's review, and the warm-up of <i>Analyse recording</i>. It always keeps
/// its diagnostics, for the debug layers.
/// </summary>
/// <param name="Settings">The settings.</param>
/// <param name="Options">The options; those of <paramref name="Settings"/> when <see langword="null"/>.</param>
/// <param name="Zones">The zones, or <see langword="null"/>.</param>
/// <param name="WarmUpSeconds">How long the detector only learns at the start.</param>
public sealed record SampleDetector(
    MotionDetectionSettings Settings,
    MotionDetectorOptions? Options = null,
    IReadOnlyList<MotionZone>? Zones = null,
    double WarmUpSeconds = 0)
{
    /// <summary>
    /// Makes a fresh detector.
    /// </summary>
    /// <returns>The detector.</returns>
    public BuiltInMotionDetector Create()
        => new(Settings, Options, Zones)
        {
            WarmUpSeconds = WarmUpSeconds,
            KeepDiagnostics = true,
        };
}