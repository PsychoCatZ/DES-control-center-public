using System.Text.Json;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Monitoring;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class StatusFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 5, 0, 0, TimeSpan.Zero);

    private static StatusSnapshot Snap(
        HealthLevel health = HealthLevel.Ok, bool noConnection = false, int problems = 0, int silenced = 0,
        int online = 8, int total = 8, int pending = 0, params string[] list) =>
        new(health, noConnection, problems, silenced, online, total, pending, list, Now);

    [Fact]
    public void Text_AllGood_SaysSoAndCountsServers()
    {
        var text = StatusFormatter.Text(Snap());

        Assert.Contains("Состояние: всё в порядке", text);
        Assert.Contains("Проблем: 0", text);
        Assert.Contains("Серверов онлайн: 8 из 8", text);
        Assert.DoesNotContain("Проблема 1", text);
    }

    [Fact]
    public void Text_Problems_ListsAtMostFive()
    {
        var list = Enumerable.Range(1, 7).Select(i => $"сервис {i} упал").ToArray();

        var text = StatusFormatter.Text(Snap(HealthLevel.Problem, problems: 7, list: list));

        Assert.Contains("Состояние: есть проблемы", text);
        Assert.Contains("Проблема 1: сервис 1 упал", text);
        Assert.Contains("Проблема 5: сервис 5 упал", text);
        Assert.DoesNotContain("Проблема 6", text);
    }

    [Fact]
    public void Text_Warnings_AreListedWithTheirReason()
    {
        var s = Snap(HealthLevel.Warning) with { Warnings = new[] { "assistant-app-migrate: не работает", "Messenger: предупреждение" } };

        var text = StatusFormatter.Text(s);

        Assert.Contains("Предупреждение 1: assistant-app-migrate: не работает", text);
        Assert.Contains("Предупреждение 2: Messenger: предупреждение", text);
        Assert.Contains("\"warningList\"", StatusFormatter.Json(s));
    }

    [Fact]
    public void Text_NoConnection_WinsOverHealth()
    {
        var s = Snap(HealthLevel.Warning, noConnection: true);

        Assert.Contains("нет связи у этого компьютера", StatusFormatter.Text(s));
        Assert.StartsWith("DES: нет связи у ПК", StatusFormatter.OneLine(s));
    }

    [Fact]
    public void Text_PendingServers_AreMentioned()
    {
        var s = Snap(online: 3, pending: 5);

        Assert.Contains("Серверов онлайн: 3 из 8 (проверяется: 5)", StatusFormatter.Text(s));
        Assert.Equal("DES: всё в порядке · серверов 3/8 (проверяется)", StatusFormatter.OneLine(s));
    }

    [Fact]
    public void OneLine_Problems_ShowsCount()
    {
        Assert.Equal("DES: проблем 2 · серверов 7/8", StatusFormatter.OneLine(Snap(HealthLevel.Problem, problems: 2, online: 7)));
    }

    [Fact]
    public void Stopped_WritesNotRunningInAllFormats()
    {
        var s = StatusSnapshot.Stopped(Now);

        Assert.Contains("DES не запущен", StatusFormatter.Text(s, stopped: true));
        Assert.Equal("DES: не запущен", StatusFormatter.OneLine(s, stopped: true));

        using var doc = JsonDocument.Parse(StatusFormatter.Json(s, stopped: true));
        Assert.False(doc.RootElement.GetProperty("running").GetBoolean());
        Assert.Equal("Stopped", doc.RootElement.GetProperty("health").GetString());
    }

    [Fact]
    public void Json_CarriesCountsAndKeepsCyrillicReadable()
    {
        var json = StatusFormatter.Json(Snap(HealthLevel.Problem, problems: 1, silenced: 2, online: 7, list: "Messenger упал"));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("running").GetBoolean());
        Assert.Equal("Problem", root.GetProperty("health").GetString());
        Assert.Equal(1, root.GetProperty("problems").GetInt32());
        Assert.Equal(2, root.GetProperty("silenced").GetInt32());
        Assert.Equal(7, root.GetProperty("serversOnline").GetInt32());
        Assert.Equal("Messenger упал", root.GetProperty("problemList")[0].GetString());
        Assert.Contains("Messenger упал", json);
    }
}
