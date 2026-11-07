using System.Windows;
using DesControlCenter.App.Startup;
using DesControlCenter.App.ViewModels;

namespace DesControlCenter.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        // Loading happens in MainWindow.OnOpenSettings, awaited BEFORE this window is even
        // constructed — never re-triggered here on Loaded, which used to let the window appear
        // (and be interacted with, including Save) before the initial load had finished.
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
    }
}
