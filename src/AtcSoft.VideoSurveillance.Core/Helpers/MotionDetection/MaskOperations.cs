namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// Picture and mask operations of the detector: a light blur, and morphology on the foreground
/// mask (open removes specks, close fills holes). Separable 3×3 passes over flat arrays; the
/// overloads with buffers allocate nothing.
/// </summary>
internal static class MaskOperations
{
    /// <summary>
    /// Blurs a grey picture with a 3×3 box, which removes sensor noise and compression blocks.
    /// </summary>
    public static byte[] Blur3(
        byte[] source,
        int width,
        int height)
    {
        var result = new byte[source.Length];
        Blur3(source, result, new ushort[source.Length], width, height);
        return result;
    }

    /// <summary>
    /// Blurs a grey picture with a 3×3 box into <paramref name="result"/>.
    /// </summary>
    public static void Blur3(
        byte[] source,
        byte[] result,
        ushort[] rows,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var left = source[row + Math.Max(0, x - 1)];
                var right = source[row + Math.Min(width - 1, x + 1)];
                rows[row + x] = (ushort)(left + source[row + x] + right);
            }
        }

        for (var y = 0; y < height; y++)
        {
            var up = Math.Max(0, y - 1) * width;
            var row = y * width;
            var down = Math.Min(height - 1, y + 1) * width;
            for (var x = 0; x < width; x++)
            {
                result[row + x] = (byte)((rows[up + x] + rows[row + x] + rows[down + x] + 4) / 9);
            }
        }
    }

    /// <summary>
    /// Removes specks smaller than the structuring element (erode, then dilate).
    /// </summary>
    public static void Open(
        byte[] mask,
        int width,
        int height,
        int radius,
        byte[]? temp = null)
    {
        temp ??= new byte[mask.Length];
        Apply(mask, temp, width, height, radius, keepIfAny: false);
        Apply(mask, temp, width, height, radius, keepIfAny: true);
    }

    /// <summary>
    /// Fills holes and gaps smaller than the structuring element (dilate, then erode).
    /// </summary>
    public static void Close(
        byte[] mask,
        int width,
        int height,
        int radius,
        byte[]? temp = null)
    {
        temp ??= new byte[mask.Length];
        Apply(mask, temp, width, height, radius, keepIfAny: true);
        Apply(mask, temp, width, height, radius, keepIfAny: false);
    }

    // A square element, as a horizontal then a vertical pass: max (dilate) or min (erode).
    private static void Apply(
        byte[] mask,
        byte[] temp,
        int width,
        int height,
        int radius,
        bool keepIfAny)
    {
        if (radius <= 0)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                temp[row + x] = Reduce(mask, row, Math.Max(0, x - radius), Math.Min(width - 1, x + radius), keepIfAny);
            }
        }

        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - radius);
            var y1 = Math.Min(height - 1, y + radius);
            for (var x = 0; x < width; x++)
            {
                var value = keepIfAny ? (byte)0 : (byte)1;
                for (var yy = y0; yy <= y1; yy++)
                {
                    var v = temp[(yy * width) + x];
                    if (keepIfAny && v != 0)
                    {
                        value = 1;
                        break;
                    }

                    if (!keepIfAny && v == 0)
                    {
                        value = 0;
                        break;
                    }
                }

                mask[(y * width) + x] = value;
            }
        }
    }

    private static byte Reduce(
        byte[] mask,
        int row,
        int x0,
        int x1,
        bool keepIfAny)
    {
        for (var x = x0; x <= x1; x++)
        {
            var v = mask[row + x];
            if (keepIfAny && v != 0)
            {
                return 1;
            }

            if (!keepIfAny && v == 0)
            {
                return 0;
            }
        }

        return keepIfAny ? (byte)0 : (byte)1;
    }
}