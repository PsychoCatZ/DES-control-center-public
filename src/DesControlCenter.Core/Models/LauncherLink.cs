namespace DesControlCenter.Core.Models;

/// <summary>
/// A plain "open this app" button on the dashboard: a provider panel or an AI API console. Unlike a
/// <see cref="Website"/> it is never polled, never silenced and never part of the health colour — it only
/// launches the installed PWA (by its Start Menu name) and falls back to the URL in the browser.
/// </summary>
public sealed class LauncherLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Card title ("Example Hosting", "OpenAI").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Second line: which servers sit at this provider, or which projects use this API.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Start Menu name of the installed PWA, exactly as <c>Get-StartApps</c> lists it.</summary>
    public string? DesktopAppName { get; set; }

    /// <summary>Opened in the browser when the app is not installed.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>"Provider" (hosting panels), "Api" (AI consoles) or "Ai" (a local AI app, shown in the AI row right after the AI sites): decides the dashboard row.</summary>
    public string Group { get; set; } = LauncherGroups.Provider;

    public bool Enabled { get; set; } = true;
}

public static class LauncherGroups
{
    public const string Provider = "Provider";
    public const string Api = "Api";

    /// <summary>A local AI app (e.g. Local Assistant): a plain launcher card placed in the AI services row, after the checked AI sites.</summary>
    public const string Ai = "Ai";
}
