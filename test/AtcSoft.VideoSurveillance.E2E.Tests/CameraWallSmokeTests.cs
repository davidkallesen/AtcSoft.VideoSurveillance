namespace AtcSoft.VideoSurveillance.E2E;

/// <summary>
/// CameraWall from start to close in a scratch folder: every ribbon tab, every dialog the
/// ribbon, the backstage and a tile's menu open (each tab and drop-down in them), then the
/// log must hold no binding error and no unhandled exception.
/// </summary>
/// <remarks>
/// A tile's context menu is left out: it needs a real right click on the video, which does
/// not open the menu reliably from a test (see docs/e2e-smoke-tests.md).
/// Left out on purpose as well: the snapshot (a save dialog), folder pickers, opening the recordings
/// folder (Explorer), update downloads (a browser), deleting a recording, Exit and restoring
/// the default settings.
/// </remarks>
[Collection(E2ETestGroup.Name)]
public sealed class CameraWallSmokeTests
{
    [Fact]
    public void Every_Window_Opens_Without_Binding_Errors()
    {
        E2ESettings.SkipUnlessEnabled();

        using var run = new CameraWallRun();
        var app = run.App;
        var main = app.WaitForWindowTitled(CameraWallRun.MainWindowTitle, DesktopApp.StartTimeout);
        var steps = new SmokeSteps(recover: () => CloseDialogs(app, main));

        if (run.HasLiveCamera)
        {
            steps.Run("live camera connects", () => app.Find(main, $"{TestCameras.LiveCameraName}: Connected", timeout: TimeSpan.FromSeconds(30)));
        }

        // Ribbon: Layouts
        steps.Run("Layouts tab", () => SelectTab(app, main, "Layouts"));
        steps.Run("ribbon drop-downs", () => app.OpenEveryDropDown(main));
        steps.Run("New Layout", () => Walk(app, main, "New Layout"));
        steps.Run("Rename Layout", () => Walk(app, main, "Rename Layout"));
        steps.Run("Assign Camera", () => Walk(app, main, "Assign Camera"));

        // Ribbon: Cameras
        steps.Run("Cameras tab", () => SelectTab(app, main, "Cameras"));
        steps.Run("Add Camera", () => Walk(app, main, "Add Camera"));
        steps.Run("Add USB Camera", () => Walk(app, main, "Add USB Camera"));
        steps.Run("Reconnect All", () => DesktopApp.Press(app.Find(main, "Reconnect All", ControlType.Button)));

        // Ribbon: View
        steps.Run("View tab", () => SelectTab(app, main, "View"));
        steps.Run("Browse Recordings", () => Walk(app, main, "Browse Recordings"));

        // Backstage
        steps.Run("Settings", () => app.WalkAndClose(app.OpenWindow(() => app.PressBackstageItem(main, "Settings"), "Settings")));
        steps.Run("About", () => app.WalkAndClose(app.OpenWindow(() => app.PressBackstageItem(main, "About"), "About")));
        steps.Run("Check for Updates", () => app.WalkAndClose(app.OpenWindow(() => app.PressBackstageItem(main, "Check for Updates"), "Check for Updates")));
        steps.Run("leave the backstage", () => SelectTab(app, main, "Layouts"));

        var closed = app.CloseApp(main);

        steps.Failures.Should().BeEmpty(steps.Report());
        LogFindings.AssertClean(run.LogFolder, "CameraWall");
        closed.Should().BeTrue("CameraWall closes when its main window closes");
    }

    private static void SelectTab(
        DesktopApp app,
        Window main,
        string name)
        => DesktopApp.Press(app.Find(main, name, ControlType.TabItem));

    private static void Walk(
        DesktopApp app,
        Window main,
        string buttonName)
        => app.WalkAndClose(app.OpenWindow(() => DesktopApp.Press(app.Find(main, buttonName, ControlType.Button)), buttonName));

    private static void CloseDialogs(
        DesktopApp app,
        Window main)
    {
        foreach (var window in app.Windows().Where(w => !w.Title.Contains(CameraWallRun.MainWindowTitle, StringComparison.OrdinalIgnoreCase)))
        {
            app.CloseWindow(window);
        }

        _ = main;
    }
}