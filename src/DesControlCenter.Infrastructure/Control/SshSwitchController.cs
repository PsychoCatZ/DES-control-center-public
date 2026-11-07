using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Infrastructure.Control;

/// <summary>Reads and flips a <see cref="SwitchResource"/> by running its configured commands over SSH.</summary>
public sealed class SshSwitchController : ISwitchController
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlipTimeout = TimeSpan.FromSeconds(90);
    private readonly ISshCommandRunner _ssh;

    public SshSwitchController(ISshCommandRunner ssh)
    {
        _ssh = ssh;
    }

    public async Task<SwitchStatus> CheckAsync(ServerResource server, SwitchResource sw, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sw.StatusCommand))
        {
            return SwitchStatus.Unknown("команда состояния не задана");
        }

        SshCommandResult result;
        try
        {
            result = await _ssh.RunAsync(server, sw.StatusCommand, ReadTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SwitchStatus.Unknown(ex.Message);
        }

        if (!result.Success)
        {
            return SwitchStatus.Unknown(result.TimedOut ? "SSH timeout" : FirstLine(result.StandardError) ?? $"ssh exit code {result.ExitCode}");
        }

        return Parse(sw, result.StandardOutput);
    }

    /// <summary>Public for tests: the first non-empty line of the output decides the position.</summary>
    public static SwitchStatus Parse(SwitchResource sw, string output)
    {
        var raw = FirstLine(output);
        var state = raw is null ? SwitchState.Unknown
            : string.Equals(raw, sw.OnValue, StringComparison.OrdinalIgnoreCase) ? SwitchState.On
            : string.Equals(raw, sw.OffValue, StringComparison.OrdinalIgnoreCase) ? SwitchState.Off
            : SwitchState.Unknown;

        return new SwitchStatus
        {
            State = state,
            RawValue = raw,
            LastError = state == SwitchState.Unknown ? (raw is null ? "пустой ответ" : "непонятный ответ: " + raw) : null,
            LastCheckedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<ServiceCommandOutcome> SetAsync(ServerResource server, SwitchResource sw, bool on, CancellationToken cancellationToken = default)
    {
        var command = on ? sw.OnCommand : sw.OffCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ServiceCommandOutcome { Success = false, Message = "команда переключения не задана" };
        }

        try
        {
            var result = await _ssh.RunAsync(server, command, FlipTimeout, cancellationToken);
            if (result.Success)
            {
                return new ServiceCommandOutcome { Success = true, Message = FirstLine(result.StandardOutput) ?? "выполнено" };
            }

            return new ServiceCommandOutcome
            {
                Success = false,
                Message = result.TimedOut ? "SSH timeout" : FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"ssh exit code {result.ExitCode}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ServiceCommandOutcome { Success = false, Message = ex.Message };
        }
    }

    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
