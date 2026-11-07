using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.ViewModels;

/// <summary>One two-position switch on a server's card ("Режим ассистента: Assistant [Выключить]"). Not a health signal and
/// not silenceable: it only shows what the server reports and flips it on a confirmed button press.</summary>
public partial class SwitchRowViewModel : ObservableObject
{
    private readonly Func<SwitchRowViewModel, Task> _toggle;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string ServerName { get; }
    public string CategoryLabel { get; }
    public ServerResource Server { get; }
    public SwitchResource Source { get; }

    [ObservableProperty] private SwitchState state = SwitchState.Unknown;
    [ObservableProperty] private bool hasBeenChecked;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? lastError;

    public SwitchRowViewModel(ServerResource server, SwitchResource source, Func<SwitchRowViewModel, Task> toggle)
    {
        Server = server;
        Source = source;
        Id = source.Id;
        DisplayName = source.DisplayName;
        ServerName = server.Name;
        CategoryLabel = string.IsNullOrWhiteSpace(source.Category) ? "Переключатель" : source.Category;
        _toggle = toggle;
    }

    public string StateText => IsBusy ? "переключаю…"
        : !HasBeenChecked ? "Проверяю…"
        : State switch
        {
            SwitchState.On => Source.OnText,
            SwitchState.Off => Source.OffText,
            _ => "не удалось определить" + (string.IsNullOrWhiteSpace(LastError) ? string.Empty : " (" + LastError + ")")
        };

    /// <summary>The button offers the OTHER position.</summary>
    public string ButtonText => State == SwitchState.On ? "Выключить" : "Включить";

    public bool CanToggle => HasBeenChecked && State != SwitchState.Unknown && !IsBusy;

    public void ApplyStatus(SwitchStatus status)
    {
        State = status.State;
        LastError = status.LastError;
        HasBeenChecked = true;
        Refresh();
    }

    partial void OnIsBusyChanged(bool value) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(CanToggle));
        ToggleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private Task Toggle() => _toggle(this);
}
