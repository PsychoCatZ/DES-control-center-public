namespace DesControlCenter.Core.Abstractions;

/// <summary>
/// Opens a service preferring its installed desktop app over the browser. Detection is real,
/// not assumed: a protocol is only used if it's actually registered on this machine, and an app
/// name is only used if Windows can currently resolve it to something launchable — otherwise
/// this falls back to the plain URL in the default browser.
/// </summary>
public interface IDesktopAppLauncher
{
    Task OpenAsync(string? desktopProtocol, string? desktopAppName, string url, CancellationToken cancellationToken = default);
}
