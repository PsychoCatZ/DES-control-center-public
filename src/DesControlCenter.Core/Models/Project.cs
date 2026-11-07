namespace DesControlCenter.Core.Models;

/// <summary>
/// Logical grouping that cuts across servers/websites/services (DES, Notifier, PublicPortal, ...).
/// </summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
