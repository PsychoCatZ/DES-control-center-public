using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Infrastructure.Network;

/// <summary>
/// Reports this machine's own public IP and internet reachability. Deliberately separate from
/// server/website monitoring so a future VPN Monitor module (AmneziaWG) can be added alongside it
/// without touching this class.
/// </summary>
public sealed class PublicIpService : INetworkStatusService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<NetworkStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ip = await Client.GetStringAsync("https://api.ipify.org", cancellationToken);
            return new NetworkStatus { InternetAvailable = true, PublicIpAddress = ip.Trim() };
        }
        catch
        {
            return new NetworkStatus { InternetAvailable = false, PublicIpAddress = null };
        }
    }
}
