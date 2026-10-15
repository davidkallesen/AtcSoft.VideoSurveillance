namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// What a camera tile shows when its stream could not be started.
/// </summary>
public static class StreamErrorText
{
    // What SignalR and the server put in front of the reason itself.
    private static readonly string[] Wrappings =
    [
        "HubException:",
        "Failed to start stream:",
    ];

    /// <summary>
    /// Gets the reason from a failed start, without the technical text
    /// SignalR and the server wrap it in.
    /// </summary>
    /// <param name="message">The message of the exception the start failed with.</param>
    /// <returns>The text to show.</returns>
    public static string For(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Translations.StreamCouldNotBeStarted;
        }

        var wrapped = false;
        foreach (var wrapping in Wrappings)
        {
            var at = message.IndexOf(wrapping, StringComparison.Ordinal);
            if (at >= 0)
            {
                message = message[(at + wrapping.Length)..];
                wrapped = true;
            }
        }

        message = message.Trim();

        // SignalR's own sentence without a reason behind it says nothing.
        var hasReason = message.Length > 0 &&
                        (wrapped || !message.StartsWith("An unexpected error occurred invoking", StringComparison.Ordinal));
        return hasReason ? message : Translations.StreamCouldNotBeStarted;
    }
}