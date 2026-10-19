namespace AtcSoft.VideoSurveillance.Models;

/// <summary>
/// Where a recording covers a timeline's span (cut to the span).
/// </summary>
/// <param name="Start">The start, in UTC.</param>
/// <param name="End">The end, in UTC.</param>
/// <param name="VideoPath">The recording.</param>
public sealed record CoverageSpan(
    DateTimeOffset Start,
    DateTimeOffset End,
    string VideoPath);