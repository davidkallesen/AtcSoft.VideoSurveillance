namespace Linksoft.VideoEngine.DirectX;

/// <summary>
/// Renders software-decoded AVFrames into a BGRA D3D11 texture so the
/// existing SwapChainPresenter pipeline can display them. Used as the
/// fallback when the decoder doesn't deliver D3D11 textures — most
/// commonly MJPEG (no D3D11VA path on Windows), and any codec that
/// falls back to a CPU decoder. Caches the SWS context and the output
/// texture per (width, height, src-format) triple so steady-state
/// playback allocates nothing.
/// </summary>
[SuppressMessage("", "CA1806:return value not used (av_*)", Justification = "FFmpeg APIs use return codes but failures still produce usable state in this path")]
internal sealed unsafe class CpuFrameUploader : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext deviceContext;

    private SwsContext* swsCtx;
    private AVFrame* bgraFrame;
    private ID3D11Texture2D? bgraTexture;

    private int cachedWidth;
    private int cachedHeight;
    private AVPixelFormat cachedSrcFormat = AVPixelFormat.None;
    private bool disposed;

    public CpuFrameUploader(D3D11Device d3d11Device)
    {
        device = d3d11Device.Device;
        deviceContext = d3d11Device.DeviceContext;
    }

    public ID3D11Texture2D? BgraTexture => bgraTexture;

    public int Width => cachedWidth;

    public int Height => cachedHeight;

    /// <summary>
    /// Uploads <paramref name="frame"/> (any libswscale-supported CPU
    /// pixel format) into the cached BGRA texture. Returns
    /// <see langword="true"/> when a valid texture is available, with
    /// <see cref="BgraTexture"/> / <see cref="Width"/> /
    /// <see cref="Height"/> reflecting the latest upload.
    /// </summary>
    public bool Upload(AVFrame* frame)
    {
        if (frame is null || frame->width <= 0 || frame->height <= 0)
        {
            return false;
        }

        var srcFormat = (AVPixelFormat)frame->format;
        if (srcFormat == AVPixelFormat.None || srcFormat == AVPixelFormat.D3d11)
        {
            // D3D11 hardware frames go through VideoProcessorRenderer
            // — this uploader exists only for CPU formats.
            return false;
        }

        if (!EnsurePipeline(frame->width, frame->height, srcFormat))
        {
            return false;
        }

        // Flyleaf.FFmpeg's byte_ptrArray8 doesn't expose a byte** root,
        // so unpack the plane pointers and strides into managed arrays
        // for the sws_scale call (mirrors FrameCapture's approach).
        byte*[] srcData =
        [
            (byte*)frame->data[0], (byte*)frame->data[1],
            (byte*)frame->data[2], (byte*)frame->data[3],
        ];

        int[] srcStride =
        [
            frame->linesize[0], frame->linesize[1],
            frame->linesize[2], frame->linesize[3],
        ];

        byte*[] dstData =
        [
            (byte*)bgraFrame->data[0], (byte*)bgraFrame->data[1],
            (byte*)bgraFrame->data[2], (byte*)bgraFrame->data[3],
        ];

        int[] dstStride =
        [
            bgraFrame->linesize[0], bgraFrame->linesize[1],
            bgraFrame->linesize[2], bgraFrame->linesize[3],
        ];

        sws_scale(swsCtx, srcData, srcStride, 0, frame->height, dstData, dstStride);

        deviceContext.UpdateSubresource(
            bgraTexture!,
            0,
            null,
            bgraFrame->data[0],
            (uint)bgraFrame->linesize[0],
            0);

        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ReleasePipeline();
    }

    private bool EnsurePipeline(
        int width,
        int height,
        AVPixelFormat srcFormat)
    {
        if (swsCtx is not null
            && bgraTexture is not null
            && cachedWidth == width
            && cachedHeight == height
            && cachedSrcFormat == srcFormat)
        {
            return true;
        }

        ReleasePipeline();

        try
        {
            swsCtx = sws_getContext(
                width,
                height,
                srcFormat,
                width,
                height,
                AVPixelFormat.Bgra,
                SwsFlags.Bilinear,
                null,
                null,
                null);

            if (swsCtx is null)
            {
                return false;
            }

            bgraFrame = av_frame_alloc();
            if (bgraFrame is null)
            {
                return false;
            }

            bgraFrame->format = (int)AVPixelFormat.Bgra;
            bgraFrame->width = width;
            bgraFrame->height = height;

            // Align to 32 bytes — comfortably above the 16-byte SIMD
            // boundary sws_scale prefers and a multiple of 4 bytes/pixel
            // so each scanline has the same stride.
            if (av_frame_get_buffer(bgraFrame, 32) < 0)
            {
                return false;
            }

            var desc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
            };

            bgraTexture = device.CreateTexture2D(desc);

            cachedWidth = width;
            cachedHeight = height;
            cachedSrcFormat = srcFormat;
            return true;
        }
        catch
        {
            ReleasePipeline();
            throw;
        }
    }

    private void ReleasePipeline()
    {
        if (bgraFrame is not null)
        {
            var f = bgraFrame;
            av_frame_free(ref f);
            bgraFrame = null;
        }

        if (swsCtx is not null)
        {
            sws_freeContext(swsCtx);
            swsCtx = null;
        }

        bgraTexture?.Dispose();
        bgraTexture = null;

        cachedWidth = 0;
        cachedHeight = 0;
        cachedSrcFormat = AVPixelFormat.None;
    }
}