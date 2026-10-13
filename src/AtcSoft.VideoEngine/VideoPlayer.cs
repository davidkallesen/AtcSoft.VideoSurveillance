// ReSharper disable IdentifierTypo
namespace AtcSoft.VideoEngine;

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

    // Guards the encoder: a recording is started, switched and stopped from
    // other threads than the one that reads the camera and feeds it.
    private readonly Lock encoderLock = new();
    private readonly EncodedPacketHandler writeEncodedPacket;
    private readonly EncodedPacketHandler writeEncodedAudioPacket;
    private readonly Stopwatch fpsWatch = new();

    private Demuxer? demuxer;
    private VideoDecoder? decoder;
    private Remuxer? remuxer;
    private FrameEncoder? frameEncoder;
    private AudioEncoder? audioEncoder;
    private volatile bool recordAudio;
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
        writeEncodedPacket = WriteEncodedPacket;
        writeEncodedAudioPacket = WriteEncodedAudioPacket;
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
        recordAudio = options.RecordAudio;
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

        // The thread that reads the camera clears these when the stream
        // ends, which it may do at any moment: work on what is there now.
        var source = demuxer;
        var sourceDecoder = decoder;
        if (state != PlayerState.Playing || source is null || sourceDecoder is null)
        {
            throw new InvalidOperationException("Cannot record: player is not playing.");
        }

        remuxer ??= new Remuxer();

        if (remuxer.IsOpen)
        {
            return;
        }

        var sound = OpenAudioEncoder(source);
        try
        {
            if (FrameEncoder.IsNeededFor(source.VideoCodecParameters->codec_id))
            {
                StartEncodedRecording(outputFilePath, source, sourceDecoder, sound);
            }
            else
            {
                lock (encoderLock)
                {
                    remuxer.Open(
                        outputFilePath,
                        source.VideoCodecParameters,
                        source.VideoTimeBase,
                        (int)rotation,
                        sound is null ? null : sound.CodecParameters);
                    audioEncoder = sound;
                }
            }
        }
        catch
        {
            sound?.Dispose();
            throw;
        }

        LogRecordingStarted(outputFilePath);
    }

    public void StopRecording()
    {
        if (remuxer is null || !remuxer.IsOpen)
        {
            return;
        }

        lock (encoderLock)
        {
            if (frameEncoder is not null)
            {
                // What the encoder still holds belongs in this file.
                frameEncoder.Flush(writeEncodedPacket);
                frameEncoder.Dispose();
                frameEncoder = null;
            }

            if (audioEncoder is not null)
            {
                audioEncoder.Flush(writeEncodedAudioPacket);
                audioEncoder.Dispose();
                audioEncoder = null;
            }

            remuxer.Close();
        }

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

        lock (encoderLock)
        {
            // The sound encoder runs on across the switch: the sound has no gap.
            var sound = audioEncoder is null ? null : audioEncoder.CodecParameters;

            if (frameEncoder is not null)
            {
                // The recorder leaves out everything before a file's first
                // key frame, so the new file is made to start with one.
                frameEncoder.RequestKeyFrame();
                remuxer.SwitchTo(newOutputFilePath, frameEncoder.CodecParameters, frameEncoder.TimeBase, (int)rotation, sound);
            }
            else
            {
                remuxer.SwitchTo(newOutputFilePath, demuxer.VideoCodecParameters, demuxer.VideoTimeBase, (int)rotation, sound);
            }
        }

        LogRecordingSwitched(newOutputFilePath);
    }

    // A camera that delivers uncompressed pictures (a webcam) cannot be
    // recorded by copying its packets: no recording format takes them. Its
    // decoded pictures are encoded to H.264 instead.
    // Sound is recorded only when it was asked for and the source has it.
    // A source whose sound cannot be encoded is recorded without it: the
    // picture is what the recording is for.
    private AudioEncoder? OpenAudioEncoder(Demuxer source)
    {
        if (!recordAudio || !source.HasAudio)
        {
            return null;
        }

        var encoder = new AudioEncoder();
        try
        {
            encoder.Open(source.AudioCodecParameters);
            return encoder;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FFmpegException)
        {
            encoder.Dispose();
            LogRecordingWithoutSound(ex.Message);
            return null;
        }
    }

    private void StartEncodedRecording(
        string outputFilePath,
        Demuxer source,
        VideoDecoder sourceDecoder,
        AudioEncoder? sound)
    {
        var frameRate = source.VideoFrameRate;
        var framesPerSecond = frameRate is { Num: > 0, Den: > 0 }
            ? (int)Math.Round((double)frameRate.Num / frameRate.Den, MidpointRounding.AwayFromZero)
            : 0;

        var encoder = new FrameEncoder();
        try
        {
            encoder.Open(sourceDecoder.Width, sourceDecoder.Height, sourceDecoder.PixelFormat, framesPerSecond);

            lock (encoderLock)
            {
                var recorder = remuxer
                    ?? throw new InvalidOperationException("Cannot record: player is not playing.");
                recorder.Open(
                    outputFilePath,
                    encoder.CodecParameters,
                    encoder.TimeBase,
                    (int)rotation,
                    sound is null ? null : sound.CodecParameters);
                frameEncoder = encoder;
                audioEncoder = sound;
            }
        }
        catch
        {
            encoder.Dispose();
            throw;
        }

        LogRecordingEncoder(encoder.EncoderName ?? string.Empty);
    }

    private void WriteEncodedAudioPacket(AVPacket* packet)
    {
        if (remuxer is { IsOpen: true } && audioEncoder is not null)
        {
            remuxer.WriteAudioPacket(packet, audioEncoder.TimeBase);
        }
    }

    private void WriteEncodedPacket(AVPacket* packet)
    {
        if (remuxer is { IsOpen: true } && frameEncoder is not null)
        {
            remuxer.WritePacket(packet, frameEncoder.TimeBase);
        }
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
                    // The GPU snapshot is already rotated; rotate here too so
                    // a capture looks the same whichever path produced it.
                    frameCapture ??= new FrameCapture();
                    return frameCapture.CaptureFrame(clone, rotation);
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
            lock (encoderLock)
            {
                frameEncoder?.Dispose();
                frameEncoder = null;
                audioEncoder?.Dispose();
                audioEncoder = null;
                remuxer?.Dispose();
                remuxer = null;
            }

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
            else if (demuxer.IsAudioPacket)
            {
                lock (encoderLock)
                {
                    audioEncoder?.Encode(demuxer.CurrentPacket, demuxer.AudioTimeBase, writeEncodedAudioPacket);
                }
            }

            demuxer.UnrefPacket();
        }
    }

    private void ProcessVideoPacket()
    {
        // A stream that is being encoded is recorded picture by picture
        // further down; its packets are not what goes into the file. The
        // check is made under the lock: a recording being started or
        // stopped on another thread has the file open for a moment without
        // the encoder, and a raw packet would land in an H.264 file.
        lock (encoderLock)
        {
            if (remuxer is { IsOpen: true } && frameEncoder is null)
            {
                remuxer.WritePacket(demuxer!.CurrentPacket, demuxer.VideoTimeBase);
            }
        }

        if (!decoder!.SendPacket(demuxer!.CurrentPacket))
        {
            return;
        }

        while (decoder.ReceiveFrame())
        {
            var decodedFrame = decoder.CurrentFrame;

            gpuAccelerator?.OnFrameDecoded(decodedFrame);

            lock (encoderLock)
            {
                frameEncoder?.Encode(decodedFrame, demuxer.VideoTimeBase, writeEncodedPacket);
            }

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