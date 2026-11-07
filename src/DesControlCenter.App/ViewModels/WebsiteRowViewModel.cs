using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Monitoring;
using DesControlCenter.Core.Status;

namespace DesControlCenter.App.ViewModels;

public partial class WebsiteRowViewModel : SilenceableRowViewModel
{
    public Guid Id { get; }
    public string Name { get; }
    public string Url { get; }
    public string CategoryLabel { get; }

    /// <summary>True for a third-party site under External Services (no ServerId — not one of
    /// our VPS). Its health is deliberately binary: see <see cref="Health"/>.</summary>
    public bool IsExternal { get; }

    private readonly int _sslWarningDays;
    private readonly IDesktopAppLauncher _appLauncher;

    [ObservableProperty] private bool isOnline;
    [ObservableProperty] private int? httpStatusCode;
    [ObservableProperty] private double? responseTimeMs;
    [ObservableProperty] private DateTimeOffset? sslExpiresUtc;
    [ObservableProperty] private DateTimeOffset? lastCheckedUtc;
    [ObservableProperty] private string? lastError;
    [ObservableProperty] private bool dnsFailure;
    [ObservableProperty] private DateTimeOffset? dnsFailureSince;
    [ObservableProperty] private bool hasBeenChecked;

    public Website Source { get; }

    protected override HealthLevel CurrentHealth => Health;

    public WebsiteRowViewModel(Website website, IDesktopAppLauncher appLauncher, SilenceRegistry silences) : base(silences, website.Id, website.ServerId)
    {
        Source = website;
        Id = website.Id;
        Name = website.Name;
        Url = website.Url;
        CategoryLabel = string.IsNullOrWhiteSpace(website.Category) ? "Сайт" : website.Category;
        IsExternal = website.ServerId is null;
        _sslWarningDays = website.SslWarningDays;
        _appLauncher = appLauncher;
    }

    public HealthLevel Health
    {
        get
        {
            if (!HasBeenChecked)
            {
                return HealthLevel.Ok;
            }

            if (!IsOnline)
            {
                return IsTransientDns ? HealthLevel.Warning : HealthLevel.Problem;
            }

            // External Services only ever need to answer "up or down" — a 403/401 from a
            // third-party API still means it's reachable, and its own SSL cert isn't ours to
            // warn about. Reserve the yellow in-between state for our own sites.
            if (IsExternal)
            {
                return HealthLevel.Ok;
            }

            if (SslExpiresUtc is { } expires && expires - DateTimeOffset.UtcNow <= TimeSpan.FromDays(_sslWarningDays))
            {
                return HealthLevel.Warning;
            }

            if (HttpStatusCode is >= 400)
            {
                return HealthLevel.Warning;
            }

            return HealthLevel.Ok;
        }
    }

    public void ApplyStatus(WebsiteStatus status)
    {
        IsOnline = status.IsOnline;
        HttpStatusCode = status.HttpStatusCode;
        ResponseTimeMs = status.ResponseTimeMs;
        SslExpiresUtc = status.SslExpiresUtc;
        LastCheckedUtc = status.LastCheckedUtc;
        LastError = status.LastError;
        DnsFailure = status.DnsFailure;
        DnsFailureSince = status.DnsFailureSince;
        HasBeenChecked = true;
        OnPropertyChanged(nameof(Health));
        OnPropertyChanged(nameof(ResponseSummary));
        OnPropertyChanged(nameof(SslSummary));
    }

    private bool IsTransientDns => !IsOnline && DnsFailure
        && (DnsFailureSince is null || DateTimeOffset.UtcNow - DnsFailureSince.Value < WebsiteStatus.DnsGrace);

    /// <summary>Title of the launcher/external card: the short pinned name when there is one.</summary>
    public string CardTitle => string.IsNullOrWhiteSpace(Source.LauncherTitle) ? Name : Source.LauncherTitle;

    public string ResponseSummary => !HasBeenChecked
        ? "Проверяю…"
        : HttpStatusCode is { } code
        ? $"{code} / {ResponseTimeMs:0} ms"
        : DnsFailure && !IsOnline
            ? (IsTransientDns
                ? "Не удалось найти адрес (DNS) — похоже на сбой вашей сети, не сервера"
                : "Адрес не находится уже " + (int)(DateTimeOffset.UtcNow - (DnsFailureSince ?? DateTimeOffset.UtcNow)).TotalMinutes + " мин")
            : (LastError ?? "—");

    public string SslSummary => SslExpiresUtc is { } expires
        ? $"SSL до {expires:dd.MM.yyyy}"
        : "SSL: —";

    // Prefers the installed desktop app (Assistant, ChatGPT, WhatsApp, ...) over the browser — see
    // IDesktopAppLauncher. Falls back to the plain URL when no matching app is actually found.
    [RelayCommand]
    private Task Open() => _appLauncher.OpenAsync(Source.DesktopProtocol, Source.DesktopAppName, Url);
}
