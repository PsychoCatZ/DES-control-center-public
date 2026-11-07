namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// Whether THIS computer is the one with the problem — as opposed to a server being down. Two sources:
/// <list type="bullet">
/// <item><see cref="IsOffline"/>: no internet at all (Wi-Fi / VPN kill-switch / ISP).</item>
/// <item><see cref="SuspectedLocalFault"/>: internet answers, but almost everything we watch failed at the same
/// moment (e.g. the owner switched VPN and the path to every server broke for a minute or two). Servers at
/// different providers do not all die in the same second, so that is almost certainly our side. It is only
/// trusted for <see cref="MassFailureGrace"/>: a real, lasting catastrophe must still surface.</item>
/// </list>
/// While either holds, failures are not turned into alarms (see <see cref="PollingScheduler"/>,
/// <see cref="ResourceHealthAggregator"/>); <see cref="AlarmsSuppressed"/> is the one to ask.
/// </summary>
public sealed class ConnectivityState
{
    /// <summary>Fewer resources than this say nothing about "everything at once".</summary>
    public const int MassFailureMinResources = 8;

    /// <summary>Share of watched resources failing that counts as "everything".</summary>
    public const double MassFailureShare = 0.7;

    /// <summary>The suspicion is dropped once the failing share falls below this (hysteresis).</summary>
    public const double MassFailureClearShare = 0.4;

    /// <summary>How long a suspicion may hide alarms before it is treated as a real outage.</summary>
    public static readonly TimeSpan MassFailureGrace = TimeSpan.FromMinutes(15);

    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private volatile bool _isOffline;
    private DateTimeOffset? _suspectedSince;

    public ConnectivityState(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>No internet on this computer.</summary>
    public bool IsOffline => _isOffline;

    /// <summary>Almost everything failed at once (see class remarks).</summary>
    public bool SuspectedLocalFault
    {
        get
        {
            lock (_gate)
            {
                return _suspectedSince is not null;
            }
        }
    }

    /// <summary>True while failures should not become alarms: no internet, or a fresh mass failure.</summary>
    public bool AlarmsSuppressed
    {
        get
        {
            if (_isOffline)
            {
                return true;
            }

            lock (_gate)
            {
                return _suspectedSince is { } since && _clock() - since < MassFailureGrace;
            }
        }
    }

    public event EventHandler? Changed;

    public void SetOffline(bool offline)
    {
        if (_isOffline == offline)
        {
            return;
        }

        _isOffline = offline;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called after every check with how many resources have a latest status and how many of those fail.</summary>
    public void ReportCheckSummary(int total, int failing)
    {
        var changed = false;
        lock (_gate)
        {
            var share = total == 0 ? 0 : (double)failing / total;
            if (_suspectedSince is null)
            {
                if (total >= MassFailureMinResources && share >= MassFailureShare)
                {
                    _suspectedSince = _clock();
                    changed = true;
                }
            }
            else if (total < MassFailureMinResources || share < MassFailureClearShare)
            {
                _suspectedSince = null;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
