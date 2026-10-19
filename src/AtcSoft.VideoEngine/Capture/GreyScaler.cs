namespace AtcSoft.VideoEngine.Capture;

/// <summary>
/// Turns a decoded picture into a grey picture for analysis: scaled straight to 8-bit grey at the
/// analysis size by swscale (area filter), then turned the way the picture is displayed. Keeps its
/// swscale context while the source size and format stay the same. Not thread-safe: one per user.
/// </summary>
[SuppressMessage("", "CA1806:calls av_*", Justification = "OK")]
internal sealed unsafe class GreyScaler : IDisposable
{
    private SwsContext* scaler;
    private int scalerWidth;
    private int scalerHeight;
    private AVPixelFormat scalerFormat = AVPixelFormat.None;
    private int targetWidth;
    private int targetHeight;

    /// <summary>
    /// Makes a grey picture of a decoded frame.
    /// </summary>
    /// <param name="frame">The decoded frame, in the stream's own orientation; a hardware frame is copied to memory first.</param>
    /// <param name="width">The width of the grey picture, as displayed.</param>
    /// <param name="height">The height of the grey picture, as displayed.</param>
    /// <param name="rotation">The clockwise rotation the picture is displayed with.</param>
    /// <returns>The pixels, row by row, or <see langword="null"/> when the frame cannot be read.</returns>
    public byte[]? ToGrey(
        AVFrame* frame,
        int width,
        int height,
        VideoRotation rotation)
    {
        if (frame is null || frame->width <= 0 || frame->height <= 0)
        {
            return null;
        }

        AVFrame* memoryFrame = null;
        if ((AVPixelFormat)frame->format == AVPixelFormat.D3d11)
        {
            memoryFrame = av_frame_alloc();
            if (memoryFrame is null || av_hwframe_transfer_data(memoryFrame, frame, 0) < 0)
            {
                av_frame_free(ref memoryFrame);
                return null;
            }

            frame = memoryFrame;
        }

        try
        {
            return Scale(frame, width, height, rotation);
        }
        finally
        {
            if (memoryFrame is not null)
            {
                av_frame_free(ref memoryFrame);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (scaler is not null)
        {
            sws_freeContext(scaler);
            scaler = null;
        }
    }

    private byte[]? Scale(
        AVFrame* frame,
        int width,
        int height,
        VideoRotation rotation)
    {
        var (scaledWidth, scaledHeight) = PixelRotator.GetRotatedSize(width, height, rotation);
        var format = (AVPixelFormat)frame->format;
        if (scaler is null ||
            scalerWidth != frame->width ||
            scalerHeight != frame->height ||
            scalerFormat != format ||
            targetWidth != scaledWidth ||
            targetHeight != scaledHeight)
        {
            Dispose();
            scaler = sws_getContext(frame->width, frame->height, format, scaledWidth, scaledHeight, AVPixelFormat.Gray8, SwsFlags.Area, null, null, null);
            if (scaler is null)
            {
                return null;
            }

            scalerWidth = frame->width;
            scalerHeight = frame->height;
            scalerFormat = format;
            targetWidth = scaledWidth;
            targetHeight = scaledHeight;
        }

        var scaled = new byte[scaledWidth * scaledHeight];
        fixed (byte* target = scaled)
        {
            byte** sourceData = stackalloc byte*[] { (byte*)frame->data[0], (byte*)frame->data[1], (byte*)frame->data[2], (byte*)frame->data[3] };
            int* sourceStride = stackalloc int[] { frame->linesize[0], frame->linesize[1], frame->linesize[2], frame->linesize[3] };
            byte** targetData = stackalloc byte*[] { target, null, null, null };
            int* targetStride = stackalloc int[] { scaledWidth, 0, 0, 0 };
            sws_scale(scaler, sourceData, sourceStride, 0, frame->height, targetData, targetStride);
        }

        if (rotation == VideoRotation.None)
        {
            return scaled;
        }

        var turned = new byte[width * height];
        fixed (byte* source = scaled)
        {
            fixed (byte* target = turned)
            {
                PixelRotator.Rotate(source, scaledWidth, scaledWidth, scaledHeight, bytesPerPixel: 1, rotation, target, width);
            }
        }

        return turned;
    }
}