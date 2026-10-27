namespace BoundingBoxWpfApp;

/// <summary>
/// What the sample opened, run through the detector.
/// </summary>
/// <param name="Name">The storybook's name or the recording's file name.</param>
/// <param name="Way">How the pictures reached the detector: <c>fast</c>, <c>encoded</c> or <c>recording</c>.</param>
/// <param name="Run">The frames, with their pictures, and the score.</param>
/// <param name="HasTruth">Whether there is a truth to show and score against (not for a recording).</param>
/// <param name="ScoreJson">The score as JSON, as shown in the score panel.</param>
public sealed record SampleRun(
    string Name,
    string Way,
    StorybookRun Run,
    bool HasTruth,
    string ScoreJson);