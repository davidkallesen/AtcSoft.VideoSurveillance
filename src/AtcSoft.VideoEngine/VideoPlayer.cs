// ReSharper disable IdentifierTypo
namespace Linksoft.VideoEngine;

/// <summary>
/// Video player with optional GPU acceleration: demuxes, decodes, records,
/// and captures frames from RTSP/HTTP streams.
/// All pipeline work runs on a dedicated background thread.
/// </summary>
[SuppressMessage("", "CA1806:calls av_*", Justification = "OK")]
[SuppressMessage("IDisposableAnalyzers", "CA2213", Justification = "Disposable fields are cleaned up by DemuxLoop's finally block on the demux thread")]
public sealed unsafe partial class VideoPlayer : IVideoPlayer
{
    private const int MaxConsecutiveReadErrors = 30;
    private const int FpsUpdateIntervalMs = 1000;
    private const int ThreadJoinTimeoutMs = 5000;

    private readonly ILogger<VideoPlayer> logger;
    private readonly IGpuAccelerator? gpuAccelerator;
    private readonly Lock frameLock = new();
    private readonly Stopwatch fpsWatch = new();

    private Demuxer? demuxer;
    private VideoDecoder? decoder;
    private Remuxer? remuxer;
    private FrameCapture? frameCapture;

    private AVFrame* latestFrame;
    private Thread? demuxThread;
    private CancellationTokenSource? demuxCts;
    private volatile bool stopRequested;
    private int fpsFrameCount;
    private long lastPacketTicks;
    private bool disposed;

    private PlayerState state = PlayerState.Stopped;
    private VideoRotation rotation = VideoRotation.None;

    public VideoPlayer(ILogger<VideoPlayer> logger)
        : this(logger, gpuAccelerator: null)
    {
    }

    public VideoPlayer(
        ILogger<VideoPlayer> logger,
        IGpuAccelerator? gpuAccelerator)
    {
        this.logger = logger;
        this.gpuAccelerator = gpuAccelerator;
    }

    public PlayerState State => state;

    public VideoStreamInfo? StreamInfo { get; private set; }

    public double CurrentFps { get; private set; }

    public long FramesDecoded { get; private set; }

    public DateTime LastPacketUtc => Interlocked.Read(ref lastPacketTicks) == 0
        ? DateTime.MinValue
        : new DateTime(Interlocked.Read(ref lastPacketTicks), DateTimeKind.Utc);

    public bool IsRecording => remuxer?.IsOpen ?? false;

    public IGpuAccelerator? GpuAccelerator => gpuAccelerator;

    public event EventHandler<PlayerStateChangedEventArgs>? StateChanged;

    public void Open(
        Uri streamUri,
        StreamOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (state != PlayerState.Stopped)
        {
            throw new InvalidOperationException(
                $"Cannot open: player is in {state} state.");
        }

        // Wait for previous demux thread to finish cleanup (runs from caller context — OK to block)
        demuxThread?.Join(ThreadJoinTimeoutMs);
        demuxThread = null;

        options ??= new StreamOptions();
        stopRequested = false;
        demuxCts = new CancellationTokenSource();
        var ct = demuxCts.Token;

        SetState(PlayerState.Opening);

        demuxThread = new Thread(() => DemuxLoop(streamUri, options, ct))
        {
            Name = "VideoEngine-Demux",
            IsBackground = true,
        };
        demuxThread.Start();
    }

    public void Close()
    {
        if (state == PlayerState.Stopped)
        {
            return;
        }

        stopRequested = true;

        // Cancel the token first — this reaches the Demuxer's interrupt callback
        // even before the demuxer field is assigned (during the Opening phase).
        demuxCts?.Cancel();
        demuxer?.RequestAbort();

        // Non-blocking: DemuxLoop's finally block owns resource cleanup.
        // The demux thread will dispose its own Demuxer/Decoder after FFmpeg returns,
        // preventing use-after-free of the GCHandle in the interrupt callback.
        SetState(PlayerState.Stopped);
    }

    public void StartRecording(string outputFilePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (state != PlayerState.Playing || demuxer is null)
        {
            throw new InvalidOperationException("Cannot record: player is not playing.");
        }

        remuxer ??= new Remuxer();

        if (remuxer.IsOpen)
        {
            return;
        }

        remuxer.Open(outputFilePath, demuxer.VideoCodecParameters, demuxer.VideoTimeBase, (int)rotation);
        LogRecordingStarted(outputFilePath);
    }

