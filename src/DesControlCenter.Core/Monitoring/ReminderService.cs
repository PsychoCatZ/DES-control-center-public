using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// Sends "remind me at …" messages to the owner's Telegram. DES runs from the tray all day, so
/// unlike a scheduler tied to some other app it keeps working with every window closed. A reminder
/// that came due while the PC was off is sent at the next start, with a note that it is late.
/// </summary>
public sealed class ReminderService
{
    private static readonly TimeSpan LateThreshold = TimeSpan.FromHours(2);

    private readonly IReminderStore _store;
    private readonly ITelegramSender _telegram;
    private readonly ConnectivityState _connectivity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _sentThisRun = new();

    public ReminderService(IReminderStore store, ITelegramSender telegram, ConnectivityState connectivity, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _telegram = telegram;
        _connectivity = connectivity;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>One step: sends everything that is due. Returns how many were sent.</summary>
    public async Task<int> TickAsync(CancellationToken cancellationToken = default)
    {
        if (!_telegram.IsConfigured || _connectivity.IsOffline)
        {
            return 0;
        }

        var reminders = _store.Load();
        if (reminders is null)
        {
            return 0;     // the file is mid-edit or broken: never overwrite what the owner wrote
        }

        var now = _clock();
        var sent = 0;
        var changed = false;
        var updated = reminders.ToList();

        foreach (var reminder in updated.Where(r => r.SentAt is null && r.At <= now && !_sentThisRun.Contains(r.Id)).OrderBy(r => r.At))
        {
            var late = now - reminder.At > LateThreshold;
            var text = late
                ? $"⏰ (запоздало — назначено на {reminder.At.ToLocalTime():dd.MM HH:mm}) {reminder.Text}"
                : $"⏰ {reminder.Text}";

            if (!await _telegram.SendAsync(text, cancellationToken))
            {
                continue;     // try again on the next tick
            }

            // Remembered in memory too, so a failed save can never turn into a message every minute.
            _sentThisRun.Add(reminder.Id);
            reminder.SentAt = now;
            sent++;
            changed = true;
        }

        if (changed)
        {
            try
            {
                _store.Save(updated);
            }
            catch
            {
                // Already delivered; the in-memory set prevents a repeat during this run.
            }
        }

        return sent;
    }

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await TickAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Reminders must never take the app down.
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
