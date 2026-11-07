using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Abstractions;

/// <summary>All fields are optional filters; null means "don't filter on this".</summary>
public sealed class EventLogFilter
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ResourceId { get; set; }
    public ResourceKind? ResourceKind { get; set; }
    public EventKind? EventKind { get; set; }
    public int Limit { get; set; } = 500;
}
