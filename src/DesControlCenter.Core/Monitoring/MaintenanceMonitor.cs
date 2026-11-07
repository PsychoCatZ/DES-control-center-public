using System.Collections.Concurrent;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Monitoring;

public sealed class MaintenanceCheckedEventArgs : EventArgs
{
    public MaintenanceCheckedEventArgs(ServerResource server, MaintenanceStatus status)
    {
        Server = server;
        Status = status;
    }

    public ServerResource Server { get; }
    public MaintenanceStatus Status { get; }
}

/// <summary>
/// Collects <see cref="MaintenanceStatus"/> for every enabled server every few hours (and on demand).
/// Slow-moving facts, so a short SSH round trip per server a few times a day is plenty.
/// </summary>
public sealed class MaintenanceMonitor
{
    private readonly IMaintenanceChecker _checker;
    private readonly ConnectivityState? _connectivity;
    private readonly ConcurrentDictionary<Guid, MaintenanceStatus> _statuses = new();

    public MaintenanceMonitor(IMaintenanceChecker checker, ConnectivityState? connectivity = null)
    {
        _checker = checker;
        _connectivity = connectivity;
    }

    public event EventHandler<MaintenanceCheckedEventArgs>? Checked;

    public MaintenanceStatus? Get(Guid serverId) => _statuses.GetValueOrDefault(serverId);

    public async Task CheckAllAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(config.Servers.Where(s => s.Enabled).Select(s => CheckOneAsync(s, cancellationToken)));
    }

    private async Task CheckOneAsync(ServerResource server, CancellationToken cancellationToken)
    {
        MaintenanceStatus status;
        try
        {
            status = await _checker.CheckAsync(server, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = new MaintenanceStatus { ServerId = server.Id, Error = ex.Message };
        }

        // A failed query keeps the last good picture: "SSH timed out" says nothing about updates.
        if (!status.Reachable && _statuses.TryGetValue(server.Id, out var previous) && previous.Reachable)
        {
            return;
        }

        _statuses[server.Id] = status;
        Checked?.Invoke(this, new MaintenanceCheckedEventArgs(server, status));
    }

    public async Task RunAsync(Func<AppConfig> config, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                // While this computer has no internet every SSH check would just time out.
                if (_connectivity?.IsOffline != true)
                {
                    await CheckAllAsync(config(), cancellationToken);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
