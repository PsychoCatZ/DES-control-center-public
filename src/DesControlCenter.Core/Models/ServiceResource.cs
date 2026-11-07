using System.Text.Json.Serialization;

namespace DesControlCenter.Core.Models;

/// <summary>What <see cref="ServiceResource.UnitName"/> names.</summary>
public enum ServiceKind
{
    /// <summary>A systemd unit (the original and default — configs written before Docker support have no "Kind" and stay this).</summary>
    Systemd,

    /// <summary>A Docker container on the server; <see cref="ServiceResource.UnitName"/> is the container name.</summary>
    DockerContainer
}

/// <summary>A systemd unit or a Docker container on one of the monitored servers.</summary>
public sealed class ServiceResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }

    /// <summary>The systemd unit name, or the container name when <see cref="Kind"/> is <see cref="ServiceKind.DockerContainer"/>.</summary>
    public string UnitName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServiceKind Kind { get; set; } = ServiceKind.Systemd;

    /// <summary>Docker only: run "docker" through "sudo -n" (needed when the SSH user isn't root and not in the docker group, e.g. the Harbor server).</summary>
    public bool UseSudo { get; set; }

    /// <summary>Settings-window checkbox view of <see cref="Kind"/>; not stored separately.</summary>
    [JsonIgnore]
    public bool IsDocker
    {
        get => Kind == ServiceKind.DockerContainer;
        set => Kind = value ? ServiceKind.DockerContainer : ServiceKind.Systemd;
    }

    /// <summary>UI badge, e.g. "Telegram-бот" or "Системный сервис / Web proxy". Free text on purpose — the set of categories keeps growing.</summary>
    public string? Category { get; set; }

    public Guid? ProjectId { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
}
