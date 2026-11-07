using System.Globalization;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Reads pending updates, a waiting reboot, disk use, firewall state and backup age in one
/// read-only SSH round trip. Works for root and for a sudo user (<c>sudo -n</c> where root rights are needed).</summary>
public sealed class SshMaintenanceChecker : IMaintenanceChecker
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(40);
    private readonly ISshCommandRunner _ssh;

    public SshMaintenanceChecker(ISshCommandRunner ssh)
    {
        _ssh = ssh;
    }

    public async Task<MaintenanceStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default)
    {
        var hasBackup = !string.IsNullOrWhiteSpace(server.BackupDirectory);

        var command =
            "S=''; [ \"$(id -u)\" -ne 0 ] && S='sudo -n'; " +
            "echo UPDATES:$(apt list --upgradable 2>/dev/null | tail -n +2 | wc -l); " +
            "echo REBOOT:$([ -f /var/run/reboot-required ] && echo 1 || echo 0); " +
            $"echo DISK:$(df -P {Quote(server.DiskPath)} | awk 'NR==2{{gsub(\"%\",\"\",$5); print $5}}'); " +
            "echo UFW:$($S ufw status 2>/dev/null | head -1 | awk '{print $2}'); " +
            (hasBackup
                ? $"echo BACKUP:$($S find {Quote(server.BackupDirectory!)} -maxdepth 1 -type f -printf '%T@\\n' 2>/dev/null | sort -n | tail -1); "
                : string.Empty) +
            ExtraBackupCommands(server) +
            UnitCommands(server) +
            "echo NOW:$(date +%s)";

        SshCommandResult result;
        try
        {
            result = await _ssh.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return new MaintenanceStatus { ServerId = server.Id, Error = ex.Message };
        }

        if (!result.Success)
        {
            var error = result.TimedOut
                ? "SSH timeout"
                : result.StandardError.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? $"ssh exit code {result.ExitCode}";
            return new MaintenanceStatus { ServerId = server.Id, Error = error };
        }

        return Parse(server, result.StandardOutput, hasBackup);
    }

    /// <summary>Public for tests: turns the tagged output into a status.</summary>
    public static MaintenanceStatus Parse(ServerResource server, string output, bool hasBackup)
    {
        var values = new Dictionary<string, string>();
        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        int? Int(string key) => values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        double? Dbl(string key) => values.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        // "bytes count" (or just "bytes" from an older checker): first and second number of the value
        string[] Parts(string key) => values.TryGetValue(key, out var v) ? v.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        long? Lng(string key) => Parts(key) is { Length: > 0 } p && long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        int? Cnt(string key) => Parts(key) is { Length: > 1 } p && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        double? backupAge = null;
        if (hasBackup && Dbl("BACKUP") is { } newest && Dbl("NOW") is { } now)
        {
            backupAge = Math.Max(0, (now - newest) / 3600.0);
        }

        var firewall = values.TryGetValue("UFW", out var ufw) && ufw.Length > 0 ? ufw : null;

        return new MaintenanceStatus
        {
            ServerId = server.Id,
            PendingUpdates = Int("UPDATES"),
            RebootRequired = Int("REBOOT") is { } r ? r == 1 : null,
            DiskUsedPercent = Int("DISK"),
            Firewall = firewall,
            BackupConfigured = hasBackup,
            BackupLabel = server.BackupLabel,
            BackupMaxAgeHours = server.BackupMaxAgeHours,
            BackupAgeHours = backupAge,
            ExtraBackups = ExtraChecks(server, Dbl("NOW"), Dbl, Lng, Cnt),
            UnitChecks = UnitResults(server, values)
        };
    }

    private static string ExtraBackupCommands(ServerResource server) =>
        string.Concat(server.ExtraBackups.Select((t, i) =>
        {
            var name = string.IsNullOrWhiteSpace(t.NamePattern) ? string.Empty : $"-name {Quote(t.NamePattern!)} ";
            var size = $"echo XBACKUPSIZE{i}:$($S find {Quote(t.Directory)} -maxdepth 1 -type f {name}-printf '%s\\n' 2>/dev/null | awk '{{s+=$1; n++}} END{{print s+0, n+0}}'); ";
            return $"echo XBACKUP{i}:$($S find {Quote(t.Directory)} -maxdepth 1 -type f {name}-printf '%T@\\n' 2>/dev/null | sort -n | tail -1); " + size;
        }));

    private static string UnitCommands(ServerResource server) =>
        string.Concat(server.WatchedUnits.Select((u, i) =>
            $"echo XUNIT{i}:$(systemctl show {Quote(u.Unit)} -p ActiveState -p Result -p LoadState 2>/dev/null | tr '\\n' ' '); "));

    private static IReadOnlyList<UnitCheck> UnitResults(ServerResource server, Dictionary<string, string> values) =>
        server.WatchedUnits.Select((u, i) =>
        {
            var props = new Dictionary<string, string>();
            if (values.TryGetValue($"XUNIT{i}", out var line))
            {
                foreach (var part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq > 0)
                    {
                        props[part[..eq]] = part[(eq + 1)..];
                    }
                }
            }

            return new UnitCheck
            {
                Label = string.IsNullOrWhiteSpace(u.Label) ? u.Unit : u.Label,
                ExpectActive = u.ExpectActive,
                ActiveState = props.GetValueOrDefault("ActiveState"),
                Result = props.GetValueOrDefault("Result"),
                LoadState = props.GetValueOrDefault("LoadState")
            };
        }).ToList();

    private static IReadOnlyList<BackupCheck> ExtraChecks(ServerResource server, double? now, Func<string, double?> dbl, Func<string, long?> lng, Func<string, int?> cnt) =>
        server.ExtraBackups.Select((t, i) => new BackupCheck
        {
            Label = string.IsNullOrWhiteSpace(t.Label) ? t.Directory : t.Label,
            MaxAgeHours = t.MaxAgeHours,
            MaxStoreBytes = t.MaxStoreBytes,
            StoreBytes = lng($"XBACKUPSIZE{i}"),
            Count = cnt($"XBACKUPSIZE{i}"),
            AgeHours = now is { } n && dbl($"XBACKUP{i}") is { } newest ? Math.Max(0, (n - newest) / 3600.0) : null
        }).ToList();

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
