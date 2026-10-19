namespace AtcSoft.VideoSurveillance.Storybook;

/// <summary>
/// Reads a play-storybook (<c>*.storybook.json</c>) and checks that it makes sense. Every error
/// names the place in the file it is about.
/// </summary>
public static class StorybookReader
{
    private static readonly string[] ObjectKinds = ["car", "human", "cat", "dog"];
    private static readonly string[] DistractorKinds = ["swayingTree", "clockOverlay", "brightnessStep", "rain"];
    private static readonly string[] States = ["mustBox", "mustNotBox", "dontCare"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Reads a storybook file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="StorybookException">The file is no storybook, or one that makes no sense.</exception>
    public static StorybookScene ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Read(File.ReadAllText(path), Path.GetFileName(path));
    }

    /// <summary>
    /// Reads a storybook.
    /// </summary>
    /// <param name="json">The storybook's text.</param>
    /// <param name="source">What to call it in an error.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="StorybookException">The text is no storybook, or one that makes no sense.</exception>
    public static StorybookScene Read(
        string json,
        string source = "storybook")
    {
        ArgumentNullException.ThrowIfNull(json);

        StorybookScene? scene;
        try
        {
            scene = JsonSerializer.Deserialize<StorybookScene>(json, Options);
        }
        catch (JsonException ex)
        {
            var where = string.IsNullOrEmpty(ex.Path) ? string.Empty : $" at {ex.Path.TrimStart('$', '.')}";
            throw new StorybookException($"{source}{where}: {ex.Message}", ex);
        }

        if (scene is null)
        {
            throw new StorybookException($"{source}: empty");
        }

        var errors = Validate(scene).ToList();
        if (errors.Count > 0)
        {
            throw new StorybookException($"{source}: " + string.Join("; ", errors));
        }

        return scene;
    }

    private static IEnumerable<string> Validate(StorybookScene scene)
    {
        if (scene.Version != 1)
        {
            yield return $"version: {scene.Version} is not supported (1 is)";
        }

        var video = scene.Video;
        if (video.Width < 16 || video.Height < 16)
        {
            yield return "video.width/height: at least 16 pixels";
        }

        if (video.Fps is < 1 or > 120)
        {
            yield return "video.fps: 1 to 120";
        }

        if (video.DurationSeconds is <= 0 or > 3600)
        {
            yield return "video.durationSeconds: above 0, at most 3600";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < scene.Objects.Count; i++)
        {
            foreach (var error in ValidateObject(scene.Objects[i], $"objects[{i}]", ids))
            {
                yield return error;
            }
        }

        for (var i = 0; i < scene.Background.Distractors.Count; i++)
        {
            var distractor = scene.Background.Distractors[i];
            if (!DistractorKinds.Contains(distractor.Kind, StringComparer.Ordinal))
            {
                yield return $"background.distractors[{i}].kind: '{distractor.Kind}' is not one of {string.Join(", ", DistractorKinds)}";
            }

            if (string.IsNullOrWhiteSpace(distractor.Id) || !ids.Add(distractor.Id))
            {
                yield return $"background.distractors[{i}].id: missing or not unique";
            }
        }

        for (var i = 0; i < scene.Expect.Spans.Count; i++)
        {
            var span = scene.Expect.Spans[i];
            if (!States.Contains(span.State, StringComparer.Ordinal))
            {
                yield return $"expect.spans[{i}].state: '{span.State}' is not one of {string.Join(", ", States)}";
            }

            if (span.ObjectId is { } objectId && !scene.Objects.Any(o => o.Id == objectId))
            {
                yield return $"expect.spans[{i}].object: no object '{objectId}'";
            }

            if (span.DistractorId is { } distractorId && !scene.Background.Distractors.Any(d => d.Id == distractorId))
            {
                yield return $"expect.spans[{i}].distractor: no distractor '{distractorId}'";
            }

            if (span.To < span.From)
            {
                yield return $"expect.spans[{i}].to: before from";
            }
        }
    }

    private static IEnumerable<string> ValidateObject(
        StorybookObject item,
        string where,
        HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
        {
            yield return $"{where}.id: missing or not unique";
        }

        if (!ObjectKinds.Contains(item.Kind, StringComparer.Ordinal))
        {
            yield return $"{where}.kind: '{item.Kind}' is not one of {string.Join(", ", ObjectKinds)}";
        }

        if (item.Size is not [> 0 and <= 1, > 0 and <= 1])
        {
            yield return $"{where}.size: two fractions above 0, at most 1";
        }

        if (item.Path.Count == 0)
        {
            yield return $"{where}.path: at least one key point";
        }

        for (var k = 0; k < item.Path.Count; k++)
        {
            if (item.Path[k].At is not { Count: 2 })
            {
                yield return $"{where}.path[{k}].at: two numbers";
            }

            if (k > 0 && item.Path[k].T <= item.Path[k - 1].T)
            {
                yield return $"{where}.path[{k}].t: must be after path[{k - 1}].t";
            }
        }
    }
}