using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Shared "docker …" command-line building for the monitor, controller and log reader.</summary>
internal static class DockerCommand
{
    /// <summary>"docker &lt;arguments&gt;", behind "sudo -n" when the service says the SSH user needs it.</summary>
    public static string Build(ServiceResource service, string arguments) =>
        service.UseSudo ? $"sudo -n docker {arguments}" : $"docker {arguments}";

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
