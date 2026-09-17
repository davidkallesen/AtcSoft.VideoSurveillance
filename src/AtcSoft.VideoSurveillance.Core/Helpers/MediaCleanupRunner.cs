namespace Linksoft.VideoSurveillance.Helpers;

/// <summary>
/// Pure file-system cleanup pass shared by the WPF and server media-cleanup
/// services. No timers, no logging, no DI — input is a directory + retention
/// rules + active-path skip set; output is counts, bytes freed, and the list
/// of deleted files (for the caller to log).
/// </summary>
public static class MediaCleanupRunner
{
    /// <summary>
    /// Deletes files in <paramref name="rootPath"/> (recursive) whose
    /// extension is in <paramref name="extensions"/> and whose last-write
    /// time is strictly before <paramref name="cutoff"/>, except those whose
    /// normalized full path is in <paramref name="skipPaths"/>.
    /// </summary>
    /// <param name="rootPath">Directory to scan recursively.</param>
    /// <param name="extensions">
    /// Lowercase file extensions including the leading dot, e.g. <c>.mkv</c>.
    /// </param>
    /// <param name="cutoff">Files older than this are eligible for deletion.</param>
    /// <param name="skipPaths">
    /// Case-insensitive set of full paths to leave untouched (active recordings
    /// and their thumbnail companions).
    /// </param>
    /// <param name="deleteCompanionThumbnail">
    /// When <c>true</c>, deleting a media file also deletes the matching
    /// <c>.png</c> thumbnail next to it.
    /// </param>
    public static MediaCleanupRunResult CleanDirectory(
        string rootPath,
        IReadOnlyCollection<string> extensions,
        DateTime cutoff,
        IReadOnlySet<string> skipPaths,
        bool deleteCompanionThumbnail)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(skipPaths);

        var extensionSet = extensions as IReadOnlySet<string>
            ?? new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var deleted = new List<string>();
        var deletedThumbnails = new List<string>();
        var errors = new List<MediaCleanupRunError>();
        long bytesFreed = 0;

