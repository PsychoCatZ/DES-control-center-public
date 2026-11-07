using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Monitoring;

public sealed class ServerCheckedEventArgs(ServerResource server, ServerStatus status) : EventArgs
{
    public ServerResource Server { get; } = server;
    public ServerStatus Status { get; } = status;
}

public sealed class WebsiteCheckedEventArgs(Website website, WebsiteStatus status) : EventArgs
{
    public Website Website { get; } = website;
    public WebsiteStatus Status { get; } = status;
}

public sealed class ServiceCheckedEventArgs(ServerResource server, ServiceResource service, ServiceStatus status) : EventArgs
{
    public ServerResource Server { get; } = server;
    public ServiceResource Service { get; } = service;
    public ServiceStatus Status { get; } = status;
}

public sealed class VpnCheckedEventArgs(ServerResource server, VpnResource vpn, VpnStatus status) : EventArgs
{
    public ServerResource Server { get; } = server;
    public VpnResource Vpn { get; } = vpn;
    public VpnStatus Status { get; } = status;
}
