using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class SilenceTests
{
    private sealed class MemoryStore : ISilenceStore
    {
        public List<Silence> Saved { get; set; } = new();
        public int SaveCount { get; private set; }

        public IReadOnlyList<Silence> Load() => Saved.ToList();

        public void Save(IReadOnlyCollection<Silence> silences)
        {
            SaveCount++;
            Saved = silences.ToList();
        }
    }

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    }

    private static (SilenceRegistry Registry, MemoryStore Store, Clock Clock) Make()
    {
        var store = new MemoryStore();
        var clock = new Clock();
        return (new SilenceRegistry(store, () => clock.Now), store, clock);
    }

    // ---------- registry ----------

    [Fact]
    public void Silence_Forever_StaysUntilLifted()
    {
        var (registry, _, clock) = Make();
        var id = Guid.NewGuid();

        registry.Silence(id, SilenceMode.Forever);
        clock.Now = clock.Now.AddDays(400);

        Assert.True(registry.IsSilenced(id));
        registry.Unsilence(id);
        Assert.False(registry.IsSilenced(id));
    }

    [Fact]
    public void Silence_Timed_LapsesWhenTheTimeComes()
    {
        var (registry, _, clock) = Make();
        var id = Guid.NewGuid();

        registry.Silence(id, SilenceMode.Until, clock.Now.AddHours(1));

        Assert.True(registry.IsSilenced(id));
        clock.Now = clock.Now.AddMinutes(59);
        Assert.True(registry.IsSilenced(id));
        clock.Now = clock.Now.AddMinutes(2);
        Assert.False(registry.IsSilenced(id));
    }

    [Fact]
    public void PurgeExpired_DropsOnlyLapsedOnes_AndRaisesChanged()
    {
        var (registry, store, clock) = Make();
        var lapsed = Guid.NewGuid();
        var permanent = Guid.NewGuid();
        registry.Silence(lapsed, SilenceMode.Until, clock.Now.AddMinutes(5));
        registry.Silence(permanent, SilenceMode.Forever);
        var changes = 0;
        registry.Changed += (_, _) => changes++;

        Assert.False(registry.PurgeExpired());
        clock.Now = clock.Now.AddMinutes(10);
        Assert.True(registry.PurgeExpired());

        Assert.Equal(1, changes);
        Assert.Single(store.Saved);
        Assert.Equal(permanent, store.Saved[0].ResourceId);
    }

    [Fact]
    public void UntilRecovery_IsLiftedByRecovery_ButTimedAndForeverAreNot()
    {
        var (registry, _, clock) = Make();
        var untilRecovery = Guid.NewGuid();
        var timed = Guid.NewGuid();
        var forever = Guid.NewGuid();
        registry.Silence(untilRecovery, SilenceMode.UntilRecovery);
        registry.Silence(timed, SilenceMode.Until, clock.Now.AddHours(8));
        registry.Silence(forever, SilenceMode.Forever);

        registry.NotifyRecovered(untilRecovery);
        registry.NotifyRecovered(timed);
        registry.NotifyRecovered(forever);

        Assert.False(registry.IsSilenced(untilRecovery));
        Assert.True(registry.IsSilenced(timed));
        Assert.True(registry.IsSilenced(forever));
    }

    [Fact]
    public void SilencedServer_SilencesWhatRunsOnIt_ButNotTheOtherWayRound()
    {
        var (registry, _, _) = Make();
        var server = Guid.NewGuid();
        var service = Guid.NewGuid();

        registry.Silence(server, SilenceMode.Forever);
        Assert.True(registry.IsSilenced(service, server));
        Assert.Null(registry.Get(service));            // not silenced on its own row

        registry.UnsilenceAll();
        registry.Silence(service, SilenceMode.Forever);
        Assert.False(registry.IsSilenced(server));
    }

    [Fact]
    public void Silences_SurviveARestart_ViaTheStore()
    {
        var (registry, store, clock) = Make();
        var id = Guid.NewGuid();
        registry.Silence(id, SilenceMode.Until, clock.Now.AddHours(3));

        var restarted = new SilenceRegistry(store, () => clock.Now);

        Assert.True(restarted.IsSilenced(id));
        Assert.Equal(1, restarted.ActiveCount);
    }

    // ---------- tray colour ----------

    private static async Task<(AppConfig Config, PollingScheduler Scheduler, ServerResource Server, ServiceResource Service)> FailedServiceOnHealthyServerAsync(SilenceRegistry? silences = null)
    {
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var service = new ServiceResource { DisplayName = "bot", UnitName = "bot.service", ServerId = server.Id };
        var config = new AppConfig { Servers = { server }, Services = { service } };
        var serviceMonitor = new FakeServiceMonitor();
        serviceMonitor.Responses.Enqueue(new ServiceStatus { State = ServiceState.Failed });
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), serviceMonitor, new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance, silences);
        await scheduler.CheckServiceNowAsync(server, service);
        return (config, scheduler, server, service);
    }

    [Fact]
    public async Task SilencedProblem_NoLongerTurnsTheTrayRed_UntilItIsLifted()
    {
        var (registry, _, _) = Make();
        var (config, scheduler, _, service) = await FailedServiceOnHealthyServerAsync(registry);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler, registry));

        registry.Silence(service.Id, SilenceMode.UntilRecovery);
        Assert.Equal(HealthLevel.Ok, ResourceHealthAggregator.ComputeOverall(config, scheduler, registry));

        registry.Unsilence(service.Id);
        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler, registry));
    }

    [Fact]
    public async Task SilencedServer_QuietsItsServicesToo()
    {
        var (registry, _, _) = Make();
        var (config, scheduler, server, _) = await FailedServiceOnHealthyServerAsync(registry);

        registry.Silence(server.Id, SilenceMode.Forever);

        Assert.Equal(HealthLevel.Ok, ResourceHealthAggregator.ComputeOverall(config, scheduler, registry));
    }

    [Fact]
    public async Task OneSilencedProblem_DoesNotHideAnotherUnsilencedOne()
    {
        var (registry, _, _) = Make();
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var quiet = new ServiceResource { DisplayName = "quiet", UnitName = "a.service", ServerId = server.Id };
        var loud = new ServiceResource { DisplayName = "loud", UnitName = "b.service", ServerId = server.Id };
        var config = new AppConfig { Servers = { server }, Services = { quiet, loud } };
        var serviceMonitor = new FakeServiceMonitor();
        serviceMonitor.Responses.Enqueue(new ServiceStatus { State = ServiceState.Failed });
        serviceMonitor.Responses.Enqueue(new ServiceStatus { State = ServiceState.Failed });
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), serviceMonitor, new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance, registry);
        await scheduler.CheckServiceNowAsync(server, quiet);
        await scheduler.CheckServiceNowAsync(server, loud);

        registry.Silence(quiet.Id, SilenceMode.Forever);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler, registry));
    }

    // ---------- toasts ----------

    [Fact]
    public async Task SilencedResource_StillJournalsTheTransition_ButShowsNoToast()
    {
        var (registry, _, _) = Make();
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            eventStore, notifications, NullLogger<PollingScheduler>.Instance, registry);
        registry.Silence(server.Id, SilenceMode.Forever);

        // baseline Online, then two Offline checks in a row = a confirmed transition (debounce is 2)
        await scheduler.CheckServerNowAsync(server);
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        await scheduler.CheckServerNowAsync(server);
        await scheduler.CheckServerNowAsync(server);

        Assert.Single(eventStore.Added);
        Assert.Equal(0, notifications.CallCount);
    }

    [Fact]
    public async Task UntilRecovery_IsLiftedOnRecovery_AndTheBackToNormalToastStillShows()
    {
        var (registry, _, _) = Make();
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var serverMonitor = new FakeServerMonitor();
        var notifications = new FakeNotificationService();
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), notifications, NullLogger<PollingScheduler>.Instance, registry);

        await scheduler.CheckServerNowAsync(server);                          // baseline Online
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        await scheduler.CheckServerNowAsync(server);
        await scheduler.CheckServerNowAsync(server);                          // confirmed Offline → toast #1
        Assert.Equal(1, notifications.CallCount);

        registry.Silence(server.Id, SilenceMode.UntilRecovery);
        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);
        await scheduler.CheckServerNowAsync(server);                          // confirmed Online

        Assert.False(registry.IsSilenced(server.Id));
        Assert.Equal(2, notifications.CallCount);                             // "снова в сети" is shown
    }
}
