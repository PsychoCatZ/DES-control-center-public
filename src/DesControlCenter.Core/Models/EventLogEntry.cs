using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Models;

/// <summary>One row in the persistent event log (SQLite-backed).</summary>
public sealed class EventLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public ResourceKind ResourceKind { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public EventKind EventKind { get; set; }
    public string? OldState { get; set; }
    public string? NewState { get; set; }
    public string Message { get; set; } = string.Empty;
}
