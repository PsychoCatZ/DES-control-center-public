using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Infrastructure.Network;

/// <summary>Reachable if ANY of several unrelated hosts answers at all (any HTTP status counts —
/// we only care that a connection was made), so a single blocked or down site isn't mistaken for
/// "this computer has no internet".</summary>
public sealed class HttpConnectivityProbe : IConnectivityProbe
{
    private static readonly string[] Targets =
    {
        "https://www.google.com/generate_204",
        "https://www.cloudflare.com/cdn-cgi/trace",
        "https://api.ipify.org",
        "https://ya.ru"
    };

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default)
    {
        var attempts = Targets.Select(url => TryAsync(url, cancellationToken)).ToList();
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            attempts.Remove(done);
            if (await done)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TryAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
