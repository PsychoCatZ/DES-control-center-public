namespace DesControlCenter.Core.Models;

/// <summary>
/// A Linux VPS to monitor over SSH. Only connection *coordinates* are stored here —
/// never a private key's contents or a password. Authentication is delegated to the
/// system's OpenSSH client (ssh-agent, ~/.ssh/config, or a key file path).
/// </summary>
public sealed class ServerResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Hostname or IP. Ignored when <see cref="SshConfigAlias"/> is set.</summary>
    public string Host { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = string.Empty;

    /// <summary>Local path to a private key file, e.g. C:\Users\me\.ssh\id_ed25519. Path only, never the key material.</summary>
    public string? SshIdentityFilePath { get; set; }

    /// <summary>
    /// Optional Host alias from ~/.ssh/config. When set, the app runs "ssh &lt;alias&gt; ..."
    /// and Host/Port/Username/IdentityFile above are ignored in favor of the config file.
    /// </summary>
    public string? SshConfigAlias { get; set; }

    public Guid? ProjectId { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;

    /// <summary>Filesystem path to report disk usage for (root by default).</summary>
    public string DiskPath { get; set; } = "/";

    /// <summary>Optional folder on this server where backups arrive; the maintenance check reports the
    /// age of its newest file (e.g. WebShop archives on Harbor). Null = no backup check.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>What that backup is ("WebShop"): shown in the report and on the card, so a server with several backups is unambiguous.</summary>
    public string? BackupLabel { get; set; }

    /// <summary>A backup older than this is "stale" (the nightly one should be well under a day).</summary>
    public int BackupMaxAgeHours { get; set; } = 30;

    /// <summary>Further backup folders on this server (e.g. another project's archives next to WebShop's).</summary>
    public List<BackupTarget> ExtraBackups { get; set; } = new();

    /// <summary>systemd units to watch (e.g. a backup job's service and timer); a bad one shows as a maintenance problem.</summary>
    public List<WatchedUnit> WatchedUnits { get; set; } = new();
}
