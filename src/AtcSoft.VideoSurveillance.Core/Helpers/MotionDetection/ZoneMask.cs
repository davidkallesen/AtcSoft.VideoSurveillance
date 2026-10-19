namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// The pixels of a picture that motion detection ignores: the ignore zones of a camera filled in at
/// the analysis size. A pixel is in a polygon when its centre is (even-odd rule), so a concave shape
/// keeps its notch and two zones that overlap are both ignored.
/// </summary>
public static class ZoneMask
{
    /// <summary>
    /// Fills in the ignore zones.
    /// </summary>
    /// <param name="zones">The camera's zones; those that are not ignore zones, or have fewer than three corners, are left out.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>One byte per pixel, 1 where ignored; <see langword="null"/> when nothing is.</returns>
    public static byte[]? Ignored(
        IReadOnlyList<MotionZone>? zones,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var ignore = zones?.Where(z => z.Kind == MotionZoneKind.Ignore && z.Points.Count >= 3).ToList() ?? [];
        if (ignore.Count == 0)
        {
            return null;
        }

        var mask = new byte[width * height];
        var crossings = new List<double>();
        foreach (var zone in ignore)
        {
            var points = zone.Points.Select(p => (X: p.X * width, Y: p.Y * height)).ToList();
            for (var y = 0; y < height; y++)
            {
                // Where the row through the pixel centres crosses the edges.
                var row = y + 0.5;
                crossings.Clear();
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    if ((a.Y <= row && b.Y > row) || (b.Y <= row && a.Y > row))
                    {
                        crossings.Add(a.X + ((row - a.Y) / (b.Y - a.Y) * (b.X - a.X)));
                    }
                }

                crossings.Sort();
                for (var c = 0; c + 1 < crossings.Count; c += 2)
                {
                    // Pixels whose centre lies between the two crossings.
                    var from = Math.Max(0, (int)Math.Ceiling(crossings[c] - 0.5));
                    var to = Math.Min(width - 1, (int)Math.Floor(crossings[c + 1] - 0.5));
                    for (var x = from; x <= to; x++)
                    {
                        mask[(y * width) + x] = 1;
                    }
                }
            }
        }

        return mask;
    }
}