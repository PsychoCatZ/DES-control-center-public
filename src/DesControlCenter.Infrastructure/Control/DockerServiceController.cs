using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Infrastructure.Monitors;

namespace DesControlCenter.Infrastructure.Control;

/// <summary>
/// Manual start/stop/restart of a known Docker container — only ever from a user-confirmed
/// button press, same contract as <see cref="SystemdServiceController"/>.
/// </summary>
public sealed class DockerServiceController : IServiceController
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60); // "docker restart" waits up to 10 s for a graceful stop
    private readonly ISshCommandRunner _sshRunner;

    public DockerServiceController(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public Task<ServiceCommandOutcome> StartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        RunActionAsync(server, service, "start", cancellationToken);

    public Task<ServiceCommandOutcome> StopAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        RunActionAsync(server, service, "stop", cancellationToken);

    public Task<ServiceCommandOutcome> RestartAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default) =>
        RunActionAsync(server, service, "restart", cancellationToken);

    private async Task<ServiceCommandOutcome> RunActionAsync(ServerResource server, ServiceResource service, string action, CancellationToken cancellationToken)
    {
        var command = DockerCommand.Build(service, $"{action} {DockerCommand.ShellQuote(service.UnitName)} 2>&1");

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ServiceCommandOutcome { Success = false, Message = ex.Message };
        }

        if (result.Success)
        {
            return new ServiceCommandOutcome { Success = true, Message = $"{service.DisplayName}: {action} выполнен" };
        }

        var detail = result.TimedOut ? "команда не ответила вовремя (timeout)" : result.StandardOutput.Trim();
        return new ServiceCommandOutcome { Success = false, Message = string.IsNullOrWhiteSpace(detail) ? $"exit code {result.ExitCode}" : detail };
    }
}
