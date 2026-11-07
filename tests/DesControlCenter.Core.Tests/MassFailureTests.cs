using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

/// <summary>"Almost everything failed in the same moment" is far more likely our own network path (a VPN switch, a
/// router) than a dozen independent servers dying at once, so it must not become a wall of alarms.</summary>
public class MassFailureTests
{
    private static (AppConfig Config, PollingScheduler Scheduler, FakeServerMonitor Servers, FakeWebsiteMonitor Sites, FakeServiceMonitor Services,
        FakeEventStore Events, FakeNotificationService Toasts, ConnectivityState State) Make(int servers = 3, int sites = 3, int services = 3)
    {
        var config = new AppConfig();
        for (var i = 0; i < servers; i++)
        {
            config.Servers.Add(new ServerResource { Name = "s" + i, Host = "10.0.0." + i });
        }

        for (var i = 0; i < sites; i++)
        {
            config.Websites.Add(new Website { Name = "w" + i, Url = "https://w" + i + ".example.com/" });
        }

        for (var i = 0; i < services; i++)
        {
            config.Services.Add(new ServiceResource { UnitName = "u" + i, DisplayName = "u" + i, ServerId = config.Servers[i % servers].Id });
        }

        var serverMonitor = new FakeServerMonitor();
        var siteMonitor = new FakeWebsiteMonitor();
        var serviceMonitor = new FakeServiceMonitor();
        var events = new FakeEventStore();
        var toasts = new FakeNotificationService();
        var state = new ConnectivityState();
        var scheduler = new PollingScheduler(serverMonitor, siteMonitor, serviceMonitor, new FakeVpnMonitor(),
            events, toasts, NullLogger<PollingScheduler>.Instance, null, state);
        return (config, scheduler, serverMonitor, siteMonitor, serviceMonitor, events, toasts, state);
    }

    private static void Round(FakeServerMonitor servers, FakeWebsiteMonitor sites, FakeServiceMonitor services, AppConfig config, bool online, int failOnly = -1)
    {
        // failOnly >= 0: only that many resources of each kind fail (the rest stay fine)
        for (var i = 0; i < config.Servers.Count; i++)
        {
            var fine = online || (failOnly >= 0 && i >= failOnly);
            servers.Responses.Enqueue(fine ? new ServerStatus { IsOnline = true } : ServerStatus.Offline("timeout"));
        }

        for (var i = 0; i < config.Websites.Count; i++)
        {
            var fine = online || (failOnly >= 0 && i >= failOnly);
            sites.Responses.Enqueue(fine ? new WebsiteStatus { IsOnline = true, HttpStatusCode = 200 } : WebsiteStatus.Offline("timeout"));
        }

        for (var i = 0; i < config.Services.Count; i++)
        {
            var fine = online || (failOnly >= 0 && i >= failOnly);
            services.Responses.Enqueue(fine ? new ServiceStatus { State = ServiceState.Running } : ServiceStatus.Unknown("ssh timeout"));
        }
    }

    [Fact]
    public async Task EverythingFailingAtOnce_IsNotJournalled_NorToasted_AndTheTrayIsOnlyAWarning()
    {
        var (config, scheduler, servers, sites, services, events, toasts, state) = Make();

        Round(servers, sites, services, config, online: true);
        await scheduler.CheckAllNowAsync(config);                     // silent baseline: all fine

        for (var i = 0; i < 3; i++)                                   // three rounds of "everything is down"
        {
            Round(servers, sites, services, config, online: false);
            await scheduler.CheckAllNowAsync(config);
        }

        Assert.True(state.SuspectedLocalFault);
        Assert.True(state.AlarmsSuppressed);
        Assert.False(state.IsOffline);                                // internet itself is fine
        Assert.Empty(events.Added);
        Assert.Equal(0, toasts.CallCount);
        Assert.Equal(HealthLevel.Warning, ResourceHealthAggregator.ComputeOverall(config, scheduler, null, state));
    }

