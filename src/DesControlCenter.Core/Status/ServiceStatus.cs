using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Status;

public sealed class ServiceStatus
{
    public ServiceState State { get; init; } = ServiceState.Unknown;

    /// <summary>When systemd last entered the "active" state for this unit — i.e. the last
    /// start/restart, whether triggered by us or by the unit's own crash-restart policy.</summary>
    public DateTimeOffset? ActiveSinceUtc { get; init; }

    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? LastError { get; init; }

    public static ServiceStatus Unknown(string? error = null) => new()
    {
        State = ServiceState.Unknown,
        LastError = error,
        LastCheckedUtc = DateTimeOffset.UtcNow
    };
}
