using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Abstractions;

public interface IServerMonitor
{
    Task<ServerStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default);
}
