using CommunityToolkit.Mvvm.ComponentModel;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.ViewModels;

public partial class ServerRowViewModel : SilenceableRowViewModel
{
    public Guid Id { get; }
    public string Name { get; }
    public string Host { get; }

    [ObservableProperty] private bool isOnline;
    [ObservableProperty] private double? pingMs;
    [ObservableProperty] private TimeSpan? uptime;
    [ObservableProperty] private double? cpuLoadPercent;
    [ObservableProperty] private double? loadAverage1Min;
    [ObservableProperty] private double? memoryUsedMb;
    [ObservableProperty] private double? memoryTotalMb;
    [ObservableProperty] private double? diskUsedGb;
    [ObservableProperty] private double? diskTotalGb;
    [ObservableProperty] private double? networkRxKbps;
    [ObservableProperty] private double? networkTxKbps;
    [ObservableProperty] private DateTimeOffset? lastCheckedUtc;
    [ObservableProperty] private string? lastError;
    [ObservableProperty] private bool hasBeenChecked;

    /// <summary>Amber line under the server header when housekeeping needs attention (empty = nothing to say).</summary>
    [ObservableProperty] private string maintenanceNote = string.Empty;
    [ObservableProperty] private string maintenanceToolTip = string.Empty;

    public ServerResource Source { get; }

    protected override HealthLevel CurrentHealth => !HasBeenChecked || IsOnline ? HealthLevel.Ok : HealthLevel.Problem;

    public ServerRowViewModel(ServerResource server, SilenceRegistry silences) : base(silences, server.Id, null)
    {
        Source = server;
        Id = server.Id;
        Name = server.Name;
        Host = server.Host;
    }

    public void ApplyMaintenance(MaintenanceStatus status)
    {
        var attention = status.Attention;
        MaintenanceNote = attention.Count == 0 ? string.Empty : "🔧 " + string.Join(" · ", attention);

        if (!status.Reachable)
        {
            MaintenanceToolTip = "Обслуживание: проверить не удалось (" + status.Error + ")";
            return;
        }

        var parts = new List<string> { "Проверено " + status.CheckedUtc.ToLocalTime().ToString("dd.MM HH:mm") };
        if (status.PendingUpdates is { } updates) { parts.Add("обновлений: " + updates); }
        if (status.RebootRequired is { } reboot) { parts.Add("перезагрузка: " + (reboot ? "нужна" : "не нужна")); }
        if (status.DiskUsedPercent is { } disk) { parts.Add("диск: " + disk + "%"); }
        if (status.Firewall is { } fw) { parts.Add("фаервол: " + fw); }
        if (status.BackupConfigured) { parts.Add(status.BackupAgeHours is { } age ? "бэкап: " + age.ToString("0") + " ч назад" : "бэкап: не найден"); }
        MaintenanceToolTip = string.Join("; ", parts);
    }

    public void ApplyStatus(ServerStatus status)
    {
        IsOnline = status.IsOnline;
        PingMs = status.PingMs;
        Uptime = status.Uptime;
        CpuLoadPercent = status.CpuLoadPercent;
        LoadAverage1Min = status.LoadAverage1Min;
        MemoryUsedMb = status.MemoryUsedMb;
        MemoryTotalMb = status.MemoryTotalMb;
        DiskUsedGb = status.DiskUsedGb;
        DiskTotalGb = status.DiskTotalGb;
        NetworkRxKbps = status.NetworkRxKbps;
        NetworkTxKbps = status.NetworkTxKbps;
        LastCheckedUtc = status.LastCheckedUtc;
        LastError = status.LastError;
        HasBeenChecked = true;

        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(PingDisplay));
        OnPropertyChanged(nameof(CpuDisplay));
        OnPropertyChanged(nameof(LoadDisplay));
        OnPropertyChanged(nameof(UptimeDisplay));
        OnPropertyChanged(nameof(MemoryDisplay));
        OnPropertyChanged(nameof(DiskDisplay));
        OnPropertyChanged(nameof(NetworkDisplay));
        OnPropertyChanged(nameof(CompactMetrics));
    }

    /// <summary>The one line a collapsed server card shows: "CPU 7% · RAM 1309/3811 MB · Disk 8.9/59 GB".</summary>
    public string CompactMetrics => $"CPU {CpuDisplay} · RAM {MemoryDisplay} · Disk {DiskDisplay}";

    public string OnlineText => !HasBeenChecked ? "Проверяю…" : IsOnline ? "Online" : "Offline";
    public string PingDisplay => PingMs is { } ping ? $"{ping:0.#} ms" : "—";
    public string CpuDisplay => CpuLoadPercent is { } cpu ? $"{cpu:0.#}%" : "—";
    public string LoadDisplay => LoadAverage1Min is { } load ? $"{load:0.00}" : "—";

    public string UptimeDisplay => Uptime switch
    {
        { } u when u.TotalDays >= 1 => $"{(int)u.TotalDays}д {u.Hours}ч",
        { } u => $"{u.Hours}ч {u.Minutes}м",
        null => "—"
    };

    public string MemoryDisplay => MemoryUsedMb is { } used && MemoryTotalMb is { } total
        ? $"{used:0}/{total:0} MB"
        : "—";

    public string DiskDisplay => DiskUsedGb is { } used && DiskTotalGb is { } total
        ? $"{used:0.#}/{total:0.#} GB"
        : "—";

    public string NetworkDisplay => NetworkRxKbps is { } rx && NetworkTxKbps is { } tx
        ? $"↓{FormatKbps(rx)} ↑{FormatKbps(tx)}"
        : "—";

    private static string FormatKbps(double kbps) => kbps >= 1024
        ? $"{kbps / 1024:0.#} MB/s"
        : $"{kbps:0.#} KB/s";
}
