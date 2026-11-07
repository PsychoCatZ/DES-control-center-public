using DesControlCenter.App.Services;
using DesControlCenter.App.Tray;
using DesControlCenter.App.ViewModels;
using DesControlCenter.App.Views;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Infrastructure;
using DesControlCenter.Infrastructure.Apps;
using DesControlCenter.Infrastructure.Control;
using DesControlCenter.Infrastructure.Monitors;
using DesControlCenter.Infrastructure.Network;
using DesControlCenter.Infrastructure.Notifications;
using DesControlCenter.Infrastructure.Persistence;
using DesControlCenter.Infrastructure.Ssh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DesControlCenter.App;

public static class AppHostBuilder
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<IConfigStore, JsonConfigStore>();
        builder.Services.AddSingleton<JsonUiSettingsStore>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<ConnectivityState>();
        builder.Services.AddSingleton<IMaintenanceChecker, SshMaintenanceChecker>();
        builder.Services.AddSingleton<MaintenanceMonitor>();
        builder.Services.AddSingleton<ITelegramSender, TelegramSender>();
        builder.Services.AddSingleton<IReportStateStore, JsonReportStateStore>();
        builder.Services.AddSingleton<IReminderStore, JsonReminderStore>();
        builder.Services.AddSingleton<ReminderService>();
        builder.Services.AddSingleton<ScheduledReportService>();
        builder.Services.AddSingleton<IConnectivityProbe, HttpConnectivityProbe>();
        builder.Services.AddSingleton<ISilenceStore, JsonSilenceStore>();
        builder.Services.AddSingleton(sp => new SilenceRegistry(sp.GetRequiredService<ISilenceStore>()));
        builder.Services.AddSingleton<IEventStore, SqliteEventStore>();
        builder.Services.AddSingleton<INotificationService, ToastNotificationService>();
        builder.Services.AddSingleton<ISshCommandRunner, OpenSshCommandRunner>();
        builder.Services.AddSingleton<IServerMonitor, ServerMonitor>();
        builder.Services.AddSingleton<IWebsiteMonitor, WebsiteMonitor>();
        // systemd units and Docker containers are both "services"; the routers pick by ServiceResource.Kind.
        builder.Services.AddSingleton<SystemdServiceMonitor>();
        builder.Services.AddSingleton<DockerServiceMonitor>();
        builder.Services.AddSingleton<IServiceMonitor, ServiceMonitorRouter>();
        builder.Services.AddSingleton<IVpnMonitor, AmneziaVpnMonitor>();
        builder.Services.AddSingleton<JournalctlLogReader>();
        builder.Services.AddSingleton<DockerLogReader>();
        builder.Services.AddSingleton<IServiceLogReader, ServiceLogReaderRouter>();
        builder.Services.AddSingleton<SystemdServiceController>();
        builder.Services.AddSingleton<DockerServiceController>();
        builder.Services.AddSingleton<IServiceController, ServiceControllerRouter>();
        builder.Services.AddSingleton<ISwitchController, SshSwitchController>();
        builder.Services.AddSingleton<SwitchPoller>();
        builder.Services.AddSingleton<ITerminalLauncher, TerminalLauncher>();
        builder.Services.AddSingleton<IDesktopAppLauncher, WindowsDesktopAppLauncher>();
        builder.Services.AddSingleton<INetworkStatusService, PublicIpService>();
        builder.Services.AddSingleton<PollingScheduler>();

        builder.Services.AddSingleton<IDialogService, DialogService>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<EventLogViewModel>();

        builder.Services.AddSingleton<MainWindow>();
        // Transient: these are plain utility windows the user can close for real (native X),
        // unlike MainWindow, which intercepts Closing to hide into the tray instead.
        builder.Services.AddTransient<SettingsWindow>();
        builder.Services.AddTransient<EventLogWindow>();
        builder.Services.AddTransient<LogViewerViewModel>();
        builder.Services.AddTransient<LogViewerWindow>();
        builder.Services.AddSingleton<TrayIconManager>();

        // DES_DEMO=1: made-up servers and in-memory fakes for screenshots; nothing real is contacted or changed.
        if (Demo.DemoMode.IsEnabled)
        {
            Demo.DemoServices.Apply(builder.Services);
        }

        return builder.Build();
    }
}
