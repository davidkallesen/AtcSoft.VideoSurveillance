namespace AtcSoft.VideoSurveillance.Wpf.Core.Windows;

/// <summary>
/// One camera of *play all from here*: its recordings from the chosen moment, played one after
/// another as the pacer's clock reaches each, and what the tile says when it shows no picture.
/// </summary>
public sealed partial class PlayAllTileViewModel : ViewModelBase
{
    private readonly IReadOnlyList<PlayAllSegment> segments;
    private IPlayAllMedia? media;
    private int index;
    private bool isPaused;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayAllTileViewModel"/> class.
    /// </summary>
    /// <param name="cameraName">The camera's name, shown on the tile.</param>
    /// <param name="segments">Its recordings that end after the chosen moment, in time order.</param>
    public PlayAllTileViewModel(
        string cameraName,
        IReadOnlyList<PlayAllSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        CameraName = cameraName;
        this.segments = segments;
        MoveTo(0);
    }

    public string CameraName { get; }

    public PlayAllTileState State { get; private set; }

    private PlayAllSegment? Current
        => index < segments.Count ? segments[index] : null;

    /// <summary>
    /// Gives the tile its player. Until then it waits.
    /// </summary>
    /// <param name="player">The player.</param>
    public void Attach(IPlayAllMedia player)
    {
        ArgumentNullException.ThrowIfNull(player);

        media = player;
        media.Opened += OnOpened;
        media.Ended += OnEnded;
        media.Failed += OnFailed;
    }

    /// <summary>
    /// Tells the pacer where the tile is.
    /// </summary>
    /// <returns>The tile as the pacer reads it.</returns>
    public PlayAllTile Report()
        => State switch
        {
            PlayAllTileState.Playing or PlayAllTileState.Held when media is not null && Current is { } segment
                => new PlayAllTile(State, At: segment.RecordingStart + media.Position),
            PlayAllTileState.Waiting when media is not null && Current is { } segment
                => new PlayAllTile(State, NextStart: segment.Start),
            PlayAllTileState.Waiting => new PlayAllTile(PlayAllTileState.Loading),
            _ => new PlayAllTile(State),
        };

    /// <summary>
    /// Does what the pacer asks.
    /// </summary>
    /// <param name="action">The pacer's action for this tile.</param>
    /// <param name="clock">The pacer's clock.</param>
    public void Apply(
        PlayAllAction action,
        DateTimeOffset clock)
    {
        switch (action)
        {
            case PlayAllAction.Start when media is not null && Current is { } segment:
                var position = clock - segment.RecordingStart;
                State = PlayAllTileState.Loading;
                StatusText = Translations.Loading;
                media.Open(segment.Address, position > TimeSpan.Zero ? position : TimeSpan.Zero);
                break;
            case PlayAllAction.Hold when State == PlayAllTileState.Playing:
                media?.Pause();
                State = PlayAllTileState.Held;
                break;
            case PlayAllAction.Resume when State == PlayAllTileState.Held:
                if (!isPaused)
                {
                    media?.Play();
                }

                State = PlayAllTileState.Playing;
                break;
            case PlayAllAction.GiveUp when State == PlayAllTileState.Loading:
                media?.Close();
                State = PlayAllTileState.Failed;
                StatusText = Translations.CouldNotBePlayed;
                break;
        }
    }

    /// <summary>
    /// Pauses or plays the tile with the others, as the viewer asks.
    /// </summary>
    /// <param name="paused">Whether everything is paused.</param>
    public void SetPaused(bool paused)
    {
        isPaused = paused;
        if (State != PlayAllTileState.Playing)
        {
            return;
        }

        if (paused)
        {
            media?.Pause();
        }
        else
        {
            media?.Play();
        }
    }

    /// <summary>
    /// Stops the tile's player.
    /// </summary>
    public void Close()
    {
        if (media is null)
        {
            return;
        }

        media.Opened -= OnOpened;
        media.Ended -= OnEnded;
        media.Failed -= OnFailed;
        media.Close();
        media = null;
    }

    private void MoveTo(int next)
    {
        index = next;
        if (Current is { } segment)
        {
            State = PlayAllTileState.Waiting;
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                Translations.NextRecordingAt1,
                segment.Start.ToLocalTime().ToString("T", CultureInfo.CurrentCulture));
        }
        else
        {
            State = PlayAllTileState.Finished;
            StatusText = Translations.NoMoreRecordings;
        }
    }

    private void OnOpened(
        object? sender,
        EventArgs e)
    {
        if (State != PlayAllTileState.Loading)
        {
            return;
        }

        State = PlayAllTileState.Playing;
        StatusText = string.Empty;
        if (isPaused)
        {
            media?.Pause();
        }
    }

    private void OnEnded(
        object? sender,
        EventArgs e)
        => MoveTo(index + 1);

    private void OnFailed(
        object? sender,
        EventArgs e)
    {
        State = PlayAllTileState.Failed;
        StatusText = Translations.CouldNotBePlayed;
    }
}