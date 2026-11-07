using System.IO;
using System.Security.Cryptography;
using System.Text;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.Demo;

/// <summary>
/// A made-up infrastructure for screenshots and demos: seven fictional servers with services, a VPN node,
/// sites and external services, all answered by in-memory fakes. Nothing here touches the network, SSH, the
/// registry, Telegram or the user's real settings. Enabled only with the environment variable DES_DEMO=1.
/// Addresses use the documentation ranges (example.com, 203.0.113.0/24) on purpose.
/// </summary>
public static class DemoMode
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("DES_DEMO") == "1";

    /// <summary>Scratch folder for the demo's own files (theme choice, captured Telegram messages).</summary>
    public static string DataDir => Path.Combine(Path.GetTempPath(), "DesControlCenterDemo");
}

public sealed class DemoWorld
{
    private readonly object _gate = new();
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private readonly Dictionary<Guid, ServiceState> _serviceStates = new();
    private readonly Dictionary<Guid, DateTimeOffset> _activeSince = new();
    private readonly Dictionary<string, ServerSpec> _serverSpecs = new();

    public AppConfig Config { get; } = new();

    private sealed record ServerSpec(double Cpu, double MemUsedMb, double MemTotalMb, double DiskUsedGb, double DiskTotalGb, double Ping, int UptimeDays, bool Online);

