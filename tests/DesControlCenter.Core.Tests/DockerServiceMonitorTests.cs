using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure;
using DesControlCenter.Infrastructure.Control;
using DesControlCenter.Infrastructure.Monitors;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class DockerServiceMonitorTests
{
    private sealed class FakeSsh : ISshCommandRunner
    {
        public string StandardOutput { get; init; } = string.Empty;
        public int ExitCode { get; init; }
        public string? LastCommand { get; private set; }

        public Task<SshCommandResult> RunAsync(ServerResource server, string remoteCommand, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCommand = remoteCommand;
            return Task.FromResult(new SshCommandResult { ExitCode = ExitCode, StandardOutput = StandardOutput, StandardError = string.Empty });
        }
    }

    private static ServerResource Server() => new() { Name = "cedar", Host = "203.0.113.10" };
    private static ServiceResource Container(string name = "assistant-app-app-1", bool sudo = false) =>
        new() { DisplayName = name, UnitName = name, Kind = ServiceKind.DockerContainer, UseSudo = sudo, ServerId = Guid.NewGuid() };

    [Theory]
    [InlineData("running|healthy|2026-10-08T14:51:20.422712281Z|0", ServiceState.Running)]
    [InlineData("running|none|2026-10-08T14:51:20.422712281Z|0", ServiceState.Running)]
    [InlineData("running|starting|2026-10-08T14:51:20.422712281Z|0", ServiceState.Running)]
    [InlineData("running|unhealthy|2026-10-08T14:51:20.422712281Z|0", ServiceState.Failed)]
    [InlineData("restarting|none|2026-10-08T14:51:20.422712281Z|1", ServiceState.Failed)]
    [InlineData("exited|none|2026-10-08T14:51:20.422712281Z|1", ServiceState.Failed)]
    [InlineData("dead|none|2026-10-08T14:51:20.422712281Z|0", ServiceState.Failed)]
    [InlineData("exited|none|2026-10-08T14:51:20.422712281Z|0", ServiceState.Stopped)]
    [InlineData("created|none|0001-01-01T00:00:00Z|0", ServiceState.Stopped)]
    [InlineData("paused|none|2026-10-08T14:51:20.422712281Z|0", ServiceState.Stopped)]
    public async Task ContainerState_MapsToServiceState(string output, ServiceState expected)
    {
        var monitor = new DockerServiceMonitor(new FakeSsh { StandardOutput = output + "\n" });

        var status = await monitor.CheckAsync(Server(), Container());

        Assert.Equal(expected, status.State);
    }

    [Fact]
    public async Task StartedAt_WithNineFractionDigits_IsParsedAsUtc()
    {
        var monitor = new DockerServiceMonitor(new FakeSsh { StandardOutput = "running|healthy|2026-10-08T14:51:20.422712281Z|0\n" });

        var status = await monitor.CheckAsync(Server(), Container());

        Assert.Equal(new DateTimeOffset(2026, 10, 8, 14, 51, 20, TimeSpan.Zero).AddTicks(4227122), status.ActiveSinceUtc);
    }

    [Fact]
    public async Task NeverStartedContainer_HasNoActiveSince()
    {
        var monitor = new DockerServiceMonitor(new FakeSsh { StandardOutput = "created|none|0001-01-01T00:00:00Z|0\n" });

        var status = await monitor.CheckAsync(Server(), Container());

        Assert.Null(status.ActiveSinceUtc);
    }

    [Fact]
    public async Task MissingContainer_IsUnknown_NotStopped()
    {
        var monitor = new DockerServiceMonitor(new FakeSsh { ExitCode = 1, StandardOutput = "Error: No such object: typo-name\n" });

        var status = await monitor.CheckAsync(Server(), Container("typo-name"));

        Assert.Equal(ServiceState.Unknown, status.State);
        Assert.Contains("не найден", status.LastError);
    }

    [Fact]
    public async Task UnhealthyContainer_ExplainsWhy()
    {
        var monitor = new DockerServiceMonitor(new FakeSsh { StandardOutput = "running|unhealthy|2026-10-08T14:51:20Z|0\n" });

        var status = await monitor.CheckAsync(Server(), Container());

        Assert.Contains("healthcheck", status.LastError);
    }

    [Fact]
    public async Task Command_UsesSudoOnlyWhenAsked()
    {
        var plain = new FakeSsh { StandardOutput = "running|none|2026-10-08T14:51:20Z|0\n" };
        await new DockerServiceMonitor(plain).CheckAsync(Server(), Container(sudo: false));
        Assert.StartsWith("docker inspect ", plain.LastCommand);

        var sudo = new FakeSsh { StandardOutput = "running|none|2026-10-08T14:51:20Z|0\n" };
        await new DockerServiceMonitor(sudo).CheckAsync(Server(), Container(sudo: true));
        Assert.StartsWith("sudo -n docker inspect ", sudo.LastCommand);
    }

    [Fact]
    public async Task Controller_RestartsTheNamedContainer()
    {
        var ssh = new FakeSsh();
        var controller = new DockerServiceController(ssh);

        var outcome = await controller.RestartAsync(Server(), Container("assistant-app-app-1", sudo: true));

        Assert.True(outcome.Success);
        Assert.Equal("sudo -n docker restart 'assistant-app-app-1' 2>&1", ssh.LastCommand);
    }

    [Fact]
    public async Task LogReader_TailsTheContainerWithTimestamps()
    {
        var ssh = new FakeSsh { StandardOutput = "line\n" };
        var reader = new DockerLogReader(ssh);

        await reader.GetRecentLogsAsync(Server(), Container(), 150);

        Assert.Equal("docker logs --tail 150 --timestamps 'assistant-app-app-1' 2>&1", ssh.LastCommand);
    }

    [Fact]
    public async Task Router_SendsEachKindToItsOwnMonitor()
    {
        var systemdSsh = new FakeSsh { StandardOutput = "loaded\nactive\n\n" };
        var dockerSsh = new FakeSsh { StandardOutput = "running|healthy|2026-10-08T14:51:20Z|0\n" };
        var router = new ServiceMonitorRouter(new SystemdServiceMonitor(systemdSsh), new DockerServiceMonitor(dockerSsh));

        await router.CheckAsync(Server(), new ServiceResource { UnitName = "nginx.service", ServerId = Guid.NewGuid() });
        await router.CheckAsync(Server(), Container());

        Assert.Contains("systemctl show", systemdSsh.LastCommand);
        Assert.Contains("docker inspect", dockerSsh.LastCommand);
    }
}
