using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// Probes the internet every few seconds. Goes "offline" only after several failed probes in a
/// row (one hiccup is not an outage) and comes back on the first success — but first runs
/// <c>onRecovered</c> (re-check everything), so stale failures from the outage never flash up red.
/// </summary>
public sealed class ConnectivityMonitor
{
    private readonly IConnectivityProbe _probe;
    private readonly ConnectivityState _state;
    private readonly Func<Task>? _onRecovered;
    private readonly int _failuresToConfirm;
    private int _failures;

    public ConnectivityMonitor(IConnectivityProbe probe, ConnectivityState state, Func<Task>? onRecovered = null, int failuresToConfirm = 2)
    {
        _probe = probe;
        _state = state;
        _onRecovered = onRecovered;
        _failuresToConfirm = failuresToConfirm;
    }

    public async Task ProbeOnceAsync(CancellationToken cancellationToken = default)
    {
        bool reachable;
        try
        {
            reachable = await _probe.IsInternetReachableAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            reachable = false;
        }

        if (reachable)
        {
            _failures = 0;
            if (_state.IsOffline)
            {
                if (_onRecovered is not null)
                {
                    try
                    {
                        await _onRecovered();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // A failed re-check must not keep the app stuck in "offline".
                    }
                }

                _state.SetOffline(false);
            }

            return;
        }

        if (++_failures >= _failuresToConfirm)
        {
            _state.SetOffline(true);
        }
    }

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                await ProbeOnceAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
