using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;

namespace DesControlCenter.App.ViewModels;

/// <summary>
/// Common "silence this" behaviour for every kind of card row (server, site, service, VPN).
/// The row keeps showing its real state; silencing only takes it out of the tray colour, the
/// problems banner and toasts (see <see cref="SilenceRegistry"/>).
/// </summary>
public abstract partial class SilenceableRowViewModel : ObservableObject
{
    private readonly SilenceRegistry _silences;
    private readonly Guid _resourceId;
    private readonly Guid? _serverId;

    protected SilenceableRowViewModel(SilenceRegistry silences, Guid resourceId, Guid? serverId)
    {
        _silences = silences;
        _resourceId = resourceId;
        _serverId = serverId;
    }

    /// <summary>The resource's own health right now (not affected by silencing).</summary>
    protected abstract HealthLevel CurrentHealth { get; }

    /// <summary>Silenced on its own or because the server it runs on is silenced.</summary>
    public bool IsSilenced => _silences.IsSilenced(_resourceId, _serverId);

    /// <summary>Silenced on this very row (the button state); false when only the server is.</summary>
    public bool IsSilencedDirectly => _silences.Get(_resourceId) is not null;

    /// <summary>"Until recovery" only makes sense for something that is currently not fine.</summary>
    public bool CanSilenceUntilRecovery => CurrentHealth != HealthLevel.Ok;

    /// <summary>One line shown on the card while silenced, e.g. "🔕 приглушено до 15:26"; empty otherwise.</summary>
    public string SilenceNote => _silences.Get(_resourceId) is { } silence
        ? "🔕 приглушено " + Describe(silence)
        : _serverId is { } sid && _silences.IsSilenced(sid) ? "🔕 приглушён сервер" : string.Empty;

    public string SilenceToolTip => _silences.Get(_resourceId) is { } silence
        ? "Оповещения приглушены " + Describe(silence) + ". Карточка показывает реальное состояние."
        : _serverId is { } sid && _silences.IsSilenced(sid)
            ? "Приглушён сервер, на котором это работает."
            : "Приглушить оповещения (трей, баннер, уведомления)";

    public void RefreshSilence()
    {
        OnPropertyChanged(nameof(IsSilenced));
        OnPropertyChanged(nameof(IsSilencedDirectly));
        OnPropertyChanged(nameof(CanSilenceUntilRecovery));
        OnPropertyChanged(nameof(SilenceNote));
        OnPropertyChanged(nameof(SilenceToolTip));
    }

    [RelayCommand]
    private void SilenceUntilRecovery() => _silences.Silence(_resourceId, SilenceMode.UntilRecovery);

    [RelayCommand]
    private void SilenceOneHour() => _silences.Silence(_resourceId, SilenceMode.Until, DateTimeOffset.UtcNow.AddHours(1));

    [RelayCommand]
    private void SilenceEightHours() => _silences.Silence(_resourceId, SilenceMode.Until, DateTimeOffset.UtcNow.AddHours(8));

    [RelayCommand]
    private void SilenceOneDay() => _silences.Silence(_resourceId, SilenceMode.Until, DateTimeOffset.UtcNow.AddDays(1));

    [RelayCommand]
    private void SilenceForever() => _silences.Silence(_resourceId, SilenceMode.Forever);

    [RelayCommand]
    private void Unsilence() => _silences.Unsilence(_resourceId);

    private static string Describe(Silence silence)
    {
        switch (silence.Mode)
        {
            case SilenceMode.UntilRecovery:
                return "до восстановления";
            case SilenceMode.Forever:
                return "навсегда";
            default:
                var local = (silence.UntilUtc ?? DateTimeOffset.UtcNow).ToLocalTime();
                return local.Date == DateTimeOffset.Now.Date ? $"до {local:HH:mm}" : $"до {local:dd.MM HH:mm}";
        }
    }
}
