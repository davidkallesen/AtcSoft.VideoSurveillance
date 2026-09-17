namespace Linksoft.VideoSurveillance.Wpf.App.Dialogs;

/// <summary>
/// Read-only reference dialog showing all keyboard shortcuts.
/// </summary>
public partial class KeyboardShortcutsDialog
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();

        ShortcutsList.ItemsSource = new[]
        {
            new { Key = "Ctrl+1", Description = "Live View" },
            new { Key = "Ctrl+2", Description = "Dashboard" },
            new { Key = "Ctrl+3", Description = "Cameras" },
            new { Key = "Ctrl+4", Description = "Layouts" },
            new { Key = "Ctrl+5", Description = "Recordings" },
            new { Key = "Ctrl+6", Description = "Notifications" },
            new { Key = "Ctrl+N", Description = "View Cameras" },
            new { Key = "Ctrl+,", Description = "Settings" },
            new { Key = "F5", Description = "Refresh Current View" },
            new { Key = "F11", Description = "Toggle Full Screen" },
            new { Key = "F1", Description = "Keyboard Shortcuts" },
        };
    }

    private void OkButton_Click(
        object sender,
        RoutedEventArgs e)
        => Close();
}