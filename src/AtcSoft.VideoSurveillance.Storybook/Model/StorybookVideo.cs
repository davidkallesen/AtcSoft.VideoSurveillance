namespace AtcSoft.VideoSurveillance.Storybook.Model;

/// <summary>
/// The picture the scene is drawn into.
/// </summary>
public sealed class StorybookVideo
{
    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int Fps { get; set; } = 15;

    public double DurationSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the sensor noise: how far a pixel strays each frame, in grey levels.
    /// </summary>
    public double Noise { get; set; } = 2;

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public int FrameCount
        => (int)Math.Round(DurationSeconds * Fps);
}