namespace DesControlCenter.Core.Status;

public enum SwitchState
{
    Unknown,
    On,
    Off
}

/// <summary>Point-in-time reading of a <see cref="Models.SwitchResource"/>. Not a health signal: a switch being
/// "off" or unreadable is never an alarm.</summary>
public sealed class SwitchStatus
{
    public SwitchState State { get; init; } = SwitchState.Unknown;

    /// <summary>What the status command printed (first non-empty line), for display when it is unrecognised.</summary>
    public string? RawValue { get; init; }

    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? LastError { get; init; }

    public static SwitchStatus Unknown(string? error = null, string? raw = null) => new()
    {
        State = SwitchState.Unknown,
        LastError = error,
        RawValue = raw,
        LastCheckedUtc = DateTimeOffset.UtcNow
    };
}
