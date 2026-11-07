namespace DesControlCenter.Core.Models;

/// <summary>
/// A two-position switch that lives on a server and is flipped by a command there — e.g. "the pet's brain:
/// real Assistant / built-in stub". DES shows its state (read with <see cref="StatusCommand"/>) and a button that
/// runs <see cref="OnCommand"/> or <see cref="OffCommand"/> after a confirmation. The commands come only from the
/// owner's own configuration file; nothing is ever sent to the server that the owner did not put there.
/// </summary>
public sealed class SwitchResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>Prints the current state; the first non-empty line is compared with <see cref="OnValue"/> / <see cref="OffValue"/>.</summary>
    public string StatusCommand { get; set; } = string.Empty;
    public string OnCommand { get; set; } = string.Empty;
    public string OffCommand { get; set; } = string.Empty;

    public string OnValue { get; set; } = "on";
    public string OffValue { get; set; } = "off";

    /// <summary>How each position is called on the card ("Assistant", "заглушка").</summary>
    public string OnText { get; set; } = "включён";
    public string OffText { get; set; } = "выключен";

    public int PollIntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
}
