namespace BoundingBoxWpfApp;

/// <summary>
/// What the sample opens when it starts: <c>--storybook file</c> or <c>--recording file</c>, with
/// <c>--rate</c> pictures a second (4 by default, the server's; CameraWall's is 15). The E2E walk
/// uses them instead of the file dialogs.
/// </summary>
/// <param name="StorybookPath">A storybook to open, or <see langword="null"/>.</param>
/// <param name="RecordingPath">A recording to open, or <see langword="null"/>.</param>
/// <param name="AnalysesPerSecond">How many pictures a second are analysed.</param>
public sealed record StartupOptions(
    string? StorybookPath,
    string? RecordingPath,
    double AnalysesPerSecond)
{
    /// <summary>
    /// The rate when none is given: the server's.
    /// </summary>
    public const double DefaultAnalysesPerSecond = 4;

    /// <summary>
    /// Reads the command line; an unknown argument is passed over.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? storybook = null;
        string? recording = null;
        var rate = DefaultAnalysesPerSecond;

        // Each option is a name and the value after it.
        foreach (var (name, value) in args.Zip(args.Skip(1)))
        {
            switch (name)
            {
                case "--storybook":
                    storybook = value;
                    break;
                case "--recording":
                    recording = value;
                    break;
                case "--rate" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0:
                    rate = parsed;
                    break;
            }
        }

        return new StartupOptions(storybook, recording, rate);
    }
}