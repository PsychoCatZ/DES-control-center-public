using System.Globalization;
using System.Text;
using System.Text.Json;
using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Monitoring;

/// <summary>What the dashboard knows right now, small enough for a second program (an OSD overlay, a widget) to read from a file.</summary>
public sealed record StatusSnapshot(
    HealthLevel Health,
    bool NoConnection,
    int ProblemCount,
    int SilencedCount,
    int ServersOnline,
    int ServersTotal,
    int ServersPending,
    IReadOnlyList<string> Problems,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? Warnings = null)
{
    /// <summary>The state DES writes when it is closed, so a reader doesn't show a stale "all good".</summary>
    public static StatusSnapshot Stopped(DateTimeOffset now) =>
        new(HealthLevel.Ok, false, 0, 0, 0, 0, 0, Array.Empty<string>(), now);
}

/// <summary>Turns a <see cref="StatusSnapshot"/> into the three files DES keeps in its data folder.</summary>
public static class StatusFormatter
{
    private const int MaxListed = 5;

    public static string StateWord(StatusSnapshot s) =>
        s.NoConnection ? "нет связи у этого компьютера"
        : s.Health == HealthLevel.Problem ? "есть проблемы"
        : s.Health == HealthLevel.Warning ? "есть предупреждения"
        : "всё в порядке";

    /// <summary>Several short "key: value" lines (status.txt).</summary>
    public static string Text(StatusSnapshot s, bool stopped = false)
    {
        var sb = new StringBuilder();
        if (stopped)
        {
            sb.Append("Состояние: DES не запущен\n");
            sb.Append("Обновлено: ").Append(Stamp(s.UpdatedAt)).Append('\n');
            return sb.ToString();
        }

        sb.Append("Состояние: ").Append(StateWord(s)).Append('\n');
        sb.Append("Проблем: ").Append(s.ProblemCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Приглушено: ").Append(s.SilencedCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Серверов онлайн: ").Append(s.ServersOnline).Append(" из ").Append(s.ServersTotal);
        if (s.ServersPending > 0)
        {
            sb.Append(" (проверяется: ").Append(s.ServersPending).Append(')');
        }

        sb.Append('\n');
        for (var i = 0; i < Math.Min(MaxListed, s.Problems.Count); i++)
        {
            sb.Append("Проблема ").Append(i + 1).Append(": ").Append(s.Problems[i]).Append('\n');
        }

        var warnings = s.Warnings ?? Array.Empty<string>();
        for (var i = 0; i < Math.Min(MaxListed, warnings.Count); i++)
        {
            sb.Append("Предупреждение ").Append(i + 1).Append(": ").Append(warnings[i]).Append('\n');
        }

        sb.Append("Обновлено: ").Append(Stamp(s.UpdatedAt)).Append('\n');
        return sb.ToString();
    }

    /// <summary>One line for a narrow panel (status-line.txt): "DES: всё в порядке · серверов 8/8".</summary>
    public static string OneLine(StatusSnapshot s, bool stopped = false)
    {
        if (stopped)
        {
            return "DES: не запущен";
        }

        var head = s.NoConnection ? "DES: нет связи у ПК"
            : s.ProblemCount > 0 ? $"DES: проблем {s.ProblemCount}"
            : s.Health == HealthLevel.Warning ? "DES: есть предупреждения"
            : "DES: всё в порядке";
        var servers = s.ServersPending > 0 ? $"серверов {s.ServersOnline}/{s.ServersTotal} (проверяется)" : $"серверов {s.ServersOnline}/{s.ServersTotal}";
        return head + " · " + servers;
    }

    /// <summary>The same data for programs that prefer JSON (status.json).</summary>
    public static string Json(StatusSnapshot s, bool stopped = false)
    {
        var obj = new Dictionary<string, object?>
        {
            ["running"] = !stopped,
            ["health"] = stopped ? "Stopped" : s.NoConnection ? "NoConnection" : s.Health.ToString(),
            ["problems"] = s.ProblemCount,
            ["silenced"] = s.SilencedCount,
            ["serversOnline"] = s.ServersOnline,
            ["serversTotal"] = s.ServersTotal,
            ["serversPending"] = s.ServersPending,
            ["problemList"] = s.Problems.Take(MaxListed).ToArray(),
            ["warningList"] = (s.Warnings ?? Array.Empty<string>()).Take(MaxListed).ToArray(),
            ["updatedAt"] = s.UpdatedAt.ToString("o", CultureInfo.InvariantCulture)
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