    public static Guid IdOf(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes("des-demo:" + key)));

    public DemoWorld()
    {
        Directory.CreateDirectory(DemoMode.DataDir);

        ServerResource AddServer(string name, double cpu, double memUsed, double memTotal, double diskUsed, double diskTotal, double ping, int uptimeDays, bool online = true, string? backupDir = null)
        {
            var server = new ServerResource
            {
                Id = IdOf("server:" + name), Name = name, Host = "203.0.113." + (10 + Config.Servers.Count), SshConfigAlias = "demo-" + name.ToLowerInvariant(),
                SshUsername = "ops", BackupDirectory = backupDir
            };
            Config.Servers.Add(server);
            _serverSpecs[name] = new ServerSpec(cpu, memUsed, memTotal, diskUsed, diskTotal, ping, uptimeDays, online);
            return server;
        }

        void AddService(ServerResource server, string unit, string display, string category, ServiceKind kind, ServiceState state, double hoursUp)
        {
            var service = new ServiceResource
            {
                Id = IdOf("service:" + server.Name + ":" + unit), ServerId = server.Id, UnitName = unit, DisplayName = display, Category = category, Kind = kind
            };
            Config.Services.Add(service);
            _serviceStates[service.Id] = state;
            _activeSince[service.Id] = DateTimeOffset.UtcNow.AddHours(-hoursUp);
        }

        void AddSite(string name, string url, ServerResource? server, string category)
        {
            Config.Websites.Add(new Website { Id = IdOf("site:" + name), Name = name, Url = url, ServerId = server?.Id, Category = category, SslWarningDays = 14 });
        }

        var atlas = AddServer("Atlas", 12, 1650, 3911, 18.4, 59, 22, 41);
        var borealis = AddServer("Borealis", 7, 780, 1967, 9.2, 39, 41, 18, backupDir: "/srv/backups/shop");
        var cedar = AddServer("Cedar", 31, 1210, 1967, 14.6, 29, 118, 9);
        var delta = AddServer("Delta", 4, 350, 1967, 5.5, 19, 87, 63);
        var ember = AddServer("Ember", 19, 2480, 3911, 51.7, 59, 33, 5);
        var fjord = AddServer("Fjord", 3, 410, 1967, 6.3, 29, 64, 27);
        var granite = AddServer("Granite", 0, 0, 1967, 0, 29, 0, 0, online: false);

        AddService(atlas, "nginx", "Веб-сервер", "Web proxy", ServiceKind.Systemd, ServiceState.Running, 24 * 41);
        AddService(atlas, "shop-api-1", "Магазин — API (контейнер)", "Приложение", ServiceKind.DockerContainer, ServiceState.Running, 30);
        AddService(atlas, "shop-db-1", "Магазин — база данных (контейнер)", "База данных", ServiceKind.DockerContainer, ServiceState.Running, 30);
        AddSite("Магазин — витрина", "https://shop.example.com/", atlas, "Сайт");

        AddService(borealis, "notifier-bot", "Бот уведомлений", "Telegram-бот", ServiceKind.Systemd, ServiceState.Running, 24 * 6);
        AddService(borealis, "report-scheduler", "Планировщик отчётов", "Фоновая служба", ServiceKind.Systemd, ServiceState.Running, 24 * 6);
        AddSite("Панель отчётов", "https://reports.example.com/login", borealis, "Сайт");

        AddService(cedar, "relay-gateway", "Шлюз сообщений", "Приложение", ServiceKind.Systemd, ServiceState.Running, 72);
        AddService(cedar, "queue-worker", "Обработчик очереди", "Фоновая служба", ServiceKind.Systemd, ServiceState.Failed, 0.3);
        AddService(cedar, "edge-proxy", "Входной прокси", "Web proxy", ServiceKind.DockerContainer, ServiceState.Running, 72);

        Config.Vpns.Add(new VpnResource { Id = IdOf("vpn:Delta"), ServerId = delta.Id, DisplayName = "Тоннель Delta", ContainerName = "vpn-node" });
        AddService(delta, "vpn-dns", "DNS для тоннеля (контейнер)", "VPN", ServiceKind.DockerContainer, ServiceState.Running, 24 * 63);

        AddService(ember, "files-app-1", "Облако — приложение (контейнер)", "Облако", ServiceKind.DockerContainer, ServiceState.Running, 24 * 5);
        AddService(ember, "files-db-1", "Облако — база (контейнер)", "Облако", ServiceKind.DockerContainer, ServiceState.Running, 24 * 5);
        AddService(ember, "files-cache-1", "Облако — кеш (контейнер)", "Облако", ServiceKind.DockerContainer, ServiceState.Running, 24 * 5);
        AddSite("Облако", "https://files.example.com/", ember, "Облако");

        // plain launcher rows (fictional providers and API consoles)
        Config.Launchers.Add(new LauncherLink { Title = "Хостинг Alpha", Subtitle = "Atlas · Borealis · Cedar", Url = "https://example.com", Group = LauncherGroups.Provider });
        Config.Launchers.Add(new LauncherLink { Title = "Хостинг Beta", Subtitle = "Delta", Url = "https://example.com", Group = LauncherGroups.Provider });
        Config.Launchers.Add(new LauncherLink { Title = "Хостинг Gamma", Subtitle = "Ember · Fjord", Url = "https://example.com", Group = LauncherGroups.Provider });
        Config.Launchers.Add(new LauncherLink { Title = "API Assistant", Subtitle = "бот поддержки · аналитика", Url = "https://example.com", Group = LauncherGroups.Api });
        Config.Launchers.Add(new LauncherLink { Title = "API Models", Subtitle = "ассистент заказов", Url = "https://example.com", Group = LauncherGroups.Api });

        AddService(fjord, "docs-site", "Документация", "Сайт", ServiceKind.Systemd, ServiceState.Running, 24 * 20);
        AddService(fjord, "search-index", "Поисковый индекс", "Фоновая служба", ServiceKind.Systemd, ServiceState.Running, 24 * 20);
        AddSite("Документация", "https://docs.example.com/", fjord, "Сайт");

        AddService(granite, "staging-app", "Тестовое окружение", "Приложение", ServiceKind.Systemd, ServiceState.Unknown, 0);

        foreach (var (name, category) in new[]
                 {
                     ("Chat Assistant", "AI-сервис"), ("Code Assistant", "AI-сервис"), ("Image Studio", "AI-сервис"),
                     ("Research Bot", "AI-сервис"), ("Team Chat", "Мессенджер"), ("Mail Gateway", "Мессенджер / API"), ("Status Page", "Мессенджер")
                 })
        {
            AddSite(name, "https://" + name.Replace(' ', '-').ToLowerInvariant() + ".example.com/", null, category);
        }
    }

    // ---------- status generators ----------

    private double Wave(string key, double amplitude)
    {
        var seed = BitConverter.ToUInt16(MD5.HashData(Encoding.UTF8.GetBytes(key)), 0) / 65535.0 * 6.28;
        return Math.Sin((DateTimeOffset.UtcNow - _start).TotalSeconds / 9.0 + seed) * amplitude;
    }

    public ServerStatus ServerStatusOf(ServerResource server)
    {
        var spec = _serverSpecs[server.Name];
        if (!spec.Online)
        {
            return DesControlCenter.Core.Status.ServerStatus.Offline("нет ответа по SSH");
        }

        return new ServerStatus
        {
            IsOnline = true,
            PingMs = spec.Ping + Wave(server.Name + "p", 2),
            Uptime = TimeSpan.FromDays(spec.UptimeDays) + TimeSpan.FromHours(3),
            CpuLoadPercent = Math.Max(0.5, spec.Cpu + Wave(server.Name + "c", 3)),
            LoadAverage1Min = Math.Max(0.01, spec.Cpu / 100 * 2 + Wave(server.Name + "l", 0.04)),
            MemoryUsedMb = spec.MemUsedMb + Wave(server.Name + "m", 12),
            MemoryTotalMb = spec.MemTotalMb,
            DiskUsedGb = spec.DiskUsedGb,
            DiskTotalGb = spec.DiskTotalGb,
            NetworkRxKbps = Math.Max(1, 40 + spec.Cpu * 6 + Wave(server.Name + "r", 20)),
            NetworkTxKbps = Math.Max(1, 25 + spec.Cpu * 4 + Wave(server.Name + "t", 12)),
            LastCheckedUtc = DateTimeOffset.UtcNow
        };
    }

    public WebsiteStatus WebsiteStatusOf(Website site)
    {
        var slow = site.Name == "Облако" ? 480 : 90;
        var seed = BitConverter.ToUInt16(MD5.HashData(Encoding.UTF8.GetBytes(site.Name)), 0);
        return new WebsiteStatus
        {
            IsOnline = true,
            HttpStatusCode = site.Category?.StartsWith("AI", StringComparison.Ordinal) == true && seed % 3 == 0 ? 403 : 200,
            ResponseTimeMs = slow + seed % 140 + Wave(site.Name, 15),
            SslExpiresUtc = DateTimeOffset.UtcNow.AddDays(25 + seed % 120),
            LastCheckedUtc = DateTimeOffset.UtcNow
        };
    }

    public ServiceStatus ServiceStatusOf(ServiceResource service)
    {
        lock (_gate)
        {
            var state = _serviceStates.GetValueOrDefault(service.Id, ServiceState.Unknown);
            return new ServiceStatus
            {
                State = state,
                ActiveSinceUtc = state == ServiceState.Running ? _activeSince.GetValueOrDefault(service.Id) : null,
                LastError = state == ServiceState.Failed ? "Main process exited, code=exited, status=1/FAILURE" : null,
                LastCheckedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public VpnStatus VpnStatusOf()
    {
        return new VpnStatus
        {
            IsOnline = true,
            InterfaceName = "wg0",
            PeerCount = 5,
            PeersConnectedNow = 2,
            TotalReceivedBytes = 38_400_000_000,
            TotalSentBytes = 112_900_000_000,
            MostRecentHandshakeAge = TimeSpan.FromSeconds(40),
            LastCheckedUtc = DateTimeOffset.UtcNow
        };
    }

    public ServiceCommandOutcome Apply(ServiceResource service, ServiceState state)
    {
        lock (_gate)
        {
            _serviceStates[service.Id] = state;
            if (state == ServiceState.Running)
            {
                _activeSince[service.Id] = DateTimeOffset.UtcNow;
            }
        }

        return new ServiceCommandOutcome { Success = true, Message = "Выполнено (демо-режим)" };
    }

    public MaintenanceStatus Maintenance(ServerResource server) => server.Name switch
    {
        "Atlas" => Make(server, 2, false, 31, ageHours: null),
        "Borealis" => Make(server, 7, false, 24, ageHours: 6),
        "Cedar" => Make(server, 3, true, 49, ageHours: null),
        "Delta" => Make(server, 0, false, 29, ageHours: null),
        "Ember" => Make(server, 11, false, 87, ageHours: null),
        "Fjord" => Make(server, 1, false, 22, ageHours: null),
        _ => new MaintenanceStatus { ServerId = server.Id, Error = "SSH timeout" }
    };

    private static MaintenanceStatus Make(ServerResource server, int updates, bool reboot, int disk, double? ageHours) => new()
    {
        ServerId = server.Id,
        PendingUpdates = updates,
        RebootRequired = reboot,
        DiskUsedPercent = disk,
        Firewall = "active",
        BackupConfigured = ageHours is not null,
        BackupMaxAgeHours = 30,
        BackupAgeHours = ageHours,
        CheckedUtc = DateTimeOffset.UtcNow
    };

    public string Logs(ServiceResource service, int lines)
    {
        var failed = _serviceStates.GetValueOrDefault(service.Id) == ServiceState.Failed;
        var host = Config.Servers.First(s => s.Id == service.ServerId).Name.ToLowerInvariant();
        var sb = new StringBuilder();
        var now = DateTimeOffset.Now.AddMinutes(-lines);
        for (var i = 0; i < lines; i++)
        {
            var t = now.AddSeconds(i * 37).ToString("MMM dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            string message;
            if (failed && i >= lines - 6)
            {
                message = i switch
                {
                    _ when i == lines - 6 => "ERROR queue: connection to broker lost (timeout 30s)",
                    _ when i == lines - 5 => "WARN  retry 1/3 in 5s",
                    _ when i == lines - 4 => "WARN  retry 2/3 in 15s",
                    _ when i == lines - 3 => "ERROR retry limit reached, giving up",
                    _ when i == lines - 2 => "systemd: " + service.UnitName + ".service: Main process exited, code=exited, status=1/FAILURE",
                    _ => "systemd: " + service.UnitName + ".service: Failed with result 'exit-code'."
                };
            }
            else
            {
                message = (i % 5) switch
                {
                    0 => "INFO  heartbeat ok, queue depth " + (i % 9),
                    1 => "INFO  processed batch of " + (20 + i % 17) + " items in " + (80 + i % 60) + " ms",
                    2 => "INFO  health check passed",
                    3 => "DEBUG cache hit ratio 0." + (80 + i % 19),
                    _ => "INFO  scheduled task completed"
                };
            }

            sb.Append(t).Append(' ').Append(host).Append(' ').Append(service.UnitName).Append('[').Append(1200 + i % 7).Append("]: ").AppendLine(message);
        }

        return sb.ToString();
    }

    public IEnumerable<EventLogEntry> SeedEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var id = 0;
        EventLogEntry E(double minutesAgo, ResourceKind kind, string key, string name, EventKind eventKind, string? from, string? to, string message) => new()
        {
            Id = ++id,
            TimestampUtc = now.AddMinutes(-minutesAgo),
            ResourceKind = kind,
            ResourceId = IdOf(key),
            ResourceName = name,
            EventKind = eventKind,
            OldState = from,
            NewState = to,
            Message = message
        };

        yield return E(1320, ResourceKind.Service, "service:Cedar:relay-gateway", "Шлюз сообщений", EventKind.StatusChanged, "Running", "Failed", "Шлюз сообщений остановился с ошибкой");
        yield return E(1318, ResourceKind.Service, "service:Cedar:relay-gateway", "Шлюз сообщений", EventKind.UserAction, null, null, "Перезапуск «Шлюз сообщений» (Cedar): выполнен");
        yield return E(1317, ResourceKind.Service, "service:Cedar:relay-gateway", "Шлюз сообщений", EventKind.StatusChanged, "Failed", "Running", "Шлюз сообщений снова работает");
        yield return E(960, ResourceKind.Website, "site:Облако", "Облако", EventKind.StatusChanged, "Online", "Offline", "Облако не отвечает");
        yield return E(958, ResourceKind.Website, "site:Облако", "Облако", EventKind.StatusChanged, "Offline", "Online", "Облако снова отвечает");
        yield return E(610, ResourceKind.Server, "server:Granite", "Granite", EventKind.StatusChanged, "Online", "Offline", "Granite недоступен");
        yield return E(420, ResourceKind.Vpn, "vpn:Delta", "Тоннель Delta", EventKind.StatusChanged, "Offline", "Online", "Тоннель Delta снова доступен");
        yield return E(240, ResourceKind.Website, "site:Image Studio", "Image Studio", EventKind.StatusChanged, "Online", "Offline", "Image Studio недоступен");
        yield return E(236, ResourceKind.Website, "site:Image Studio", "Image Studio", EventKind.StatusChanged, "Offline", "Online", "Image Studio снова отвечает");
        yield return E(95, ResourceKind.Service, "service:Cedar:queue-worker", "Обработчик очереди", EventKind.StatusChanged, "Running", "Failed", "Обработчик очереди остановился с ошибкой");
        yield return E(40, ResourceKind.Server, "server:Ember", "Ember", EventKind.CheckError, null, null, "Обслуживание Ember: диск 87%");
    }
}
