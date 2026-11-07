using System.Collections.Concurrent;
using System.Globalization;
using System.Net.NetworkInformation;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Gathers uptime/CPU/RAM/disk/network from a Linux VPS in a single SSH round trip.</summary>
public sealed class ServerMonitor : IServerMonitor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly ISshCommandRunner _sshRunner;
    private readonly ILogger<ServerMonitor> _logger;

    // Total bytes counters only ever go up, so a rate needs the previous sample — kept here,
    // per server, across polls. A singleton service (see AppHostBuilder), so this lives for the
    // app's lifetime, same as the polling loops themselves.
    private readonly ConcurrentDictionary<Guid, (double RxBytes, double TxBytes, DateTimeOffset SampledAt)> _previousNetSample = new();

    public ServerMonitor(ISshCommandRunner sshRunner, ILogger<ServerMonitor> logger)
    {
        _sshRunner = sshRunner;
        _logger = logger;
    }

    public async Task<ServerStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default)
    {
        var pingTask = PingAsync(server.Host, cancellationToken);

        var remoteCommand =
            "echo UPTIME_SEC:$(cut -d. -f1 /proc/uptime) && " +
            "echo LOAD1:$(cut -d' ' -f1 /proc/loadavg) && " +
            "echo CPU_COUNT:$(nproc) && " +
            "echo MEM:$(free -m | awk '/^Mem:/{print $2, $3}') && " +
            $"echo DISK:$(df -Pk {ShellQuote(server.DiskPath)} | awk 'NR==2{{print $2, $3}}') && " +
            "echo NET:$(awk 'NR>2 && $1!=\"lo:\"{rx+=$2; tx+=$10} END{print rx, tx}' /proc/net/dev)";

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, remoteCommand, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH check failed for {Server}", server.Name);
            return ServerStatus.Offline(ex.Message);
        }

        var pingMs = await pingTask;

        if (!result.Success)
        {
            var error = result.TimedOut
                ? "SSH timeout"
                : FirstNonEmptyLine(result.StandardError) ?? $"ssh exit code {result.ExitCode}";
            return new ServerStatus { IsOnline = false, PingMs = pingMs, LastError = error };
        }

        var values = ParseTaggedLines(result.StandardOutput);

        TimeSpan? uptime = values.TryGetValue("UPTIME_SEC", out var uptimeRaw)
                            && double.TryParse(uptimeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var uptimeSec)
            ? TimeSpan.FromSeconds(uptimeSec)
            : null;

        // Approximated from 1-minute load average / core count, since a true instantaneous
        // CPU% needs two /proc/stat samples a second apart over SSH — not worth the round trip.
        double? cpuPercent = null;
        double? loadAverage1 = null;
        if (values.TryGetValue("LOAD1", out var loadRaw)
            && double.TryParse(loadRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var load))
        {
            loadAverage1 = load;
            if (values.TryGetValue("CPU_COUNT", out var cpuCountRaw)
                && int.TryParse(cpuCountRaw, out var cpuCount) && cpuCount > 0)
            {
                cpuPercent = Math.Round(load / cpuCount * 100.0, 1);
            }
        }

        var (memTotal, memUsed) = ParsePair(values, "MEM");
        var (diskTotalKb, diskUsedKb) = ParsePair(values, "DISK");
        var (netRxBytes, netTxBytes) = ParsePair(values, "NET");
        var (rxKbps, txKbps) = ComputeNetworkRate(server.Id, netRxBytes, netTxBytes);

        return new ServerStatus
        {
            IsOnline = true,
            PingMs = pingMs,
            Uptime = uptime,
            CpuLoadPercent = cpuPercent,
            LoadAverage1Min = loadAverage1,
            MemoryTotalMb = memTotal,
            MemoryUsedMb = memUsed,
            DiskTotalGb = diskTotalKb / 1024 / 1024,
            DiskUsedGb = diskUsedKb / 1024 / 1024,
            NetworkRxKbps = rxKbps,
            NetworkTxKbps = txKbps
        };
    }

    /// <summary>Total bytes only ever go up, so a rate needs a delta against the previous sample —
    /// null on the very first poll for a server (matches every other "first check" in this app).</summary>
    private (double? RxKbps, double? TxKbps) ComputeNetworkRate(Guid serverId, double? rxBytes, double? txBytes)
    {
        var now = DateTimeOffset.UtcNow;
        if (rxBytes is not { } rx || txBytes is not { } tx)
        {
            return (null, null);
        }

        var previous = _previousNetSample.GetValueOrDefault(serverId);
        _previousNetSample[serverId] = (rx, tx, now);

        if (previous.SampledAt == default)
        {
            return (null, null);
        }

        var elapsedSeconds = (now - previous.SampledAt).TotalSeconds;
        if (elapsedSeconds <= 0 || rx < previous.RxBytes || tx < previous.TxBytes)
        {
            // Counter reset (reboot, interface flap) or a too-fast repeat check — skip this sample
            // rather than report a nonsensical or negative rate.
            return (null, null);
        }

        return ((rx - previous.RxBytes) / elapsedSeconds / 1024, (tx - previous.TxBytes) / elapsedSeconds / 1024);
    }

    private static async Task<double?> PingAsync(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000).WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static (double? Total, double? Used) ParsePair(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return (null, null);
        }

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var used))
        {
            return (total, used);
        }

        return (null, null);
    }

    private static Dictionary<string, string> ParseTaggedLines(string output)
    {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ');
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            result[line[..separatorIndex]] = line[(separatorIndex + 1)..].Trim();
        }

        return result;
    }

    private static string? FirstNonEmptyLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
