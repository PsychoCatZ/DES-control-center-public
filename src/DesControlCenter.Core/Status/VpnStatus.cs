namespace DesControlCenter.Core.Status;

/// <summary>
/// Point-in-time snapshot of a WireGuard/AmneziaWG tunnel, read from "wg show" run inside the
/// container. IsOnline means the container is up AND the wg control protocol answered with a
/// real interface — not just that the container process exists.
/// </summary>
public sealed class VpnStatus
{
    public bool IsOnline { get; init; }
    public string? InterfaceName { get; init; }
    public int PeerCount { get; init; }

    /// <summary>Peers with a handshake in the last 3 minutes. Low/zero is normal when nobody is
    /// currently connected — this is diagnostic, not by itself a failure signal.</summary>
    public int PeersConnectedNow { get; init; }

    /// <summary>Total bytes across all peers, straight from "wg show"'s own running counters
    /// (since the interface came up) — not a per-poll rate.</summary>
    public long TotalReceivedBytes { get; init; }
    public long TotalSentBytes { get; init; }

    /// <summary>How long ago the single most recent handshake (across all peers) happened —
    /// null if no peer has ever connected.</summary>
    public TimeSpan? MostRecentHandshakeAge { get; init; }

    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? LastError { get; init; }

    public static VpnStatus Offline(string? error = null) => new()
    {
        IsOnline = false,
        LastError = error,
        LastCheckedUtc = DateTimeOffset.UtcNow
    };
}
