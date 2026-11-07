using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Configuration;

/// <summary>The whole user configuration, round-tripped to %AppData%\DesControlCenter\config.json.</summary>
public sealed class AppConfig
{
    public List<Project> Projects { get; set; } = new();
    public List<ServerResource> Servers { get; set; } = new();
    public List<Website> Websites { get; set; } = new();
    public List<ServiceResource> Services { get; set; } = new();
    public List<VpnResource> Vpns { get; set; } = new();
    public List<SwitchResource> Switches { get; set; } = new();
    public List<LauncherLink> Launchers { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
