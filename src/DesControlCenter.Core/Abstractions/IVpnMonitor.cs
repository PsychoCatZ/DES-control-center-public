using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Abstractions;

public interface IVpnMonitor
{
    Task<VpnStatus> CheckAsync(ServerResource server, VpnResource vpn, CancellationToken cancellationToken = default);
}
