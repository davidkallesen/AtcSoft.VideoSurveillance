// ReSharper disable CommentTypo
// ReSharper disable IdentifierTypo
namespace AtcSoft.VideoEngine.Recording;

/// <summary>
/// Records a video stream by remuxing (packet copy, no transcoding) to a container file.
/// </summary>
[SuppressMessage("", "CA1806:calls av_*", Justification = "OK")]
internal sealed unsafe class Remuxer : IDisposable
{
    // Bounded queue capacity sized so a slow disk hiccup of a few seconds
    // (e.g. a cleanup pass thrashing the same drive) doesn't drop frames at
    // 30 fps. When the queue fills, the producer side drops the oldest
    // packet — keeping the freshest history when the disk is permanently
    // behind.
    private const int WriteQueueCapacity = 1000;

    private readonly Lock syncLock = new();

    // BlockingCollection (not Channel) because the surrounding class is
    // `unsafe` and cannot host `await`. The writer thread waits for
    // packetQueued instead of an async wait.
    private readonly BlockingCollection<IntPtr> writeQueue =
        new(new ConcurrentQueue<IntPtr>(), boundedCapacity: WriteQueueCapacity);

    // Wakes the writer when a packet was queued, and when it has to stop.
    private readonly AutoResetEvent packetQueued = new(initialState: false);

    private AVFormatContext* outputCtx;
    private AVRational outputTimeBase;
    private long firstDts = AV_NOPTS_VALUE;
    private long lastDts = AV_NOPTS_VALUE;
    private bool receivedKeyframe;
    private bool disposed;

    private volatile bool stopWriter;

    private Thread? writerThread;

    public bool IsOpen => outputCtx is not null;

    public void Open(
        string outputPath,
        AVCodecParameters* codecpar,
        AVRational inputTimeBase,
        int rotationDegrees = 0)
    {
        lock (syncLock)
        {
            OpenLocked(outputPath, codecpar, inputTimeBase, rotationDegrees);
            EnsureWriterRunning();
        }
    }

    /// <summary>
    /// Atomically closes the current output file and opens
    /// <paramref name="newOutputPath"/>. Holding the lock across both
    /// operations means packets arriving from the demux thread mid-switch
    /// either land in the previous segment or the new one — they are
    /// never silently dropped in the close/open gap.
    /// </summary>
    public void SwitchTo(
        string newOutputPath,
        AVCodecParameters* codecpar,
        AVRational inputTimeBase,
        int rotationDegrees = 0)
    {
        lock (syncLock)
        {
            CloseLocked();
            OpenLocked(newOutputPath, codecpar, inputTimeBase, rotationDegrees);
        }
    }

    private void OpenLocked(
        string outputPath,
        AVCodecParameters* codecpar,
        AVRational inputTimeBase,
        int rotationDegrees)
    {
        try
        {
            OpenOutputLocked(outputPath, codecpar, rotationDegrees);
        }
        catch
        {
            // A recording that could not be started must not stay "open":
            // the next start would be taken for one that is already running,
            // and packets would go into a file without a header.
            AbandonOutputLocked(outputPath);
            throw;
        }
    }

