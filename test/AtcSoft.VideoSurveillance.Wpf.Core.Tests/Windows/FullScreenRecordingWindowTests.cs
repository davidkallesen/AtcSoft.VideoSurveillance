namespace AtcSoft.VideoSurveillance.Wpf.Core.Windows;

public class FullScreenRecordingWindowTests
{
    [Fact]
    public void The_Window_Can_Be_Created()
    {
        // Loading the XAML resolves every StaticResource in it, so a resource
        // key that no longer exists fails here instead of when a user tries
        // to play a recording.
        var error = RunOnStaThread(() =>
        {
            using var viewModel = new FullScreenRecordingWindowViewModel(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-recording.mkv"),
                "Cam_20261012_101500_000.mkv");
            using var window = new FullScreenRecordingWindow(viewModel);
            window.Close();
        });

        error.Should().BeNull();
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error;
    }
}