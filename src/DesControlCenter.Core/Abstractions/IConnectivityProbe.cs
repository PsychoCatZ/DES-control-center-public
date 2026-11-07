namespace DesControlCenter.Core.Abstractions;

/// <summary>Answers "can THIS computer reach the internet at all?" using several independent hosts,
/// so one blocked or down site doesn't read as "no internet".</summary>
public interface IConnectivityProbe
{
    Task<bool> IsInternetReachableAsync(CancellationToken cancellationToken = default);
}
