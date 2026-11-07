using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Abstractions;

public interface IServiceMonitor
{
    Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default);
}
