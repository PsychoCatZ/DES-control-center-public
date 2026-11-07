using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

public interface IServiceLogReader
{
    /// <summary>Returns the last <paramref name="lines"/> lines of the unit's journal, newest
    /// last (same order "journalctl" prints them in).</summary>
    Task<string> GetRecentLogsAsync(ServerResource server, ServiceResource service, int lines, CancellationToken cancellationToken = default);
}
