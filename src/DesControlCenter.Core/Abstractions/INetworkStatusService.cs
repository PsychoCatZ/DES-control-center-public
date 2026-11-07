namespace DesControlCenter.Core.Abstractions;

public sealed class NetworkStatus
{
    public bool InternetAvailable { get; init; }
    public string? PublicIpAddress { get; init; }
    public DateTimeOffset CheckedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Local machine's own network state (public IP + connectivity), shown in the UI.
/// Kept separate from server/website monitoring so a future VPN Monitor module can slot in here.
/// </summary>
public interface INetworkStatusService
{
    Task<NetworkStatus> CheckAsync(CancellationToken cancellationToken = default);
}
