namespace DesControlCenter.Core.Abstractions;

public sealed class SshCommandResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool TimedOut { get; init; }

    public bool Success => !TimedOut && ExitCode == 0;
}
