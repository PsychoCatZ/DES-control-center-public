using DesControlCenter.Core.Models;
using DesControlCenter.Core.Status;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Reads and flips a <see cref="SwitchResource"/> on its server. Flipping is only ever a direct,
/// confirmed button press; the app never flips a switch on its own.</summary>
public interface ISwitchController
{
    Task<SwitchStatus> CheckAsync(ServerResource server, SwitchResource sw, CancellationToken cancellationToken = default);

    Task<ServiceCommandOutcome> SetAsync(ServerResource server, SwitchResource sw, bool on, CancellationToken cancellationToken = default);
}
