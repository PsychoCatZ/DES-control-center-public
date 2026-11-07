namespace DesControlCenter.Core.Status;

/// <summary>
/// Point-in-time snapshot of a server's health. Not persisted — only the transitions
/// derived from it are written to the event log.
/// </summary>
public sealed class ServerStatus
{
    public bool IsOnline { get; init; }
    public double? PingMs { get; init; }
    public TimeSpan? Uptime { get; init; }
    public double? CpuLoadPercent { get; init; }
    public double? LoadAverage1Min { get; init; }
    public double? MemoryUsedMb { get; init; }
    public double? MemoryTotalMb { get; init; }
    public double? DiskUsedGb { get; init; }
    public double? DiskTotalGb { get; init; }

    /// <summary>KB/s since the previous poll — null on the first check for a server (no prior
    /// sample to diff against yet).</summary>
    public double? NetworkRxKbps { get; init; }
    public double? NetworkTxKbps { get; init; }

    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? LastError { get; init; }

    public static ServerStatus Offline(string? error = null) => new()
    {
        IsOnline = false,
        LastError = error,
        LastCheckedUtc = DateTimeOffset.UtcNow
    };
}
