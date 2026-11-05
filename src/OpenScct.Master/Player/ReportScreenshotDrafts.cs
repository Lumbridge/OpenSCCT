using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace OpenScct.Master.Player;

/// <summary>Private, expiring handoff between a game and its browser. Nothing here is publicly served.</summary>
internal sealed class ReportScreenshotDrafts(TimeProvider? clock = null)
{
    internal const string UploadRoute = "/help/report-bug/capture";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, (byte[] Png, DateTimeOffset Expires)> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Count, DateTimeOffset Expires)> _rates = new(StringComparer.Ordinal);
    private int _uploads;

    // Bound resident images, in-flight uploads and the IP ledger, including callers without an account.
    internal bool Begin(string ip)
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            foreach (string key in _drafts.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) _drafts.Remove(key);
            foreach (string key in _rates.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) _rates.Remove(key);
            if (_drafts.Count + _uploads >= 32 || (!_rates.ContainsKey(ip) && _rates.Count >= 256)) return false;
            var rate = _rates.GetValueOrDefault(ip, (0, now.AddMinutes(1)));
            if (rate.Item1 >= 6) return false;
            _rates[ip] = (rate.Item1 + 1, rate.Item2);
            _uploads++;
            return true;
        }
    }

    internal void End() { lock (_gate) _uploads--; }
    internal string Add(byte[] png)
    {
        lock (_gate)
        {
            string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _drafts.Add(token, (png, _clock.GetUtcNow().AddMinutes(15)));
            return token;
        }
    }

    internal byte[]? Find(string token)
    {
        if (!ReportScreenshot.ValidId(token)) return null;
        lock (_gate)
        {
            if (!_drafts.TryGetValue(token, out var draft)) return null;
            if (draft.Expires > _clock.GetUtcNow()) return draft.Png;
            _drafts.Remove(token);
            return null;
        }
    }
}
