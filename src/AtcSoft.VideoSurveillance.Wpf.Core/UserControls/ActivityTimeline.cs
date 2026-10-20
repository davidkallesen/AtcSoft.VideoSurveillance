namespace AtcSoft.VideoSurveillance.Wpf.Core.UserControls;

/// <summary>
/// The activity of a recording drawn under its player: a bar for how much of the picture changed
/// in each stretch, an orange band where something went somewhere (a confirmed track; in the colour
/// of the watch zone it happened in, when that zone has one, see <see cref="ZoneBrushes"/>), dim where
/// nothing was analysed, and a line where the player is. A click asks to jump there through
/// <see cref="SeekCommand"/> with the place as a fraction of the recording, 0 to 1.
/// </summary>
public sealed partial class ActivityTimeline : FrameworkElement
{
    private const double BandHeight = 4;

    // The looks when none is set: the generator takes no default from a field's initialiser.
    private static readonly Brush DefaultBarBrush = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush DefaultEventBrush = Frozen(Color.FromRgb(0xFF, 0x8C, 0x00));
    private static readonly Brush DefaultNotAnalysedBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private IReadOnlyList<ActivityBucket>? buckets;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private double position;

    [DependencyProperty]
    private ICommand? seekCommand;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private Brush? barBrush;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private Brush? eventBrush;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private Brush? notAnalysedBrush;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private Brush? positionBrush;

    [DependencyProperty(PropertyChangedCallback = nameof(OnLookChanged))]
    private IReadOnlyDictionary<string, Brush>? zoneBrushes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityTimeline"/> class.
    /// </summary>
    public ActivityTimeline()
    {
        Cursor = Cursors.Hand;
        ClipToBounds = true;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;

        // Transparent, not empty: the whole strip takes clicks.
        drawingContext.DrawRectangle(Brushes.Transparent, pen: null, new Rect(0, 0, width, height));

        if (Buckets is not { Count: > 0 } stretches || width <= 0 || height <= BandHeight)
        {
            return;
        }

        var barArea = height - BandHeight - 1;
        var step = width / stretches.Count;
        for (var i = 0; i < stretches.Count; i++)
        {
            var stretch = stretches[i];
            var x = i * step;
            if (!stretch.IsAnalysed)
            {
                drawingContext.DrawRectangle(NotAnalysedBrush ?? DefaultNotAnalysedBrush, pen: null, new Rect(x, 0, Math.Max(step, 1), barArea));
                continue;
            }

            var barHeight = ActivityTimelineLayout.BarHeight(stretch.Level) * barArea;
            if (barHeight > 0)
            {
                drawingContext.DrawRectangle(BarBrush ?? DefaultBarBrush, pen: null, new Rect(x, barArea - barHeight, Math.Max(step, 1), barHeight));
            }
        }

        foreach (var activityEvent in ActivityTimelineLayout.FindEvents(stretches))
        {
            var (x, bandWidth) = ActivityTimelineLayout.ToPixels(activityEvent, width);
            var brush = activityEvent.Zone is { } zone && ZoneBrushes?.TryGetValue(zone, out var zoned) == true
                ? zoned
                : EventBrush ?? DefaultEventBrush;
            drawingContext.DrawRectangle(brush, pen: null, new Rect(x, height - BandHeight, bandWidth, BandHeight));
        }

        var playhead = Math.Clamp(Position, 0, 1) * width;
        drawingContext.DrawRectangle(PositionBrush ?? Brushes.White, pen: null, new Rect(Math.Clamp(playhead - 1, 0, Math.Max(width - 2, 0)), 0, 2, height));
    }

    /// <inheritdoc />
    /// <remarks>A plain element has no peer: without one, screen readers and UI tests cannot see the timeline.</remarks>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);

        if (ActualWidth <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true)
        {
            SeekCommand.Execute(fraction);
            e.Handled = true;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OnLookChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is ActivityTimeline timeline)
        {
            timeline.InvalidateVisual();
        }
    }
}