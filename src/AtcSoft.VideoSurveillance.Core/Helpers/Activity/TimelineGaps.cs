namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// Finds the stretches of a camera's timeline without a recording, and explains each from the
/// history of whether the camera could be recorded: the state at the gap's start decides, and a
/// gap whose reason changes on the way (back online, but the disk is full) is cut in two there.
/// </summary>
public static class TimelineGaps
{
    /// <summary>
    /// Finds the gaps of a span.
    /// </summary>
    /// <param name="coverage">Where recordings cover the span, cut to it.</param>
    /// <param name="from">The start of the span.</param>
    /// <param name="to">The end of the span.</param>
    /// <param name="history">The camera's changes and the server's starts and stops, in any order.</param>
    /// <returns>The gaps, from the start of the span.</returns>
    public static IReadOnlyList<TimelineGap> Find(
        IReadOnlyList<CoverageSpan> coverage,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<AvailabilityEntry> history)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(history);

        var ordered = history.OrderBy(e => e.Time).ToList();
        var gaps = new List<TimelineGap>();
        var cursor = from;
        foreach (var span in coverage.OrderBy(c => c.Start))
        {
            if (span.Start > cursor)
            {
                AddExplained(gaps, ordered, cursor, span.Start);
            }

            if (span.End > cursor)
            {
                cursor = span.End;
            }
        }

        if (cursor < to)
        {
            AddExplained(gaps, ordered, cursor, to);
        }

        return gaps;
    }

    private static void AddExplained(
        List<TimelineGap> gaps,
        List<AvailabilityEntry> ordered,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var reason = Explain(ordered, start);
        foreach (var entry in ordered)
        {
            if (entry.Time <= start.AddSeconds(1))
            {
                continue;
            }

            if (entry.Time >= end)
            {
                break;
            }

            var next = Explain(ordered, entry.Time);
            if (next != reason)
            {
                gaps.Add(new TimelineGap(start, entry.Time, reason));
                start = entry.Time;
                reason = next;
            }
        }

        gaps.Add(new TimelineGap(start, end, reason));
    }

    /// <summary>
    /// Explains why nothing was recorded from a moment: the latest state known then.
    /// </summary>
    /// <param name="history">The changes, oldest first.</param>
    /// <param name="moment">The start of the gap.</param>
    public static GapReason Explain(
        IReadOnlyList<AvailabilityEntry> history,
        DateTimeOffset moment)
    {
        ArgumentNullException.ThrowIfNull(history);

        // A second of slack: the recording's end and the change that ended it are noted apart.
        var known = false;
        var offline = false;
        var serverDown = false;
        var diskFull = false;
        foreach (var entry in history)
        {
            if (entry.Time > moment.AddSeconds(1))
            {
                break;
            }

            known = true;
            switch (entry.Change)
            {
                case AvailabilityChange.Online:
                    offline = false;
                    break;
                case AvailabilityChange.Offline:
                    offline = true;
                    break;
                case AvailabilityChange.RecordingStarted:
                    diskFull = false;
                    break;
                case AvailabilityChange.DiskFull:
                    diskFull = true;
                    break;
                case AvailabilityChange.ServerStarted:
                    serverDown = false;
                    offline = false;
                    break;
                case AvailabilityChange.ServerStopped:
                    serverDown = true;
                    break;
            }
        }

        if (!known)
        {
            return GapReason.Unknown;
        }

        if (serverDown)
        {
            return GapReason.ServerDown;
        }

        if (offline)
        {
            return GapReason.Offline;
        }

        return diskFull ? GapReason.DiskFull : GapReason.NotRecording;
    }
}