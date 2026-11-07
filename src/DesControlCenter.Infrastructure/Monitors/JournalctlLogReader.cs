using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Reads a systemd unit's recent journal over SSH — the same "Логи" a `journalctl -u`
/// on the box itself would show, just without opening a terminal.</summary>
public sealed class JournalctlLogReader : IServiceLogReader
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly ISshCommandRunner _sshRunner;

    public JournalctlLogReader(ISshCommandRunner sshRunner)
    {
        _sshRunner = sshRunner;
    }

    public async Task<string> GetRecentLogsAsync(ServerResource server, ServiceResource service, int lines, CancellationToken cancellationToken = default)
    {
        var command = $"journalctl -u {ShellQuote(service.UnitName)} -n {lines} --no-pager --output=short-iso 2>&1";

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

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? "(журнал пуст)" : result.StandardOutput;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
