namespace DesControlCenter.Core.Configuration;

public sealed class AppSettings
{
    // Off by default: the registry Run key is only ever written when the user explicitly
    // checks this in Settings and saves — never automatically at first launch.
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
}
