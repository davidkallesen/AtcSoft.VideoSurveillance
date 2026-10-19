namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// Keeps the summaries of activity indexes that timelines have read, so a timeline over days
/// reads each index once. A summary is read again when its file changed (size or time, as an
/// index grows while recording); the least recently used are dropped beyond the limit.
/// </summary>
public sealed class ActivitySummaryCache
{
    private readonly int capacity;
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private long clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivitySummaryCache"/> class.
    /// </summary>
    /// <param name="capacity">How many summaries are kept at most.</param>
    public ActivitySummaryCache(int capacity = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    /// <summary>
    /// Gets how many summaries are kept.
    /// </summary>
    public int Count
        => entries.Count;

    /// <summary>
    /// Gets the summary of the activity index at the path.
    /// </summary>
    /// <param name="activityPath">The index, from <see cref="RecordingPathHelper.BuildActivityPath"/>.</param>
    /// <returns>The summary, or <see langword="null"/> when there is no readable index.</returns>
    public ActivitySummary? Get(string activityPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(activityPath);

        var info = new FileInfo(activityPath);
        if (!info.Exists)
        {
            entries.TryRemove(activityPath, out _);
            return null;
        }

        var used = Interlocked.Increment(ref clock);
        if (entries.TryGetValue(activityPath, out var kept) &&
            kept.Length == info.Length &&
            kept.LastWriteUtc == info.LastWriteTimeUtc)
        {
            kept.LastUsed = used;
            return kept.Summary;
        }

        var summary = ActivityLogReader.Read(activityPath) is { } log
            ? ActivitySummarizer.Summarize(log)
            : null;
        entries[activityPath] = new Entry(info.Length, info.LastWriteTimeUtc, summary) { LastUsed = used };
        Trim();
        return summary;
    }

    private void Trim()
    {
        var excess = entries.Count - capacity;
        if (excess <= 0)
        {
            return;
        }

        foreach (var path in entries.OrderBy(e => e.Value.LastUsed).Take(excess).Select(e => e.Key).ToList())
        {
            entries.TryRemove(path, out _);
        }
    }

    private sealed class Entry(
        long length,
        DateTime lastWriteUtc,
        ActivitySummary? summary)
    {
        public long Length { get; } = length;

        public DateTime LastWriteUtc { get; } = lastWriteUtc;

        public ActivitySummary? Summary { get; } = summary;

        public long LastUsed { get; set; }
    }
}