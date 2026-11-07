using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Infrastructure.Persistence;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class ReminderTests
{
    private sealed class MemoryStore : IReminderStore
    {
        public List<Reminder>? Items { get; set; } = new();
        public bool FailSave { get; set; }

        public IReadOnlyList<Reminder>? Load() => Items;

        public void Save(IReadOnlyList<Reminder> reminders)
        {
            if (FailSave)
            {
                throw new IOException("disk full");
            }

            Items = reminders.ToList();
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

    private static readonly TimeSpan Ict = TimeSpan.FromHours(7);

    private static (ReminderService Service, MemoryStore Store, FakeTelegram Telegram, ConnectivityState Connectivity, Func<DateTimeOffset> Now) Make(DateTimeOffset now)
    {
        var store = new MemoryStore();
        var telegram = new FakeTelegram();
        var connectivity = new ConnectivityState();
        var current = now;
        Func<DateTimeOffset> clock = () => current;
        return (new ReminderService(store, telegram, connectivity, clock), store, telegram, connectivity, clock);
    }

    [Fact]
    public async Task ADueReminder_IsSentOnce_AndMarkedAsSent()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 16, 10, 1, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "Уборка на Harbor" });

        Assert.Equal(1, await service.TickAsync());
        Assert.Equal(0, await service.TickAsync());

        Assert.Single(telegram.Sent);
        Assert.Equal("⏰ Уборка на Harbor", telegram.Sent[0]);
        Assert.NotNull(store.Items![0].SentAt);
    }

    [Fact]
    public async Task AFutureReminder_WaitsForItsTime()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 15, 23, 59, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "x" });

        Assert.Equal(0, await service.TickAsync());
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task AReminderThatCameDueWhilePcWasOff_IsSentLate_WithANote()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 17, 9, 0, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "Уборка на Harbor" });

        await service.TickAsync();

        Assert.StartsWith("⏰ (запоздало", telegram.Sent.Single());
        Assert.Contains("Уборка на Harbor", telegram.Sent[0]);
    }

    [Fact]
    public async Task AlreadySentReminders_AreNeverSentAgain()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 20, 9, 0, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "x", SentAt = new DateTimeOffset(2026, 10, 16, 10, 0, 5, Ict) });

        Assert.Equal(0, await service.TickAsync());
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task WhenTelegramRefusesOrWeAreOffline_ItIsRetriedLater_NotLost()
    {
        var (service, store, telegram, connectivity, _) = Make(new DateTimeOffset(2026, 10, 16, 10, 1, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "x" });

        telegram.Accept = false;
        Assert.Equal(0, await service.TickAsync());
        Assert.Null(store.Items![0].SentAt);

        telegram.Accept = true;
        connectivity.SetOffline(true);
        Assert.Equal(0, await service.TickAsync());

        connectivity.SetOffline(false);
        Assert.Equal(1, await service.TickAsync());
    }

    [Fact]
    public async Task ABrokenFile_IsLeftAlone()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 16, 10, 1, 0, Ict));
        store.Items = null;      // what the store reports for a file it cannot parse

        Assert.Equal(0, await service.TickAsync());
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task AFailedSave_DoesNotTurnIntoAMessageEveryMinute()
    {
        var (service, store, telegram, _, _) = Make(new DateTimeOffset(2026, 10, 16, 10, 1, 0, Ict));
        store.Items!.Add(new Reminder { Id = "a", At = new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), Text = "x" });
        store.FailSave = true;

        await service.TickAsync();
        await service.TickAsync();
        await service.TickAsync();

        Assert.Single(telegram.Sent);
    }

    [Fact]
    public void TheJsonStore_ReadsAHandWrittenFile_AndTreatsGarbageAsHandsOff()
    {
        var dir = Path.Combine(Path.GetTempPath(), "des-rem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "reminders.json");
        try
        {
            var store = new JsonReminderStore(path);
            Assert.Empty(store.Load()!);                                   // no file = no reminders

            File.WriteAllText(path, "﻿[ { \"id\": \"x\", \"at\": \"2026-10-16T10:00:00+07:00\", \"text\": \"Уборка\" } ]");
            var loaded = store.Load()!;
            Assert.Single(loaded);
            Assert.Equal(new DateTimeOffset(2026, 10, 16, 10, 0, 0, Ict), loaded[0].At);

            loaded[0].SentAt = new DateTimeOffset(2026, 10, 16, 10, 0, 3, Ict);
            store.Save(loaded);
            Assert.NotNull(store.Load()![0].SentAt);
            Assert.Contains("Уборка", File.ReadAllText(path));             // Cyrillic stays readable, not \u-escaped

            File.WriteAllText(path, "[ this is not json");
            Assert.Null(store.Load());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
