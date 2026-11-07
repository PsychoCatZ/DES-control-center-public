using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class ConnectivityTests
{
    private sealed class FakeProbe : IConnectivityProbe
    {
        public bool Reachable { get; set; } = true;

        public Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Reachable);
    }

    // ---------- monitor ----------

    [Fact]
    public async Task OneFailedProbe_IsNotAnOutage_ButTwoInARowAre()
    {
        var probe = new FakeProbe { Reachable = false };
        var state = new ConnectivityState();
        var monitor = new ConnectivityMonitor(probe, state);

        await monitor.ProbeOnceAsync();
        Assert.False(state.IsOffline);

        await monitor.ProbeOnceAsync();
        Assert.True(state.IsOffline);
    }

    [Fact]
    public async Task ASuccessBetweenFailures_ResetsTheCount()
    {
        var probe = new FakeProbe { Reachable = false };
        var state = new ConnectivityState();
        var monitor = new ConnectivityMonitor(probe, state);

        await monitor.ProbeOnceAsync();
        probe.Reachable = true;
        await monitor.ProbeOnceAsync();
        probe.Reachable = false;
        await monitor.ProbeOnceAsync();

        Assert.False(state.IsOffline);
    }

    [Fact]
    public async Task Recovery_RunsTheRecheckBeforeClearingOffline()
    {
        var probe = new FakeProbe { Reachable = false };
        var state = new ConnectivityState();
        bool? offlineDuringRecheck = null;
        var monitor = new ConnectivityMonitor(probe, state, () =>
        {
            offlineDuringRecheck = state.IsOffline;
            return Task.CompletedTask;
        });
        await monitor.ProbeOnceAsync();
        await monitor.ProbeOnceAsync();
        Assert.True(state.IsOffline);

        probe.Reachable = true;
        await monitor.ProbeOnceAsync();

        Assert.True(offlineDuringRecheck);   // re-check ran while still flagged offline
        Assert.False(state.IsOffline);       // and only then was the flag cleared
    }

    [Fact]
    public async Task AFailingRecheck_DoesNotKeepTheAppStuckOffline()
    {
        var probe = new FakeProbe { Reachable = false };
        var state = new ConnectivityState();
        var monitor = new ConnectivityMonitor(probe, state, () => throw new InvalidOperationException("boom"));
        await monitor.ProbeOnceAsync();
        await monitor.ProbeOnceAsync();

        probe.Reachable = true;
        await monitor.ProbeOnceAsync();

        Assert.False(state.IsOffline);
    }

    // ---------- tray colour ----------

    [Fact]
    public async Task WhileOffline_TheTrayIsAWarning_NotRed()
    {
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };
        var serverMonitor = new FakeServerMonitor();
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("timeout"));
        var state = new ConnectivityState();
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance, null, state);
        await scheduler.CheckServerNowAsync(server);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler, null, state));

        state.SetOffline(true);
        Assert.Equal(HealthLevel.Warning, ResourceHealthAggregator.ComputeOverall(config, scheduler, null, state));

        state.SetOffline(false);
        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler, null, state));
    }

    // ---------- no false alarms / journal entries ----------

    [Fact]
    public async Task FailuresWhileOffline_ProduceNoJournalEntryAndNoToast()
    {
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var state = new ConnectivityState();
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            eventStore, notifications, NullLogger<PollingScheduler>.Instance, null, state);

        await scheduler.CheckServerNowAsync(server);                 // baseline Online
        state.SetOffline(true);
        for (var i = 0; i < 4; i++)
        {
            serverMonitor.Responses.Enqueue(ServerStatus.Offline("timeout"));
            await scheduler.CheckServerNowAsync(server);
        }

        Assert.Empty(eventStore.Added);
        Assert.Equal(0, notifications.CallCount);
    }

    [Fact]
    public async Task ARealOutageStillAlarms_WhenWeAreOnline()
    {
        var server = new ServerResource { Name = "vps", Host = "203.0.113.10" };
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var state = new ConnectivityState();
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            eventStore, notifications, NullLogger<PollingScheduler>.Instance, null, state);

        await scheduler.CheckServerNowAsync(server);
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        await scheduler.CheckServerNowAsync(server);
        await scheduler.CheckServerNowAsync(server);

        Assert.Single(eventStore.Added);
        Assert.Equal(1, notifications.CallCount);
    }
}
