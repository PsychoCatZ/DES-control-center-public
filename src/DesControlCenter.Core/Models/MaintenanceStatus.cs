namespace DesControlCenter.Core.Models;

/// <summary>
/// Housekeeping facts about one server — NOT "is it up" (that is <see cref="Status.ServerStatus"/>),
/// but "does it need attention soon": pending updates, a reboot waiting, a full disk, a switched-off
/// firewall, a stale backup. Gathered rarely (every few hours), read-only.
/// </summary>
public sealed class MaintenanceStatus
{
    public const int UpdatesAttentionThreshold = 5;
    public const int DiskAttentionPercent = 85;
    public const int DiskCriticalPercent = 90;

    public Guid ServerId { get; init; }
    public DateTimeOffset CheckedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the server could not be queried at all (SSH down, timeout).</summary>
    public string? Error { get; init; }

    public int? PendingUpdates { get; init; }
    public bool? RebootRequired { get; init; }
    public int? DiskUsedPercent { get; init; }

    /// <summary>"active" / "inactive" as reported by <c>ufw status</c>; null if ufw is not installed.</summary>
    public string? Firewall { get; init; }

    /// <summary>Age of the newest file in the server's configured backup folder; null if none configured/found.</summary>
    public double? BackupAgeHours { get; init; }
    public bool BackupConfigured { get; init; }
    public string? BackupLabel { get; init; }
    public int BackupMaxAgeHours { get; init; }

    /// <summary>Results for the server's extra backup folders (<see cref="ServerResource.ExtraBackups"/>).</summary>
    public IReadOnlyList<BackupCheck> ExtraBackups { get; init; } = Array.Empty<BackupCheck>();

    public IReadOnlyList<UnitCheck> UnitChecks { get; init; } = Array.Empty<UnitCheck>();

    public bool Reachable => Error is null;

    private string BackupName => string.IsNullOrWhiteSpace(BackupLabel) ? "бэкап" : "бэкап " + BackupLabel;

    public bool BackupStale => BackupConfigured && (BackupAgeHours is null || BackupAgeHours > BackupMaxAgeHours);

    /// <summary>Things worth a yellow line on the card / a line in the weekly report.</summary>
    public IReadOnlyList<string> Attention
    {
        get
        {
            var notes = new List<string>();
            if (!Reachable)
            {
                return notes;
            }

            if (RebootRequired == true)
            {
                notes.Add("нужна перезагрузка");
            }

            if (PendingUpdates is >= UpdatesAttentionThreshold)
            {
                notes.Add($"обновлений: {PendingUpdates}");
            }

            if (DiskUsedPercent is >= DiskAttentionPercent)
            {
                notes.Add($"диск {DiskUsedPercent}%");
            }

            if (Firewall is not null && Firewall != "active")
            {
                notes.Add("фаервол выключен");
            }

            if (BackupStale)
            {
                notes.Add(BackupAgeHours is null
                    ? (string.IsNullOrWhiteSpace(BackupLabel) ? "бэкапов нет" : $"бэкапов нет ({BackupLabel})")
                    : $"{BackupName} устарел ({BackupAgeHours:0} ч)");
            }

            foreach (var b in ExtraBackups.Where(b => b.Stale))
            {
                notes.Add(b.AgeHours is null ? $"бэкапов нет ({b.Label})" : $"бэкап {b.Label} устарел ({b.AgeHours:0} ч)");
            }

            foreach (var b in ExtraBackups.Where(b => b.QuotaPercent >= BackupCheck.QuotaAttentionPercent))
            {
                notes.Add($"хранилище {b.Label} заполнено на {b.QuotaPercent}%");
            }

            foreach (var u in UnitChecks.Where(u => u.Bad))
            {
                notes.Add($"сбой службы: {u.Label}");
            }

            return notes;
        }
    }

    /// <summary>The subset worth interrupting the owner for right away, before the Monday report.</summary>
    public IReadOnlyList<string> Critical
    {
        get
        {
            var notes = new List<string>();
            if (!Reachable)
            {
                return notes;
            }

            if (Firewall is not null && Firewall != "active")
            {
                notes.Add("фаервол выключен");
            }

            if (DiskUsedPercent is >= DiskCriticalPercent)
            {
                notes.Add($"диск заполнен на {DiskUsedPercent}%");
            }

            if (BackupStale)
            {
                notes.Add(BackupAgeHours is null
                    ? (string.IsNullOrWhiteSpace(BackupLabel) ? "бэкапов нет" : $"бэкапов нет ({BackupLabel})")
                    : $"{BackupName} не обновлялся {BackupAgeHours:0} ч");
            }

            foreach (var b in ExtraBackups.Where(b => b.Stale))
            {
                notes.Add(b.AgeHours is null ? $"бэкапов нет ({b.Label})" : $"бэкап {b.Label} не обновлялся {b.AgeHours:0} ч");
            }

            foreach (var b in ExtraBackups.Where(b => b.QuotaPercent >= BackupCheck.QuotaCriticalPercent))
            {
                notes.Add($"хранилище {b.Label} почти полное ({b.QuotaPercent}%), новые копии будут отвергнуты");
            }

            foreach (var u in UnitChecks.Where(u => u.Bad))
            {
                notes.Add($"сбой службы: {u.Label}");
            }

            return notes;
        }
    }
}
