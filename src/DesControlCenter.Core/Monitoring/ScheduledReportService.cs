using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;

namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// Sends the walk-round to Telegram every day at 13:00 local (using the configured local schedule) (or, if a weekday is given, once a
/// week at that hour). If the computer was off at that moment, it goes out as soon as DES is running
/// again (never twice for the same slot).
/// Also sends an immediate message when a server newly develops a critical problem
/// (firewall switched off, disk nearly full, backup gone stale) — once per problem, not every check.
/// </summary>
public sealed class ScheduledReportService
{
    private readonly MaintenanceMonitor _maintenance;
    private readonly PollingScheduler _scheduler;
    private readonly SilenceRegistry _silences;
    private readonly ITelegramSender _telegram;
    private readonly IReportStateStore _state;
    private readonly ConnectivityState _connectivity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly DayOfWeek? _day;
    private readonly int _hour;
    private readonly HashSet<string> _alertedCritical = new();

    public ScheduledReportService(
        MaintenanceMonitor maintenance,
        PollingScheduler scheduler,
        SilenceRegistry silences,
        ITelegramSender telegram,
        IReportStateStore state,
        ConnectivityState connectivity,
        Func<DateTimeOffset>? clock = null,
        DayOfWeek? day = null,
        int hour = 13)
    {
        _maintenance = maintenance;
        _scheduler = scheduler;
        _silences = silences;
        _telegram = telegram;
        _state = state;
        _connectivity = connectivity;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _day = day;
        _hour = hour;
    }

    /// <summary>The most recent moment (not in the future) at which the report is due.</summary>
    public DateTimeOffset LastDueMoment(DateTimeOffset nowLocal)
    {
        var due = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, _hour, 0, 0, nowLocal.Offset);
        if (_day is { } day)
        {
            due = due.AddDays(-(((int)nowLocal.DayOfWeek - (int)day + 7) % 7));
        }

        if (due > nowLocal)
        {
            due = due.AddDays(_day is null ? -1 : -7);
        }

        return due;
    }

    /// <summary>One scheduling step. Returns true if a report was sent.</summary>
    public async Task<bool> TickAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        if (!_telegram.IsConfigured || _connectivity.AlarmsSuppressed)
        {
            return false;
        }

        var now = _clock();
        var due = LastDueMoment(now);
        if (_state.LoadLastSent() is { } last && last >= due)
        {
            return false;
        }

        // Fresh facts right before writing the report, not whatever is a few hours old.
        await _maintenance.CheckAllAsync(config, cancellationToken);

        var text = ReportComposer.Compose(
            config,
            now,
            _maintenance.Get,
            _scheduler.GetServerStatus,
            _silences.ActiveCount,
            NearestOwnCertificate(config));

        if (!await _telegram.SendAsync(text, cancellationToken))
        {
            return false;
        }

        _state.SaveLastSent(now);
        return true;
    }

    /// <summary>Hooked to <see cref="MaintenanceMonitor.Checked"/>: tells the owner right away about a new critical problem.</summary>
    public async Task OnMaintenanceCheckedAsync(string serverName, Models.MaintenanceStatus status)
    {
        var critical = status.Critical;
        var keyPrefix = status.ServerId + "|";

        // Forget problems that have cleared, so a later recurrence is reported again.
        _alertedCritical.RemoveWhere(k => k.StartsWith(keyPrefix, StringComparison.Ordinal) && !critical.Contains(k[keyPrefix.Length..]));

        foreach (var note in critical)
        {
            if (!_alertedCritical.Add(keyPrefix + note))
            {
                continue;
            }

            if (_telegram.IsConfigured && !_connectivity.AlarmsSuppressed && !_silences.IsSilenced(status.ServerId))
            {
                await _telegram.SendAsync($"🚨 {serverName}: {note}");
            }
        }
    }

    public async Task RunAsync(Func<AppConfig> config, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await TickAsync(config(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // The report is a convenience; it must never take the app down.
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private (string Name, DateTimeOffset Expires)? NearestOwnCertificate(AppConfig config)
    {
        (string Name, DateTimeOffset Expires)? best = null;
        foreach (var site in config.Websites.Where(w => w.Enabled && w.ServerId is not null))
        {
            if (_scheduler.GetWebsiteStatus(site.Id) is { IsOnline: true, SslExpiresUtc: { } expires }
                && (best is null || expires < best.Value.Expires))
            {
                best = (site.Name, expires);
            }
        }

        return best;
    }
}
