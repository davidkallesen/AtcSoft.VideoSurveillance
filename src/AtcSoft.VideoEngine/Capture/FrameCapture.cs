namespace Linksoft.VideoEngine.Capture;

/// <summary>
/// CPU-based frame capture: converts a decoded frame to RGB24 and encodes as PNG.
/// Caches the SWS context and PNG encoder for repeated captures at the same resolution.
/// </summary>
[SuppressMessage("", "CA1806:calls av_*", Justification = "OK")]
internal sealed unsafe class FrameCapture : IDisposable
{
    private SwsContext* swsCtx;
    private AVCodecContext* pngEncCtx;
    private AVFrame* rgbFrame;
    private int cachedWidth;
    private int cachedHeight;
    private AVPixelFormat cachedSrcFormat;
    private bool disposed;

    public byte[]? CaptureFrame(AVFrame* srcFrame)
    {
        if (srcFrame is null || srcFrame->width <= 0 || srcFrame->height <= 0)
        {
            return null;
        }

        // Hardware pixel formats (e.g., D3D11) are not supported by libswscale.
        // Transfer the frame to CPU memory first.
        AVFrame* cpuFrame = null;
        var srcFormat = (AVPixelFormat)srcFrame->format;
        if (srcFormat == AVPixelFormat.D3d11)
        {
            cpuFrame = av_frame_alloc();
            if (cpuFrame is null)
            {
                return null;
            }

            if (av_hwframe_transfer_data(cpuFrame, srcFrame, 0) < 0)
            {
                av_frame_free(ref cpuFrame);
                return null;
            }

            srcFrame = cpuFrame;
            srcFormat = (AVPixelFormat)cpuFrame->format;
        }

        try
        {
            var width = srcFrame->width;
            var height = srcFrame->height;

            EnsureContexts(width, height, srcFormat);

            ConvertToRgb24(srcFrame, height);

            return EncodePng();
        }
        finally
        {
            if (cpuFrame is not null)
            {
                av_frame_free(ref cpuFrame);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        FreeContexts();
    }

    private void EnsureContexts(
        int width,
        int height,
        AVPixelFormat srcFormat)
    {
        if (swsCtx is not null
            && cachedWidth == width
            && cachedHeight == height
            && cachedSrcFormat == srcFormat)
        {
            return;
        }

        FreeContexts();

        // A throw between the first successful alloc and the final
        // avcodec_open2 used to leave a partially-initialized state
        // around (e.g. swsCtx allocated but rgbFrame null). The next
        // matching-dimension call would skip re-init and crash on use.
        // Run all allocations under try/catch so any failure resets
        // every field via FreeContexts before propagating.
        AVDictionary* opts = null;
        try
        {
            swsCtx = sws_getContext(
                width,
                height,
                srcFormat,
                width,
                height,
                AVPixelFormat.Rgb24,
                SwsFlags.Bilinear,
                null,
                null,
                null);

            if (swsCtx is null)
            {
                throw new InvalidOperationException("Failed to create SwsContext.");
            }

            rgbFrame = av_frame_alloc();
            if (rgbFrame is null)
            {
                throw new InvalidOperationException("Failed to allocate RGB frame.");
            }

            rgbFrame->format = (int)AVPixelFormat.Rgb24;
            rgbFrame->width = width;
            rgbFrame->height = height;

            var ret = av_frame_get_buffer(rgbFrame, 0);
            if (ret < 0)
            {
                throw new FFmpegException(ret, "Failed to allocate RGB frame buffer");
            }

            var encoder = avcodec_find_encoder_by_name("png");
            if (encoder is null)
            {
                throw new InvalidOperationException("PNG encoder not found.");
            }

            pngEncCtx = avcodec_alloc_context3(encoder);
            if (pngEncCtx is null)
            {
                throw new InvalidOperationException("Failed to allocate PNG encoder context.");
            }

            pngEncCtx->width = width;
            pngEncCtx->height = height;
            pngEncCtx->pix_fmt = AVPixelFormat.Rgb24;
            pngEncCtx->time_base = new AVRational { Num = 1, Den = 1 };

            ret = avcodec_open2(pngEncCtx, encoder, ref opts);
            if (ret < 0)
            {
                throw new FFmpegException(ret, "Failed to open PNG encoder");
            }

            cachedWidth = width;
            cachedHeight = height;
            cachedSrcFormat = srcFormat;
        }
        catch
        {
            FreeContexts();
            throw;
        }
        finally
        {
            av_dict_free(ref opts);
        }
    }

    [SuppressMessage("", "SA1019:Member access symbol '->' should not be followed by a space", Justification = "OK")]
    private void ConvertToRgb24(
        AVFrame* srcFrame,
        int height)
    {
        byte*[] srcData =
        [
            (byte*)srcFrame->data[0], (byte*)srcFrame->data[1],
            (byte*)srcFrame->data[2], (byte*)srcFrame->data[3],
        ];

        int[] srcStride =
        [
            srcFrame->linesize[0], srcFrame->linesize[1],
            srcFrame->linesize[2], srcFrame->linesize[3],
        ];

        byte*[] dstData =
        [
            (byte*)rgbFrame->data[0], (byte*)rgbFrame->data[1],
            (byte*)rgbFrame->data[2], (byte*)rgbFrame->data[3],
        ];

        int[] dstStride =
        [
            rgbFrame->linesize[0], rgbFrame -> linesize[1],
            rgbFrame->linesize[2], rgbFrame -> linesize[3],
        ];

        sws_scale(swsCtx, srcData, srcStride, 0, height, dstData, dstStride);
    }

    private byte[]? EncodePng()
    {
        var ret = avcodec_send_frame(pngEncCtx, rgbFrame);
        if (ret < 0)
        {
            return null;
        }

        var pkt = av_packet_alloc();
        if (pkt is null)
        {
            return null;
        }

        try
        {
            ret = avcodec_receive_packet(pngEncCtx, pkt);
            if (ret < 0)
            {
                return null;
            }

            var result = new byte[pkt->size];
            Marshal.Copy((IntPtr)pkt->data, result, 0, pkt->size);
            return result;
        }
        finally
        {
            av_packet_free(ref pkt);
        }
    }

    private void FreeContexts()
    {
        if (pngEncCtx is not null)
        {
            var ctx = pngEncCtx;
            avcodec_free_context(ref ctx);
            pngEncCtx = null;
        }

        if (rgbFrame is not null)
        {
            var f = rgbFrame;
            av_frame_free(ref f);
            rgbFrame = null;
        }

        if (swsCtx is not null)
        {
            sws_freeContext(swsCtx);
            swsCtx = null;
        }

        cachedWidth = 0;
        cachedHeight = 0;
    }
}