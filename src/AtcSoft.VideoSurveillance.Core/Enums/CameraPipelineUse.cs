namespace AtcSoft.VideoSurveillance.Enums;

/// <summary>
/// What someone needs a camera's shared pipeline for.
/// </summary>
public enum CameraPipelineUse
{
    /// <summary>
    /// The camera is being watched live.
    /// </summary>
    Viewing = 0,

    /// <summary>
    /// The camera is being recorded because someone asked for it.
    /// </summary>
    Recording = 1,
}