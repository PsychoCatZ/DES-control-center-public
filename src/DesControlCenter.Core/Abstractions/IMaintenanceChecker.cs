using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Reads housekeeping facts from one server (read-only).</summary>
public interface IMaintenanceChecker
{
    Task<MaintenanceStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default);
}
