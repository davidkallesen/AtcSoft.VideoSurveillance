namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// One camera of *play all from here* on the web: its recordings from the chosen moment, played
/// one after another as the pacer's clock reaches each, and what the tile says when it shows no
/// picture. It does not touch the browser: it answers with the commands for the video element.
/// </summary>
public sealed class PlayAllTileLogic
{
    private readonly IReadOnlyList<PlayAllSegment> segments;
    private int index;
    private bool isPaused;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayAllTileLogic"/> class.
    /// </summary>
    /// <param name="cameraName">The camera's name, shown on the tile.</param>
    /// <param name="segments">Its recordings that end after the chosen moment, in time order.</param>
    public PlayAllTileLogic(
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

    /// <summary>
    /// Gets what the tile says over its picture: empty while it plays.
    /// </summary>
    public string StatusText { get; private set; } = string.Empty;

    private PlayAllSegment? Current
        => index < segments.Count ? segments[index] : null;

    /// <summary>
    /// Tells the pacer where the tile is.
    /// </summary>
    /// <param name="currentSeconds">How far into the recording the video element is.</param>
    /// <returns>The tile as the pacer reads it.</returns>
    public PlayAllTile Report(double currentSeconds)
        => State switch
        {
            PlayAllTileState.Playing or PlayAllTileState.Held when Current is { } segment
                => new PlayAllTile(State, At: segment.RecordingStart + TimeSpan.FromSeconds(currentSeconds)),
            PlayAllTileState.Waiting when Current is { } segment
                => new PlayAllTile(State, NextStart: segment.Start),
            _ => new PlayAllTile(State),
        };

    /// <summary>
    /// Does what the pacer asks.
    /// </summary>
    /// <param name="action">The pacer's action for this tile.</param>
    /// <param name="clock">The pacer's clock.</param>
    /// <returns>The command for the video element, or <see langword="null"/>.</returns>
    public PlayAllCommand? Apply(
        PlayAllAction action,
        DateTimeOffset clock)
    {
        switch (action)
        {
            case PlayAllAction.Start when Current is { } segment:
                State = PlayAllTileState.Loading;
                StatusText = Translations.Loading;
                return new PlayAllCommand(PlayAllCommandKind.Open, segment.Source, Math.Max((clock - segment.RecordingStart).TotalSeconds, 0));
            case PlayAllAction.Hold when State == PlayAllTileState.Playing:
                State = PlayAllTileState.Held;
                return new PlayAllCommand(PlayAllCommandKind.Pause);
            case PlayAllAction.Resume when State == PlayAllTileState.Held:
                State = PlayAllTileState.Playing;
                return isPaused ? null : new PlayAllCommand(PlayAllCommandKind.Play);
            case PlayAllAction.GiveUp when State == PlayAllTileState.Loading:
                Fail();
                return new PlayAllCommand(PlayAllCommandKind.Close);
            default:
                return null;
        }
    }

    /// <summary>
    /// The video element shows the opened recording.
    /// </summary>
    /// <returns>A pause when everything is paused, else <see langword="null"/>.</returns>
    public PlayAllCommand? Opened()
    {
        if (State != PlayAllTileState.Loading)
        {
            return null;
        }

        State = PlayAllTileState.Playing;
        StatusText = string.Empty;
        return isPaused ? new PlayAllCommand(PlayAllCommandKind.Pause) : null;
    }

    /// <summary>
    /// The recording played to its end: the tile waits for its next one.
    /// </summary>
    public void Ended()
        => MoveTo(index + 1);

    /// <summary>
    /// The video element cannot play the recording.
    /// </summary>
    public void Failed()
        => Fail();

    /// <summary>
    /// Pauses or plays the tile with the others, as the viewer asks.
    /// </summary>
    /// <param name="paused">Whether everything is paused.</param>
    /// <returns>The command for the video element, or <see langword="null"/>.</returns>
    public PlayAllCommand? SetPaused(bool paused)
    {
        isPaused = paused;
        if (State != PlayAllTileState.Playing)
        {
            return null;
        }

        return new PlayAllCommand(paused ? PlayAllCommandKind.Pause : PlayAllCommandKind.Play);
    }

    private void Fail()
    {
        State = PlayAllTileState.Failed;
        StatusText = Translations.CouldNotBePlayed;
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
}