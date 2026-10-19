namespace AtcSoft.VideoSurveillance.Storybook.Scoring;

/// <summary>
/// One analysed frame: its truth and the boxes shown on it.
/// </summary>
/// <param name="Time">The time in seconds.</param>
/// <param name="Truth">What was drawn and what a detector should do about it.</param>
/// <param name="IgnoreAll">Whether the whole frame does not count (a lighting step).</param>
/// <param name="Detected">The boxes shown.</param>
public sealed record ScoredFrame(
    double Time,
    IReadOnlyList<TruthBox> Truth,
    bool IgnoreAll,
    IReadOnlyList<DetectedBox> Detected);