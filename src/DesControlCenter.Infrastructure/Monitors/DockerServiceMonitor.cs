using System.Globalization;
using System.Text.RegularExpressions;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>
/// State of one Docker container, read with a single "docker inspect" over SSH. Uses the
/// container's own healthcheck when it has one (so "running" but "unhealthy" is a Failed, not a
/// green light) — which is the point of watching a container rather than just the site in front of it.
/// </summary>
public sealed class DockerServiceMonitor : IServiceMonitor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    // 9 fractional digits ("…20.422712281Z") are more than DateTimeOffset parses; keep 7.
    private static readonly Regex TooManyFractionDigits = new(@"(\.\d{7})\d+", RegexOptions.Compiled);

    private readonly ISshCommandRunner _sshRunner;

    public DockerServiceMonitor(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public async Task<ServiceStatus> CheckAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken = default)
    {
        var command = DockerCommand.Build(service,
            $"inspect {DockerCommand.ShellQuote(service.UnitName)} --format " +
            "'{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.State.StartedAt}}|{{.State.ExitCode}}' 2>&1");

        SshCommandResult result;
        try
        {
            result = await _sshRunner.RunAsync(server, command, CommandTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            return ServiceStatus.Unknown(ex.Message);
        }

        if (!result.Success)
        {
            var text = result.StandardOutput.Trim();
            return ServiceStatus.Unknown(result.TimedOut ? "SSH timeout"
                : text.Contains("No such object", StringComparison.OrdinalIgnoreCase) ? $"контейнер '{service.UnitName}' не найден на сервере"
                : text.Length == 0 ? $"docker вернул код {result.ExitCode}" : text);
        }

        var parts = result.StandardOutput.Trim().Split('|');
        if (parts.Length < 4)
        {
            return ServiceStatus.Unknown($"неожиданный ответ docker: {result.StandardOutput.Trim()}");
        }

        var status = parts[0].Trim();
        var health = parts[1].Trim();
        var startedAt = ParseDockerTimestamp(parts[2].Trim());
        _ = int.TryParse(parts[3].Trim(), out var exitCode);

        switch (status)
        {
            case "running" when health == "unhealthy":
                return new ServiceStatus { State = ServiceState.Failed, ActiveSinceUtc = startedAt, LastError = "контейнер запущен, но healthcheck не проходит" };
            case "running":
                return new ServiceStatus { State = ServiceState.Running, ActiveSinceUtc = startedAt, LastError = health == "starting" ? "запускается (healthcheck ещё не прошёл)" : null };
            case "restarting":
                return new ServiceStatus { State = ServiceState.Failed, LastError = "контейнер постоянно перезапускается" };
            case "exited" when exitCode != 0:
            case "dead":
                return new ServiceStatus { State = ServiceState.Failed, LastError = $"контейнер остановился с ошибкой (код {exitCode})" };
            case "exited":
            case "created":
            case "paused":
                return new ServiceStatus { State = ServiceState.Stopped };
            default:
                return ServiceStatus.Unknown($"неизвестное состояние контейнера: {status}");
        }
    }

    private static DateTimeOffset? ParseDockerTimestamp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("0001-", StringComparison.Ordinal))
        {
            return null;
        }

        var trimmed = TooManyFractionDigits.Replace(raw, "$1");
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
