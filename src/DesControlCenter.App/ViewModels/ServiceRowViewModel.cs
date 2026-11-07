using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.ViewModels;

public partial class ServiceRowViewModel : SilenceableRowViewModel
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public string UnitName { get; }
    public string ServerName { get; }
    public string CategoryLabel { get; }

    [ObservableProperty] private ServiceState state = ServiceState.Unknown;
    [ObservableProperty] private DateTimeOffset? activeSinceUtc;
    [ObservableProperty] private DateTimeOffset? lastCheckedUtc;
    [ObservableProperty] private string? lastError;
    [ObservableProperty] private bool isBusy;

    public ServerResource Server { get; }
    public ServiceResource Source { get; }

    protected override HealthLevel CurrentHealth => Health;

    private readonly Func<ServiceRowViewModel, ControlAction, Task> _executeAction;

    public ServiceRowViewModel(ServerResource server, ServiceResource service, Func<ServiceRowViewModel, ControlAction, Task> executeAction, SilenceRegistry silences) : base(silences, service.Id, server.Id)
    {
        Server = server;
        Source = service;
        Id = service.Id;
        DisplayName = service.DisplayName;
        UnitName = service.UnitName;
        ServerName = server.Name;
        CategoryLabel = string.IsNullOrWhiteSpace(service.Category) ? "Системный сервис" : service.Category;
        _executeAction = executeAction;
    }

    /// <summary>Not looked at yet (first check still running) is shown as such, not as the raw enum "Unknown".</summary>
    public bool IsPending => State == ServiceState.Unknown && LastCheckedUtc is null;

    public string StateDisplay => IsPending ? "Проверяю…" : State.ToString();

    public HealthLevel Health => State switch
    {
        ServiceState.Running => HealthLevel.Ok,
        ServiceState.Stopped => HealthLevel.Warning,
        ServiceState.Failed => HealthLevel.Problem,
        _ => HealthLevel.Warning
    };

    public void ApplyStatus(ServiceStatus status)
    {
        State = status.State;
        ActiveSinceUtc = status.ActiveSinceUtc;
        LastCheckedUtc = status.LastCheckedUtc;
        LastError = status.LastError;
        OnPropertyChanged(nameof(Health));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(StateDisplay));
        OnPropertyChanged(nameof(ActiveSinceDisplay));
    }

    public string ActiveSinceDisplay
    {
        get
        {
            if (ActiveSinceUtc is not { } since)
            {
                return "—";
            }

            var elapsed = DateTimeOffset.UtcNow - since;
            var elapsedText = elapsed switch
            {
                { TotalDays: >= 1 } => $"{(int)elapsed.TotalDays}д {elapsed.Hours}ч назад",
                { TotalHours: >= 1 } => $"{(int)elapsed.TotalHours}ч {elapsed.Minutes}м назад",
                _ => $"{Math.Max(0, (int)elapsed.TotalMinutes)}м назад"
            };

            return $"работает с {since.ToLocalTime():dd.MM HH:mm} ({elapsedText})";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private Task Start() => RunAction(ControlAction.Start);

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private Task Stop() => RunAction(ControlAction.Stop);

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private Task Restart() => RunAction(ControlAction.Restart);

    private bool CanExecuteAction() => !IsBusy;

    private async Task RunAction(ControlAction action)
    {
        IsBusy = true;
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        try
        {
            await _executeAction(this, action);
        }
        finally
        {
            IsBusy = false;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            RestartCommand.NotifyCanExecuteChanged();
        }
    }
}
