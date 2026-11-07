using System.Globalization;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Monitoring;

/// <summary>Builds the Monday message — plain text, short, readable on a phone.</summary>
public static class ReportComposer
{
    private static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>"· копий 4 · 871 КБ (0% квоты)": proof that copies keep arriving and the store is not filling up.</summary>
    private static string Details(BackupCheck x)
    {
        var parts = new List<string>();
        if (x.Count is { } count)
        {
            parts.Add($"копий {count}");
        }

        if (x.StoreBytes is { } bytes)
        {
            parts.Add(FormatBytes(bytes) + (x.QuotaPercent is { } q ? $" ({q}% квоты)" : string.Empty));
        }

        return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.#", Ru) + " ГБ",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.#", Ru) + " МБ",
        _ => (bytes / 1024.0).ToString("0", Ru) + " КБ"
    };

    private static string Label(ServerResource server) =>
        string.IsNullOrWhiteSpace(server.BackupLabel) ? string.Empty : " " + server.BackupLabel;

    public static string Compose(
        AppConfig config,
        DateTimeOffset nowLocal,
        Func<Guid, MaintenanceStatus?> maintenance,
        Func<Guid, ServerStatus?> serverStatus,
        int silencedCount,
        (string Name, DateTimeOffset Expires)? nearestCertificate)
    {
        var lines = new List<string>
        {
            $"🐈 Обход серверов — {nowLocal.ToString("dddd, dd.MM.yyyy HH:mm", Ru)}",
            string.Empty
        };

        foreach (var server in config.Servers.Where(s => s.Enabled))
        {
            var live = serverStatus(server.Id);
            var m = maintenance(server.Id);

            if (live is { IsOnline: false })
            {
                lines.Add($"❌ {server.Name} — не отвечает");
                continue;
            }

            if (m is null)
            {
                lines.Add($"❔ {server.Name} — данных об обслуживании пока нет");
                continue;
            }

            if (!m.Reachable)
            {
                lines.Add($"❔ {server.Name} — не удалось проверить ({m.Error})");
                continue;
            }

            var attention = m.Attention;
            lines.Add(attention.Count == 0
                ? $"✅ {server.Name} — всё в порядке"
                : $"⚠️ {server.Name} — {string.Join("; ", attention)}");
        }

        var backupServer = config.Servers.FirstOrDefault(s => s.Enabled && !string.IsNullOrWhiteSpace(s.BackupDirectory));
        if (backupServer is not null && maintenance(backupServer.Id) is { Reachable: true, BackupConfigured: true } b)
        {
            lines.Add(string.Empty);
            lines.Add(b.BackupStale
                ? $"💾 Бэкап{Label(backupServer)} ({backupServer.Name}): ⚠️ {(b.BackupAgeHours is null ? "архивов нет" : $"последний {b.BackupAgeHours:0} ч назад")}"
                : $"💾 Бэкап{Label(backupServer)} ({backupServer.Name}): свежий, {b.BackupAgeHours:0} ч назад");
        }

        foreach (var server in config.Servers.Where(s => s.Enabled))
        {
            if (maintenance(server.Id) is { Reachable: true } m2)
            {
                foreach (var x in m2.ExtraBackups)
                {
                    lines.Add(x.Stale
                        ? $"💾 Бэкап {x.Label} ({server.Name}): ⚠️ {(x.AgeHours is null ? "архивов нет" : $"последний {x.AgeHours:0} ч назад")}"
                        : $"💾 Бэкап {x.Label} ({server.Name}): свежий, {x.AgeHours:0} ч назад{Details(x)}");
                }
            }
        }

        if (nearestCertificate is { } cert)
        {
            var days = (int)Math.Floor((cert.Expires - nowLocal).TotalDays);
            lines.Add($"🔒 Ближайший сертификат: {cert.Name}, ещё {days} дн. ({cert.Expires.ToLocalTime():dd.MM.yyyy})");
        }

        if (silencedCount > 0)
        {
            lines.Add($"🔕 Приглушено проблем: {silencedCount}");
        }

        return string.Join("\n", lines);
    }
}
