using System.Windows;
using DesControlCenter.App.Startup;
using DesControlCenter.App.ViewModels;

namespace DesControlCenter.App.Views;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow(LogViewerViewModel viewModel)
    {
        // Loading happens in MainWindow.OnShowServiceLogs, awaited before this window is shown.
        DataContext = viewModel;
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
    }
}
