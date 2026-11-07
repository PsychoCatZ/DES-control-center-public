using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Abstractions;

public interface IWebsiteMonitor
{
    Task<WebsiteStatus> CheckAsync(Website website, CancellationToken cancellationToken = default);
}
