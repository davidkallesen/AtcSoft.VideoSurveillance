namespace AtcSoft.VideoSurveillance.Wpf.Windows;

public class FullScreenCameraWindowTests
{
    [Fact]
    public void The_Window_Can_Be_Created()
    {
        // Loading the XAML resolves every StaticResource in it, so a resource
        // key that no longer exists fails here instead of when a user opens a
        // camera in full screen.
        var error = RunOnStaThread(() =>
        {
            var hub = new SurveillanceHubService("http://localhost");
            using var viewModel = new FullScreenCameraWindowViewModel(
                hub,
                Guid.NewGuid(),
                existingPlayer: null,
                "Cam",
                string.Empty);
            var window = new FullScreenCameraWindow(viewModel);
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