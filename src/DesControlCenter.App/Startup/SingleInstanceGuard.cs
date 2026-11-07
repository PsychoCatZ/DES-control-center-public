namespace DesControlCenter.App.Startup;

/// <summary>Ensures only one DES Control Center instance runs per Windows user session.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\DesControlCenter-SingleInstance";
    private readonly Mutex _mutex;

    /// <summary>The demo build (DES_DEMO=1) has its own mutex so it can run next to the real, tray-resident instance.</summary>
    private static string Name => Demo.DemoMode.IsEnabled ? MutexName + "-Demo" : MutexName;

    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
