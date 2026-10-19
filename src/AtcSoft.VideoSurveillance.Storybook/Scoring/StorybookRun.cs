namespace AtcSoft.VideoSurveillance.Storybook.Scoring;

/// <summary>
/// A scene run through a detector: the score and every analysed frame.
/// </summary>
/// <param name="Score">The score.</param>
/// <param name="Frames">The analysed frames with their truth and the boxes shown.</param>
public sealed record StorybookRun(
    StorybookScore Score,
    IReadOnlyList<ScoredFrame> Frames);