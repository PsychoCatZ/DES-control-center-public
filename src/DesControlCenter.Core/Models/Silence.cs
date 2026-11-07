namespace DesControlCenter.Core.Models;

public enum SilenceMode
{
    /// <summary>Quiet until the resource is healthy again — then the silence lifts by itself, so the next failure is loud.</summary>
    UntilRecovery,

    /// <summary>Quiet until <see cref="Silence.UntilUtc"/> (e.g. a maintenance window).</summary>
    Until,

    /// <summary>Quiet until the owner turns it back on.</summary>
    Forever
}

/// <summary>
/// "I know about this problem, stop lighting the tray red / popping toasts for it." A silenced
/// resource is still checked and still shows its real state on its card — it just doesn't count
/// towards the tray colour, the problems banner or notifications.
/// </summary>
public sealed class Silence
{
    public Guid ResourceId { get; set; }
    public SilenceMode Mode { get; set; }
    public DateTimeOffset? UntilUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
