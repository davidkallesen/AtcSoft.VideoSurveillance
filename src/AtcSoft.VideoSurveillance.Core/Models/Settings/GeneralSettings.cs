namespace Linksoft.VideoSurveillance.Models.Settings;

/// <summary>
/// General application settings.
/// </summary>
public class GeneralSettings
{
    public string ThemeBase { get; set; } = "Dark";

    public string ThemeAccent { get; set; } = "Blue";

    public string Language { get; set; } = "1033";

    public bool ConnectCamerasOnStartup { get; set; } = true;

    public bool StartMaximized { get; set; }

    public bool StartRibbonCollapsed { get; set; }

    /// <inheritdoc />
    public override string ToString()
        => $"GeneralSettings {{ Theme='{ThemeBase}/{ThemeAccent}', Language='{Language}' }}";

    public GeneralSettings Clone()
        => new()
        {
            ThemeBase = ThemeBase,
            ThemeAccent = ThemeAccent,
            Language = Language,
            ConnectCamerasOnStartup = ConnectCamerasOnStartup,
            StartMaximized = StartMaximized,
            StartRibbonCollapsed = StartRibbonCollapsed,
        };
}