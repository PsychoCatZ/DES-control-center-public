using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Monitoring;

/// <summary>
/// The set of currently silenced resources (any kind — server, site, service, VPN; every one has
/// a unique Guid). A silenced SERVER also silences everything that runs on it, which is what you
/// want when a whole VPS is down or under maintenance. Thread-safe: the scheduler's loops and the
/// UI both use it.
/// </summary>
public sealed class SilenceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Silence> _silences = new();
    private readonly ISilenceStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public SilenceRegistry(ISilenceStore store, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        foreach (var silence in store.Load())
        {
            _silences[silence.ResourceId] = silence;
        }
    }

    /// <summary>Raised after any change (silenced, lifted, expired). Fires on whatever thread made the change.</summary>
    public event EventHandler? Changed;

    /// <summary>Is this resource itself — or, if given, the server it runs on — silenced right now?</summary>
    public bool IsSilenced(Guid resourceId, Guid? serverId = null)
    {
        lock (_gate)
        {
            return IsActive(resourceId) || (serverId is { } sid && IsActive(sid));
        }
    }

    /// <summary>The silence set directly on this resource (not inherited from its server), if active.</summary>
    public Silence? Get(Guid resourceId)
    {
        lock (_gate)
        {
            return IsActive(resourceId) ? _silences[resourceId] : null;
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _silences.Keys.Count(IsActive);
            }
        }
    }

    public void Silence(Guid resourceId, SilenceMode mode, DateTimeOffset? untilUtc = null)
    {
        lock (_gate)
        {
            _silences[resourceId] = new Silence
            {
                ResourceId = resourceId,
                Mode = mode,
                UntilUtc = mode == SilenceMode.Until ? untilUtc : null,
                CreatedUtc = _clock()
            };
            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Unsilence(Guid resourceId)
    {
        lock (_gate)
        {
            if (!_silences.Remove(resourceId))
            {
                return;
            }

            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UnsilenceAll()
    {
        lock (_gate)
        {
            if (_silences.Count == 0)
            {
                return;
            }

            _silences.Clear();
            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A resource became healthy: lifts an "until recovery" silence (and only that kind),
    /// so a later failure is loud again. Timed and permanent silences are left alone.</summary>
    public void NotifyRecovered(Guid resourceId)
    {
        lock (_gate)
        {
            if (!_silences.TryGetValue(resourceId, out var silence) || silence.Mode != SilenceMode.UntilRecovery)
            {
                return;
            }

            _silences.Remove(resourceId);
            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops timed silences whose time has passed. Returns true if anything changed.</summary>
    public bool PurgeExpired()
    {
        lock (_gate)
        {
            var expired = _silences.Values.Where(s => !IsActive(s.ResourceId)).Select(s => s.ResourceId).ToList();
            if (expired.Count == 0)
            {
                return false;
            }

            foreach (var id in expired)
            {
                _silences.Remove(id);
            }

            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool IsActive(Guid resourceId) =>
        _silences.TryGetValue(resourceId, out var silence)
        && (silence.Mode != SilenceMode.Until || (silence.UntilUtc is { } until && until > _clock()));

    private void PersistLocked()
    {
        try
        {
            _store.Save(_silences.Values.ToList());
        }
        catch
        {
            // A failed save must never break monitoring; the silence just won't survive a restart.
        }
    }
}
