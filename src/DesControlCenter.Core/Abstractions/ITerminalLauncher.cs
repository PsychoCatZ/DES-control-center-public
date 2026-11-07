using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Opens an interactive SSH session for a server in an external terminal window.</summary>
public interface ITerminalLauncher
{
    void OpenSshSession(ServerResource server);
}
