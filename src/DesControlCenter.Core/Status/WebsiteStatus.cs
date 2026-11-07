namespace DesControlCenter.Core.Status;

public sealed class WebsiteStatus
{
    /// <summary>How long "the address could not be found" is treated as a probable hiccup of the
    /// owner's own network/DNS (yellow, no alarm) before it is escalated to a real problem — so a
    /// genuinely lapsed domain can never hide behind it for long.</summary>
    public static readonly TimeSpan DnsGrace = TimeSpan.FromMinutes(30);

    public bool IsOnline { get; init; }
    public int? HttpStatusCode { get; init; }
    public double? ResponseTimeMs { get; init; }
    public DateTimeOffset? SslExpiresUtc { get; init; }
    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? LastError { get; init; }

    /// <summary>The name could not be resolved (no such host / DNS error) — as opposed to a refused
    /// connection or a timeout, which say something about the server itself.</summary>
    public bool DnsFailure { get; init; }

    /// <summary>When the current run of DNS failures began (set by the scheduler).</summary>
    public DateTimeOffset? DnsFailureSince { get; init; }

    public static WebsiteStatus Offline(string? error = null, bool dnsFailure = false) => new()
    {
        IsOnline = false,
        LastError = error,
        DnsFailure = dnsFailure,
        LastCheckedUtc = DateTimeOffset.UtcNow
    };

    public WebsiteStatus WithDnsFailureSince(DateTimeOffset? since) => new()
    {
        IsOnline = IsOnline,
        HttpStatusCode = HttpStatusCode,
        ResponseTimeMs = ResponseTimeMs,
        SslExpiresUtc = SslExpiresUtc,
        LastCheckedUtc = LastCheckedUtc,
        LastError = LastError,
        DnsFailure = DnsFailure,
        DnsFailureSince = since
    };

    /// <summary>A name-resolution failure that has not (yet) lasted long enough to count as a real problem.</summary>
    public bool IsTransientDnsFailure(DateTimeOffset now) =>
        !IsOnline && DnsFailure && (DnsFailureSince is null || now - DnsFailureSince.Value < DnsGrace);
}
