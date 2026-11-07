namespace DesControlCenter.Core.Models;

public sealed class Website
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Nests this site under a VPS card in the dashboard. Null means it's shown under
    /// External Services instead — for a third-party service we don't host.</summary>
    public Guid? ServerId { get; set; }

    /// <summary>UI badge, e.g. "Сайт" or "AI-сервис". Free text on purpose — the set of categories keeps growing.</summary>
    public string? Category { get; set; }

    /// <summary>Custom URI scheme (no "://", e.g. "tg", "whatsapp") to try before falling back to
    /// the browser — only set when that scheme is actually registered as a protocol handler on
    /// this machine (checked at launch time, not assumed).</summary>
    public string? DesktopProtocol { get; set; }

    /// <summary>Start Menu app display name (as Windows itself lists it) to resolve and launch
    /// when there's no protocol handler — e.g. "ChatGPT", "Gemini". Resolved fresh each time
    /// rather than cached to a fixed app id, since a packaged app's id can change on update.</summary>
    public string? DesktopAppName { get; set; }

    /// <summary>A site that belongs to a server (and is monitored there) can ALSO get a card in the launcher row under the
    /// external services, so its app (usually an installed PWA) is one click away. The same row is shown twice, so it is
    /// still checked once and an outage is still reported once.</summary>
    public bool PinToLauncher { get; set; }

    /// <summary>Short name for that launcher card ("Ассистент"); the site's own name is used when empty.</summary>
    public string? LauncherTitle { get; set; }

    public Guid? ProjectId { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public int SslWarningDays { get; set; } = 14;
    public bool Enabled { get; set; } = true;
}
