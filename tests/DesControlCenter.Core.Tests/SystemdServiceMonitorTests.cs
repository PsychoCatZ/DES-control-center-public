using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Infrastructure.Monitors;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class SystemdServiceMonitorTests
{
    private sealed class FakeSshCommandRunner : ISshCommandRunner
    {
        public required string StandardOutput { get; init; }
        public int ExitCode { get; init; }
        public string? LastCommand { get; private set; }

        public Task<SshCommandResult> RunAsync(ServerResource server, string remoteCommand, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCommand = remoteCommand;
            return Task.FromResult(new SshCommandResult { ExitCode = ExitCode, StandardOutput = StandardOutput, StandardError = string.Empty });
        }
    }

    private static ServerResource Server() => new() { Name = "vps-1", Host = "203.0.113.10" };
    private static ServiceResource Service(string unit) => new() { DisplayName = unit, UnitName = unit, ServerId = Guid.NewGuid() };

    [Fact]
    public async Task NotFoundUnit_ReportsUnknown_NotStopped()
    {
        // Real systemd behavior: "systemctl show" on a nonexistent unit still exits 0 and
        // prints ActiveState=inactive — LoadState=not-found is the only way to tell it apart
        // from a genuinely, deliberately stopped service.
        var runner = new FakeSshCommandRunner { StandardOutput = "not-found\ninactive\n" };
        var monitor = new SystemdServiceMonitor(runner);

        var status = await monitor.CheckAsync(Server(), Service("typo-unit.service"));

        Assert.Equal(ServiceState.Unknown, status.State);
        Assert.Contains("not found", status.LastError);
    }

    [Theory]
    [InlineData("loaded\nactive\n", ServiceState.Running)]
    [InlineData("loaded\nfailed\n", ServiceState.Failed)]
    [InlineData("loaded\ninactive\n", ServiceState.Stopped)]
    public async Task LoadedUnit_MapsActiveStateCorrectly(string output, ServiceState expected)
    {
        var runner = new FakeSshCommandRunner { StandardOutput = output };
        var monitor = new SystemdServiceMonitor(runner);

        var status = await monitor.CheckAsync(Server(), Service("real.service"));

        Assert.Equal(expected, status.State);
    }

    [Fact]
    public async Task ActiveEnterTimestamp_InUtc_IsParsed()
    {
        var runner = new FakeSshCommandRunner { StandardOutput = "loaded\nactive\nMon 2026-09-28 16:41:26 UTC\n" };
        var monitor = new SystemdServiceMonitor(runner);

        var status = await monitor.CheckAsync(Server(), Service("real.service"));

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 16, 41, 26, TimeSpan.Zero), status.ActiveSinceUtc);
    }

    [Fact]
    public async Task NeverStartedUnit_HasNoActiveSince()
    {
        // A unit that has never been active prints an empty timestamp line.
        var runner = new FakeSshCommandRunner { StandardOutput = "loaded\ninactive\n\n" };
        var monitor = new SystemdServiceMonitor(runner);

        var status = await monitor.CheckAsync(Server(), Service("real.service"));

        Assert.Null(status.ActiveSinceUtc);
    }

    [Fact]
    public async Task Command_ForcesUtc_SoServersInOtherTimezonesParse()
    {
        // Regression: Borealis printed "... CEST" and Atlas "... MSK", which the parser (it
        // only understands "UTC") silently turned into "no restart time". Only the server-side
        // TZ=UTC prefix keeps that from coming back.
        var runner = new FakeSshCommandRunner { StandardOutput = "loaded\nactive\n\n" };
        var monitor = new SystemdServiceMonitor(runner);

        await monitor.CheckAsync(Server(), Service("real.service"));

        Assert.StartsWith("TZ=UTC ", runner.LastCommand);
    }
}