        if (!Directory.Exists(rootPath))
        {
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory
                .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensionSet.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }
        catch (IOException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        foreach (var file in files)
        {
            try
            {
                if (skipPaths.Contains(Path.GetFullPath(file)))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (info.LastWriteTime >= cutoff)
                {
                    continue;
                }

                var fileSize = info.Length;
                info.Delete();
                deleted.Add(file);
                bytesFreed += fileSize;

                if (deleteCompanionThumbnail)
                {
                    var thumbnailPath = Path.ChangeExtension(file, ".png");
                    if (File.Exists(thumbnailPath))
                    {
                        var thumbInfo = new FileInfo(thumbnailPath);
                        var thumbSize = thumbInfo.Length;
                        thumbInfo.Delete();
                        deletedThumbnails.Add(thumbnailPath);
                        bytesFreed += thumbSize;
                    }
                }
            }
            catch (IOException ex)
            {
                errors.Add(new MediaCleanupRunError(file, ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new MediaCleanupRunError(file, ex));
            }
        }

        return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
    }

    /// <summary>
    /// Deletes files in <paramref name="rootPath"/> (recursive), oldest first
    /// by <c>LastWriteTime</c>, until the recording drive has
    /// at least <paramref name="targetFreeBytes"/> of free space, or all
    /// eligible files have been exhausted.
    /// </summary>
    /// <param name="rootPath">Directory to scan recursively.</param>
    /// <param name="extensions">
    /// Lowercase file extensions including the leading dot, e.g. <c>.mkv</c>.
    /// </param>
    /// <param name="targetFreeBytes">
    /// Desired minimum free bytes on the drive that hosts
    /// <paramref name="rootPath"/>. When the drive already has this much free
    /// space the method returns immediately with an empty result.
    /// </param>
    /// <param name="skipPaths">
    /// Case-insensitive set of full paths to leave untouched (active recordings
    /// and their thumbnail companions).
    /// </param>
    /// <param name="deleteCompanionThumbnail">
    /// When <c>true</c>, deleting a media file also deletes the matching
    /// <c>.png</c> thumbnail next to it.
    /// </param>
    /// <returns>
    /// A <see cref="MediaCleanupRunResult"/> describing the deleted files and
    /// bytes freed. Check <see cref="MediaCleanupRunResult.StillShort"/> to
    /// determine whether the target free-space goal was reached.
    /// </returns>
    public static MediaCleanupRunResult ReclaimBySize(
        string rootPath,
        IReadOnlyCollection<string> extensions,
        long targetFreeBytes,
        IReadOnlySet<string> skipPaths,
        bool deleteCompanionThumbnail)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(skipPaths);

        var deleted = new List<string>();
        var deletedThumbnails = new List<string>();
        var errors = new List<MediaCleanupRunError>();
        long bytesFreed = 0;

        if (!Directory.Exists(rootPath))
        {
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        // Determine drive and check current free space
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
        if (string.IsNullOrEmpty(driveRoot))
        {
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        long initialFreeBytes;
        try
        {
            initialFreeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        if (initialFreeBytes >= targetFreeBytes)
        {
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors);
        }

        var neededBytes = targetFreeBytes - initialFreeBytes;

        var extensionSet = extensions as IReadOnlySet<string>
            ?? new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        IList<FileInfo> files;
        try
        {
            files = Directory
                .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensionSet.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !skipPaths.Contains(Path.GetFullPath(f)))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTime)
                .ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors) { StillShort = true };
        }
        catch (IOException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors) { StillShort = true };
        }

        foreach (var file in files)
        {
            if (bytesFreed >= neededBytes)
            {
                break;
            }

            try
            {
                var fileSize = file.Length;
                file.Delete();
                deleted.Add(file.FullName);
                bytesFreed += fileSize;

                if (deleteCompanionThumbnail)
                {
                    var thumbnailPath = Path.ChangeExtension(file.FullName, ".png");
                    if (File.Exists(thumbnailPath))
                    {
                        var thumbInfo = new FileInfo(thumbnailPath);
                        var thumbSize = thumbInfo.Length;
                        thumbInfo.Delete();
                        deletedThumbnails.Add(thumbnailPath);
                        bytesFreed += thumbSize;
                    }
                }
            }
            catch (IOException ex)
            {
                errors.Add(new MediaCleanupRunError(file.FullName, ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new MediaCleanupRunError(file.FullName, ex));
            }
        }

        return new MediaCleanupRunResult(deleted, deletedThumbnails, bytesFreed, errors)
        {
            StillShort = bytesFreed < neededBytes,
        };
    }

    /// <summary>
    /// Removes every empty directory under <paramref name="rootPath"/>,
    /// deepest-first. The root itself is left in place. Failures are
    /// returned in the result; never thrown.
    /// </summary>
    public static MediaCleanupDirectoryResult RemoveEmptyDirectoriesBelow(
        string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        var removed = new List<string>();
        var errors = new List<MediaCleanupRunError>();

        if (!Directory.Exists(rootPath))
        {
            return new MediaCleanupDirectoryResult(removed, errors);
        }

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory
                .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length)
                .ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupDirectoryResult(removed, errors);
        }
        catch (IOException ex)
        {
            errors.Add(new MediaCleanupRunError(rootPath, ex));
            return new MediaCleanupDirectoryResult(removed, errors);
        }

        foreach (var dir in dirs)
        {
            try
            {
                if (string.Equals(dir, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    continue;
                }

                Directory.Delete(dir);
                removed.Add(dir);
            }
            catch (IOException ex)
            {
                errors.Add(new MediaCleanupRunError(dir, ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new MediaCleanupRunError(dir, ex));
            }
        }

        return new MediaCleanupDirectoryResult(removed, errors);
    }
}