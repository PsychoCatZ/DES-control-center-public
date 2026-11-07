using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Ssh;

/// <summary>
/// Builds the ssh.exe argument list for a server, shared by the monitoring command runner and
/// the "Open SSH" terminal launcher so both resolve the same way. If the server has an
/// ~/.ssh/config alias configured, we defer entirely to that alias (ssh resolves port/identity/
/// proxy from the config file itself); otherwise we pass host/port/identity explicitly.
/// </summary>
public static class SshTargetArgs
{
    /// <summary>Non-interactive args for background monitoring: no prompts, fail fast, auto-trust new hosts.</summary>
    public static List<string> BuildMonitoringArgs(ServerResource server)
    {
        var args = new List<string>
        {
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "ConnectTimeout=10"
        };
        args.AddRange(BuildTargetArgs(server));
        return args;
    }

    /// <summary>Args for an interactive terminal session: no BatchMode, so a password prompt still works.</summary>
    public static List<string> BuildInteractiveArgs(ServerResource server) => BuildTargetArgs(server);

    private static List<string> BuildTargetArgs(ServerResource server)
    {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(server.SshConfigAlias))
        {
            args.Add(server.SshConfigAlias);
            return args;
        }

        if (server.SshPort != 22)
        {
            args.Add("-p");
            args.Add(server.SshPort.ToString());
        }

        if (!string.IsNullOrWhiteSpace(server.SshIdentityFilePath))
        {
            args.Add("-i");
            args.Add(server.SshIdentityFilePath);
        }

        var target = string.IsNullOrWhiteSpace(server.SshUsername)
            ? server.Host
            : $"{server.SshUsername}@{server.Host}";
        args.Add(target);

        return args;
    }
}
