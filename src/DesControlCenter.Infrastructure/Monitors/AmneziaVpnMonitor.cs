using System.Globalization;
using System.Text.RegularExpressions;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>
/// Checks an AmneziaWG tunnel by running "wg show" inside its Docker container over SSH — this
/// asks the actual WireGuard control protocol for its interface, not just whether a process
/// with a familiar name happens to be running. A container that's up but whose wg interface
/// doesn't answer is reported offline.
/// </summary>
public sealed partial class AmneziaVpnMonitor : IVpnMonitor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly ISshCommandRunner _sshRunner;

    public AmneziaVpnMonitor(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public async Task<VpnStatus> CheckAsync(ServerResource server, VpnResource vpn, CancellationToken cancellationToken = default)
    {
        var dockerPrefix = vpn.UseSudo ? "sudo -n docker" : "docker";
        var command = $"{dockerPrefix} exec {ShellQuote(vpn.ContainerName)} wg show 2>&1";

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return VpnStatus.Offline(ex.Message);
        }

        if (!result.Success)
        {
            var error = result.TimedOut ? "SSH timeout" : FirstNonEmptyLine(result.StandardOutput) ?? $"docker exec exit code {result.ExitCode}";
            return VpnStatus.Offline(error);
        }

        var output = result.StandardOutput;
        var interfaceMatch = InterfaceRegex().Match(output);
        if (!interfaceMatch.Success)
        {
            return VpnStatus.Offline("wg show returned no interface");
        }

        var peers = ParsePeers(output);
        var connectedNow = peers.Count(p => p.HandshakeAge is { } age && age < TimeSpan.FromMinutes(3));
        TimeSpan? mostRecent = peers.Where(p => p.HandshakeAge is not null)
            .Select(p => p.HandshakeAge!.Value)
            .OrderBy(age => age)
            .Cast<TimeSpan?>()
            .FirstOrDefault();

        return new VpnStatus
        {
            IsOnline = true,
            InterfaceName = interfaceMatch.Groups[1].Value,
            PeerCount = peers.Count,
            PeersConnectedNow = connectedNow,
            TotalReceivedBytes = peers.Sum(p => p.ReceivedBytes),
            TotalSentBytes = peers.Sum(p => p.SentBytes),
            MostRecentHandshakeAge = mostRecent
        };
    }

    private sealed record PeerSnapshot(TimeSpan? HandshakeAge, long ReceivedBytes, long SentBytes);

    /// <summary>Walks "wg show"'s output peer-block by peer-block (imperative, not one big regex —
    /// a peer's fields span several lines and this is easier to get right than a multiline pattern).</summary>
    private static List<PeerSnapshot> ParsePeers(string output)
    {
        var peers = new List<PeerSnapshot>();
        var inPeerBlock = false;
        TimeSpan? handshakeAge = null;
        long received = 0, sent = 0;

        void Flush()
        {
            if (inPeerBlock)
            {
                peers.Add(new PeerSnapshot(handshakeAge, received, sent));
            }
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("peer:", StringComparison.Ordinal))
            {
                Flush();
                inPeerBlock = true;
                handshakeAge = null;
                received = 0;
                sent = 0;
            }
            else if (inPeerBlock && line.StartsWith("latest handshake:", StringComparison.Ordinal))
            {
                handshakeAge = ParseHandshakeAge(line["latest handshake:".Length..].Trim());
            }
            else if (inPeerBlock && line.StartsWith("transfer:", StringComparison.Ordinal))
            {
                var parts = line["transfer:".Length..].Split(',');
                if (parts.Length == 2)
                {
                    received = ParseByteSize(parts[0]);
                    sent = ParseByteSize(parts[1]);
                }
            }
        }

        Flush();
        return peers;
    }

    // "wg show" prints handshake age as free text, e.g. "3 hours, 12 minutes, 18 seconds ago",
    // "58 seconds ago" or "(none)" for a peer that has never connected.
    private static TimeSpan? ParseHandshakeAge(string text)
    {
        if (text.Contains("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var days = ExtractUnit(text, "day");
        var hours = ExtractUnit(text, "hour");
        var minutes = ExtractUnit(text, "minute");
        var seconds = ExtractUnit(text, "second");

        return days == 0 && hours == 0 && minutes == 0 && seconds == 0
            ? null
            : new TimeSpan(days, hours, minutes, seconds);
    }

    private static int ExtractUnit(string text, string unitWord)
    {
        var match = Regex.Match(text, $@"(\d+)\s*{unitWord}", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    // e.g. "117.79 KiB received" / " 509.01 KiB sent" — wg's own units are binary (KiB/MiB/GiB).
    private static long ParseByteSize(string text)
    {
        var match = Regex.Match(text, @"([\d.]+)\s*(TiB|GiB|MiB|KiB|B)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        double multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "TIB" => 1024d * 1024 * 1024 * 1024,
            "GIB" => 1024d * 1024 * 1024,
            "MIB" => 1024d * 1024,
            "KIB" => 1024d,
            _ => 1d
        };

        return (long)(value * multiplier);
    }

    private static string? FirstNonEmptyLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    [GeneratedRegex(@"^interface:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex InterfaceRegex();
}
