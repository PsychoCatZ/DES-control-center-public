using System.Collections.Concurrent;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Monitoring;

public sealed class SwitchCheckedEventArgs(ServerResource server, SwitchResource sw, SwitchStatus status) : EventArgs
{
    public ServerResource Server { get; } = server;
    public SwitchResource Switch { get; } = sw;
    public SwitchStatus Status { get; } = status;
}

/// <summary>
/// Keeps the displayed position of every configured <see cref="SwitchResource"/> current (one loop per switch).
/// Deliberately separate from <see cref="PollingScheduler"/>: a switch has no health, so no debounce, no journal
/// entries for its readings and no alarms — it only tells the card what the server says right now.
/// </summary>
public sealed class SwitchPoller : IAsyncDisposable
{
    private readonly ISwitchController _controller;
    private readonly ConcurrentDictionary<Guid, SwitchStatus> _statuses = new();
    private readonly List<(Task Loop, CancellationTokenSource Cts)> _loops = new();

    public event EventHandler<SwitchCheckedEventArgs>? Checked;

    public SwitchPoller(ISwitchController controller)
    {
        _controller = controller;
    }

    public SwitchStatus? GetStatus(Guid switchId) => _statuses.GetValueOrDefault(switchId);

    public async Task RestartAsync(AppConfig config)
    {
        await StopAsync();
        var serversById = config.Servers.ToDictionary(s => s.Id);

        foreach (var sw in config.Switches.Where(s => s.Enabled))
        {
            if (!serversById.TryGetValue(sw.ServerId, out var server) || !server.Enabled)
            {
                continue;
            }

            var cts = new CancellationTokenSource();
            var interval = TimeSpan.FromSeconds(Math.Max(15, sw.PollIntervalSeconds));
            _loops.Add((Task.Run(() => RunLoopAsync(server, sw, interval, cts.Token), cts.Token), cts));
        }
    }

    /// <summary>Re-reads one switch right now, e.g. just after its button was pressed.</summary>
    public Task CheckNowAsync(ServerResource server, SwitchResource sw) => PollAsync(server, sw, CancellationToken.None);

    private async Task RunLoopAsync(ServerResource server, SwitchResource sw, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (true)
        {
            try
            {
                await PollAsync(server, sw, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // a failing read must never stop the loop
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollAsync(ServerResource server, SwitchResource sw, CancellationToken cancellationToken)
    {
        SwitchStatus status;
        try
        {
            status = await _controller.CheckAsync(server, sw, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = SwitchStatus.Unknown(ex.Message);
        }

        _statuses[sw.Id] = status;
        Checked?.Invoke(this, new SwitchCheckedEventArgs(server, sw, status));
    }

    public async Task StopAsync()
    {
        foreach (var (_, cts) in _loops)
        {
            cts.Cancel();
        }

        foreach (var (loop, cts) in _loops)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        _loops.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