    private void AbandonOutputLocked(string outputPath)
    {
        if (outputCtx is not null)
        {
            if (outputCtx->pb is not null)
            {
                avio_closep(ref outputCtx->pb);
            }

            avformat_free_context(outputCtx);
            outputCtx = null;
        }

        try
        {
            File.Delete(outputPath);
        }
        catch (IOException)
        {
            // An empty file left behind is the lesser problem.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private void OpenOutputLocked(
        string outputPath,
        AVCodecParameters* codecpar,
        int rotationDegrees)
    {
        AVFormatContext* ctx = null;

        var ret = avformat_alloc_output_context2(ref ctx, null, null, outputPath);
        if (ret < 0 || ctx is null)
        {
            throw new FFmpegException(ret, "Failed to allocate output context");
        }

        outputCtx = ctx;

        var outStream = avformat_new_stream(outputCtx, null);
        if (outStream is null)
        {
            throw new InvalidOperationException("Failed to create output stream.");
        }

        ret = avcodec_parameters_copy(outStream->codecpar, codecpar);
        if (ret < 0)
        {
            throw new FFmpegException(ret, "Failed to copy codec parameters to output");
        }

        outStream->codecpar->codec_tag = 0;
        outputTimeBase = outStream->time_base;

        // Flag the rotation so players turn the picture at playback. The
        // packets are copied untouched, so the pixels stay unrotated.
        DisplayMatrix.Write(outStream->codecpar, rotationDegrees);

        ret = avio_open(ref outputCtx->pb, outputPath, IOFlags.Write);
        if (ret < 0)
        {
            throw new FFmpegException(ret, "Failed to open output file");
        }

        AVDictionary* opts = null;
        try
        {
            if (IsMp4Family(outputPath))
            {
                // A plain MP4 keeps its index (the moov box) for the end of
                // the file, so a recording cut short by a crash or a power
                // cut has no index and cannot be opened at all. Writing the
                // header up front and the video in self-contained fragments,
                // one per keyframe, keeps everything up to the last fragment
                // playable. Matroska is already written this way.
                av_dict_set(ref opts, "movflags", "frag_keyframe+empty_moov+default_base_moof", DictWriteFlags.None);
            }

            ret = avformat_write_header(outputCtx, ref opts);
        }
        finally
        {
            av_dict_free(ref opts);
        }

        if (ret < 0)
        {
            throw new FFmpegException(ret, "Failed to write output header");
        }

        outputTimeBase = outStream->time_base;
        firstDts = AV_NOPTS_VALUE;
        lastDts = AV_NOPTS_VALUE;
        receivedKeyframe = false;
    }

    private static bool IsMp4Family(string outputPath)
        => Path.GetExtension(outputPath).ToUpperInvariant() is ".MP4" or ".MOV" or ".M4V";

    public void WritePacket(
        AVPacket* srcPacket,
        AVRational srcTimeBase)
    {
        lock (syncLock)
        {
            if (outputCtx is null)
            {
                return;
            }

            var isKeyframe = (srcPacket->flags & PktFlags.Key) != PktFlags.None;
            if (!receivedKeyframe)
            {
                if (!isKeyframe)
                {
                    return;
                }

                receivedKeyframe = true;
            }

            var clonedPkt = av_packet_clone(srcPacket);
            if (clonedPkt is null)
            {
                return;
            }

            try
            {
                if (firstDts == AV_NOPTS_VALUE)
                {
                    firstDts = clonedPkt->dts != AV_NOPTS_VALUE ? clonedPkt->dts : 0;
                }

                var dtsOffset = clonedPkt->dts != AV_NOPTS_VALUE ? clonedPkt->dts - firstDts : 0;
                var ptsOffset = clonedPkt->pts != AV_NOPTS_VALUE ? clonedPkt->pts - firstDts : dtsOffset;

                clonedPkt->dts = av_rescale_q(dtsOffset, srcTimeBase, outputTimeBase);
                clonedPkt->pts = av_rescale_q(ptsOffset, srcTimeBase, outputTimeBase);
                clonedPkt->stream_index = 0;
                clonedPkt->pos = -1;

                // Enforce monotonically increasing DTS — required by strict muxers (e.g. Matroska).
                // RTSP streams with B-frames can deliver packets out of DTS order.
                if (lastDts != AV_NOPTS_VALUE && clonedPkt->dts <= lastDts)
                {
                    clonedPkt->dts = lastDts + 1;
                    if (clonedPkt->pts < clonedPkt->dts)
                    {
                        clonedPkt->pts = clonedPkt->dts;
                    }
                }

                lastDts = clonedPkt->dts;

                // Hand the packet to the background writer. The synchronous
                // disk write used to block the demux thread; on a slow
                // drive (cleanup pass on the same disk, network share
                // hiccup) that backed up the RTSP buffer and dropped
                // frames upstream. The writer drains under syncLock so
                // CloseLocked can flush before changing outputCtx.
                if (TryEnqueueDropOldest((IntPtr)clonedPkt))
                {
                    // Ownership transferred to the queue — do NOT free here.
                    clonedPkt = null;
                    packetQueued.Set();
                }
            }
            finally
            {
                if (clonedPkt is not null)
                {
                    av_packet_free(ref clonedPkt);
                }
            }
        }
    }

    // BlockingCollection has no native drop-oldest mode; emulate it by
    // attempting a non-blocking add and, on failure, draining the oldest
    // queued packet (freeing it) before retrying once.
    private bool TryEnqueueDropOldest(IntPtr ptr)
    {
        if (writeQueue.IsAddingCompleted)
        {
            return false;
        }

        if (writeQueue.TryAdd(ptr))
        {
            return true;
        }

        if (writeQueue.TryTake(out var dropped))
        {
            FreeQueuedPacket(dropped);
        }

        return writeQueue.TryAdd(ptr);
    }

    // Started lazily on first Open and stopped in Dispose. Writes the
    // queued packets to the current outputCtx under syncLock. CloseLocked
    // drains synchronously before closing the muxer, so packets are never
    // written to a stale outputCtx.
    private void EnsureWriterRunning()
    {
        if (writerThread is not null)
        {
            return;
        }

        writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "Remuxer.Writer",
        };
        writerThread.Start();
    }

