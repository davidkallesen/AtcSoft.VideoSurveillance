namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// A stretch of a recording in which something moved (a confirmed track), as fractions of the
/// recording from its start (0) to its end (1).
/// </summary>
/// <param name="Start">Where it starts, 0 to 1.</param>
/// <param name="End">Where it ends, 0 to 1, after <paramref name="Start"/>.</param>
public sealed record ActivityTimelineEvent(
    double Start,
    double End);