using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class ResourceHealthAggregatorTests
{
    [Fact]
    public async Task AllOnline_IsOk()
    {
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };

        var serverMonitor = new FakeServerMonitor();
        serverMonitor.Responses.Enqueue(new ServerStatus { IsOnline = true });
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckServerNowAsync(server);

        Assert.Equal(HealthLevel.Ok, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }

    [Fact]
    public async Task OfflineServer_IsProblem()
    {
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };

        var serverMonitor = new FakeServerMonitor();
        serverMonitor.Responses.Enqueue(ServerStatus.Offline("down"));
        var scheduler = new PollingScheduler(serverMonitor, new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckServerNowAsync(server);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }

    [Fact]
    public async Task WebsiteSslExpiringSoon_IsWarning_NotProblem()
    {
        // ServerId set: this is one of our own sites, not an External Services entry — those are
        // deliberately binary and never warn on SSL (see WebsiteSslExpiringSoon_ForExternalService_IsStillOk).
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };
        var website = new Website { Name = "site", Url = "https://example.com", ServerId = server.Id, SslWarningDays = 14 };
        var config = new AppConfig { Servers = { server }, Websites = { website } };

        var websiteMonitor = new FakeWebsiteMonitor();
        websiteMonitor.Responses.Enqueue(new WebsiteStatus
        {
            IsOnline = true,
            HttpStatusCode = 200,
            SslExpiresUtc = DateTimeOffset.UtcNow.AddDays(5)
        });
        var scheduler = new PollingScheduler(new FakeServerMonitor(), websiteMonitor, new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckWebsiteNowAsync(website);

        Assert.Equal(HealthLevel.Warning, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }

    [Fact]
    public async Task ExternalServiceWithExpiringSslOr4xx_IsStillOk()
    {
        // ServerId left null: an External Services entry (ChatGPT/Assistant/Telegram/...), not one
        // of our own sites. A 403 there just confirms the endpoint is alive, and its SSL cert
        // isn't ours to warn about — this must stay Ok, not tip the tray icon yellow.
        var website = new Website { Name = "ChatGPT", Url = "https://chatgpt.com/", SslWarningDays = 14 };
        var config = new AppConfig { Websites = { website } };

        var websiteMonitor = new FakeWebsiteMonitor();
        websiteMonitor.Responses.Enqueue(new WebsiteStatus
        {
            IsOnline = true,
            HttpStatusCode = 403,
            SslExpiresUtc = DateTimeOffset.UtcNow.AddDays(5)
        });
        var scheduler = new PollingScheduler(new FakeServerMonitor(), websiteMonitor, new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckWebsiteNowAsync(website);

        Assert.Equal(HealthLevel.Ok, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }

    [Fact]
    public async Task ExternalServiceUnreachable_IsProblem()
    {
        var website = new Website { Name = "ChatGPT", Url = "https://chatgpt.com/" };
        var config = new AppConfig { Websites = { website } };

        var websiteMonitor = new FakeWebsiteMonitor();
        websiteMonitor.Responses.Enqueue(WebsiteStatus.Offline("connection refused"));
        var scheduler = new PollingScheduler(new FakeServerMonitor(), websiteMonitor, new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckWebsiteNowAsync(website);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }

    [Fact]
    public async Task FailedService_IsProblem()
    {
        var server = new ServerResource { Name = "vps-1", Host = "203.0.113.10" };
        var service = new ServiceResource { DisplayName = "Notifier", UnitName = "notifier.service", ServerId = server.Id };
        var config = new AppConfig { Servers = { server }, Services = { service } };

        var serviceMonitor = new FakeServiceMonitor();
        serviceMonitor.Responses.Enqueue(new ServiceStatus { State = ServiceState.Failed });
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), serviceMonitor, new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        await scheduler.CheckServiceNowAsync(server, service);

        Assert.Equal(HealthLevel.Problem, ResourceHealthAggregator.ComputeOverall(config, scheduler));
    }
}
