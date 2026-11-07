using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DesControlCenter.App.Services;
using DesControlCenter.App.Startup;
using DesControlCenter.Infrastructure.Persistence;
using DesControlCenter.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DesControlCenter.App.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        DataContext = viewModel;
        InitializeComponent();
        if (Demo.DemoMode.IsEnabled)
        {
            Title = "DES Control Center — демо (вымышленные данные)";
        }

        WindowChromeHelper.EnableDarkTitleBar(this);
    }

    /// <summary>The X button hides the window instead of exiting — the app keeps running in the tray (spec §10).</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    /// <summary>Clicking a server card's header opens/closes it; clicks on its own buttons (SSH…) must not.</summary>
    private void OnServerHeaderClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, sender); node = Parent(node))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
            {
                return;
            }
        }

        if ((sender as FrameworkElement)?.DataContext is ServerGroupViewModel group)
        {
            group.IsExpanded = !group.IsExpanded;
        }

        static DependencyObject? Parent(DependencyObject node) => node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        var themes = _serviceProvider.GetRequiredService<ThemeService>();
        var menu = new ContextMenu { PlacementTarget = ThemeButton };

        void Add(string header, ThemeMode mode)
        {
            var item = new MenuItem { Header = header, IsChecked = themes.Mode == mode };
            item.Click += (_, _) => themes.SetMode(mode);
            menu.Items.Add(item);
        }

        Add("Тёмная", ThemeMode.Dark);
        Add("Светлая", ThemeMode.Light);
        Add("Как в Windows", ThemeMode.System);
        menu.IsOpen = true;
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        // Load fully BEFORE the window is shown, not on its Loaded event: if the window
        // appeared while still loading, clicking Save in that window while the load was still
        // in flight would overwrite the real config with SettingsViewModel's still-empty
        // collections — a config-wiping race, not just a cosmetic one.
        var viewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        await viewModel.InitializeAsync();

        var window = _serviceProvider.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        window.Show();
        window.Activate();
    }

    private async void OnOpenEventLog(object sender, RoutedEventArgs e)
    {
        var viewModel = _serviceProvider.GetRequiredService<EventLogViewModel>();
        await viewModel.InitializeAsync();

        var window = _serviceProvider.GetRequiredService<EventLogWindow>();
        window.Owner = this;
        window.Show();
        window.Activate();
    }

    private async void OnShowServiceLogs(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ServiceRowViewModel row)
        {
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService<LogViewerViewModel>();
        await viewModel.InitializeAsync(row.Server, row.Source);

        // The window must receive THIS, already loaded, view model: both are transient, so resolving
        // the window from the container would hand it a fresh, empty one (the log window opened blank).
        var window = ActivatorUtilities.CreateInstance<LogViewerWindow>(_serviceProvider, viewModel);
        window.Owner = this;
        window.Show();
        window.Activate();
    }
}
