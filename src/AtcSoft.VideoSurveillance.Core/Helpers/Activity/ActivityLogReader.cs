namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// Reads a recording's activity index, also while it is being written. A line that is cut off
/// or not understood is skipped; the rest of the index is still read.
/// </summary>
public static class ActivityLogReader
{
    /// <summary>
    /// Reads the activity index file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file, from <see cref="RecordingPathHelper.BuildActivityPath"/>.</param>
    /// <returns>The index, or <see langword="null"/> when there is no file or it has no header.</returns>
    public static ActivityLog? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Read(reader);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an activity index.
    /// </summary>
    /// <param name="reader">The lines.</param>
    /// <returns>The index, or <see langword="null"/> when it has no header.</returns>
    public static ActivityLog? Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ReadLine() is not { } header ||
            !ActivityLogFormat.TryReadHeader(header, out var start))
        {
            return null;
        }

        var seconds = new SortedDictionary<int, ActivitySecond>();
        while (reader.ReadLine() is { } line)
        {
            if (!ActivityLogFormat.TryReadSecond(line, out var second))
            {
                continue;
            }

            // Written once per second; should one come twice, the most of both counts.
            seconds[second.Offset] = seconds.TryGetValue(second.Offset, out var known)
                ? new ActivitySecond(
                    second.Offset,
                    Math.Max(known.Level, second.Level),
                    Math.Max(known.Tracks, second.Tracks),
                    [.. known.TrackIds.Union(second.TrackIds).Order()])
                : second;
        }

        return new ActivityLog(start, [.. seconds.Values]);
    }
}