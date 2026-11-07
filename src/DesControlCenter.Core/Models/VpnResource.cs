namespace DesControlCenter.Core.Models;

/// <summary>An AmneziaWG tunnel running as a Docker container on one of the monitored servers.</summary>
public sealed class VpnResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "amnezia-awg2";

    /// <summary>Set when the server's SSH user isn't in the docker group (e.g. Harbor's "vps" user) and needs "sudo -n".</summary>
    public bool UseSudo { get; set; }

    public Guid? ProjectId { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
}
