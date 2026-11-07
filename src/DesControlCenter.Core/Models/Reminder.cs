namespace DesControlCenter.Core.Models;

/// <summary>"Remind me at this time" — a line in <c>reminders.json</c>. Sent once, then marked with <see cref="SentAt"/>.</summary>
public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>When it is due (with the UTC offset, e.g. 2026-10-16T10:00:00+07:00).</summary>
    public DateTimeOffset At { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Set once it has been delivered; a reminder with a value here is never sent again.</summary>
    public DateTimeOffset? SentAt { get; set; }
}
