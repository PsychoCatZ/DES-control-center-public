using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Tests;

internal sealed class FakeServerMonitor : IServerMonitor
{
    public Queue<ServerStatus> Responses { get; } = new();

    public Task<ServerStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default) =>
        Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : new ServerStatus { IsOnline = true });
}

internal sealed class FakeWebsiteMonitor : IWebsiteMonitor
{
    public Queue<WebsiteStatus> Responses { get; } = new();

    public Task<WebsiteStatus> CheckAsync(Website website, CancellationToken cancellationToken = default) =>
        Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : new WebsiteStatus { IsOnline = true });
}

internal sealed class FakeServiceMonitor : IServiceMonitor
{
    public Queue<ServiceStatus> Responses { get; } = new();

    public Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : new ServiceStatus());
}

internal sealed class FakeVpnMonitor : IVpnMonitor
{
    public Queue<VpnStatus> Responses { get; } = new();

    public Task<VpnStatus> CheckAsync(ServerResource server, VpnResource vpn, CancellationToken cancellationToken = default) =>
        Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : new VpnStatus { IsOnline = true });
}

internal sealed class FakeEventStore : IEventStore
{
    public List<EventLogEntry> Added { get; } = new();

    public Task AddAsync(EventLogEntry entry, CancellationToken cancellationToken = default)
    {
        Added.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EventLogEntry>> QueryAsync(EventLogFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventLogEntry>>(Added);
}

internal sealed class FakeNotificationService : INotificationService
{
    public int CallCount { get; private set; }

    public void NotifyStatusChange(string title, string message, HealthLevel level) => CallCount++;
}
