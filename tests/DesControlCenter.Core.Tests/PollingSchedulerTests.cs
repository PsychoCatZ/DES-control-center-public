using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class PollingSchedulerTests
{
    private static PollingScheduler CreateScheduler(
        FakeServerMonitor serverMonitor,
        FakeEventStore eventStore,
        FakeNotificationService notifications) =>
        new(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(), eventStore, notifications, NullLogger<PollingScheduler>.Instance);

    [Fact]
    public async Task FirstCheck_DoesNotLogOrNotify_EvenWhenOffline()
    {
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = CreateScheduler(serverMonitor, eventStore, notifications);
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };

        serverMonitor.Responses.Enqueue(ServerStatus.Offline("connection refused"));
        await scheduler.CheckServerNowAsync(server);

        Assert.Empty(eventStore.Added);
        Assert.Equal(0, notifications.CallCount);
        Assert.False(scheduler.GetServerStatus(server.Id)!.IsOnline);
    }

    [Fact]
    public async Task OnlineToOffline_Transition_LogsEventAndNotifies()
    {
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = CreateScheduler(serverMonitor, eventStore, notifications);
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };

        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);

        // A transition needs two consecutive polls agreeing on the new state before it's
        // confirmed (debounced) — see SingleBlip_DoesNotLogOrNotify below for the other half.
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("timeout"));
        await scheduler.CheckServerNowAsync(server);
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("timeout"));
        await scheduler.CheckServerNowAsync(server);

        var entry = Assert.Single(eventStore.Added);
        Assert.Equal("Online", entry.OldState);
        Assert.Equal("Offline", entry.NewState);
        Assert.Equal(1, notifications.CallCount);
    }

    [Fact]
    public async Task SingleBlip_DoesNotLogOrNotify()
    {
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = CreateScheduler(serverMonitor, eventStore, notifications);
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };

        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);

        // One bad poll, then it recovers on the very next one — never confirmed, so silent.
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("one dropped packet"));
        await scheduler.CheckServerNowAsync(server);
        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);

        Assert.Empty(eventStore.Added);
        Assert.Equal(0, notifications.CallCount);
        Assert.True(scheduler.GetServerStatus(server.Id)!.IsOnline);
    }

    [Fact]
    public async Task RepeatedSameState_DoesNotLogAgain()
    {
        var serverMonitor = new FakeServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = CreateScheduler(serverMonitor, eventStore, notifications);
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };

        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);
        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        await scheduler.CheckServerNowAsync(server);

        Assert.Empty(eventStore.Added);
        Assert.Equal(0, notifications.CallCount);
    }

    [Fact]
    public async Task OneServerFailing_DoesNotPreventOthersFromBeingChecked()
    {
        var throwingMonitor = new ThrowingServerMonitor();
        var eventStore = new FakeEventStore();
        var notifications = new FakeNotificationService();
        var scheduler = new PollingScheduler(throwingMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            eventStore, notifications, NullLogger<PollingScheduler>.Instance);

        var brokenServer = new ServerResource { Name = "broken", Host = "192.0.2.10" };
        var healthyServer = new ServerResource { Name = "healthy", Host = "192.0.2.20" };

        await scheduler.CheckServerNowAsync(brokenServer);
        await scheduler.CheckServerNowAsync(healthyServer);

        Assert.False(scheduler.GetServerStatus(brokenServer.Id)!.IsOnline);
        Assert.True(scheduler.GetServerStatus(healthyServer.Id)!.IsOnline);
    }

    private sealed class ThrowingServerMonitor : IServerMonitor
    {
        public Task<ServerStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default)
        {
            if (server.Name == "broken")
            {
                throw new InvalidOperationException("simulated SSH failure");
            }

            return Task.FromResult(new ServerStatus { IsOnline = true });
        }
    }
}
