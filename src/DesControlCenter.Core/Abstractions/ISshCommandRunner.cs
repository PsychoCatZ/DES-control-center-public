using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

/// <summary>
/// Runs a single non-interactive command on a server over SSH. Implementations must shell out
/// to the system's own SSH client (never re-implement the protocol or touch key material) so that
/// ssh-agent and ~/.ssh/config keep working exactly as they do outside this app.
/// </summary>
public interface ISshCommandRunner
{
    Task<SshCommandResult> RunAsync(ServerResource server, string remoteCommand, TimeSpan timeout, CancellationToken cancellationToken = default);
}
