using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;

namespace DesControlCenter.Core.Monitoring;

/// <summary>Rolls up every resource's current status into the single traffic-light shown on the tray icon.</summary>
public static class ResourceHealthAggregator
{
    /// <param name="silences">Resources the owner chose to silence don't count towards the colour
    /// (a silenced server also silences what runs on it). They keep their real state on their own cards.</param>
    public static HealthLevel ComputeOverall(AppConfig config, PollingScheduler scheduler, SilenceRegistry? silences = null, ConnectivityState? connectivity = null)
    {
        // No connection on THIS computer: every check is failing for that reason, so don't paint
        // the tray red — a calm warning that says what is going on instead.
        if (connectivity?.AlarmsSuppressed == true)
        {
            return HealthLevel.Warning;
        }

        var level = HealthLevel.Ok;

        foreach (var server in config.Servers.Where(s => s.Enabled))
        {
            if (silences?.IsSilenced(server.Id) == true)
            {
                continue;
            }

            var status = scheduler.GetServerStatus(server.Id);
            if (status is not null && !status.IsOnline)
            {
                level = Max(level, HealthLevel.Problem);
            }
        }

        foreach (var website in config.Websites.Where(w => w.Enabled))
        {
            if (silences?.IsSilenced(website.Id, website.ServerId) == true)
            {
                continue;
            }

            var status = scheduler.GetWebsiteStatus(website.Id);
            if (status is null)
            {
                continue;
            }

            if (!status.IsOnline)
            {
                if (status.IsTransientDnsFailure(DateTimeOffset.UtcNow))
                {
                    // Probably the owner's own DNS/network: a calm warning for our own sites, nothing for
                    // third-party ones. Escalates to a real problem after WebsiteStatus.DnsGrace.
                    if (website.ServerId is not null)
                    {
                        level = Max(level, HealthLevel.Warning);
                    }
                }
                else
                {
                    level = Max(level, HealthLevel.Problem);
                }
            }
            // External Services (ServerId null — a third-party site, not one of our VPS) are
            // deliberately binary: reachable or not. Its SSL cert isn't ours to warn about, and
            // warning here is exactly what made the tray icon flip yellow for someone else's
            // certificate — never for our own sites, servers, bots or VPN.
            else if (website.ServerId is not null
                     && status.SslExpiresUtc is { } expires
                     && expires - DateTimeOffset.UtcNow <= TimeSpan.FromDays(website.SslWarningDays))
            {
                level = Max(level, HealthLevel.Warning);
            }
        }

        foreach (var service in config.Services.Where(s => s.Enabled))
        {
            if (silences?.IsSilenced(service.Id, service.ServerId) == true)
            {
                continue;
            }

            var status = scheduler.GetServiceStatus(service.Id);
            if (status is null)
            {
                continue;
            }

            level = Max(level, status.State switch
            {
                ServiceState.Failed => HealthLevel.Problem,
                ServiceState.Stopped => HealthLevel.Warning,
                ServiceState.Unknown => HealthLevel.Warning,
                _ => HealthLevel.Ok
            });
        }

        foreach (var vpn in config.Vpns.Where(v => v.Enabled))
        {
            if (silences?.IsSilenced(vpn.Id, vpn.ServerId) == true)
            {
                continue;
            }

            var status = scheduler.GetVpnStatus(vpn.Id);
            if (status is not null && !status.IsOnline)
            {
                level = Max(level, HealthLevel.Problem);
            }
        }

        return level;
    }

    private static HealthLevel Max(HealthLevel a, HealthLevel b) => a > b ? a : b;
}
