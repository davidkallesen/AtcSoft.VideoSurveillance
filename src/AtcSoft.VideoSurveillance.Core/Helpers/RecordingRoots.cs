namespace AtcSoft.VideoSurveillance.Helpers;

/// <summary>
/// The folders recordings live in: the global recording folder and each camera's own folder
/// (<c>Overrides.Recording.RecordingPath</c>). Everything that reads recordings reads all of
/// them, each folder once: a folder inside another is read as part of it.
/// </summary>
public static class RecordingRoots
{
    /// <summary>
    /// Gets the folders to read, the global one first, as full paths.
    /// </summary>
    /// <param name="globalRoot">The global recording folder, or <see langword="null"/>.</param>
    /// <param name="cameraRoots">Each camera's own recording folder; blanks are skipped.</param>
    public static IReadOnlyList<string> Collect(
        string? globalRoot,
        IEnumerable<string?> cameraRoots)
    {
        ArgumentNullException.ThrowIfNull(cameraRoots);

        var candidates = new List<string>();
        foreach (var root in new[] { globalRoot }.Concat(cameraRoots))
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
            if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(full);
            }
        }

        return candidates
            .Where(root => !candidates.Any(other => !ReferenceEquals(other, root) && IsInside(root, other)))
            .ToList();
    }

    private static bool IsInside(
        string folder,
        string possibleParent)
        => folder.StartsWith(possibleParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}