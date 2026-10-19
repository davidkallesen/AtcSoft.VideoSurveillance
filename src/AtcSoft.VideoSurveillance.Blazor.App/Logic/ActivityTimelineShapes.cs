namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The shapes of a recording's activity timeline under the web player, drawn as SVG in a box one
/// unit wide per stretch and <see cref="BoxHeight"/> high, stretched to the player's width: bars
/// for how much changed, a band at the bottom where something went somewhere, and dim stretches
/// that were not analysed. The same look as the desktop apps' timeline.
/// </summary>
public static class ActivityTimelineShapes
{
    /// <summary>
    /// The height of the drawing's box.
    /// </summary>
    public const double BoxHeight = 100;

    /// <summary>
    /// The height of an event band at the bottom of the box.
    /// </summary>
    public const double BandHeight = 16;

    /// <summary>
    /// Gets the bars of the analysed stretches in which something changed.
    /// </summary>
    /// <param name="buckets">The stretches of the recording.</param>
    public static IReadOnlyList<TimelineRect> Bars(
        IReadOnlyList<RecordingActivityBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var area = BoxHeight - BandHeight;
        var bars = new List<TimelineRect>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var height = ActivityTimelineLayout.BarHeight(buckets[i].Level) * area;
            if (buckets[i].IsAnalysed && height > 0)
            {
                bars.Add(new TimelineRect(i, area - height, 1, height));
            }
        }

        return bars;
    }

    /// <summary>
    /// Gets the stretches that were not analysed, which are unknown rather than quiet.
    /// </summary>
    /// <param name="buckets">The stretches of the recording.</param>
    public static IReadOnlyList<TimelineRect> NotAnalysed(
        IReadOnlyList<RecordingActivityBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        return [.. buckets
            .Select((bucket, i) => (bucket, i))
            .Where(b => !b.bucket.IsAnalysed)
            .Select(b => new TimelineRect(b.i, 0, 1, BoxHeight - BandHeight))];
    }

    /// <summary>
    /// Gets the bands of the events, at least <see cref="ActivityTimelineLayout.MinimumEventWidth"/> stretches wide.
    /// </summary>
    /// <param name="buckets">The stretches of the recording.</param>
    public static IReadOnlyList<TimelineRect> Bands(
        IReadOnlyList<RecordingActivityBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        return [.. ActivityTimelineLayout.FindEvents(buckets)
            .Select(e => ActivityTimelineLayout.ToPixels(e, buckets.Count))
            .Select(b => new TimelineRect(b.X, BoxHeight - BandHeight, b.Width, BandHeight))];
    }
}