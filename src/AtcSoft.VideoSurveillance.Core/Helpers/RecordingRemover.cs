namespace AtcSoft.VideoSurveillance.Helpers;

/// <summary>
/// Removes one recording that a client names by its path below the
/// recording folder. The path comes from outside, so it is only followed
/// when it leads to a video file in a camera folder below that root.
/// </summary>
public static class RecordingRemover
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi"];

    /// <summary>
    /// Removes a recording together with its thumbnail, and the day folder
    /// it leaves empty.
    /// </summary>
    /// <param name="recordingRoot">The recording folder.</param>
    /// <param name="relativePath">The recording's path below it, as a listing gives it (<c>{camera}/.../{file}</c>).</param>
    /// <param name="activePaths">The full paths of the files that are being recorded right now.</param>
    /// <returns>How it went.</returns>
    public static RecordingRemovalOutcome Remove(
        string recordingRoot,
        string? relativePath,
        IReadOnlySet<string> activePaths)
    {
        ArgumentNullException.ThrowIfNull(activePaths);

        if (!TryResolve(recordingRoot, relativePath, out var fullPath))
        {
            return RecordingRemovalOutcome.NotARecording;
        }

        if (!File.Exists(fullPath))
        {
            return RecordingRemovalOutcome.NotFound;
        }

        // Asked of the file itself as well: another program may be
        // recording into the same folder.
        if (activePaths.Contains(fullPath) || RecordingPathHelper.IsBeingWritten(fullPath))
        {
            return RecordingRemovalOutcome.InProgress;
        }

        try
        {
            File.Delete(fullPath);
        }
        catch (IOException)
        {
            return RecordingRemovalOutcome.InProgress;
        }

        foreach (var thumbnail in RecordingPathHelper.GetThumbnailCandidates(fullPath, recordingRoot))
        {
            try
            {
                File.Delete(thumbnail);
            }
            catch (IOException)
            {
                // The recording is gone; a thumbnail left behind is removed by the clean-up.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }
        }

        _ = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(recordingRoot, MediaCleanupRunner.EmptyDirectoryGracePeriod);

        return RecordingRemovalOutcome.Removed;
    }

    private static bool TryResolve(
        string recordingRoot,
        string? relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(recordingRoot) || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var relative = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        var root = Path.GetFullPath(recordingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relative));

        // ".." may not lead out of the recording folder.
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // {camera}\...\{file}: a video, in a camera folder, not among the thumbnails.
        if (!VideoExtensions.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase) ||
            RecordingPathHelper.GetCameraFolderFromPath(candidate, recordingRoot) is null ||
            RecordingPathHelper.IsInThumbnailsFolder(candidate, recordingRoot))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}