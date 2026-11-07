using System.Collections.Concurrent;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Configuration;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;
using Microsoft.Extensions.Logging;

namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// Owns one independent polling loop per enabled resource. A failure or slow response from
/// one resource never delays or blocks the others (each loop swallows its own exceptions and
/// keeps ticking on its own configured interval). State transitions are the only things that
/// reach the event log and notifications; every tick still raises a "checked" event so the UI
/// can refresh live values like ping/response time.
/// </summary>
public sealed class PollingScheduler : IAsyncDisposable
{
    private readonly IServerMonitor _serverMonitor;
    private readonly IWebsiteMonitor _websiteMonitor;
    private readonly IServiceMonitor _serviceMonitor;
    private readonly IVpnMonitor _vpnMonitor;
    private readonly IEventStore _eventStore;
    private readonly INotificationService _notifications;
    private readonly SilenceRegistry? _silences;
    private readonly ConnectivityState? _connectivity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _dnsFailureSince = new();
    private readonly ILogger<PollingScheduler> _logger;

    private readonly ConcurrentDictionary<Guid, ServerStatus> _serverStatuses = new();
    private readonly ConcurrentDictionary<Guid, WebsiteStatus> _websiteStatuses = new();
    private readonly ConcurrentDictionary<Guid, ServiceStatus> _serviceStatuses = new();
    private readonly ConcurrentDictionary<Guid, VpnStatus> _vpnStatuses = new();

    private readonly List<(Task Loop, CancellationTokenSource Cts)> _loops = new();
    private IReadOnlyDictionary<Guid, Project> _projectsById = new Dictionary<Guid, Project>();

    // A resource must report the SAME new state this many consecutive polls before it's treated
    // as a real transition (logged + notified) — a single blip (e.g. one dropped SSH round trip)
    // no longer spams a notification. The live status shown in the UI is never delayed by this;
    // only the journal entry / toast is. Guids never collide across resource kinds, so one shared
    // dictionary is fine.
    private const int TransitionConfirmationThreshold = 2;
    private readonly ConcurrentDictionary<Guid, TransitionTracker> _transitionTracking = new();

    private readonly record struct TransitionTracker(string Confirmed, string Candidate, int Streak);

    public event EventHandler<ServerCheckedEventArgs>? ServerChecked;
    public event EventHandler<WebsiteCheckedEventArgs>? WebsiteChecked;
    public event EventHandler<ServiceCheckedEventArgs>? ServiceChecked;
    public event EventHandler<VpnCheckedEventArgs>? VpnChecked;

