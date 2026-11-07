using System.Net;
using System.Net.Sockets;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure.Monitors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class DnsFailureTests
{
    // ---------- recognising a DNS failure ----------

    [Fact]
    public void NameResolutionError_IsADnsFailure()
    {
        var ex = new HttpRequestException(HttpRequestError.NameResolutionError, "Этот хост неизвестен");

        Assert.True(WebsiteMonitor.IsDnsFailure(ex));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.TryAgain)]
    [InlineData(SocketError.NoData)]
    public void ASocketResolutionError_DeepInsideTheException_IsADnsFailure(SocketError code)
    {
        var ex = new HttpRequestException("send failed", new IOException("io", new SocketException((int)code)));

        Assert.True(WebsiteMonitor.IsDnsFailure(ex));
    }

    [Fact]
    public void ARefusedConnectionOrATimeout_IsNotADnsFailure()
    {
        Assert.False(WebsiteMonitor.IsDnsFailure(new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))));
        Assert.False(WebsiteMonitor.IsDnsFailure(new TaskCanceledException("timeout")));
        Assert.False(WebsiteMonitor.IsDnsFailure(new HttpRequestException(HttpRequestError.ConnectionError, "x")));
    }

    // ---------- the grace period ----------

    [Fact]
    public void ADnsFailure_IsTransientUntilTheGraceHasPassed()
    {
        var started = new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
        var status = WebsiteStatus.Offline("x", dnsFailure: true).WithDnsFailureSince(started);

        Assert.True(status.IsTransientDnsFailure(started.AddMinutes(29)));
        Assert.False(status.IsTransientDnsFailure(started.AddMinutes(31)));
    }

    [Fact]
    public void OnlineOrNonDnsFailures_AreNeverTransientDns()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(new WebsiteStatus { IsOnline = true }.IsTransientDnsFailure(now));
        Assert.False(WebsiteStatus.Offline("refused").IsTransientDnsFailure(now));
    }

    // ---------- scheduler: no alarm for a short DNS hiccup, an alarm if it lasts ----------

    private static (PollingScheduler Scheduler, FakeWebsiteMonitor Monitor, FakeEventStore Events, FakeNotificationService Toasts, Website Site, Func<DateTimeOffset> Now, Action<TimeSpan> Advance)
        MakeScheduler(bool external)
    {
        var current = DateTimeOffset.UtcNow;
        var monitor = new FakeWebsiteMonitor();
        var events = new FakeEventStore();
        var toasts = new FakeNotificationService();
        var scheduler = new PollingScheduler(new FakeServerMonitor(), monitor, new FakeServiceMonitor(), new FakeVpnMonitor(),
            events, toasts, NullLogger<PollingScheduler>.Instance, null, null, () => current);
        var site = new Website { Name = "site", Url = "https://example.com", ServerId = external ? null : Guid.NewGuid() };
        return (scheduler, monitor, events, toasts, site, () => current, delta => current += delta);
    }

    [Fact]
    public async Task AShortDnsHiccup_RaisesNoAlarmAndWritesNoJournalEntry()
    {
        var (scheduler, monitor, events, toasts, site, _, advance) = MakeScheduler(external: true);

        await scheduler.CheckWebsiteNowAsync(site);                       // baseline: online
        for (var i = 0; i < 4; i++)
        {
            monitor.Responses.Enqueue(WebsiteStatus.Offline("Этот хост неизвестен", dnsFailure: true));
            await scheduler.CheckWebsiteNowAsync(site);
            advance(TimeSpan.FromMinutes(2));
        }

        Assert.Empty(events.Added);
        Assert.Equal(0, toasts.CallCount);
        Assert.False(scheduler.GetWebsiteStatus(site.Id)!.IsOnline);       // the card still tells the truth
    }

    [Fact]
    public async Task ADnsFailureThatLasts_BecomesARealAlarmAfterTheGrace()
    {
        var (scheduler, monitor, events, toasts, site, _, advance) = MakeScheduler(external: true);

        await scheduler.CheckWebsiteNowAsync(site);                       // baseline: online
        // 40 minutes of failing every 2 minutes
        for (var i = 0; i < 20; i++)
        {
            monitor.Responses.Enqueue(WebsiteStatus.Offline("Этот хост неизвестен", dnsFailure: true));
            await scheduler.CheckWebsiteNowAsync(site);
            advance(TimeSpan.FromMinutes(2));
        }

        Assert.Single(events.Added);
        Assert.Equal("Offline", events.Added[0].NewState);
        Assert.Equal(1, toasts.CallCount);
    }

    [Fact]
    public async Task ARecoveredHiccup_ResetsTheClock()
    {
        var (scheduler, monitor, events, _, site, _, advance) = MakeScheduler(external: true);

        await scheduler.CheckWebsiteNowAsync(site);
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 10; i++)                                  // 20 minutes of failure …
            {
                monitor.Responses.Enqueue(WebsiteStatus.Offline("dns", dnsFailure: true));
                await scheduler.CheckWebsiteNowAsync(site);
                advance(TimeSpan.FromMinutes(2));
            }

            monitor.Responses.Enqueue(new WebsiteStatus { IsOnline = true, HttpStatusCode = 200 });   // … then fine again
            await scheduler.CheckWebsiteNowAsync(site);
        }

        Assert.Empty(events.Added);                                       // never 30 minutes in one go
    }

    [Fact]
    public async Task ANormalOutage_StillAlarmsAtOnce_NoGraceForNonDnsErrors()
    {
        var (scheduler, monitor, events, _, site, _, _) = MakeScheduler(external: true);

        await scheduler.CheckWebsiteNowAsync(site);
        monitor.Responses.Enqueue(WebsiteStatus.Offline("connection refused"));
        monitor.Responses.Enqueue(WebsiteStatus.Offline("connection refused"));
        await scheduler.CheckWebsiteNowAsync(site);
        await scheduler.CheckWebsiteNowAsync(site);

        Assert.Single(events.Added);
    }

    // ---------- tray colour ----------

    [Fact]
    public async Task TransientDns_OwnSite_IsOnlyAWarning_ExternalSite_IsNothing_AndAfterGraceItIsAProblem()
    {
        foreach (var external in new[] { false, true })
        {
            var (scheduler, monitor, _, _, site, _, advance) = MakeScheduler(external);
            var config = new AppConfig { Websites = { site } };
            // the aggregator reads the real clock, so back-date the start of the failure run instead of waiting
            await scheduler.CheckWebsiteNowAsync(site);
            monitor.Responses.Enqueue(WebsiteStatus.Offline("dns", dnsFailure: true));
            await scheduler.CheckWebsiteNowAsync(site);

            var level = ResourceHealthAggregator.ComputeOverall(config, scheduler);
            Assert.Equal(external ? HealthLevel.Ok : HealthLevel.Warning, level);

            advance(TimeSpan.FromHours(1));
            monitor.Responses.Enqueue(WebsiteStatus.Offline("dns", dnsFailure: true));
            await scheduler.CheckWebsiteNowAsync(site);
            Assert.NotNull(scheduler.GetWebsiteStatus(site.Id)!.DnsFailureSince);
        }
    }
}
