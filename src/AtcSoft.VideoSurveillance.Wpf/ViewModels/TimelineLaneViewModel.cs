namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// One camera's lane on the Timeline view: what the lane control draws, and a click that plays
/// from that moment.
/// </summary>
public sealed partial class TimelineLaneViewModel : ViewModelBase
{
    private readonly Action<TimelineLaneViewModel, double> playAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineLaneViewModel"/> class.
    /// </summary>
    /// <param name="lane">The camera's lane as the server gives it.</param>
    /// <param name="from">The start of the span.</param>
    /// <param name="to">The end of the span.</param>
    /// <param name="now">Now, for the red line when the span reaches it.</param>
    /// <param name="playAt">Plays the lane from a place, as a fraction of the span.</param>
    public TimelineLaneViewModel(
        TimelineLane lane,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        Action<TimelineLaneViewModel, double> playAt)
    {
        ArgumentNullException.ThrowIfNull(lane);
        ArgumentNullException.ThrowIfNull(playAt);

        this.playAt = playAt;
        CameraId = lane.CameraId;
        CameraName = lane.CameraName;
        Coverage = lane.Coverage;
        Stretches = [.. lane.Buckets.Select(b => new TimelineStretch(b.Level, b.Tracks, b.IsRecorded, b.IsAnalysed))];

        var length = (to - from).TotalSeconds;
        EventBands = length <= 0
            ? []
            : [.. lane.Events.Select(e => new ActivityEvent(
                Math.Clamp((e.Start - from).TotalSeconds / length, 0, 1),
                Math.Clamp((e.End - from).TotalSeconds / length, 0, 1)))];
        NowFraction = now >= from && now <= to && length > 0 ? (now - from).TotalSeconds / length : -1;
    }

    public Guid CameraId { get; }

    public string CameraName { get; }

    public IReadOnlyList<TimelineCoverage> Coverage { get; }

    public IReadOnlyList<TimelineStretch> Stretches { get; }

    public IReadOnlyList<ActivityEvent> EventBands { get; }

    public double NowFraction { get; }

    [RelayCommand]
    private void PlayAt(double fraction)
        => playAt(this, fraction);
}