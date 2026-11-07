using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure.Control;
using DesControlCenter.Infrastructure.Monitors;

namespace DesControlCenter.Infrastructure;

// The scheduler and the view models know one IServiceMonitor / IServiceController /
// IServiceLogReader; these pick the systemd or the Docker implementation by ServiceResource.Kind,
// so a Docker container is just another "service" row everywhere else.

public sealed class ServiceMonitorRouter : IServiceMonitor
{
    private readonly SystemdServiceMonitor _systemd;
    private readonly DockerServiceMonitor _docker;

    public ServiceMonitorRouter(SystemdServiceMonitor systemd, DockerServiceMonitor docker)
    {
        _systemd = systemd;
        _docker = docker;
    }

    public Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        (service.Kind == ServiceKind.DockerContainer ? (IServiceMonitor)_docker : _systemd).CheckAsync(server, service, cancellationToken);
}

public sealed class ServiceControllerRouter : IServiceController
{
    private readonly SystemdServiceController _systemd;
    private readonly DockerServiceController _docker;

    public ServiceControllerRouter(SystemdServiceController systemd, DockerServiceController docker)
    {
        _systemd = systemd;
        _docker = docker;
    }

    private IServiceController For(ServiceResource service) =>
        service.Kind == ServiceKind.DockerContainer ? _docker : _systemd;

    public Task<ServiceCommandOutcome> StartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        For(service).StartAsync(server, service, cancellationToken);

    public Task<ServiceCommandOutcome> StopAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        For(service).StopAsync(server, service, cancellationToken);

    public Task<ServiceCommandOutcome> RestartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        For(service).RestartAsync(server, service, cancellationToken);
}

public sealed class ServiceLogReaderRouter : IServiceLogReader
{
    private readonly JournalctlLogReader _systemd;
    private readonly DockerLogReader _docker;

    public ServiceLogReaderRouter(JournalctlLogReader systemd, DockerLogReader docker)
    {
        _systemd = systemd;
        _docker = docker;
    }

    public Task<string> GetRecentLogsAsync(ServerResource server, ServiceResource service, int lines, CancellationToken cancellationToken = default) =>
        (service.Kind == ServiceKind.DockerContainer ? (IServiceLogReader)_docker : _systemd).GetRecentLogsAsync(server, service, lines, cancellationToken);
}
