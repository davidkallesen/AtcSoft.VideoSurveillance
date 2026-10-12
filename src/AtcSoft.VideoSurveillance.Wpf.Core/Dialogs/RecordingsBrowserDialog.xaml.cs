namespace AtcSoft.VideoSurveillance.Wpf.Core.Dialogs;

/// <summary>
/// Dialog for browsing recorded video files.
/// </summary>
public partial class RecordingsBrowserDialog : IDisposable
{
    private readonly RecordingsBrowserDialogViewModel viewModel;
    private readonly Action<RecordingEntry> playRecording;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingsBrowserDialog"/> class.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="playRecording">
    /// Plays a recording and returns when playback has ended.
    /// Defaults to the full-screen player.
    /// </param>
    public RecordingsBrowserDialog(
        RecordingsBrowserDialogViewModel viewModel,
        Action<RecordingEntry>? playRecording = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        this.viewModel = viewModel;
        this.playRecording = playRecording ?? ShowFullScreenPlayer;
        DataContext = viewModel;

        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PlayRecordingRequested += OnPlayRecordingRequested;
        viewModel.ThumbnailPreviewRequested += OnThumbnailPreviewRequested;
        Closed += OnWindowClosed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the dialog resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PlayRecordingRequested -= OnPlayRecordingRequested;
            viewModel.ThumbnailPreviewRequested -= OnThumbnailPreviewRequested;
            Closed -= OnWindowClosed;
        }

        disposed = true;
    }

    private void OnCloseRequested(
        object? sender,
        DialogClosedEventArgs e)
    {
        DialogResult = e.DialogResult;
        Close();
    }

    private void OnPlayRecordingRequested(
        object? sender,
        RecordingEntry recording)
    {
        // Close thumbnail panel if open
        CloseThumbnailFlyout();

        // The browser stays open behind the player, so the filters, the
        // selection and the scroll position are still there afterwards.
        playRecording(recording);

        FocusSelectedRecording();
    }

    /// <summary>
    /// Puts the keyboard back on the selected row, so the arrow keys carry
    /// on from the recording that was just played.
    /// </summary>
    private void FocusSelectedRecording()
    {
        Activate();

        if (RecordingsGrid.SelectedItem is not { } selected)
        {
            RecordingsGrid.Focus();
            return;
        }

        RecordingsGrid.ScrollIntoView(selected);
        RecordingsGrid.UpdateLayout();

        if (RecordingsGrid.ItemContainerGenerator.ContainerFromItem(selected) is DataGridRow row)
        {
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
        else
        {
            RecordingsGrid.Focus();
        }
    }

    private void ShowFullScreenPlayer(RecordingEntry recording)
    {
        // Open fullscreen recording window with playback overlay settings
        var overlaySettings = viewModel.PlaybackOverlaySettings;
        using var fullScreenViewModel = new FullScreenRecordingWindowViewModel(recording.FilePath, recording.FileName, overlaySettings);
        using var fullScreenWindow = new FullScreenRecordingWindow(fullScreenViewModel)
        {
            Owner = this,
        };
        fullScreenWindow.ShowDialog();
    }

    private void OnThumbnailPreviewRequested(
        object? sender,
        RecordingEntry recording)
    {
        // Update header with camera name
        ThumbnailFlyoutHeader.Text = string.Format(
            CultureInfo.CurrentCulture,
            Translations.ThumbnailPreviewTitle1,
            recording.CameraName);

        // Load the thumbnail
        LoadThumbnail(recording.ThumbnailPath);
        ShowThumbnailFlyout();
    }

    private void LoadThumbnail(string thumbnailPath)
    {
        if (!File.Exists(thumbnailPath))
        {
            ThumbnailImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            ThumbnailImage.Source = bitmap;
        }
        catch
        {
            ThumbnailImage.Source = null;
        }
    }

    private void ShowThumbnailFlyout()
    {
        FlyoutOverlay.Visibility = Visibility.Visible;
        ThumbnailFlyoutPanel.Visibility = Visibility.Visible;
    }

    private void CloseThumbnailFlyout()
    {
        ThumbnailFlyoutPanel.Visibility = Visibility.Collapsed;
        FlyoutOverlay.Visibility = Visibility.Collapsed;
    }

    private void CloseThumbnailFlyout_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseThumbnailFlyout();
    }

    private void FlyoutOverlay_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        // Close thumbnail flyout when clicking the overlay
        if (ThumbnailFlyoutPanel.Visibility == Visibility.Visible)
        {
            CloseThumbnailFlyout();
        }
    }

    private void OnWindowClosed(
        object? sender,
        EventArgs e)
        => Dispose();
}