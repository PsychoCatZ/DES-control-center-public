using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DesControlCenter.Infrastructure.Notifications;

public sealed class ToastNotificationService : INotificationService
{
    public void NotifyStatusChange(string title, string message, HealthLevel level)
    {
        var icon = level switch
        {
            HealthLevel.Problem => "\U0001F534",
            HealthLevel.Warning => "\U0001F7E1",
            _ => "\U0001F7E2"
        };

        new ToastContentBuilder()
            .AddText($"{icon} {title}")
            .AddText(message)
            .Show();
    }
}
