namespace AtcSoft.VideoSurveillance.Wpf.Views;

/// <summary>
/// Interaction logic for TimelineView.
/// </summary>
public partial class TimelineView
{
    public TimelineView()
    {
        InitializeComponent();
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(parameter: null);
        }
    }
}