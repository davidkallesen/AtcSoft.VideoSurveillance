namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// Where things go on a recording's activity timeline: the height of each bar, the bands of the
/// events, and the next or previous event from where the player is. Positions are fractions of
/// the recording, 0 to 1, so the same layout serves any width. The web client has a copy under
/// <c>Logic/</c>, kept equal by a test.
/// </summary>
public static class ActivityTimelineLayout
{
    /// <summary>
    /// The narrowest an event band is drawn, in pixels: a five-second event in an hour stays visible.
    /// </summary>
    public const double MinimumEventWidth = 3;

    /// <summary>
    /// Gets the height of a bar, 0 to 1. The square root lifts small changes, so a person far
    /// away shows, while a change of most of the picture does not dwarf everything else.
    /// </summary>
    /// <param name="level">The level of the stretch, 0 to 100.</param>
    public static double BarHeight(double level)
        => Math.Sqrt(Math.Clamp(level, 0, 100) / 100);

    /// <summary>
    /// Finds the events: the runs of stretches in which something moved.
    /// </summary>
    /// <param name="buckets">The stretches of the recording, from its start.</param>
    /// <returns>The events, from the start.</returns>
    public static IReadOnlyList<ActivityEvent> FindEvents(
        IReadOnlyList<ActivityBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var events = new List<ActivityEvent>();
        var count = buckets.Count;
        int? start = null;
        for (var i = 0; i <= count; i++)
        {
            var moving = i < count && buckets[i].Tracks > 0;
            if (moving && start is null)
            {
                start = i;
            }
            else if (!moving && start is { } first)
            {
                events.Add(new ActivityEvent((double)first / count, (double)i / count));
                start = null;
            }
        }

        return events;
    }

    /// <summary>
    /// Gets where an event band is drawn on a timeline of the given width: at least
    /// <see cref="MinimumEventWidth"/> wide, widened around its middle, and kept on the timeline.
    /// </summary>
    /// <param name="activityEvent">The event.</param>
    /// <param name="width">The width of the timeline, in pixels.</param>
    /// <returns>The left edge and the width, in pixels.</returns>
    public static (double X, double Width) ToPixels(
        ActivityEvent activityEvent,
        double width)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);

        if (width <= 0)
        {
            return (0, 0);
        }

        var x = activityEvent.Start * width;
        var w = (activityEvent.End - activityEvent.Start) * width;
        if (w < MinimumEventWidth)
        {
            var bandWidth = Math.Min(MinimumEventWidth, width);
            x = Math.Clamp(x + (w / 2) - (bandWidth / 2), 0, width - bandWidth);
            w = bandWidth;
        }

        return (x, w);
    }

    /// <summary>
    /// Gets the start of the first event after the position.
    /// </summary>
    /// <param name="events">The events, from the start.</param>
    /// <param name="position">Where the player is, 0 to 1.</param>
    /// <returns>Where to go, or <see langword="null"/> when no event comes after.</returns>
    public static double? NextEventStart(
        IReadOnlyList<ActivityEvent> events,
        double position)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var activityEvent in events)
        {
            if (activityEvent.Start > position)
            {
                return activityEvent.Start;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the start of the event before the position. Just after an event's start, as right
    /// after jumping to it, that event is skipped, the way a player's "previous" goes one back.
    /// </summary>
    /// <param name="events">The events, from the start.</param>
    /// <param name="position">Where the player is, 0 to 1.</param>
    /// <param name="slack">How far after a start still counts as at it, 0 to 1.</param>
    /// <returns>Where to go, or <see langword="null"/> when no event comes before.</returns>
    public static double? PreviousEventStart(
        IReadOnlyList<ActivityEvent> events,
        double position,
        double slack)
    {
        ArgumentNullException.ThrowIfNull(events);

        double? previous = null;
        foreach (var activityEvent in events)
        {
            if (activityEvent.Start >= position - slack)
            {
                break;
            }

            previous = activityEvent.Start;
        }

        return previous;
    }
}