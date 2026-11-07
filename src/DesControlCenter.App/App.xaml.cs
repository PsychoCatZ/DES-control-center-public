using System.Windows;
using DesControlCenter.App.Services;
using DesControlCenter.App.Startup;
using DesControlCenter.App.Tray;
using DesControlCenter.App.ViewModels;
using DesControlCenter.App.Views;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DesControlCenter.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private IHost? _host;
    private ConnectivityMonitor? _connectivityMonitor;
    private StatusFileWriter? _statusWriter;
    private readonly CancellationTokenSource _connectivityCts = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsFirstInstance)
        {
            MessageBox.Show("DES Control Center уже запущен — смотрите в системном трее.",
                "DES Control Center", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Closing the main window hides it into the tray (see MainWindow.OnWindowClosing);
        // only the tray menu's "Выход" or an explicit Shutdown() call should end the process.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Must happen before any toast can be shown — gives this unpackaged app a real
        // notification identity instead of being silently dropped or shown as "unknown app".
        if (!Demo.DemoMode.IsEnabled)
        {
            NotificationRegistrar.Register();
        }

        _host = AppHostBuilder.Build();
        await _host.StartAsync();

        // Before any window exists, so nothing flashes in the wrong colours.
        _host.Services.GetRequiredService<ThemeService>().Initialize();

        var configStore = _host.Services.GetRequiredService<IConfigStore>();
        var config = await configStore.LoadAsync();

        // Deliberately not syncing the registry Run key here: it is only ever written from
        // SettingsViewModel.SaveAsync, in direct response to the user checking the box.
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
        await mainViewModel.InitializeAsync(config);

        var scheduler = _host.Services.GetRequiredService<PollingScheduler>();
        await scheduler.RestartAsync(config);
        await _host.Services.GetRequiredService<SwitchPoller>().RestartAsync(config);

        // Demo only: open the named servers at start (DES_DEMO_EXPAND=Name1,Name2) so screenshots need no mouse.
        if (Demo.DemoMode.IsEnabled && Environment.GetEnvironmentVariable("DES_DEMO_EXPAND") is { Length: > 0 } expand)
        {
            var names = expand.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var group in mainViewModel.ServerGroups.Where(g => names.Contains(g.Server.Name)))
            {
                group.IsExpanded = true;
            }
        }

        // Watches this computer's own connection so an outage on our side isn't reported as every
        // server and service being down. On recovery everything is re-checked before alarms resume.
        _connectivityMonitor = new ConnectivityMonitor(
            _host.Services.GetRequiredService<IConnectivityProbe>(),
            _host.Services.GetRequiredService<ConnectivityState>(),
            () => mainViewModel.CheckAllNowAsync());
        _ = _connectivityMonitor.RunAsync(TimeSpan.FromSeconds(10), _connectivityCts.Token);

        // Slow-moving housekeeping (updates, reboot, disk, firewall, backups) every 6 h, the Monday
        // report to Telegram, and an immediate message for a new critical problem.
        var maintenance = _host.Services.GetRequiredService<MaintenanceMonitor>();
        var weekly = _host.Services.GetRequiredService<ScheduledReportService>();
        maintenance.Checked += (_, e) => _ = weekly.OnMaintenanceCheckedAsync(e.Server.Name, e.Status);
        _ = maintenance.RunAsync(() => mainViewModel.CurrentConfig, TimeSpan.FromHours(6), _connectivityCts.Token);
        _ = weekly.RunAsync(() => mainViewModel.CurrentConfig, TimeSpan.FromMinutes(5), _connectivityCts.Token);

        // "Remind me at …" lines from reminders.json go to Telegram; DES lives in the tray, so they arrive on time.
        var reminders = _host.Services.GetRequiredService<ReminderService>();
        _ = reminders.RunAsync(TimeSpan.FromMinutes(1), _connectivityCts.Token);

        if (!Demo.DemoMode.IsEnabled)
        {
            var tray = _host.Services.GetRequiredService<TrayIconManager>();
            tray.Initialize();

            // status.txt / status-line.txt / status.json in the data folder: the owner's OSD overlay reads them.
            _statusWriter = new StatusFileWriter(mainViewModel);
            _statusWriter.Start();
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _connectivityCts.Cancel();
        _statusWriter?.Stop();
        if (_host is not null)
        {
            var scheduler = _host.Services.GetRequiredService<PollingScheduler>();
            await scheduler.StopAsync();
            await _host.StopAsync();
            _host.Dispose();
        }

        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