    public void StopRecording()
    {
        if (remuxer is null || !remuxer.IsOpen)
        {
            return;
        }

        remuxer.Close();
        LogRecordingStopped();
    }

    /// <summary>
    /// Atomically transitions the active recording to a new file. Packets
    /// arriving from the demux thread between Close and Open are no longer
    /// silently dropped — they land in the previous segment or the new one.
    /// </summary>
    public void SwitchRecording(string newOutputFilePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (state != PlayerState.Playing || demuxer is null)
        {
            throw new InvalidOperationException("Cannot switch recording: player is not playing.");
        }

        if (remuxer is null || !remuxer.IsOpen)
        {
            // Nothing to switch — fall back to a plain start.
            StartRecording(newOutputFilePath);
            return;
        }

        remuxer.SwitchTo(newOutputFilePath, demuxer.VideoCodecParameters, demuxer.VideoTimeBase, (int)rotation);
        LogRecordingSwitched(newOutputFilePath);
    }

    public void SetRotation(VideoRotation rotation)
    {
        this.rotation = rotation;
        gpuAccelerator?.SetRotation(rotation);
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (state != PlayerState.Playing)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return Task.Run(
            () =>
            {
                // Prefer GPU snapshot when available.
                if (gpuAccelerator is { IsInitialized: true })
                {
                    var gpuResult = gpuAccelerator.CaptureSnapshot();
                    if (gpuResult is not null)
                    {
                        return gpuResult;
                    }
                }

                // CPU fallback.
                AVFrame* clone;
                lock (frameLock)
                {
                    if (latestFrame is null || latestFrame->width <= 0)
                    {
                        return null;
                    }

                    clone = av_frame_clone(latestFrame);
                }

                if (clone is null)
                {
                    return null;
                }

                try
                {
                    frameCapture ??= new FrameCapture();
                    return frameCapture.CaptureFrame(clone);
                }
                finally
                {
                    av_frame_free(ref clone);
                }
            },
            ct);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();

        // Ensure the demux thread has fully completed cleanup before final disposal
        demuxThread?.Join(ThreadJoinTimeoutMs);
        demuxThread = null;

        frameCapture?.Dispose();
        frameCapture = null;
    }

    private void DemuxLoop(
        Uri streamUri,
        StreamOptions options,
        CancellationToken ct)
    {
        Demuxer? localDemuxer = null;
        VideoDecoder? localDecoder = null;
        var source = options.Source;

        try
        {
            localDemuxer = new Demuxer(logger);
            localDecoder = new VideoDecoder();

            LogOpeningStream(source, UriRedactor.Redact(streamUri));
            localDemuxer.Open(streamUri, options, ct);
            LogStreamDemuxerOpened(source);

            var threadCount = VideoEngineBootstrap.Config?.DecoderThreads ?? 0;
            var useHwAccel = options.HardwareAcceleration
                && gpuAccelerator is { IsInitialized: true };
            var hwDeviceCtx = useHwAccel
                ? gpuAccelerator!.HwDeviceContext
                : null;

            localDecoder.Open(localDemuxer.VideoCodecParameters, threadCount, hwDeviceCtx);
            LogDecoderOpened(source, localDecoder.IsHardwareAccelerated);

            demuxer = localDemuxer;
            decoder = localDecoder;

            latestFrame = av_frame_alloc();

            StreamInfo = new VideoStreamInfo
            {
                Width = localDecoder.Width,
                Height = localDecoder.Height,
                CodecName = localDecoder.CodecName,
                PixelFormat = localDecoder.PixelFormat.ToString(),
                IsHardwareAccelerated = localDecoder.IsHardwareAccelerated,
            };

            LogStreamOpened(source, StreamInfo, UriRedactor.Redact(streamUri));

            SetState(PlayerState.Playing);

            RunReadLoop(source, options.InputFormat);
        }
        catch (Exception ex)
        {
            if (!stopRequested)
            {
                LogDemuxLoopFailed(source, ex);
                SetState(PlayerState.Error, ex.Message);
            }
        }
        finally
        {
            // The demux thread owns cleanup of all pipeline resources it created.
            // This ensures the Demuxer's GCHandle is only freed after FFmpeg is done
            // using the interrupt callback — preventing use-after-free.
            remuxer?.Dispose();
            remuxer = null;

            localDecoder?.Dispose();
            if (ReferenceEquals(decoder, localDecoder))
            {
                decoder = null;
            }

            localDemuxer?.Dispose();
            if (ReferenceEquals(demuxer, localDemuxer))
            {
                demuxer = null;
            }

            lock (frameLock)
            {
                if (latestFrame is not null)
                {
                    av_frame_free(ref latestFrame);
                }
            }

            demuxCts?.Dispose();
            demuxCts = null;

            StreamInfo = null;
            CurrentFps = 0;
            FramesDecoded = 0;
            Interlocked.Exchange(ref lastPacketTicks, 0);
        }
    }

