using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Where reminders live. The owner may edit the file by hand, so it is re-read every time.</summary>
public interface IReminderStore
{
    /// <summary>Null when the file exists but cannot be understood — the caller must then leave it alone.</summary>
    IReadOnlyList<Reminder>? Load();

    void Save(IReadOnlyList<Reminder> reminders);
}
