using System.Globalization;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Infrastructure.Monitors;

public sealed class SystemdServiceMonitor : IServiceMonitor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly ISshCommandRunner _sshRunner;

    public SystemdServiceMonitor(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public async Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default)
    {
        // LoadState first, ActiveState second: "systemctl show" happily returns
        // LoadState=not-found/ActiveState=inactive for a unit name that doesn't exist at all
        // (e.g. a typo'd UnitName), which would otherwise be indistinguishable from a real,
        // deliberately-stopped service.
        // TZ=UTC: systemd prints timestamps in the server's own zone ("... CEST", "... MSK"),
        // which ParseSystemdTimestamp can't read — forcing UTC makes every server's output the
        // same regardless of where it lives.
        var command = $"TZ=UTC systemctl show {ShellQuote(service.UnitName)} --property=LoadState,ActiveState,ActiveEnterTimestamp --value 2>&1";

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceStatus.Unknown(ex.Message);
        }

        if (!result.Success)
        {
            return ServiceStatus.Unknown(result.TimedOut ? "SSH timeout" : result.StandardOutput.Trim());
        }

        var lines = result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries);
        var loadState = lines.Length > 0 ? lines[0] : string.Empty;
        var activeState = lines.Length > 1 ? lines[1] : string.Empty;
        var activeSinceRaw = lines.Length > 2 ? lines[2] : string.Empty;

        if (loadState == "not-found")
        {
            return ServiceStatus.Unknown($"unit '{service.UnitName}' not found on server");
        }

        var state = activeState switch
        {
            "active" => ServiceState.Running,
            "failed" => ServiceState.Failed,
            "inactive" => ServiceState.Stopped,
            _ => ServiceState.Unknown
        };

        return new ServiceStatus { State = state, ActiveSinceUtc = ParseSystemdTimestamp(activeSinceRaw) };
    }

    private static DateTimeOffset? ParseSystemdTimestamp(string raw)
    {
        // Blank for a unit that has never been active. systemd's default human-readable format,
        // e.g. "Thu 2026-09-25 09:26:28 UTC" — every server we monitor runs in UTC.
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(raw, "ddd yyyy-MM-dd HH:mm:ss 'UTC'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