    private void RunReadLoop(
        string source,
        InputFormatKind inputFormat)
    {
        int consecutiveErrors = 0;
        int lastErrorCode = 0;
        bool anyFramesReceived = false;
        fpsWatch.Restart();
        fpsFrameCount = 0;

        while (!stopRequested)
        {
            int ret = demuxer!.ReadPacket();

            if (ret == AVERROR_EOF)
            {
                LogEndOfStreamReached(source);

                // Surface as Error (not silent) so the upstream state machine
                // routes through ConnectionFailed → TryAutoReconnect. Without
                // this, the demux thread exits cleanly and the tile is left
                // believing the stream is still Playing forever (recording
                // dies, no recovery).
                if (!stopRequested)
                {
                    SetState(PlayerState.Error, "End of stream", StreamFailureReason.EndOfStream);
                }

                return;
            }

            if (ret < 0)
            {
                consecutiveErrors++;
                lastErrorCode = ret;

                if (consecutiveErrors > MaxConsecutiveReadErrors)
                {
                    LogExceededConsecutiveReadErrors(source, MaxConsecutiveReadErrors);

                    var reason = ReadErrorClassifier.Classify(
                        inputFormat,
                        anyFramesReceived,
                        lastErrorCode);

                    // Log the raw FFmpeg error code so unknown patterns can
                    // be diagnosed from operator logs without rebuilding.
                    LogReadErrorCode(source, lastErrorCode, reason);

                    // Same reasoning as the EOF path above: drive a real state
                    // transition so auto-reconnect can fire.
                    if (!stopRequested)
                    {
                        SetState(PlayerState.Error, "Exceeded consecutive read errors", reason);
                    }

                    return;
                }

                continue;
            }

            consecutiveErrors = 0;
            anyFramesReceived = true;
            Interlocked.Exchange(ref lastPacketTicks, DateTime.UtcNow.Ticks);

            if (demuxer.IsVideoPacket)
            {
                ProcessVideoPacket();
            }

            demuxer.UnrefPacket();
        }
    }

    private void ProcessVideoPacket()
    {
        if (remuxer is { IsOpen: true })
        {
            remuxer.WritePacket(demuxer!.CurrentPacket, demuxer.VideoTimeBase);
        }

        if (!decoder!.SendPacket(demuxer!.CurrentPacket))
        {
            return;
        }

        while (decoder.ReceiveFrame())
        {
            var decodedFrame = decoder.CurrentFrame;

            gpuAccelerator?.OnFrameDecoded(decodedFrame);

            lock (frameLock)
            {
                av_frame_unref(latestFrame);
                av_frame_ref(latestFrame, decodedFrame);
            }

            FramesDecoded++;
            UpdateFps();
        }
    }

    private void UpdateFps()
    {
        fpsFrameCount++;

        if (fpsWatch.ElapsedMilliseconds < FpsUpdateIntervalMs)
        {
            return;
        }

        CurrentFps = fpsFrameCount * 1000.0 / fpsWatch.ElapsedMilliseconds;
        fpsFrameCount = 0;
        fpsWatch.Restart();
    }

    private void SetState(
        PlayerState newState,
        string? errorMessage = null,
        StreamFailureReason reason = StreamFailureReason.Unknown)
    {
        var previous = state;
        state = newState;
        StateChanged?.Invoke(this, new PlayerStateChangedEventArgs(previous, newState, errorMessage, reason));
    }
}