    private void WriterLoop()
    {
        try
        {
            while (!stopWriter)
            {
                packetQueued.WaitOne();

                // A packet is only taken out of the queue while holding the
                // lock. Taken first and written after waiting for the lock,
                // it could be overtaken by a Close or a SwitchTo: the file
                // it belonged to was closed without it, and the packet was
                // dropped or written into the next file.
                // The lock is taken per packet so WritePacket is never kept
                // waiting for a whole backlog to be written.
                bool written;
                do
                {
                    written = WriteNextQueuedPacket();
                }
                while (written);
            }
        }
        catch (ObjectDisposedException)
        {
            // Race during Dispose; safe to ignore.
        }
    }

    private bool WriteNextQueuedPacket()
    {
        lock (syncLock)
        {
            if (!writeQueue.TryTake(out var ptr))
            {
                return false;
            }

            if (outputCtx is not null)
            {
                var pkt = (AVPacket*)ptr;
                _ = av_interleaved_write_frame(outputCtx, pkt);
            }

            FreeQueuedPacket(ptr);
            return true;
        }
    }

    // Must be called while holding syncLock. Writes every queued packet to
    // the current outputCtx (or just frees them if outputCtx is null).
    private void DrainQueueLocked()
    {
        while (writeQueue.TryTake(out var ptr))
        {
            if (outputCtx is not null)
            {
                var pkt = (AVPacket*)ptr;
                _ = av_interleaved_write_frame(outputCtx, pkt);
            }

            FreeQueuedPacket(ptr);
        }
    }

    private static void FreeQueuedPacket(IntPtr ptr)
    {
        var pkt = (AVPacket*)ptr;
        av_packet_free(ref pkt);
    }

    public void Close()
    {
        lock (syncLock)
        {
            CloseLocked();
        }
    }

    private void CloseLocked()
    {
        if (outputCtx is null)
        {
            return;
        }

        // Flush every packet still in the queue to the current outputCtx
        // before writing the trailer. The writer task is blocked on the
        // syncLock by us, so this drain is exclusive.
        DrainQueueLocked();

        if (receivedKeyframe)
        {
            av_write_trailer(outputCtx);
        }

        avio_closep(ref outputCtx->pb);
        avformat_free_context(outputCtx);
        outputCtx = null;
        firstDts = AV_NOPTS_VALUE;
        lastDts = AV_NOPTS_VALUE;
        receivedKeyframe = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();

        // Stop the writer thread cleanly: no more packets, wake it so it
        // sees the stop, then join with a bounded wait.
        writeQueue.CompleteAdding();
        stopWriter = true;
        packetQueued.Set();
        writerThread?.Join(TimeSpan.FromSeconds(2));

        // Free any packets still queued at shutdown so we don't leak
        // native memory.
        while (writeQueue.TryTake(out var ptr))
        {
            FreeQueuedPacket(ptr);
        }

        writeQueue.Dispose();
        packetQueued.Dispose();
        writerThread = null;
    }
}