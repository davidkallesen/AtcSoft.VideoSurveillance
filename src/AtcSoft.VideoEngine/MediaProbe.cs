namespace AtcSoft.VideoEngine;

/// <summary>
/// Static utility for probing media file metadata using FFmpeg.
/// </summary>
public static class MediaProbe
{
    /// <summary>
    /// Gets the duration of a media file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>The duration, or <see cref="TimeSpan.Zero"/> if it cannot be determined.</returns>
    public static unsafe TimeSpan GetDuration(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        AVFormatContext* fmtCtx = null;
        AVDictionary* dict = null;
        try
        {
            int ret = avformat_open_input(ref fmtCtx, filePath, null, ref dict);
            if (ret < 0)
            {
                return TimeSpan.Zero;
            }

            ret = avformat_find_stream_info(fmtCtx, ref dict);
            if (ret < 0)
            {
                return TimeSpan.Zero;
            }

            var duration = fmtCtx->duration;
            if (duration <= 0)
            {
                return TimeSpan.Zero;
            }

            // fmtCtx->duration is in AV_TIME_BASE (microseconds)
            return TimeSpan.FromTicks(duration * 10);
        }
        catch
        {
            return TimeSpan.Zero;
        }
        finally
        {
            if (fmtCtx is not null)
            {
                avformat_close_input(ref fmtCtx);
            }
        }
    }

    /// <summary>
    /// Gets the clockwise rotation a media file asks players to apply to its
    /// video, as flagged by the recorder for a rotated camera.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>0, 90, 180 or 270; 0 when the file has no rotation flag or cannot be read.</returns>
    public static unsafe int GetRotation(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        AVFormatContext* fmtCtx = null;
        AVDictionary* dict = null;
        try
        {
            int ret = avformat_open_input(ref fmtCtx, filePath, null, ref dict);
            if (ret < 0)
            {
                return 0;
            }

            for (var i = 0; i < fmtCtx->nb_streams; i++)
            {
                var codecpar = fmtCtx->streams[i]->codecpar;
                if (codecpar->codec_type == AVMediaType.Video)
                {
                    return Recording.DisplayMatrix.ReadClockwiseDegrees(codecpar);
                }
            }

            return 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (fmtCtx is not null)
            {
                avformat_close_input(ref fmtCtx);
            }
        }
    }
}