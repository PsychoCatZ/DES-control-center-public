using System.Diagnostics;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesControlCenter.Infrastructure.Ssh;

/// <summary>Opens an interactive ssh session (system OpenSSH, by full path) in Windows Terminal, falling back to PowerShell.</summary>
public sealed class TerminalLauncher : ITerminalLauncher
{
    private readonly ILogger<TerminalLauncher> _logger;

    public TerminalLauncher(ILogger<TerminalLauncher> logger)
    {
        _logger = logger;
    }

    public void OpenSshSession(ServerResource server)
    {
        var sshArgs = SshTargetArgs.BuildInteractiveArgs(server);
        var quotedSshArgs = string.Join(' ', sshArgs.Select(Quote));
        // Full path to the Windows-shipped OpenSSH, never a bare "ssh": when this app is started
        // from a shell that has Git's usr\bin first in PATH, a bare "ssh" resolves to Git's own
        // build, which doesn't find %USERPROFILE%\.ssh\config under a Cyrillic profile path
        // ("Could not resolve hostname <alias>"). Same locator the monitoring runner uses.
        var sshCommandLine = $"& {Quote(SshExecutableLocator.Resolve())} {quotedSshArgs}";

        if (TryStart("wt.exe", $"new-tab powershell -NoExit -Command {Quote(sshCommandLine)}"))
        {
            return;
        }

        if (TryStart("powershell.exe", $"-NoExit -Command {Quote(sshCommandLine)}"))
        {
            return;
        }

        _logger.LogError("Could not launch a terminal to open an SSH session for {Server}", server.Name);
    }

    private bool TryStart(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName)
            {
                Arguments = arguments,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start {FileName} to open an SSH terminal", fileName);
            return false;
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
