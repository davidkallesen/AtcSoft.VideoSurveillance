namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// What the recordings page says before and after it deletes a recording.
/// </summary>
public static class RecordingDeletion
{
    /// <summary>
    /// Gets the question asked before a recording is deleted.
    /// </summary>
    /// <param name="cameraName">The camera, if it is still known.</param>
    /// <param name="started">When the recording started, as the list shows it.</param>
    public static string Question(
        string? cameraName,
        string started)
    {
        var what = string.IsNullOrEmpty(cameraName)
            ? "the recording"
            : $"the recording of {cameraName}";

        return $"Delete {what} from {started}? The file is removed from the server and cannot be brought back.";
    }

    /// <summary>
    /// Gets what to tell the user about the outcome.
    /// </summary>
    public static string ResultText(RecordingDeletionOutcome outcome)
        => outcome switch
        {
            RecordingDeletionOutcome.Deleted => "Recording deleted.",
            RecordingDeletionOutcome.InProgress => "The recording is still being made. Delete it when it has ended.",
            RecordingDeletionOutcome.NotFound => "The recording was already gone.",
            _ => "The recording could not be deleted.",
        };

    /// <summary>
    /// Gets whether the outcome is the one the user asked for.
    /// </summary>
    public static bool Succeeded(RecordingDeletionOutcome outcome)
        => outcome == RecordingDeletionOutcome.Deleted;

    /// <summary>
    /// Gets whether the recording no longer exists and leaves the list.
    /// </summary>
    public static bool IsGone(RecordingDeletionOutcome outcome)
        => outcome is RecordingDeletionOutcome.Deleted or RecordingDeletionOutcome.NotFound;
}