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

    /// <summary>
    /// Gets the playhead: a line the full height of the box where the player is, at least one
    /// stretch wide and kept inside the box.
    /// </summary>
    /// <param name="position">Where the player is, 0 to 1; below 0 before it plays.</param>
    /// <param name="stretches">How many stretches the box is wide.</param>
    /// <returns>The line, or <see langword="null"/> while there is no position.</returns>
    public static TimelineRect? Playhead(
        double position,
        int stretches)
    {
        if (position < 0 || stretches <= 0)
        {
            return null;
        }

        var width = Math.Max(stretches / 200.0, 1);
        var x = Math.Clamp((Math.Min(position, 1) * stretches) - (width / 2), 0, stretches - width);
        return new TimelineRect(x, 0, width, BoxHeight);
    }
}