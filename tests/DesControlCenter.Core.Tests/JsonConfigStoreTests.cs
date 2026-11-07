using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;
using DesControlCenter.Infrastructure.Persistence;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public async Task ServiceKind_RoundTrips_AndOldConfigsDefaultToSystemd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"des-cc-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            var server = new ServerResource { Name = "s", Host = "203.0.113.10" };
            var original = new AppConfig
            {
                Servers = { server },
                Services =
                {
                    new ServiceResource { DisplayName = "App", UnitName = "assistant-app-app-1", ServerId = server.Id, Kind = ServiceKind.DockerContainer, UseSudo = true }
                }
            };

            await store.SaveAsync(original);
            var loaded = await store.LoadAsync();
            var service = Assert.Single(loaded.Services);
            Assert.Equal(ServiceKind.DockerContainer, service.Kind);
            Assert.True(service.UseSudo);
            Assert.True(service.IsDocker);
            Assert.Contains("\"DockerContainer\"", await File.ReadAllTextAsync(path));

            // A config written before Docker support has no Kind/UseSudo at all.
            await File.WriteAllTextAsync(path, "{\"Services\":[{\"UnitName\":\"nginx.service\",\"DisplayName\":\"Nginx\"}]}");
            var old = Assert.Single((await store.LoadAsync()).Services);
            Assert.Equal(ServiceKind.Systemd, old.Kind);
            Assert.False(old.UseSudo);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Launchers_RoundTrip_AndOldConfigsHaveNone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"des-cc-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            var original = new AppConfig
            {
                Launchers =
                {
                    new LauncherLink { Title = "Example Hosting", Subtitle = "Atlas", DesktopAppName = "Hosting Console", Url = "https://example.com", Group = LauncherGroups.Provider },
                    new LauncherLink { Title = "OpenAI", DesktopAppName = "OpenAI Platform", Url = "https://example.org", Group = LauncherGroups.Api, Enabled = false },
                    new LauncherLink { Title = "Local agent", DesktopAppName = "Agent", Url = "https://example.net", Group = LauncherGroups.Ai }
                }
            };

            await store.SaveAsync(original);
            var loaded = await store.LoadAsync();
            Assert.Equal(3, loaded.Launchers.Count);
            Assert.Equal("Ai", loaded.Launchers[2].Group);
            Assert.Equal("Hosting Console", loaded.Launchers[0].DesktopAppName);
            Assert.Equal(LauncherGroups.Api, loaded.Launchers[1].Group);
            Assert.False(loaded.Launchers[1].Enabled);

            // A config written before launchers existed simply has none.
            await File.WriteAllTextAsync(path, "{\"Projects\":[]}");
            Assert.Empty((await store.LoadAsync()).Launchers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsEverything()
    {
        var path = Path.Combine(Path.GetTempPath(), $"des-cc-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            var project = new Project { Name = "Notifier" };
            var server = new ServerResource { Name = "Atlas", Host = "203.0.113.10", ProjectId = project.Id };
            var website = new Website { Name = "site", Url = "https://example.com", ProjectId = project.Id };
            var service = new ServiceResource { DisplayName = "Bot", UnitName = "bot.service", ServerId = server.Id };

            var original = new AppConfig
            {
                Projects = { project },
                Servers = { server },
                Websites = { website },
                Services = { service }
            };
            original.Settings.StartWithWindows = true;

            await store.SaveAsync(original);
            var loaded = await store.LoadAsync();

            var loadedServer = Assert.Single(loaded.Servers);
            Assert.Equal(server.Name, loadedServer.Name);
            Assert.Equal(server.Host, loadedServer.Host);
            Assert.Equal(project.Id, loadedServer.ProjectId);
            Assert.True(loaded.Settings.StartWithWindows);
            Assert.Single(loaded.Websites);
            Assert.Single(loaded.Services);
            Assert.Single(loaded.Projects);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_WithNoFile_ReturnsEmptyConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"des-cc-test-{Guid.NewGuid():N}.json");
        var store = new JsonConfigStore(path);

        var config = await store.LoadAsync();

        Assert.Empty(config.Servers);
        Assert.Empty(config.Websites);
        Assert.Empty(config.Services);
        Assert.Empty(config.Projects);
    }
}
