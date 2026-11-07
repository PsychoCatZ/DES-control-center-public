using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Windows toast notifications, sent only on state transitions (never per poll cycle).</summary>
public interface INotificationService
{
    void NotifyStatusChange(string title, string message, HealthLevel level);
}
