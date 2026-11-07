using System.IO;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DesControlCenter.App.Demo;

/// <summary>Swaps every outward-facing service for an in-memory fake (see <see cref="DemoWorld"/>).</summary>
public static class DemoServices
{
    public static void Apply(IServiceCollection services)
    {
        var world = new DemoWorld();
        services.AddSingleton(world);
        services.AddSingleton<IConfigStore>(new DemoConfigStore(world));
        services.AddSingleton(new JsonUiSettingsStore(Path.Combine(DemoMode.DataDir, "ui-settings.json")));
        services.AddSingleton<ISilenceStore, DemoSilenceStore>();
        services.AddSingleton<IEventStore>(new DemoEventStore(world));
        services.AddSingleton<IReminderStore, DemoReminderStore>();
        services.AddSingleton<IReportStateStore, DemoReportStateStore>();
        services.AddSingleton<ITelegramSender, DemoTelegramSender>();
        services.AddSingleton<IServerMonitor>(new DemoServerMonitor(world));
        services.AddSingleton<IWebsiteMonitor>(new DemoWebsiteMonitor(world));
        services.AddSingleton<IServiceMonitor>(new DemoServiceMonitor(world));
        services.AddSingleton<IVpnMonitor>(new DemoVpnMonitor(world));
        services.AddSingleton<IServiceController>(new DemoServiceController(world));
        services.AddSingleton<IServiceLogReader>(new DemoLogReader(world));
        services.AddSingleton<IMaintenanceChecker>(new DemoMaintenanceChecker(world));
        services.AddSingleton<INetworkStatusService, DemoNetwork>();
        services.AddSingleton<IConnectivityProbe, DemoConnectivity>();
        services.AddSingleton<INotificationService, DemoNotifications>();
        services.AddSingleton<IDesktopAppLauncher, DemoLauncher>();
        services.AddSingleton<ITerminalLauncher, DemoTerminal>();
    }

    private sealed class DemoConfigStore(DemoWorld world) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(world.Config);
        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DemoSilenceStore : ISilenceStore
    {
        public IReadOnlyList<Silence> Load() => Array.Empty<Silence>();
        public void Save(IReadOnlyCollection<Silence> silences) { }
    }

    private sealed class DemoEventStore : IEventStore
    {
        private readonly List<EventLogEntry> _entries;
        private long _next = 1000;

        public DemoEventStore(DemoWorld world) => _entries = world.SeedEvents().ToList();

        public Task AddAsync(EventLogEntry entry, CancellationToken cancellationToken = default)
        {
            lock (_entries)
            {
                entry.Id = ++_next;
                _entries.Add(entry);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EventLogEntry>> QueryAsync(EventLogFilter filter, CancellationToken cancellationToken = default)
        {
            lock (_entries)
            {
                IEnumerable<EventLogEntry> query = _entries;
                if (filter.FromUtc is { } from) { query = query.Where(e => e.TimestampUtc >= from); }
                if (filter.ToUtc is { } to) { query = query.Where(e => e.TimestampUtc <= to); }
                if (filter.ResourceId is { } rid) { query = query.Where(e => e.ResourceId == rid); }
                if (filter.ResourceKind is { } kind) { query = query.Where(e => e.ResourceKind == kind); }
                if (filter.EventKind is { } ek) { query = query.Where(e => e.EventKind == ek); }
                return Task.FromResult<IReadOnlyList<EventLogEntry>>(query.OrderByDescending(e => e.TimestampUtc).Take(filter.Limit).ToList());
            }
        }
    }

    private sealed class DemoReminderStore : IReminderStore
    {
        public IReadOnlyList<Reminder>? Load() => Array.Empty<Reminder>();
        public void Save(IReadOnlyList<Reminder> reminders) { }
    }

    private sealed class DemoReportStateStore : IReportStateStore
    {
        // "never sent": the real ScheduledReportService composes and sends the report on its first tick.
        public DateTimeOffset? LoadLastSent() => null;
        public void SaveLastSent(DateTimeOffset sentAt) { }
    }

    /// <summary>Pretends to be a configured bot; keeps what the real report/alert code wanted to send in a text file.</summary>
    private sealed class DemoTelegramSender : ITelegramSender
    {
        public bool IsConfigured => true;

        public Task<bool> SendAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                File.AppendAllText(Path.Combine(DemoMode.DataDir, "telegram-messages.txt"), text + "\n=====\n");
            }
            catch
            {
            }

            return Task.FromResult(true);
        }
    }

    private sealed class DemoServerMonitor(DemoWorld world) : IServerMonitor
    {
        public Task<ServerStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default) => Task.FromResult(world.ServerStatusOf(server));
    }

    private sealed class DemoWebsiteMonitor(DemoWorld world) : IWebsiteMonitor
    {
        public Task<WebsiteStatus> CheckAsync(Website website, CancellationToken cancellationToken = default) => Task.FromResult(world.WebsiteStatusOf(website));
    }

    private sealed class DemoServiceMonitor(DemoWorld world) : IServiceMonitor
    {
        public Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
            Task.FromResult(world.ServiceStatusOf(service));
    }

    private sealed class DemoVpnMonitor(DemoWorld world) : IVpnMonitor
    {
        public Task<VpnStatus> CheckAsync(ServerResource server, VpnResource vpn, CancellationToken cancellationToken = default) => Task.FromResult(world.VpnStatusOf());
    }

    private sealed class DemoServiceController(DemoWorld world) : IServiceController
    {
        public async Task<ServiceCommandOutcome> StartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default)
        {
            await Task.Delay(700, cancellationToken);
            return world.Apply(service, ServiceState.Running);
        }

        public async Task<ServiceCommandOutcome> StopAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default)
        {
            await Task.Delay(700, cancellationToken);
            return world.Apply(service, ServiceState.Stopped);
        }

        public async Task<ServiceCommandOutcome> RestartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default)
        {
            await Task.Delay(900, cancellationToken);
            return world.Apply(service, ServiceState.Running);
        }
    }

    private sealed class DemoLogReader(DemoWorld world) : IServiceLogReader
    {
        public Task<string> GetRecentLogsAsync(ServerResource server, ServiceResource service, int lines, CancellationToken cancellationToken = default) =>
            Task.FromResult(world.Logs(service, Math.Min(lines, 23)));
    }

    private sealed class DemoMaintenanceChecker(DemoWorld world) : IMaintenanceChecker
    {
        public Task<MaintenanceStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default) => Task.FromResult(world.Maintenance(server));
    }

    private sealed class DemoNetwork : INetworkStatusService
    {
        // 203.0.113.0/24 is reserved for documentation.
        public Task<NetworkStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NetworkStatus { InternetAvailable = true, PublicIpAddress = "203.0.113.5" });
    }

    private sealed class DemoConnectivity : IConnectivityProbe
    {
        public Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DemoNotifications : INotificationService
    {
        public void NotifyStatusChange(string title, string message, HealthLevel level) { }
    }

    private sealed class DemoLauncher : IDesktopAppLauncher
    {
        public Task OpenAsync(string? desktopProtocol, string? desktopAppName, string url, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DemoTerminal : ITerminalLauncher
    {
        public void OpenSshSession(ServerResource server) { }
    }
}
