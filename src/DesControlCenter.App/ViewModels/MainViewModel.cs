using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.App.Services;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;

namespace DesControlCenter.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PollingScheduler _scheduler;
    private readonly IServiceController _serviceController;
    private readonly IConfigStore _configStore;
    private readonly INetworkStatusService _networkStatusService;
    private readonly IDialogService _dialogService;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly IDesktopAppLauncher _appLauncher;
    private readonly SilenceRegistry _silences;
    private readonly ConnectivityState _connectivity;
    private readonly MaintenanceMonitor _maintenance;
    private readonly SwitchPoller _switchPoller;
    private readonly ISwitchController _switchController;
    private readonly Dictionary<Guid, SwitchRowViewModel> _switchRowsById = new();
    private DispatcherTimer? _silenceExpiryTimer;

    private AppConfig _config = new();

    // Lookups by resource id, kept in sync with the collections below, so a status tick doesn't
    // need to walk the hierarchy to find the row it belongs to.
    private readonly Dictionary<Guid, ServerRowViewModel> _serverRowsById = new();
    private readonly Dictionary<Guid, WebsiteRowViewModel> _websiteRowsById = new();
    private readonly Dictionary<Guid, ServiceRowViewModel> _serviceRowsById = new();
    private readonly Dictionary<Guid, VpnRowViewModel> _vpnRowsById = new();

    /// <summary>VPS → its services/VPN tunnels/sites, in that hierarchy for the dashboard.</summary>
    public ObservableCollection<ServerGroupViewModel> ServerGroups { get; } = new();

    /// <summary>Sites not tied to any of our VPS — third-party services we depend on but don't host.</summary>
    public ObservableCollection<WebsiteRowViewModel> ExternalServices { get; } = new();

    /// <summary>The same external services split into two fixed rows for the dashboard — AI
    /// services first, everything else (messengers) under them — so a wide window doesn't mix
    /// them into one wrapped strip.</summary>
    public ObservableCollection<WebsiteRowViewModel> ExternalAiServices { get; } = new();

    /// <summary>What the AI row actually shows: the checked AI sites first, then the local AI launchers (group "Ai"), mixed types on purpose.</summary>
    public ObservableCollection<object> ExternalAiItems { get; } = new();
    public ObservableCollection<WebsiteRowViewModel> ExternalOtherServices { get; } = new();

    /// <summary>The owner's own apps (pinned sites of servers) as a third row under the messengers: one click opens the installed PWA.</summary>
    public ObservableCollection<WebsiteRowViewModel> PinnedLauncherServices { get; } = new();

    /// <summary>Two more rows of plain open-the-app buttons under the pinned apps: hosting provider panels and AI API consoles (never polled).</summary>
    public ObservableCollection<LauncherLinkViewModel> ProviderLaunchers { get; } = new();
    public ObservableCollection<LauncherLinkViewModel> ApiLaunchers { get; } = new();

    /// <summary>Plain-text summary of everything currently at HealthLevel.Problem, recomputed
    /// alongside OverallHealth — drives the always-visible top banner, so "click tray icon" shows
    /// what's broken immediately without hunting for it in the columns below.</summary>
    public ObservableCollection<string> Problems { get; } = new();

    [ObservableProperty] private HealthLevel overallHealth = HealthLevel.Ok;

    /// <summary>How many current problems are silenced (not in <see cref="Problems"/>, not in the tray colour).</summary>
    /// <summary>True while this computer has no internet — shows the calm banner instead of a wall of red.</summary>
    [ObservableProperty] private bool isLocalNetworkOutage;

    /// <summary>What the calm banner says: no internet, or "everything failed at once, so probably our side".</summary>
    [ObservableProperty] private string localNetworkOutageText = string.Empty;

    [ObservableProperty] private int silencedProblemCount;
    [ObservableProperty] private string silencedProblemsText = string.Empty;
    [ObservableProperty] private string? publicIpAddress;
    [ObservableProperty] private bool internetAvailable;

    /// <summary>Fired whenever OverallHealth is recomputed, so the tray icon can react without polling this VM.</summary>
    public event EventHandler? OverallHealthChanged;

    public MainViewModel(
        PollingScheduler scheduler,
        IServiceController serviceController,
        IConfigStore configStore,
        INetworkStatusService networkStatusService,
        IDialogService dialogService,
        ITerminalLauncher terminalLauncher,
        IDesktopAppLauncher appLauncher,
        SilenceRegistry silences,
        ConnectivityState connectivity,
        MaintenanceMonitor maintenance,
        SwitchPoller switchPoller,
        ISwitchController switchController)
    {
        _switchPoller = switchPoller;
        _switchController = switchController;
        _switchPoller.Checked += (_, e) => RunOnUi(() =>
        {
            if (_switchRowsById.TryGetValue(e.Switch.Id, out var row))
            {
                row.ApplyStatus(e.Status);
            }
        });
        _scheduler = scheduler;
        _serviceController = serviceController;
        _configStore = configStore;
        _appLauncher = appLauncher;
        _silences = silences;
        _connectivity = connectivity;
        _maintenance = maintenance;
        _maintenance.Checked += (_, e) => RunOnUi(() =>
        {
            if (_serverRowsById.TryGetValue(e.Server.Id, out var row))
            {
                row.ApplyMaintenance(e.Status);
            }
        });
        _connectivity.Changed += (_, _) => RunOnUi(RecomputeOverallHealth);
        _networkStatusService = networkStatusService;
        _dialogService = dialogService;
        _terminalLauncher = terminalLauncher;

        _scheduler.ServerChecked += (_, e) => RunOnUi(() =>
        {
            if (_serverRowsById.TryGetValue(e.Server.Id, out var row))
            {
                row.ApplyStatus(e.Status);
                row.RefreshSilence();
            }

            RecomputeOverallHealth();
        });

        _scheduler.WebsiteChecked += (_, e) => RunOnUi(() =>
        {
            if (_websiteRowsById.TryGetValue(e.Website.Id, out var row))
            {
                row.ApplyStatus(e.Status);
                row.RefreshSilence();
            }

            RecomputeOverallHealth();
        });

        _scheduler.ServiceChecked += (_, e) => RunOnUi(() =>
        {
            if (_serviceRowsById.TryGetValue(e.Service.Id, out var row))
            {
                row.ApplyStatus(e.Status);
                row.RefreshSilence();
            }

            RecomputeOverallHealth();
        });

        _scheduler.VpnChecked += (_, e) => RunOnUi(() =>
        {
            if (_vpnRowsById.TryGetValue(e.Vpn.Id, out var row))
            {
                row.ApplyStatus(e.Status);
                row.RefreshSilence();
            }

            RecomputeOverallHealth();
        });
    }

    public async Task InitializeAsync(AppConfig config)
    {
        _config = config;
        RebuildRows();

        _silences.Changed += (_, _) => RunOnUi(OnSilencesChanged);
        // Timed silences lapse on their own; sweep every 30 s so the tray colour and the cards
        // follow without waiting for the next check of some resource.
        _silenceExpiryTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background,
            (_, _) => _silences.PurgeExpired(), Application.Current.Dispatcher);
        _silenceExpiryTimer.Start();
        _silences.PurgeExpired();
        await RefreshNetworkStatusAsync();
    }

    /// <summary>Reloads config from disk (after Settings saved it) and restarts polling to match.</summary>
    public async Task ReloadConfigAsync()
    {
        _config = await _configStore.LoadAsync();
        RebuildRows();
        await _scheduler.RestartAsync(_config);
        await _switchPoller.RestartAsync(_config);
        RecomputeOverallHealth();
    }

    private void RebuildRows()
    {
        // A settings save rebuilds every card; keep the ones the owner had opened open.
        var expandedServers = ServerGroups.Where(g => g.IsExpanded).Select(g => g.Server.Id).ToHashSet();
        ServerGroups.Clear();
        ExternalServices.Clear();
        ExternalAiServices.Clear();
        ExternalAiItems.Clear();
        ExternalOtherServices.Clear();
        PinnedLauncherServices.Clear();
        ProviderLaunchers.Clear();
        ApiLaunchers.Clear();
        _serverRowsById.Clear();
        _websiteRowsById.Clear();
        _serviceRowsById.Clear();
        _vpnRowsById.Clear();
        _switchRowsById.Clear();

        var groupsByServerId = new Dictionary<Guid, ServerGroupViewModel>();
        foreach (var server in _config.Servers.Where(s => s.Enabled))
        {
            var serverRow = new ServerRowViewModel(server, _silences);
            _serverRowsById[server.Id] = serverRow;

            var group = new ServerGroupViewModel(serverRow) { IsExpanded = expandedServers.Contains(server.Id) };
            groupsByServerId[server.Id] = group;
            ServerGroups.Add(group);
        }

        foreach (var service in _config.Services.Where(s => s.Enabled))
        {
            if (!groupsByServerId.TryGetValue(service.ServerId, out var group))
            {
                continue;
            }

            var serverRow = group.Server;
            var serviceRow = new ServiceRowViewModel(serverRow.Source, service, ExecuteServiceActionAsync, _silences);
            _serviceRowsById[service.Id] = serviceRow;
            group.Children.Add(serviceRow);
        }

        foreach (var vpn in _config.Vpns.Where(v => v.Enabled))
        {
            if (!groupsByServerId.TryGetValue(vpn.ServerId, out var group))
            {
                continue;
            }

            var vpnRow = new VpnRowViewModel(group.Server.Source, vpn, _silences);
            _vpnRowsById[vpn.Id] = vpnRow;
            group.Children.Add(vpnRow);
        }

        foreach (var sw in _config.Switches.Where(s => s.Enabled))
        {
            if (!groupsByServerId.TryGetValue(sw.ServerId, out var group))
            {
                continue;
            }

            var switchRow = new SwitchRowViewModel(group.Server.Source, sw, ToggleSwitchAsync);
            if (_switchPoller.GetStatus(sw.Id) is { } known)
            {
                switchRow.ApplyStatus(known);
            }

            _switchRowsById[sw.Id] = switchRow;
            group.Children.Add(switchRow);
        }

        foreach (var website in _config.Websites.Where(w => w.Enabled))
        {
            var websiteRow = new WebsiteRowViewModel(website, _appLauncher, _silences);
            _websiteRowsById[website.Id] = websiteRow;

            if (website.ServerId is { } serverId && groupsByServerId.TryGetValue(serverId, out var group))
            {
                group.Children.Add(websiteRow);

                // Also a launcher card in its own row under the messengers. Deliberately NOT in ExternalServices (that
                // list feeds the problems banner): the site is already reported through its server.
                if (website.PinToLauncher)
                {
                    PinnedLauncherServices.Add(websiteRow);
                }
            }
            else
            {
                ExternalServices.Add(websiteRow);
                var isAi = website.Category?.StartsWith("AI", StringComparison.OrdinalIgnoreCase) == true;
                (isAi ? ExternalAiServices : ExternalOtherServices).Add(websiteRow);
                if (isAi)
                {
                    ExternalAiItems.Add(websiteRow);
                }
            }
        }

        RebuildLaunchers();
    }

    private void RebuildLaunchers()
    {
        foreach (var link in _config.Launchers.Where(l => l.Enabled))
        {
            var row = new LauncherLinkViewModel(link, _appLauncher);
            if (string.Equals(link.Group, LauncherGroups.Ai, StringComparison.OrdinalIgnoreCase))
            {
                ExternalAiItems.Add(row);
                continue;
            }

            (string.Equals(link.Group, LauncherGroups.Api, StringComparison.OrdinalIgnoreCase) ? ApiLaunchers : ProviderLaunchers).Add(row);
        }
    }

    private void RecomputeOverallHealth()
    {
        OverallHealth = ResourceHealthAggregator.ComputeOverall(_config, _scheduler, _silences, _connectivity);
        IsLocalNetworkOutage = _connectivity.AlarmsSuppressed;
        LocalNetworkOutageText = _connectivity.IsOffline
            ? "Нет связи с интернетом на этом компьютере. Тревоги по серверам и сервисам приостановлены — как только связь вернётся, всё перепроверится само."
            : "Почти всё недоступно одновременно — похоже на сбой связи у этого компьютера (например, переключение VPN), а не на падение серверов. Тревоги приостановлены, всё перепроверится само.";
        RecomputeProblems();
        OverallHealthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecomputeProblems()
    {
        var found = new List<string>();
        var silencedFound = new List<string>();

        // This computer is offline: the cards still show what they last saw, but nothing is
        // reported as a problem — see ConnectivityState.
        if (_connectivity.AlarmsSuppressed)
        {
            SilencedProblemCount = 0;
            SilencedProblemsText = string.Empty;
            if (Problems.Count > 0)
            {
                Problems.Clear();
            }

            return;
        }

        // Silenced problems still exist (and show on their cards) but don't go to the red banner.
        void Add(bool silenced, string text) => (silenced ? silencedFound : found).Add(text);

        foreach (var group in ServerGroups)
        {
            var server = group.Server;
            if (server.HasBeenChecked && !server.IsOnline)
            {
                Add(server.IsSilenced, $"{server.Name} (VPS) недоступен");
            }

            foreach (var child in group.Children)
            {
                switch (child)
                {
                    case ServiceRowViewModel svc when svc.Health == HealthLevel.Problem:
                        Add(svc.IsSilenced, $"{svc.DisplayName} упал");
                        break;
                    case VpnRowViewModel vpn when vpn.Health == HealthLevel.Problem:
                        Add(vpn.IsSilenced, $"{vpn.DisplayName} недоступен");
                        break;
                    case WebsiteRowViewModel site when site.Health == HealthLevel.Problem:
                        Add(site.IsSilenced, $"{site.Name} не отвечает");
                        break;
                }
            }
        }

        foreach (var external in ExternalServices.Where(e => e.Health == HealthLevel.Problem))
        {
            Add(external.IsSilenced, $"{external.Name} недоступен");
        }

        SilencedProblemCount = silencedFound.Count;
        SilencedProblemsText = silencedFound.Count == 0
            ? string.Empty
            : "Приглушено:" + Environment.NewLine + string.Join(Environment.NewLine, silencedFound);

        if (Problems.SequenceEqual(found))
        {
            return;
        }

        Problems.Clear();
        foreach (var p in found)
        {
            Problems.Add(p);
        }
    }

    private void OnSilencesChanged()
    {
        foreach (var row in _serverRowsById.Values) { row.RefreshSilence(); }
        foreach (var row in _websiteRowsById.Values) { row.RefreshSilence(); }
        foreach (var row in _serviceRowsById.Values) { row.RefreshSilence(); }
        foreach (var row in _vpnRowsById.Values) { row.RefreshSilence(); }
        RecomputeOverallHealth();
    }

    [RelayCommand]
    private void UnsilenceAll() => _silences.UnsilenceAll();

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in ServerGroups)
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in ServerGroups)
        {
            group.IsExpanded = false;
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }

    /// <summary>The configuration currently in effect (for background jobs that follow settings changes).</summary>
    public AppConfig CurrentConfig => _config;

    [RelayCommand]
    public async Task CheckAllNowAsync()
    {
        // Housekeeping facts are slow to change; refresh them in the background without delaying the main check.
        _ = Task.Run(async () =>
        {
            try
            {
                await _maintenance.CheckAllAsync(_config);
            }
            catch
            {
            }
        });

        await _scheduler.CheckAllNowAsync(_config);
    }

    [RelayCommand]
    private void OpenSsh(ServerRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _terminalLauncher.OpenSshSession(row.Source);
    }

    [RelayCommand]
    private Task RecheckServer(ServerRowViewModel? row) =>
        row is null ? Task.CompletedTask : _scheduler.CheckServerNowAsync(row.Source);

    [RelayCommand]
    private Task RecheckVpn(VpnRowViewModel? row) =>
        row is null ? Task.CompletedTask : _scheduler.CheckVpnNowAsync(row.Server, row.Source);

    [RelayCommand]
    private Task RecheckWebsite(WebsiteRowViewModel? row) =>
        row is null ? Task.CompletedTask : _scheduler.CheckWebsiteNowAsync(row.Source);

    private async Task ExecuteServiceActionAsync(ServiceRowViewModel row, ControlAction action)
    {
        var verbRu = action switch
        {
            ControlAction.Start => "Запустить",
            ControlAction.Stop => "Остановить",
            _ => "Перезапустить"
        };

        if (!_dialogService.Confirm("Подтверждение", $"{verbRu} «{row.DisplayName}» на {row.ServerName}?"))
        {
            return;
        }

        var outcome = action switch
        {
            ControlAction.Start => await _serviceController.StartAsync(row.Server, row.Source),
            ControlAction.Stop => await _serviceController.StopAsync(row.Server, row.Source),
            _ => await _serviceController.RestartAsync(row.Server, row.Source)
        };

        if (!outcome.Success)
        {
            _dialogService.ShowInfo("Ошибка", outcome.Message);
        }

        await _scheduler.RecordUserActionAsync(
            ResourceKind.Service, row.Source.Id, row.DisplayName, row.Source.ProjectId,
            outcome.Success ? $"{verbRu}: успешно" : $"{verbRu}: ошибка — {outcome.Message}");

        // Reflect the new state immediately instead of waiting for the next scheduled poll tick.
        await _scheduler.CheckServiceNowAsync(row.Server, row.Source);
    }

    /// <summary>The switch button: confirm, flip over SSH, journal the press, re-read the position.</summary>
    private async Task ToggleSwitchAsync(SwitchRowViewModel row)
    {
        var turnOn = row.State != Core.Status.SwitchState.On;
        var target = turnOn ? row.Source.OnText : row.Source.OffText;
        if (!_dialogService.Confirm("Подтверждение", $"Переключить «{row.DisplayName}» на «{target}» ({row.ServerName})?"))
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            var outcome = await _switchController.SetAsync(row.Server, row.Source, turnOn);
            if (!outcome.Success)
            {
                _dialogService.ShowInfo("Ошибка", outcome.Message);
            }

            await _scheduler.RecordUserActionAsync(
                ResourceKind.Switch, row.Source.Id, row.DisplayName, null,
                outcome.Success ? $"«{row.DisplayName}»: {target}" : $"«{row.DisplayName}» → {target}: ошибка — {outcome.Message}");
        }
        finally
        {
            row.IsBusy = false;
        }

        await _switchPoller.CheckNowAsync(row.Server, row.Source);
    }

    private async Task RefreshNetworkStatusAsync()
    {
        var status = await _networkStatusService.CheckAsync();
        InternetAvailable = status.InternetAvailable;
        PublicIpAddress = status.PublicIpAddress;
    }
}
