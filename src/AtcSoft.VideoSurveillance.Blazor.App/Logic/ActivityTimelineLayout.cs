namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// Where things go on a recording's activity timeline: the height of each bar, the bands of the
/// events, and the next or previous event from where the player is. Positions are fractions of
/// the recording, 0 to 1. A copy of Core's <c>ActivityTimelineLayout</c> on the server's
/// <see cref="RecordingActivityBucket"/>, kept equal by a test.
/// </summary>
public static class ActivityTimelineLayout
{
    /// <summary>
    /// The narrowest an event band is drawn, in the units of the timeline's width.
    /// </summary>
    public const double MinimumEventWidth = 3;

    /// <summary>
    /// Gets the height of a bar, 0 to 1: the square root lifts small changes.
    /// </summary>
    /// <param name="level">The level of the stretch, 0 to 100.</param>
    public static double BarHeight(double level)
        => Math.Sqrt(Math.Clamp(level, 0, 100) / 100);

    /// <summary>
    /// Finds the events: the runs of stretches in which something moved.
    /// </summary>
    /// <param name="buckets">The stretches of the recording, from its start.</param>
    /// <returns>The events, from the start.</returns>
    public static IReadOnlyList<ActivityTimelineEvent> FindEvents(
        IReadOnlyList<RecordingActivityBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var events = new List<ActivityTimelineEvent>();
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
                events.Add(new ActivityTimelineEvent((double)first / count, (double)i / count));
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
    /// <param name="width">The width of the timeline.</param>
    /// <returns>The left edge and the width.</returns>
    public static (double X, double Width) ToPixels(
        ActivityTimelineEvent activityEvent,
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
        IReadOnlyList<ActivityTimelineEvent> events,
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
    /// Gets the start of the event before the position; just after an event's start, that event
    /// is skipped, the way a player's "previous" goes one back.
    /// </summary>
    /// <param name="events">The events, from the start.</param>
    /// <param name="position">Where the player is, 0 to 1.</param>
    /// <param name="slack">How far after a start still counts as at it, 0 to 1.</param>
    /// <returns>Where to go, or <see langword="null"/> when no event comes before.</returns>
    public static double? PreviousEventStart(
        IReadOnlyList<ActivityTimelineEvent> events,
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