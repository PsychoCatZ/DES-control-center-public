namespace DesControlCenter.Core.Models;

/// <summary>One more backup folder to watch on a server, with its own name and freshness limit.</summary>
public sealed class BackupTarget
{
    /// <summary>Shown in alerts and the report, e.g. "TaskBoard".</summary>
    public string Label { get; set; } = string.Empty;

    public string Directory { get; set; } = string.Empty;

    /// <summary>Only files matching this shell glob count (e.g. <c>taskboard-*.enc</c>), so bookkeeping files
    /// such as a checksum list, rewritten on every rotation, cannot make a stale folder look fresh. Null = any file.</summary>
    public string? NamePattern { get; set; }

    public int MaxAgeHours { get; set; } = 30;

    /// <summary>Size the folder's receiver refuses to grow past (e.g. an 8 GiB quota): when matching files add up to
    /// most of it, new backups are about to be rejected. Null = no quota check.</summary>
    public long? MaxStoreBytes { get; set; }
}

/// <summary>Result of checking one <see cref="BackupTarget"/>.</summary>
public sealed class BackupCheck
{
    public string Label { get; init; } = string.Empty;
    public double? AgeHours { get; init; }
    public int MaxAgeHours { get; init; }
    public long? StoreBytes { get; init; }

    /// <summary>How many matching files the folder holds (shown in the morning report as proof the nightly copy arrived).</summary>
    public int? Count { get; init; }
    public long? MaxStoreBytes { get; init; }

    public const int QuotaAttentionPercent = 75;
    public const int QuotaCriticalPercent = 90;

    public int? QuotaPercent => StoreBytes is { } used && MaxStoreBytes is > 0 ? (int)(used * 100 / MaxStoreBytes.Value) : null;
    public bool Stale => AgeHours is null || AgeHours > MaxAgeHours;
}

/// <summary>A systemd unit worth watching on a server: a one-shot job (a failed last run is the problem; being
/// "inactive" after success is normal) or, with <see cref="ExpectActive"/>, a timer/service that must be running.</summary>
public sealed class WatchedUnit
{
    public string Label { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool ExpectActive { get; set; }
}

/// <summary>Result of checking one <see cref="WatchedUnit"/>.</summary>
public sealed class UnitCheck
{
    public string Label { get; init; } = string.Empty;
    public string? ActiveState { get; init; }
    public string? Result { get; init; }
    public string? LoadState { get; init; }
    public bool ExpectActive { get; init; }

    /// <summary>No answer at all (nothing parsed) is not a verdict, so it is not "bad".</summary>
    public bool Bad => ActiveState is not null && (
        LoadState == "not-found"
        || ActiveState == "failed"
        || (Result is not null && Result != "success")
        || (ExpectActive && ActiveState != "active"));
}
