using CommunityToolkit.Mvvm.ComponentModel;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.ViewModels;

public partial class VpnRowViewModel : SilenceableRowViewModel
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public string CategoryLabel => "VPN";

    [ObservableProperty] private bool isOnline;
    [ObservableProperty] private int peerCount;
    [ObservableProperty] private int peersConnectedNow;
    [ObservableProperty] private long totalReceivedBytes;
    [ObservableProperty] private long totalSentBytes;
    [ObservableProperty] private TimeSpan? mostRecentHandshakeAge;
    [ObservableProperty] private DateTimeOffset? lastCheckedUtc;
    [ObservableProperty] private string? lastError;
    [ObservableProperty] private bool hasBeenChecked;

    public ServerResource Server { get; }
    public VpnResource Source { get; }

    protected override HealthLevel CurrentHealth => Health;

    public VpnRowViewModel(ServerResource server, VpnResource vpn, SilenceRegistry silences) : base(silences, vpn.Id, server.Id)
    {
        Server = server;
        Source = vpn;
        Id = vpn.Id;
        DisplayName = vpn.DisplayName;
    }

    public HealthLevel Health => !HasBeenChecked ? HealthLevel.Ok : (IsOnline ? HealthLevel.Ok : HealthLevel.Problem);

    public void ApplyStatus(VpnStatus status)
    {
        IsOnline = status.IsOnline;
        PeerCount = status.PeerCount;
        PeersConnectedNow = status.PeersConnectedNow;
        TotalReceivedBytes = status.TotalReceivedBytes;
        TotalSentBytes = status.TotalSentBytes;
        MostRecentHandshakeAge = status.MostRecentHandshakeAge;
        LastCheckedUtc = status.LastCheckedUtc;
        LastError = status.LastError;
        HasBeenChecked = true;
        OnPropertyChanged(nameof(Health));
        OnPropertyChanged(nameof(PeersSummary));
        OnPropertyChanged(nameof(TrafficSummary));
    }

    public string PeersSummary
    {
        get
        {
            if (!HasBeenChecked)
            {
                return "Проверяю…";
            }

            if (!IsOnline)
            {
                return LastError ?? "недоступен";
            }

            var handshakeText = MostRecentHandshakeAge switch
            {
                null => "нет подключений",
                { } age when age < TimeSpan.FromMinutes(1) => "handshake только что",
                { } age when age < TimeSpan.FromHours(1) => $"handshake {(int)age.TotalMinutes}м назад",
                { } age when age < TimeSpan.FromDays(1) => $"handshake {(int)age.TotalHours}ч назад",
                { } age => $"handshake {(int)age.TotalDays}д назад"
            };

            return $"туннель поднят · {PeersConnectedNow}/{PeerCount} peer онлайн · {handshakeText}";
        }
    }

    public string TrafficSummary => HasBeenChecked && IsOnline
        ? $"↓{FormatBytes(TotalReceivedBytes)} / ↑{FormatBytes(TotalSentBytes)}"
        : "—";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} B"
    };
}
