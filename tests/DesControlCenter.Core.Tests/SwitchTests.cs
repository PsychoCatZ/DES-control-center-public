using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure.Control;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class SwitchTests
{
    private sealed class FakeSsh : ISshCommandRunner
    {
        public List<string> Commands { get; } = new();
        public SshCommandResult Next { get; set; } = new() { ExitCode = 0, StandardOutput = "", StandardError = "" };
        public Exception? Throw { get; set; }

        public Task<SshCommandResult> RunAsync(ServerResource server, string remoteCommand, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Commands.Add(remoteCommand);
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Next);
        }
    }

    private static SwitchResource Brain() => new()
    {
        DisplayName = "Режим ассистента",
        StatusCommand = "tool --status",
        OnCommand = "tool --on",
        OffCommand = "tool --off",
        OnValue = "assistant",
        OffValue = "stub",
        OnText = "Assistant",
        OffText = "заглушка"
    };

    private static SshCommandResult Ok(string output) => new() { ExitCode = 0, StandardOutput = output, StandardError = "" };

    // ---------- reading the position ----------

    [Theory]
    [InlineData("assistant\n", SwitchState.On)]
    [InlineData("  ASSISTANT  \r\n", SwitchState.On)]
    [InlineData("stub", SwitchState.Off)]
    [InlineData("\n\nstub\nsomething else", SwitchState.Off)]
    [InlineData("maybe", SwitchState.Unknown)]
    [InlineData("", SwitchState.Unknown)]
    public void Parse_ReadsTheFirstNonEmptyLine_CaseInsensitively(string output, SwitchState expected)
    {
        Assert.Equal(expected, SshSwitchController.Parse(Brain(), output).State);
    }

    [Fact]
    public void AnUnrecognisedAnswer_IsUnknown_AndSaysWhatItWas()
    {
        var status = SshSwitchController.Parse(Brain(), "maybe\n");

        Assert.Equal("maybe", status.RawValue);
        Assert.Contains("maybe", status.LastError);
    }

    [Fact]
    public async Task Check_RunsTheConfiguredStatusCommand()
    {
        var ssh = new FakeSsh { Next = Ok("assistant\n") };
        var controller = new SshSwitchController(ssh);

        var status = await controller.CheckAsync(new ServerResource(), Brain());

        Assert.Equal(SwitchState.On, status.State);
        Assert.Equal(new[] { "tool --status" }, ssh.Commands);
    }

    [Fact]
    public async Task Check_WhenSshFails_IsUnknown_NotAnException()
    {
        var failing = new SshSwitchController(new FakeSsh { Throw = new InvalidOperationException("boom") });
        var timedOut = new SshSwitchController(new FakeSsh { Next = new SshCommandResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true } });
        var exitCode = new SshSwitchController(new FakeSsh { Next = new SshCommandResult { ExitCode = 255, StandardOutput = "", StandardError = "Permission denied\n" } });

        Assert.Equal("boom", (await failing.CheckAsync(new ServerResource(), Brain())).LastError);
        Assert.Equal("SSH timeout", (await timedOut.CheckAsync(new ServerResource(), Brain())).LastError);
        Assert.Equal("Permission denied", (await exitCode.CheckAsync(new ServerResource(), Brain())).LastError);
    }

    [Fact]
    public async Task Check_WithoutAStatusCommand_DoesNotCallSsh()
    {
        var ssh = new FakeSsh();
        var sw = Brain();
        sw.StatusCommand = " ";

        var status = await new SshSwitchController(ssh).CheckAsync(new ServerResource(), sw);

        Assert.Equal(SwitchState.Unknown, status.State);
        Assert.Empty(ssh.Commands);
    }

    // ---------- flipping ----------

    [Fact]
    public async Task Set_RunsTheOnOrOffCommandThatMatches()
    {
        var ssh = new FakeSsh { Next = Ok("Done: brain is on\n") };
        var controller = new SshSwitchController(ssh);

        var on = await controller.SetAsync(new ServerResource(), Brain(), on: true);
        var off = await controller.SetAsync(new ServerResource(), Brain(), on: false);

        Assert.True(on.Success);
        Assert.Equal("Done: brain is on", on.Message);
        Assert.True(off.Success);
        Assert.Equal(new[] { "tool --on", "tool --off" }, ssh.Commands);
    }

    [Fact]
    public async Task Set_ReportsAFailureWithItsReason_AndNeverThrows()
    {
        var failing = new SshSwitchController(new FakeSsh { Next = new SshCommandResult { ExitCode = 1, StandardOutput = "", StandardError = "service did not come back\n" } });
        var broken = new SshSwitchController(new FakeSsh { Throw = new InvalidOperationException("no route") });

        var a = await failing.SetAsync(new ServerResource(), Brain(), true);
        var b = await broken.SetAsync(new ServerResource(), Brain(), false);

        Assert.False(a.Success);
        Assert.Equal("service did not come back", a.Message);
        Assert.False(b.Success);
        Assert.Equal("no route", b.Message);
    }

    [Fact]
    public async Task Set_WithoutACommand_IsRefusedBeforeAnythingIsSent()
    {
        var ssh = new FakeSsh();
        var sw = Brain();
        sw.OffCommand = "";

        var outcome = await new SshSwitchController(ssh).SetAsync(new ServerResource(), sw, on: false);

        Assert.False(outcome.Success);
        Assert.Empty(ssh.Commands);
    }

    // ---------- keeping the card current ----------

    private sealed class FakeController : ISwitchController
    {
        public SwitchState State { get; set; } = SwitchState.On;

        public Task<SwitchStatus> CheckAsync(ServerResource server, SwitchResource sw, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SwitchStatus { State = State });

        public Task<ServiceCommandOutcome> SetAsync(ServerResource server, SwitchResource sw, bool on, CancellationToken cancellationToken = default)
        {
            State = on ? SwitchState.On : SwitchState.Off;
            return Task.FromResult(new ServiceCommandOutcome { Success = true, Message = "ok" });
        }
    }

    [Fact]
    public async Task ThePoller_ReadsEnabledSwitchesOfKnownServers_AndRaisesChecked()
    {
        var server = new ServerResource { Name = "pet" };
        var sw = Brain();
        sw.ServerId = server.Id;
        var orphan = Brain();
        orphan.ServerId = Guid.NewGuid();                               // its server is not configured
        var disabled = Brain();
        disabled.ServerId = server.Id;
        disabled.Enabled = false;
        var config = new AppConfig { Servers = { server }, Switches = { sw, orphan, disabled } };

        await using var poller = new SwitchPoller(new FakeController());
        var seen = new TaskCompletionSource<SwitchCheckedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.Checked += (_, e) => seen.TrySetResult(e);

        await poller.RestartAsync(config);
        var first = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(sw.Id, first.Switch.Id);
        Assert.Equal(SwitchState.On, poller.GetStatus(sw.Id)!.State);
        Assert.Null(poller.GetStatus(orphan.Id));
        Assert.Null(poller.GetStatus(disabled.Id));
    }

    [Fact]
    public async Task CheckNow_ReflectsAFlipImmediately()
    {
        var server = new ServerResource { Name = "pet" };
        var sw = Brain();
        sw.ServerId = server.Id;
        var controller = new FakeController { State = SwitchState.On };
        await using var poller = new SwitchPoller(controller);

        await controller.SetAsync(server, sw, on: false);
        await poller.CheckNowAsync(server, sw);

        Assert.Equal(SwitchState.Off, poller.GetStatus(sw.Id)!.State);
    }

    [Fact]
    public void TheNewSectionIsOptionalInOldConfigs()
    {
        Assert.Empty(new AppConfig().Switches);
    }
}
