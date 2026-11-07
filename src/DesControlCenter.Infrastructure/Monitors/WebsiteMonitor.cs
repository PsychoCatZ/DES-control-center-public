using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Infrastructure.Monitors;

/// <summary>Checks HTTP reachability, response time and (for https URLs) certificate expiry.</summary>
public sealed class WebsiteMonitor : IWebsiteMonitor
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<WebsiteStatus> CheckAsync(Website website, CancellationToken cancellationToken = default)
    {
        X509Certificate2? serverCertificate = null;

        // Accept any certificate here on purpose: we want to read expiry metadata even for an
        // already-expired or otherwise-invalid cert, which is the whole point of this monitor.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert is not null)
                {
                    serverCertificate = new X509Certificate2(cert);
                }

                return true;
            }
        };
        using var client = new HttpClient(handler) { Timeout = RequestTimeout };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, website.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();

            return new WebsiteStatus
            {
                IsOnline = true,
                HttpStatusCode = (int)response.StatusCode,
                ResponseTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                SslExpiresUtc = serverCertificate is not null
                    ? new DateTimeOffset(serverCertificate.NotAfter.ToUniversalTime())
                    : null
            };
        }
        catch (Exception ex)
        {
            return WebsiteStatus.Offline(ex.Message, dnsFailure: IsDnsFailure(ex));
        }
    }

    /// <summary>True when the request failed because the host name could not be resolved — a DNS problem
    /// (often on the owner's own network) rather than evidence that the server is down.</summary>
    public static bool IsDnsFailure(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
            {
                return true;
            }

            if (ex is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData })
            {
                return true;
            }
        }

        return false;
    }
}
