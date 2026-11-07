using System.Windows;
using DesControlCenter.App.Startup;
using DesControlCenter.App.ViewModels;

namespace DesControlCenter.App.Views;

public partial class EventLogWindow : Window
{
    private readonly EventLogViewModel _viewModel;

    public EventLogWindow(EventLogViewModel viewModel)
    {
        // Loading happens in MainWindow.OnOpenEventLog, awaited before this window is shown.
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
    }
}
