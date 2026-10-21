namespace AtcSoft.VideoSurveillance.Storybook.Model;

/// <summary>
/// The detector settings the scene is meant for, where they differ from the defaults: a door
/// camera, where someone fills most of the picture, does not ignore lighting changes.
/// </summary>
public sealed class StorybookDetector
{
    /// <summary>
    /// Gets or sets whether lighting changes are ignored; the default's when not given.
    /// </summary>
    public bool? IgnoreLightingChanges { get; set; }

    /// <summary>
    /// Gets the settings to run the scene with: the defaults, with what the scene gives.
    /// </summary>
    /// <returns>New settings.</returns>
    public MotionDetectionSettings Settings()
    {
        var settings = new MotionDetectionSettings();
        if (IgnoreLightingChanges is { } ignore)
        {
            settings.IgnoreLightingChanges = ignore;
        }

        return settings;
    }
}