using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.Master;

/// <summary>Short-lived interactive client leases, independent of server rosters.</summary>
internal sealed class PresenceRegistry(TimeProvider time, int capacity = 100_000)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (long? Account, DateTimeOffset Seen)> _sessions = new();

    public bool Touch(Guid session, long? account)
    {
        lock (_gate)
        {
            Sweep();
            if (session == Guid.Empty || (!_sessions.ContainsKey(session) && _sessions.Count >= capacity)) return false;
            _sessions[session] = (account, time.GetUtcNow());
            return true;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                Sweep();
                return _sessions.Values.Count(v => v.Account is null) +
                    _sessions.Values.Where(v => v.Account is not null).Select(v => v.Account).Distinct().Count();
            }
        }
    }

    private void Sweep()
    {
        var cutoff = time.GetUtcNow().AddSeconds(-90);
        foreach (var key in _sessions.Where(p => p.Value.Seen <= cutoff).Select(p => p.Key).ToArray())
            _sessions.Remove(key);
    }
}
