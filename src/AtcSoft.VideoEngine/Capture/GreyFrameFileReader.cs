namespace AtcSoft.VideoEngine.Capture;

/// <summary>
/// Reads a recording file as grey pictures for analysis, as fast as it decodes: every frame is
/// decoded (a compressed picture needs the ones before it), and those asked for are scaled straight
/// to grey at the analysis size by swscale and turned the way the picture is displayed. No PNG, no
/// player. For *Analyse recording*, the storybook scorer and the sample app.
/// </summary>
[SuppressMessage("", "CA1806:calls av_*", Justification = "OK")]
public sealed unsafe class GreyFrameFileReader : IDisposable
{
    private readonly VideoDecoder decoder = new();
    private AVFormatContext* formatContext;
    private AVPacket* packet;
    private SwsContext* scaler;
    private int streamIndex = -1;
    private AVRational timeBase;
    private long startPts;
    private int scalerWidth;
    private int scalerHeight;
    private AVPixelFormat scalerFormat = AVPixelFormat.None;
    private bool draining;
    private bool finished;
    private bool disposed;

    private GreyFrameFileReader(
        int width,
        int height,
        VideoRotation rotation)
    {
        Width = width;
        Height = height;
        Rotation = rotation;
    }

    /// <summary>
    /// Gets the width of the pictures, as displayed.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the pictures, as displayed.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the clockwise rotation the pictures are turned by.
    /// </summary>
    public VideoRotation Rotation { get; }

    /// <summary>
    /// Gets how long the recording is, or <see cref="TimeSpan.Zero"/> when the file does not say.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Opens a recording file.
    /// </summary>
    /// <param name="filePath">The file.</param>
    /// <param name="width">The width of the pictures, as displayed.</param>
    /// <param name="height">The height of the pictures, as displayed.</param>
    /// <param name="rotation">The clockwise rotation the picture is displayed with.</param>
    /// <param name="decoderThreads">
    /// How many threads decode; 0 for one per processor. An app that records and shows live
    /// cameras at the same time passes a few, so the analysis does not crowd them out.
    /// </param>
    /// <returns>The reader.</returns>
    /// <exception cref="FFmpegException">The file cannot be opened or decoded.</exception>
    public static GreyFrameFileReader Open(
        string filePath,
        int width,
        int height,
        VideoRotation rotation = VideoRotation.None,
        int decoderThreads = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var reader = new GreyFrameFileReader(width, height, rotation);
        try
        {
            reader.OpenFile(filePath, decoderThreads > 0 ? decoderThreads : Environment.ProcessorCount);
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the pictures at most <paramref name="framesPerSecond"/> a second, from the start.
    /// </summary>
    /// <param name="framesPerSecond">How many pictures a second, at most.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The pictures, in the order they are shown.</returns>
    public IEnumerable<GreyFrame> ReadFrames(
        double framesPerSecond,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var interval = TimeSpan.FromSeconds(1 / framesPerSecond);
        var next = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested && ReadNext(next) is { } frame)
        {
            yield return frame;

            // On a fixed grid, so the rate does not drift below the one asked for; a gap in the
            // recording moves the grid on.
            next += interval;
            if (next <= frame.Position)
            {
                next = frame.Position + interval;
            }
        }
    }

    /// <summary>
    /// Reads the next picture at or after a place in the recording.
    /// </summary>
    /// <param name="notBefore">The earliest place wanted; pictures before it are decoded and passed over.</param>
    /// <returns>The picture, or <see langword="null"/> at the end of the recording.</returns>
    public GreyFrame? ReadNext(TimeSpan notBefore)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        while (!finished)
        {
            if (decoder.ReceiveFrame())
            {
                var frame = decoder.CurrentFrame;
                var position = PositionOf(frame->pts);
                if (position >= notBefore)
                {
                    return new GreyFrame(position, Width, Height, ToGrey(frame));
                }

                continue;
            }

            if (draining)
            {
                finished = true;
                break;
            }

            if (av_read_frame(formatContext, packet) < 0)
            {
                // The end: what the decoder still holds comes out after an empty packet.
                decoder.SendPacket(null);
                draining = true;
                continue;
            }

            try
            {
                if (packet->stream_index == streamIndex)
                {
                    decoder.SendPacket(packet);
                }
            }
            finally
            {
                av_packet_unref(packet);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        decoder.Dispose();

        if (scaler is not null)
        {
            sws_freeContext(scaler);
            scaler = null;
        }

        if (packet is not null)
        {
            var p = packet;
            av_packet_free(ref p);
            packet = null;
        }

        if (formatContext is not null)
        {
            avformat_close_input(ref formatContext);
        }
    }

    private void OpenFile(
        string filePath,
        int decoderThreads)
    {
        AVDictionary* options = null;
        try
        {
            var ret = avformat_open_input(ref formatContext, filePath, null, ref options);
            if (ret < 0)
            {
                throw new FFmpegException(ret, "Failed to open the recording");
            }

            // Not "ref options": this argument is one dictionary per stream.
            ret = avformat_find_stream_info(formatContext, (AVDictionary**)null);
            if (ret < 0)
            {
                throw new FFmpegException(ret, "Failed to read the recording's streams");
            }
        }
        finally
        {
            av_dict_free(ref options);
        }

        for (uint i = 0; i < formatContext->nb_streams; i++)
        {
            var stream = formatContext->streams[i];
            if (stream->codecpar->codec_type != AVMediaType.Video)
            {
                continue;
            }

            streamIndex = (int)i;
            timeBase = stream->time_base;
            startPts = stream->start_time == AV_NOPTS_VALUE ? 0 : stream->start_time;
            decoder.Open(stream->codecpar, decoderThreads);
            break;
        }

        if (streamIndex < 0)
        {
            throw new InvalidOperationException("The recording has no video.");
        }

        Duration = formatContext->duration > 0 ? TimeSpan.FromTicks(formatContext->duration * 10) : TimeSpan.Zero;
        packet = av_packet_alloc();
        if (packet is null)
        {
            throw new InvalidOperationException("Failed to allocate AVPacket.");
        }
    }

    private TimeSpan PositionOf(long pts)
    {
        if (pts == AV_NOPTS_VALUE || timeBase.Den == 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = (pts - startPts) * (double)timeBase.Num / timeBase.Den;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    // Scales straight to grey at the size the picture has before it is turned, then turns it.
    private byte[] ToGrey(AVFrame* frame)
    {
        var (scaledWidth, scaledHeight) = PixelRotator.GetRotatedSize(Width, Height, Rotation);
        var format = (AVPixelFormat)frame->format;
        if (scaler is null || scalerWidth != frame->width || scalerHeight != frame->height || scalerFormat != format)
        {
            if (scaler is not null)
            {
                sws_freeContext(scaler);
            }

            scaler = sws_getContext(frame->width, frame->height, format, scaledWidth, scaledHeight, AVPixelFormat.Gray8, SwsFlags.Area, null, null, null);
            if (scaler is null)
            {
                throw new InvalidOperationException("Failed to create SwsContext.");
            }

            scalerWidth = frame->width;
            scalerHeight = frame->height;
            scalerFormat = format;
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

        if (Rotation == VideoRotation.None)
        {
            return scaled;
        }

        var turned = new byte[Width * Height];
        fixed (byte* source = scaled)
        {
            fixed (byte* target = turned)
            {
                PixelRotator.Rotate(source, scaledWidth, scaledWidth, scaledHeight, bytesPerPixel: 1, Rotation, target, Width);
            }
        }

        return turned;
    }
}