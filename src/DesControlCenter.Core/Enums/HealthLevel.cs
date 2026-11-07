namespace DesControlCenter.Core.Enums;

/// <summary>
/// Overall traffic-light health, used to drive the tray icon and dashboard dots.
/// Ordered so callers can take the max across resources.
/// </summary>
public enum HealthLevel
{
    Ok = 0,
    Warning = 1,
    Problem = 2
}
