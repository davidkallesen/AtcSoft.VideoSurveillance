namespace AtcSoft.VideoSurveillance.Storybook.Scoring;

/// <summary>
/// How a scene is fed to a detector. The defaults are what the apps do today: pictures squeezed
/// to 800×600 (the aspect ratio is not kept) at the server's rate of 4 a second.
/// </summary>
/// <param name="AnalysisWidth">The width the pictures are scaled to.</param>
/// <param name="AnalysisHeight">The height the pictures are scaled to.</param>
/// <param name="AnalysesPerSecond">How many pictures a second are analysed.</param>
public sealed record StorybookRunOptions(
    int AnalysisWidth = 800,
    int AnalysisHeight = 600,
    double AnalysesPerSecond = 4);