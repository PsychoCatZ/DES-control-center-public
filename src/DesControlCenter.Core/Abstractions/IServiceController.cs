using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

public sealed class ServiceCommandOutcome
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Manual, user-confirmed control of a known systemd unit. Never invoked automatically —
/// the app only monitors on its own; every state change here is a direct button press.
/// </summary>
public interface IServiceController
{
    Task<ServiceCommandOutcome> StartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default);
    Task<ServiceCommandOutcome> StopAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default);
    Task<ServiceCommandOutcome> RestartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default);
}
