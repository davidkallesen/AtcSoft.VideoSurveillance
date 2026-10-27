namespace AtcSoft.VideoSurveillance.Storybook.Scoring;

/// <summary>
/// One analysed frame: its truth and the boxes shown on it.
/// </summary>
/// <param name="Time">The time in seconds.</param>
/// <param name="Truth">What was drawn and what a detector should do about it.</param>
/// <param name="IgnoreAll">Whether the whole frame does not count (a lighting step).</param>
/// <param name="Detected">The boxes shown.</param>
/// <param name="Pixels">
/// The grey picture the detector was given, at the run's analysis size; only when the run was
/// asked to keep them (<see cref="StorybookRunOptions.KeepPictures"/>).
/// </param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "A picture is handed on as it is.")]
public sealed record ScoredFrame(
    double Time,
    IReadOnlyList<TruthBox> Truth,
    bool IgnoreAll,
    IReadOnlyList<DetectedBox> Detected,
    byte[]? Pixels = null);