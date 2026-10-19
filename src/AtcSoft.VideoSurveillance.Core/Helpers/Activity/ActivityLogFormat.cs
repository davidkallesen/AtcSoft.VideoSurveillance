namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// The lines of an activity index file (JSON Lines). The first line is the header,
/// <c>{"v":1,"start":"2026-10-19T10:15:00.000Z"}</c>; every further line is one analysed second,
/// <c>{"t":12,"a":3.4,"n":1,"ids":[4]}</c> (offset, level, tracks, track ids; the ids are left out
/// when there are none).
/// </summary>
internal static class ActivityLogFormat
{
    public const int Version = 1;

    public static string Header(DateTimeOffset start)
        => Write(w =>
        {
            w.WriteNumber("v", Version);
            w.WriteString("start", start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        });

    public static string Line(ActivitySecond second)
        => Write(w =>
        {
            w.WriteNumber("t", second.Offset);
            w.WriteNumber("a", Math.Round(second.Level, 1));
            w.WriteNumber("n", second.Tracks);
            if (second.TrackIds.Count > 0)
            {
                w.WriteStartArray("ids");
                foreach (var id in second.TrackIds)
                {
                    w.WriteNumberValue(id);
                }

                w.WriteEndArray();
            }
        });

    public static bool TryReadHeader(
        string line,
        out DateTimeOffset start)
    {
        start = default;
        return TryParse(line, out var root) &&
               root.TryGetProperty("v", out var version) &&
               version.ValueKind == JsonValueKind.Number &&
               root.TryGetProperty("start", out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out start);
    }

    public static bool TryReadSecond(
        string line,
        [NotNullWhen(true)] out ActivitySecond? second)
    {
        second = null;
        if (!TryParse(line, out var root) ||
            !root.TryGetProperty("t", out var t) || !t.TryGetInt32(out var offset) || offset < 0)
        {
            return false;
        }

        var level = root.TryGetProperty("a", out var a) && a.TryGetDouble(out var l) ? l : 0;
        var tracks = root.TryGetProperty("n", out var n) && n.TryGetInt32(out var c) ? c : 0;
        var ids = new List<int>();
        if (root.TryGetProperty("ids", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetInt32(out var id))
                {
                    ids.Add(id);
                }
            }
        }

        second = new ActivitySecond(offset, level, tracks, ids);
        return true;
    }

    // A line cut off by a crash or still being written is not JSON; it is skipped.
    private static bool TryParse(
        string line,
        out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}