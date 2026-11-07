using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;
using DesControlCenter.Infrastructure.Monitors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class MaintenanceTests
{
    private sealed class FakeChecker : IMaintenanceChecker
    {
        public Func<ServerResource, MaintenanceStatus> Respond { get; set; } = s => new MaintenanceStatus { ServerId = s.Id, PendingUpdates = 0, RebootRequired = false, DiskUsedPercent = 20, Firewall = "active" };
        public int Calls { get; private set; }

        public Task<MaintenanceStatus> CheckAsync(ServerResource server, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Respond(server));
        }
    }

    private sealed class FakeTelegram : ITelegramSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Accept { get; set; } = true;
        public List<string> Sent { get; } = new();

        public Task<bool> SendAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!Accept)
            {
                return Task.FromResult(false);
            }

            Sent.Add(text);
            return Task.FromResult(true);
        }
    }

    private sealed class MemoryState : IReportStateStore
    {
        public DateTimeOffset? Last { get; set; }

        public DateTimeOffset? LoadLastSent() => Last;

        public void SaveLastSent(DateTimeOffset sentAt) => Last = sentAt;
    }

    private sealed class SilenceMemory : ISilenceStore
    {
        public IReadOnlyList<Silence> Load() => Array.Empty<Silence>();

        public void Save(IReadOnlyCollection<Silence> silences)
        {
        }
    }

    private static readonly TimeSpan Msk = TimeSpan.FromHours(3);

    private static (ScheduledReportService Service, FakeTelegram Telegram, MemoryState State, FakeChecker Checker, AppConfig Config, ConnectivityState Connectivity, ServerResource Server)
        Make(DateTimeOffset now, bool backupOnServer = false, DayOfWeek? day = DayOfWeek.Monday, int hour = 10)
    {
        var server = new ServerResource { Name = "Atlas", Host = "203.0.113.10", BackupDirectory = backupOnServer ? "/srv/b" : null };
        var config = new AppConfig { Servers = { server } };
        var checker = new FakeChecker();
        var connectivity = new ConnectivityState();
        var maintenance = new MaintenanceMonitor(checker, connectivity);
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        var telegram = new FakeTelegram();
        var state = new MemoryState();
        var service = new ScheduledReportService(maintenance, scheduler, new SilenceRegistry(new SilenceMemory()), telegram, state, connectivity, () => now, day, hour);
        return (service, telegram, state, checker, config, connectivity, server);
    }

    // ---------- parsing the server's answer ----------

    [Fact]
    public void Parse_ReadsAllTaggedValues_AndComputesBackupAge()
    {
        var server = new ServerResource { BackupDirectory = "/srv/b", BackupMaxAgeHours = 30 };
        var status = SshMaintenanceChecker.Parse(server, "UPDATES:23\nREBOOT:1\nDISK:41\nUFW:active\nBACKUP:1000.5\nNOW:8200.5\n", hasBackup: true);

        Assert.Equal(23, status.PendingUpdates);
        Assert.True(status.RebootRequired);
        Assert.Equal(41, status.DiskUsedPercent);
        Assert.Equal("active", status.Firewall);
        Assert.Equal(2.0, status.BackupAgeHours!.Value, 3);
    }

    [Fact]
    public void Parse_ExtraBackups_AreMatchedByPosition_AndComputeTheirOwnAge()
    {
        var server = new ServerResource
        {
            ExtraBackups =
            {
                new BackupTarget { Label = "TaskBoard", Directory = "/srv/k", NamePattern = "taskboard-*.enc", MaxAgeHours = 30 },
                new BackupTarget { Label = "Other", Directory = "/srv/o", MaxAgeHours = 10 }
            }
        };
        var status = SshMaintenanceChecker.Parse(server, "UPDATES:0\nREBOOT:0\nDISK:10\nUFW:active\nXBACKUP0:1000\nXBACKUP1:\nNOW:8200\n", hasBackup: false);

        Assert.Equal(2, status.ExtraBackups.Count);
        Assert.Equal(2.0, status.ExtraBackups[0].AgeHours!.Value, 3);
        Assert.False(status.ExtraBackups[0].Stale);
        Assert.Null(status.ExtraBackups[1].AgeHours);      // folder empty -> stale
        Assert.Contains("бэкапов нет (Other)", status.Critical);
        Assert.DoesNotContain(status.Critical, c => c.Contains("TaskBoard"));
    }

    [Fact]
    public void Parse_QuotaAndUnits_ComeFromTheirTaggedLines()
    {
        var server = new ServerResource
        {
            ExtraBackups = { new BackupTarget { Label = "KL", Directory = "/srv/k", MaxStoreBytes = 8L * 1024 * 1024 * 1024 } },
            WatchedUnits =
            {
                new WatchedUnit { Label = "sender", Unit = "a.service" },
                new WatchedUnit { Label = "timer", Unit = "a.timer", ExpectActive = true },
                new WatchedUnit { Label = "gone", Unit = "b.service" }
            }
        };
        var output = "UPDATES:0\nREBOOT:0\nDISK:10\nUFW:active\nXBACKUP0:1000\nXBACKUPSIZE0:7516192768\n" +
                     "XUNIT0:ActiveState=inactive Result=success LoadState=loaded \n" +
                     "XUNIT1:ActiveState=inactive Result=success LoadState=loaded \n" +
                     "XUNIT2:ActiveState=inactive Result=success LoadState=not-found \nNOW:2000\n";
        var status = SshMaintenanceChecker.Parse(server, output, hasBackup: false);

        Assert.Equal(87, status.ExtraBackups[0].QuotaPercent);
        Assert.Contains("хранилище KL заполнено на 87%", status.Attention);
        Assert.DoesNotContain(status.Critical, c => c.Contains("хранилище"));    // 87 < 90
        Assert.False(status.UnitChecks[0].Bad);        // one-shot finished fine: inactive is normal
        Assert.True(status.UnitChecks[1].Bad);         // timer expected active
        Assert.True(status.UnitChecks[2].Bad);         // unit does not exist
        Assert.Contains("сбой службы: timer", status.Critical);
    }

    [Fact]
    public void Parse_BackupSize_AcceptsBytesAndCount_AndTheOldBytesOnlyForm()
    {
        var server = new ServerResource
        {
            ExtraBackups =
            {
                new BackupTarget { Label = "A", Directory = "/a", MaxStoreBytes = 1000 },
                new BackupTarget { Label = "B", Directory = "/b" }
            }
        };
        var status = SshMaintenanceChecker.Parse(server, "XBACKUP0:100\nXBACKUPSIZE0:250 4\nXBACKUP1:100\nXBACKUPSIZE1:900\nNOW:200\n", hasBackup: false);

        Assert.Equal(250, status.ExtraBackups[0].StoreBytes);
        Assert.Equal(4, status.ExtraBackups[0].Count);
        Assert.Equal(25, status.ExtraBackups[0].QuotaPercent);
        Assert.Equal(900, status.ExtraBackups[1].StoreBytes);      // older checker output still understood
        Assert.Null(status.ExtraBackups[1].Count);
    }

    [Fact]
    public void TheMorningReport_ShowsHowManyCopiesAndHowMuchSpace()
    {
        var server = new ServerResource { Name = "Harbor" };
        var config = new AppConfig { Servers = { server } };
        var status = new MaintenanceStatus
        {
            ServerId = server.Id,
            ExtraBackups = new[] { new BackupCheck { Label = "TaskBoard", AgeHours = 7, MaxAgeHours = 28, Count = 4, StoreBytes = 891930, MaxStoreBytes = 8L << 30 } }
        };

        var text = ReportComposer.Compose(config, DateTimeOffset.Now, _ => status, _ => null, 0, null);

        Assert.Contains("Бэкап TaskBoard (Harbor): свежий, 7 ч назад", text);
        Assert.Contains("копий 4", text);
        Assert.Contains("871 КБ (0% квоты)", text);
    }

    [Fact]
    public void TheMainBackup_IsNamedInTheReportAndInItsAlerts_WhenALabelIsSet()
    {
        var server = new ServerResource { Name = "Harbor", BackupDirectory = "/srv/b", BackupLabel = "WebShop" };
        var config = new AppConfig { Servers = { server } };
        var fresh = new MaintenanceStatus { ServerId = server.Id, BackupConfigured = true, BackupLabel = "WebShop", BackupMaxAgeHours = 30, BackupAgeHours = 2 };
        var stale = new MaintenanceStatus { ServerId = server.Id, BackupConfigured = true, BackupLabel = "WebShop", BackupMaxAgeHours = 30, BackupAgeHours = 50 };

        Assert.Contains("Бэкап WebShop (Harbor): свежий, 2 ч назад", ReportComposer.Compose(config, DateTimeOffset.Now, _ => fresh, _ => null, 0, null));
        Assert.Contains("бэкап WebShop устарел (50 ч)", stale.Attention);
        Assert.Contains("бэкап WebShop не обновлялся 50 ч", stale.Critical);
    }

    [Fact]
    public void UnitCheck_FailedRun_IsBad_ButNoAnswerIsNot()
    {
        Assert.True(new UnitCheck { ActiveState = "failed", Result = "exit-code" }.Bad);
        Assert.True(new UnitCheck { ActiveState = "inactive", Result = "timeout" }.Bad);
        Assert.False(new UnitCheck { ActiveState = "inactive", Result = "success" }.Bad);
        Assert.False(new UnitCheck().Bad);
    }

    [Fact]
    public void FullQuota_IsCritical()
    {
        var s = new MaintenanceStatus { ExtraBackups = new[] { new BackupCheck { Label = "KL", AgeHours = 1, MaxAgeHours = 36, StoreBytes = 95, MaxStoreBytes = 100 } } };

        Assert.Contains(s.Critical, c => c.Contains("почти полное (95%)"));
    }

    [Fact]
    public void StaleExtraBackup_IsAttentionAndCritical_WithItsLabel()
    {
        var s = new MaintenanceStatus { ExtraBackups = new[] { new BackupCheck { Label = "TaskBoard", AgeHours = 50, MaxAgeHours = 30 } } };

        Assert.Contains("бэкап TaskBoard устарел (50 ч)", s.Attention);
        Assert.Contains("бэкап TaskBoard не обновлялся 50 ч", s.Critical);
    }

    [Fact]
    public void Parse_NoUfwInstalled_MeansNoFirewallOpinion_NotAnAlarm()
    {
        var status = SshMaintenanceChecker.Parse(new ServerResource(), "UPDATES:0\nREBOOT:0\nDISK:10\nUFW:\nNOW:1\n", hasBackup: false);

        Assert.Null(status.Firewall);
        Assert.Empty(status.Attention);
    }

    // ---------- what counts as "needs attention" / "critical" ----------

    [Fact]
    public void Attention_ListsRebootManyUpdatesFullDiskFirewallAndStaleBackup()
    {
        var s = new MaintenanceStatus
        {
            RebootRequired = true, PendingUpdates = 23, DiskUsedPercent = 91, Firewall = "inactive",
            BackupConfigured = true, BackupMaxAgeHours = 30, BackupAgeHours = 50
        };

        Assert.Equal(5, s.Attention.Count);
        Assert.Equal(3, s.Critical.Count);   // firewall, disk >= 90, backup
    }

    [Fact]
    public void FewUpdates_AndAReboot_AreNotCritical()
    {
        var s = new MaintenanceStatus { RebootRequired = true, PendingUpdates = 4, DiskUsedPercent = 40, Firewall = "active" };

        Assert.Single(s.Attention);          // only the reboot (4 updates is below the threshold)
        Assert.Empty(s.Critical);
    }

    [Fact]
    public void MissingBackup_WhenOneIsExpected_IsStale()
    {
        var s = new MaintenanceStatus { BackupConfigured = true, BackupMaxAgeHours = 30, BackupAgeHours = null };

        Assert.True(s.BackupStale);
    }

    [Fact]
    public void AnUnreachableServer_HasNoAttentionItems()
    {
        var s = new MaintenanceStatus { Error = "SSH timeout", RebootRequired = true };

        Assert.Empty(s.Attention);
        Assert.Empty(s.Critical);
    }

    // ---------- the weekly schedule ----------

    [Fact]
    public void LastDueMoment_IsTheMostRecentMonday10()
    {
        var (service, _, _, _, _, _, _) = Make(DateTimeOffset.Now);

        // Tuesday 13.10.2026 12:00 → Monday 12.10 10:00
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk)));
        // Monday 09:00 (not yet due) → the previous Monday
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 12, 9, 0, 0, Msk)));
        // Monday 10:30 → today 10:00
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 12, 10, 30, 0, Msk)));
    }

    [Fact]
    public async Task Tick_SendsOncePerWeek_AndNotAgain()
    {
        var (service, telegram, state, _, config, _, _) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));

        Assert.True(await service.TickAsync(config));
        Assert.False(await service.TickAsync(config));

        Assert.Single(telegram.Sent);
        Assert.NotNull(state.Last);
        Assert.Contains("Atlas", telegram.Sent[0]);
    }

    [Fact]
    public async Task Tick_StaysQuiet_WhenThisWeeksReportAlreadyWentOut()
    {
        var (service, telegram, state, _, config, _, _) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));
        state.Last = new DateTimeOffset(2026, 10, 12, 10, 5, 0, Msk);

        Assert.False(await service.TickAsync(config));
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task Tick_RetriesLater_WhenTelegramRefusesOrIsNotConfigured_OrWeAreOffline()
    {
        var (service, telegram, state, _, config, connectivity, _) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));

        telegram.Accept = false;
        Assert.False(await service.TickAsync(config));
        Assert.Null(state.Last);                 // not marked as sent, so it will try again

        telegram.Accept = true;
        connectivity.SetOffline(true);
        Assert.False(await service.TickAsync(config));
        Assert.Null(state.Last);

        connectivity.SetOffline(false);
        telegram.IsConfigured = false;
        Assert.False(await service.TickAsync(config));

        telegram.IsConfigured = true;
        Assert.True(await service.TickAsync(config));
    }

    [Fact]
    public void Daily_LastDueMoment_Is9OClockToday_OrYesterdayIfNotYetNine()
    {
        var (service, _, _, _, _, _, _) = Make(DateTimeOffset.Now, day: null, hour: 9);

        Assert.Equal(new DateTimeOffset(2026, 10, 13, 9, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk)));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 13, 8, 59, 0, Msk)));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 9, 0, 0, Msk), service.LastDueMoment(new DateTimeOffset(2026, 10, 13, 9, 0, 0, Msk)));
    }

    [Fact]
    public async Task Daily_SendsOncePerDay_AndAgainTheNextMorning_NotBefore()
    {
        var now = new DateTimeOffset(2026, 10, 13, 13, 5, 0, Msk);
        var server = new ServerResource { Name = "Atlas", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };
        var connectivity = new ConnectivityState();
        var telegram = new FakeTelegram();
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        var service = new ScheduledReportService(new MaintenanceMonitor(new FakeChecker(), connectivity), scheduler,
            new SilenceRegistry(new SilenceMemory()), telegram, new MemoryState(), connectivity, () => now);

        Assert.True(await service.TickAsync(config));                        // 13.10 13:05
        now = now.AddHours(3);
        Assert.False(await service.TickAsync(config));                       // same day, 16:05
        now = new DateTimeOffset(2026, 10, 14, 12, 30, 0, Msk);
        Assert.False(await service.TickAsync(config));                       // next day, before 13:00
        now = new DateTimeOffset(2026, 10, 14, 13, 1, 0, Msk);
        Assert.True(await service.TickAsync(config));                        // next morning
        Assert.Equal(2, telegram.Sent.Count);
    }

    [Fact]
    public async Task Daily_PcWasOffAtReportTime_SendsOnceAtFirstStart_NotOncePerMissedDay()
    {
        var now = new DateTimeOffset(2026, 10, 15, 18, 0, 0, Msk);          // PC off for days, switched on at 18:00
        var server = new ServerResource { Name = "Atlas", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };
        var connectivity = new ConnectivityState();
        var telegram = new FakeTelegram();
        var state = new MemoryState { Last = new DateTimeOffset(2026, 10, 11, 13, 2, 0, Msk) };
        var scheduler = new PollingScheduler(new FakeServerMonitor(), new FakeWebsiteMonitor(), new FakeServiceMonitor(), new FakeVpnMonitor(),
            new FakeEventStore(), new FakeNotificationService(), NullLogger<PollingScheduler>.Instance);
        var service = new ScheduledReportService(new MaintenanceMonitor(new FakeChecker(), connectivity), scheduler,
            new SilenceRegistry(new SilenceMemory()), telegram, state, connectivity, () => now);

        Assert.True(await service.TickAsync(config));
        Assert.False(await service.TickAsync(config));
        Assert.Single(telegram.Sent);
    }

    // ---------- report text ----------

    [Fact]
    public async Task Report_MarksProblemsPerServer_AndSaysEverythingElseIsFine()
    {
        var (service, telegram, _, checker, config, _, server) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));
        var healthy = new ServerResource { Name = "Harbor", Host = "198.51.100.20" };
        config.Servers.Add(healthy);
        checker.Respond = s => s.Id == server.Id
            ? new MaintenanceStatus { ServerId = s.Id, RebootRequired = true, PendingUpdates = 9, DiskUsedPercent = 30, Firewall = "active" }
            : new MaintenanceStatus { ServerId = s.Id, RebootRequired = false, PendingUpdates = 0, DiskUsedPercent = 30, Firewall = "active" };

        await service.TickAsync(config);

        var text = telegram.Sent.Single();
        Assert.Contains("⚠️ Atlas — нужна перезагрузка; обновлений: 9", text);
        Assert.Contains("✅ Harbor — всё в порядке", text);
    }

    // ---------- immediate alerts ----------

    [Fact]
    public async Task CriticalProblem_IsReportedOnce_AndAgainOnlyAfterItClearedAndReturned()
    {
        var (service, telegram, _, _, _, _, server) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));
        var bad = new MaintenanceStatus { ServerId = server.Id, Firewall = "inactive" };
        var good = new MaintenanceStatus { ServerId = server.Id, Firewall = "active" };

        await service.OnMaintenanceCheckedAsync("Atlas", bad);
        await service.OnMaintenanceCheckedAsync("Atlas", bad);
        Assert.Single(telegram.Sent);
        Assert.Contains("фаервол выключен", telegram.Sent[0]);

        await service.OnMaintenanceCheckedAsync("Atlas", good);
        await service.OnMaintenanceCheckedAsync("Atlas", bad);
        Assert.Equal(2, telegram.Sent.Count);
    }

    [Fact]
    public async Task NonCriticalNotes_NeverSendAnImmediateMessage()
    {
        var (service, telegram, _, _, _, _, server) = Make(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Msk));

        await service.OnMaintenanceCheckedAsync("Atlas", new MaintenanceStatus { ServerId = server.Id, RebootRequired = true, PendingUpdates = 30, Firewall = "active" });

        Assert.Empty(telegram.Sent);
    }

    // ---------- monitor ----------

    [Fact]
    public async Task AFailedQuery_KeepsTheLastGoodPicture()
    {
        var server = new ServerResource { Name = "Atlas", Host = "203.0.113.10" };
        var config = new AppConfig { Servers = { server } };
        var checker = new FakeChecker();
        var monitor = new MaintenanceMonitor(checker);

        await monitor.CheckAllAsync(config);
        checker.Respond = s => new MaintenanceStatus { ServerId = s.Id, Error = "SSH timeout" };
        await monitor.CheckAllAsync(config);

        Assert.True(monitor.Get(server.Id)!.Reachable);
    }
}
