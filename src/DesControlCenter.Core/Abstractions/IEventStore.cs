using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

public interface IEventStore
{
    Task AddAsync(EventLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventLogEntry>> QueryAsync(EventLogFilter filter, CancellationToken cancellationToken = default);
}
