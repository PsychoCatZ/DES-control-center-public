using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Reads a container's recent output over SSH — the same "Логи" as `docker logs --tail`.</summary>
public sealed class DockerLogReader : IServiceLogReader
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly ISshCommandRunner _sshRunner;

    public DockerLogReader(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public async Task<string> GetRecentLogsAsync(ServerResource server, ServiceResource service, int lines, CancellationToken cancellationToken = default)
    {
        var command = DockerCommand.Build(service, $"logs --tail {lines} --timestamps {DockerCommand.ShellQuote(service.UnitName)} 2>&1");

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Не удалось получить логи: {ex.Message}";
        }

        if (result.TimedOut)
        {
            return "Не удалось получить логи: SSH timeout";
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? "(логи контейнера пусты)" : result.StandardOutput;
    }
}