    [Fact]
    public async Task WhenEverythingComesBack_NothingWasLoggedSoThereIsNoOrphanRecoveryEither()
    {
        var (config, scheduler, servers, sites, services, events, toasts, state) = Make();

        Round(servers, sites, services, config, online: true);
        await scheduler.CheckAllNowAsync(config);
        for (var i = 0; i < 3; i++)
        {
            Round(servers, sites, services, config, online: false);
            await scheduler.CheckAllNowAsync(config);
        }

        for (var i = 0; i < 2; i++)
        {
            Round(servers, sites, services, config, online: true);
            await scheduler.CheckAllNowAsync(config);
        }

        Assert.False(state.SuspectedLocalFault);
        Assert.Empty(events.Added);                                   // no "снова в сети" for something never reported down
        Assert.Equal(0, toasts.CallCount);
        Assert.Equal(HealthLevel.Ok, ResourceHealthAggregator.ComputeOverall(config, scheduler, null, state));
    }

    [Fact]
    public async Task APartialRealOutage_StillAlarms()
    {
        var (config, scheduler, servers, sites, services, events, toasts, state) = Make();

        Round(servers, sites, services, config, online: true);
        await scheduler.CheckAllNowAsync(config);
        for (var i = 0; i < 2; i++)                                   // only 1 of each kind (3 of 9) fails: a real, local problem
        {
            Round(servers, sites, services, config, online: false, failOnly: 1);
            await scheduler.CheckAllNowAsync(config);
        }

        Assert.False(state.SuspectedLocalFault);
        Assert.NotEmpty(events.Added);
        Assert.True(toasts.CallCount > 0);
    }

    [Fact]
    public async Task TooFewResources_NeverCountAsAMassFailure()
    {
        var (config, scheduler, servers, sites, services, events, _, state) = Make(servers: 2, sites: 1, services: 1);   // 4 resources

        Round(servers, sites, services, config, online: true);
        await scheduler.CheckAllNowAsync(config);
        for (var i = 0; i < 2; i++)
        {
            Round(servers, sites, services, config, online: false);
            await scheduler.CheckAllNowAsync(config);
        }

        Assert.False(state.SuspectedLocalFault);
        Assert.NotEmpty(events.Added);                                // with so few, a total failure is plausibly real
    }

    // ---------- the state machine itself ----------

    [Fact]
    public void ThePassOfTimeEndsTheBenefitOfTheDoubt_ButNotTheSuspicion()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConnectivityState(() => now);

        state.ReportCheckSummary(total: 10, failing: 10);
        Assert.True(state.AlarmsSuppressed);

        now += ConnectivityState.MassFailureGrace + TimeSpan.FromMinutes(1);   // a lasting "outage" must surface after all
        Assert.False(state.AlarmsSuppressed);
        Assert.True(state.SuspectedLocalFault);
    }

    [Fact]
    public void Hysteresis_HoldsBetweenTheThresholds_AndClearsBelowTheLowOne()
    {
        var state = new ConnectivityState();
        state.ReportCheckSummary(10, 7);                              // 70 %: enters
        Assert.True(state.SuspectedLocalFault);

        state.ReportCheckSummary(10, 5);                              // 50 %: still suspected
        Assert.True(state.SuspectedLocalFault);

        state.ReportCheckSummary(10, 3);                              // 30 %: clears
        Assert.False(state.SuspectedLocalFault);
        Assert.False(state.AlarmsSuppressed);
    }

    [Fact]
    public void BelowTheMinimumCount_NeverTriggers()
    {
        var state = new ConnectivityState();
        state.ReportCheckSummary(ConnectivityState.MassFailureMinResources - 1, ConnectivityState.MassFailureMinResources - 1);

        Assert.False(state.SuspectedLocalFault);
    }

    [Fact]
    public void TheChangedEvent_FiresOnEntryAndExit_SoTheBannerFollows()
    {
        var state = new ConnectivityState();
        var fired = 0;
        state.Changed += (_, _) => fired++;

        state.ReportCheckSummary(10, 10);
        state.ReportCheckSummary(10, 10);                             // no change, no event
        state.ReportCheckSummary(10, 0);

        Assert.Equal(2, fired);
    }
}