    public PollingScheduler(
        IServerMonitor serverMonitor,
        IWebsiteMonitor websiteMonitor,
        IServiceMonitor serviceMonitor,
        IVpnMonitor vpnMonitor,
        IEventStore eventStore,
        INotificationService notifications,
        ILogger<PollingScheduler> logger,
        SilenceRegistry? silences = null,
        ConnectivityState? connectivity = null,
        Func<DateTimeOffset>? clock = null)
    {
        _serverMonitor = serverMonitor;
        _websiteMonitor = websiteMonitor;
        _serviceMonitor = serviceMonitor;
        _vpnMonitor = vpnMonitor;
        _eventStore = eventStore;
        _notifications = notifications;
        _logger = logger;
        _silences = silences;
        _connectivity = connectivity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ServerStatus? GetServerStatus(Guid serverId) => _serverStatuses.GetValueOrDefault(serverId);
    public WebsiteStatus? GetWebsiteStatus(Guid websiteId) => _websiteStatuses.GetValueOrDefault(websiteId);
    public ServiceStatus? GetServiceStatus(Guid serviceId) => _serviceStatuses.GetValueOrDefault(serviceId);
    public VpnStatus? GetVpnStatus(Guid vpnId) => _vpnStatuses.GetValueOrDefault(vpnId);

    /// <summary>Stops any loops from a previous config and starts one loop per enabled resource.</summary>
    public async Task RestartAsync(AppConfig config)
    {
        await StopAsync();

        _projectsById = config.Projects.ToDictionary(p => p.Id);
        var serversById = config.Servers.ToDictionary(s => s.Id);

        foreach (var server in config.Servers.Where(s => s.Enabled))
        {
            StartLoop(server.PollIntervalSeconds, ct => PollServerAsync(server, ct));
        }

        foreach (var website in config.Websites.Where(w => w.Enabled))
        {
            StartLoop(website.PollIntervalSeconds, ct => PollWebsiteAsync(website, ct));
        }

        foreach (var service in config.Services.Where(s => s.Enabled))
        {
            if (!serversById.TryGetValue(service.ServerId, out var server))
            {
                _logger.LogWarning("Service {Service} references unknown server {ServerId}; skipping", service.DisplayName, service.ServerId);
                continue;
            }

            StartLoop(service.PollIntervalSeconds, ct => PollServiceAsync(server, service, ct));
        }

        foreach (var vpn in config.Vpns.Where(v => v.Enabled))
        {
            if (!serversById.TryGetValue(vpn.ServerId, out var server))
            {
                _logger.LogWarning("VPN {Vpn} references unknown server {ServerId}; skipping", vpn.DisplayName, vpn.ServerId);
                continue;
            }

            StartLoop(vpn.PollIntervalSeconds, ct => PollVpnAsync(server, vpn, ct));
        }
    }

    /// <summary>Runs every currently-scheduled resource once, outside of its normal timer tick ("Check all now").</summary>
    public async Task CheckAllNowAsync(AppConfig config)
    {
        var serversById = config.Servers.ToDictionary(s => s.Id);

        var tasks = new List<Task>();
        tasks.AddRange(config.Servers.Where(s => s.Enabled).Select(s => PollServerAsync(s, CancellationToken.None)));
        tasks.AddRange(config.Websites.Where(w => w.Enabled).Select(w => PollWebsiteAsync(w, CancellationToken.None)));
        tasks.AddRange(config.Services.Where(s => s.Enabled && serversById.ContainsKey(s.ServerId))
            .Select(s => PollServiceAsync(serversById[s.ServerId], s, CancellationToken.None)));
        tasks.AddRange(config.Vpns.Where(v => v.Enabled && serversById.ContainsKey(v.ServerId))
            .Select(v => PollVpnAsync(serversById[v.ServerId], v, CancellationToken.None)));

        await Task.WhenAll(tasks);
    }

    /// <summary>Re-checks one server outside its timer, e.g. right after the user opens its detail view.</summary>
    public Task CheckServerNowAsync(ServerResource server) => PollServerAsync(server, CancellationToken.None);

    /// <summary>Re-checks one website outside its timer.</summary>
    public Task CheckWebsiteNowAsync(Website website) => PollWebsiteAsync(website, CancellationToken.None);

    /// <summary>Re-checks one service right after a manual start/stop/restart, so the UI doesn't wait for the next tick.</summary>
    public Task CheckServiceNowAsync(ServerResource server, ServiceResource service) => PollServiceAsync(server, service, CancellationToken.None);

    /// <summary>Re-checks one VPN tunnel outside its timer.</summary>
    public Task CheckVpnNowAsync(ServerResource server, VpnResource vpn) => PollVpnAsync(server, vpn, CancellationToken.None);

    private void StartLoop(int intervalSeconds, Func<CancellationToken, Task> checkOnce)
    {
        var cts = new CancellationTokenSource();
        var interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));
        var loop = Task.Run(() => RunLoopAsync(interval, checkOnce, cts.Token), cts.Token);
        _loops.Add((loop, cts));
    }

    private async Task RunLoopAsync(TimeSpan interval, Func<CancellationToken, Task> checkOnce, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (true)
        {
            try
            {
                await checkOnce(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in polling loop");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollServerAsync(ServerResource server, CancellationToken cancellationToken)
    {
        ServerStatus status;
        try
        {
            status = await _serverMonitor.CheckAsync(server, cancellationToken);
        }
        catch (Exception ex)
        {
            status = ServerStatus.Offline(ex.Message);
        }

        _serverStatuses[server.Id] = status;
        ServerChecked?.Invoke(this, new ServerCheckedEventArgs(server, status));
        ReportCheckSummary();

        // This computer has lost its own connection (or everything failed at once): the failure says nothing about the server.
        if (!status.IsOnline && _connectivity?.AlarmsSuppressed == true)
        {
            return;
        }

        var newState = status.IsOnline ? "Online" : "Offline";
        if (TryConfirmTransition(server.Id, newState, out var oldState))
        {
            await RecordTransitionAsync(
                ResourceKind.Server, server.Id, server.Name, server.ProjectId, null,
                oldState, newState,
                status.IsOnline ? HealthLevel.Ok : HealthLevel.Problem,
                status.IsOnline ? $"{server.Name} снова в сети" : $"{server.Name} недоступен");
        }
    }

    private async Task PollWebsiteAsync(Website website, CancellationToken cancellationToken)
    {
        WebsiteStatus status;
        try
        {
            status = await _websiteMonitor.CheckAsync(website, cancellationToken);
        }
        catch (Exception ex)
        {
            status = WebsiteStatus.Offline(ex.Message);
        }

        // "The address could not be found" is as likely to be a hiccup of the owner's own DNS/network as a
        // dead site. Remember when the run of such failures began: for a while it is only a yellow note,
        // and only if it persists (WebsiteStatus.DnsGrace) does it become a real alarm.
        if (!status.IsOnline && status.DnsFailure)
        {
            var since = _dnsFailureSince.GetOrAdd(website.Id, _ => _clock());
            status = status.WithDnsFailureSince(since);
        }
        else
        {
            _dnsFailureSince.TryRemove(website.Id, out _);
        }

        _websiteStatuses[website.Id] = status;
        WebsiteChecked?.Invoke(this, new WebsiteCheckedEventArgs(website, status));
        ReportCheckSummary();

        // This computer has lost its own connection (or everything failed at once): the failure says nothing about the site.
        if (!status.IsOnline && _connectivity?.AlarmsSuppressed == true)
        {
            return;
        }

        if (status.IsTransientDnsFailure(_clock()))
        {
            return;
        }

        var newState = status.IsOnline ? "Online" : "Offline";
        if (TryConfirmTransition(website.Id, newState, out var oldState))
        {
            await RecordTransitionAsync(
                ResourceKind.Website, website.Id, website.Name, website.ProjectId, website.ServerId,
                oldState, newState,
                status.IsOnline ? HealthLevel.Ok : HealthLevel.Problem,
                status.IsOnline ? $"{website.Name} снова отвечает" : $"{website.Name} не отвечает");
        }
    }

    private async Task PollServiceAsync(ServerResource server, ServiceResource service, CancellationToken cancellationToken)
    {
        ServiceStatus status;
        try
        {
            status = await _serviceMonitor.CheckAsync(server, service, cancellationToken);
        }
        catch (Exception ex)
        {
            status = ServiceStatus.Unknown(ex.Message);
        }

        _serviceStatuses[service.Id] = status;
        ServiceChecked?.Invoke(this, new ServiceCheckedEventArgs(server, service, status));
        ReportCheckSummary();

        // This computer has lost its own connection (or everything failed at once): the failure says nothing about the service.
        if (status.State != ServiceState.Running && _connectivity?.AlarmsSuppressed == true)
        {
            return;
        }

        var newState = status.State.ToString();
        if (TryConfirmTransition(service.Id, newState, out var oldState))
        {
            var level = status.State switch
            {
                ServiceState.Running => HealthLevel.Ok,
                ServiceState.Stopped => HealthLevel.Warning,
                ServiceState.Failed => HealthLevel.Problem,
                _ => HealthLevel.Warning
            };

            await RecordTransitionAsync(
                ResourceKind.Service, service.Id, service.DisplayName, service.ProjectId, service.ServerId,
                oldState, newState, level,
                $"{service.DisplayName}: {oldState} → {newState}");
        }
    }

    private async Task PollVpnAsync(ServerResource server, VpnResource vpn, CancellationToken cancellationToken)
    {
        VpnStatus status;
        try
        {
            status = await _vpnMonitor.CheckAsync(server, vpn, cancellationToken);
        }
        catch (Exception ex)
        {
            status = VpnStatus.Offline(ex.Message);
        }

        _vpnStatuses[vpn.Id] = status;
        VpnChecked?.Invoke(this, new VpnCheckedEventArgs(server, vpn, status));
        ReportCheckSummary();

        // This computer has lost its own connection (or everything failed at once): the failure says nothing about the VPN.
        if (!status.IsOnline && _connectivity?.AlarmsSuppressed == true)
        {
            return;
        }

        var newState = status.IsOnline ? "Online" : "Offline";
        if (TryConfirmTransition(vpn.Id, newState, out var oldState))
        {
            await RecordTransitionAsync(
                ResourceKind.Vpn, vpn.Id, vpn.DisplayName, vpn.ProjectId, vpn.ServerId,
                oldState, newState,
                status.IsOnline ? HealthLevel.Ok : HealthLevel.Problem,
                status.IsOnline ? $"{vpn.DisplayName} снова поднят" : $"{vpn.DisplayName} недоступен");
        }
    }

    /// <summary>
    /// Tells <see cref="ConnectivityState"/> how much of what we watch currently fails. When almost all of it fails
    /// in the same moment, the likelier culprit is this computer's own network path (a VPN switch, a router), not
    /// a dozen independent servers — so the failures stop being alarms for a while.
    /// </summary>
    private void ReportCheckSummary()
    {
        if (_connectivity is null)
        {
            return;
        }

        var total = _serverStatuses.Count + _websiteStatuses.Count + _serviceStatuses.Count + _vpnStatuses.Count;
        var failing = _serverStatuses.Values.Count(s => !s.IsOnline)
                      + _websiteStatuses.Values.Count(s => !s.IsOnline)
                      + _serviceStatuses.Values.Count(s => s.State != ServiceState.Running)
                      + _vpnStatuses.Values.Count(s => !s.IsOnline);
        _connectivity.ReportCheckSummary(total, failing);
    }

    /// <summary>
    /// Returns true only once a resource's new state has been seen
    /// <see cref="TransitionConfirmationThreshold"/> polls in a row and differs from what was last
    /// confirmed — i.e. a real, debounced transition. <paramref name="previousConfirmed"/> is the
    /// state to log as "from". The very first check for a resource just establishes a silent
    /// baseline (matches the old "previous is not null" guard).
    /// </summary>
    private bool TryConfirmTransition(Guid resourceId, string newState, out string previousConfirmed)
    {
        var current = _transitionTracking.GetOrAdd(resourceId, _ => new TransitionTracker(newState, newState, 0));
        previousConfirmed = current.Confirmed;

        if (current.Confirmed == newState)
        {
            if (current.Streak != 0)
            {
                _transitionTracking[resourceId] = current with { Candidate = newState, Streak = 0 };
            }

            return false;
        }

        var streak = current.Candidate == newState ? current.Streak + 1 : 1;
        if (streak >= TransitionConfirmationThreshold)
        {
            _transitionTracking[resourceId] = new TransitionTracker(newState, newState, 0);
            return true;
        }

        _transitionTracking[resourceId] = current with { Candidate = newState, Streak = streak };
        return false;
    }

    /// <summary>Logs a manual button press (Start/Stop/Restart, ...) to the journal —
    /// EventKind.UserAction — with its outcome. Never triggers a toast: the user is already
    /// looking at the button they just pressed.</summary>
    public Task RecordUserActionAsync(ResourceKind kind, Guid resourceId, string resourceName, Guid? projectId, string message)
    {
        _projectsById.TryGetValue(projectId ?? Guid.Empty, out var project);

        var entry = new EventLogEntry
        {
            ResourceKind = kind,
            ResourceId = resourceId,
            ResourceName = resourceName,
            ProjectId = projectId,
            ProjectName = project?.Name,
            EventKind = EventKind.UserAction,
            Message = message
        };

        return _eventStore.AddAsync(entry);
    }

    private async Task RecordTransitionAsync(ResourceKind kind, Guid resourceId, string resourceName, Guid? projectId, Guid? serverId,
        string oldState, string newState, HealthLevel level, string notificationMessage)
    {
        _projectsById.TryGetValue(projectId ?? Guid.Empty, out var project);

        var entry = new EventLogEntry
        {
            ResourceKind = kind,
            ResourceId = resourceId,
            ResourceName = resourceName,
            ProjectId = projectId,
            ProjectName = project?.Name,
            EventKind = EventKind.StatusChanged,
            OldState = oldState,
            NewState = newState,
            Message = notificationMessage
        };

        await _eventStore.AddAsync(entry);

        // Recovery lifts an "until recovery" silence first, so the "is back" toast still shows.
        if (level == HealthLevel.Ok)
        {
            _silences?.NotifyRecovered(resourceId);
        }

        // Still journalled above, but a silenced resource (or one on a silenced server) doesn't pop a toast.
        if (_silences?.IsSilenced(resourceId, serverId) != true)
        {
            _notifications.NotifyStatusChange(resourceName, notificationMessage, level);
        }
    }

    public async Task StopAsync()
    {
        foreach (var (_, cts) in _loops)
        {
            cts.Cancel();
        }

        foreach (var (loop, cts) in _loops)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            finally
            {
                cts.Dispose();
            }
        }

        _loops.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
