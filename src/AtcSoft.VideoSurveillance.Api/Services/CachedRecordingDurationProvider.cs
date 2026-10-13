namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Reads the length of a recording from the file and remembers it. A
/// recording does not change once it is finished, so each file is opened
/// once; the listing asks for every file every time.
/// </summary>
public sealed class CachedRecordingDurationProvider : IRecordingDurationProvider
{
    private readonly Func<string, TimeSpan> probe;
    private readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedRecordingDurationProvider"/> class
    /// that reads lengths with the video engine.
    /// </summary>
    public CachedRecordingDurationProvider()
        : this(MediaProbe.GetDuration)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedRecordingDurationProvider"/> class.
    /// </summary>
    /// <param name="probe">Reads the length of a file; zero when it cannot.</param>
    public CachedRecordingDurationProvider(Func<string, TimeSpan> probe)
    {
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <inheritdoc />
    public TimeSpan? GetDuration(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                cache.TryRemove(filePath, out _);
                return null;
            }

            // The size and the time of the last write tell a changed file
            // (or another file under the same name) from the one that was read.
            if (cache.TryGetValue(filePath, out var entry) &&
                entry.Length == info.Length &&
                entry.LastWriteUtc == info.LastWriteTimeUtc)
            {
                return entry.Duration;
            }

            var duration = probe(filePath);
            if (duration <= TimeSpan.Zero)
            {
                return null;
            }

            cache[filePath] = new Entry(info.Length, info.LastWriteTimeUtc, duration);
            return duration;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private readonly record struct Entry(long Length, DateTime LastWriteUtc, TimeSpan Duration);
}