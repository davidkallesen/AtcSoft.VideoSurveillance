namespace Linksoft.VideoSurveillance.Blazor.App.Shared;

public partial class MainLayout
{
    [CascadingParameter]
    private App? AppInstance { get; set; }

    private bool drawerOpen = true;

    private void ToggleDrawer()
    {
        drawerOpen = !drawerOpen;
    }

    private void ToggleDarkMode()
    {
        AppInstance?.ToggleDarkMode();
    }
}