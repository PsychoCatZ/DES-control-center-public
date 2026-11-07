using System.Diagnostics;
using System.Text;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesControlCenter.Infrastructure.Ssh;

/// <summary>
/// Runs one non-interactive remote command via the system's OpenSSH client. Never touches key
/// material: authentication is entirely delegated to ssh.exe, ssh-agent and ~/.ssh/config.
/// </summary>
public sealed class OpenSshCommandRunner : ISshCommandRunner
{
    private readonly ILogger<OpenSshCommandRunner> _logger;
    private readonly string _sshPath = SshExecutableLocator.Resolve();

    public OpenSshCommandRunner(ILogger<OpenSshCommandRunner> logger)
    {
        _logger = logger;
    }

    public async Task<SshCommandResult> RunAsync(ServerResource server, string remoteCommand, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var args = SshTargetArgs.BuildMonitoringArgs(server);
        args.Add(remoteCommand);

        var startInfo = new ProcessStartInfo(_sshPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            // Linux logs are UTF-8; Windows' default code page corrupts Cyrillic output.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(timeoutCts.Token);

            return new SshCommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                TimedOut = false
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new SshCommandResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                TimedOut = true
            };
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill timed-out ssh process for {ProcessId}", process.Id);
        }
    }
}
