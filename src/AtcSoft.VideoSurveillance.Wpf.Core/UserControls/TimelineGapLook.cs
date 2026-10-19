namespace AtcSoft.VideoSurveillance.Wpf.Core.UserControls;

/// <summary>
/// How a timeline shows why a camera has a gap: a colour and a text per reason, the same colours
/// as the web client. The brushes are public for a legend in XAML.
/// </summary>
public static class TimelineGapLook
{
    public static Brush OfflineBrush { get; } = Frozen(Color.FromArgb(0x59, 0xE5, 0x39, 0x35));

    public static Brush DiskFullBrush { get; } = Frozen(Color.FromArgb(0x73, 0xFF, 0xB3, 0x00));

    public static Brush ServerDownBrush { get; } = Frozen(Color.FromArgb(0x59, 0x7E, 0x57, 0xC2));

    public static Brush NotRecordingBrush { get; } = Frozen(Color.FromArgb(0x59, 0x80, 0x80, 0x80));

    public static Brush UnknownBrush { get; } = Frozen(Color.FromArgb(0x2E, 0x80, 0x80, 0x80));

    /// <summary>
    /// Gets the brush of a reason.
    /// </summary>
    /// <param name="reason">Why nothing was recorded.</param>
    public static Brush BrushOf(GapReason reason)
        => reason switch
        {
            GapReason.Offline => OfflineBrush,
            GapReason.DiskFull => DiskFullBrush,
            GapReason.ServerDown => ServerDownBrush,
            GapReason.NotRecording => NotRecordingBrush,
            _ => UnknownBrush,
        };

    /// <summary>
    /// Gets the text of a reason, in the viewer's language.
    /// </summary>
    /// <param name="reason">Why nothing was recorded.</param>
    public static string TextOf(GapReason reason)
        => reason switch
        {
            GapReason.Offline => Translations.GapOffline,
            GapReason.DiskFull => Translations.GapDiskFull,
            GapReason.ServerDown => Translations.GapServerDown,
            GapReason.NotRecording => Translations.GapNotRecording,
            _ => Translations.GapUnknown,
        };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}