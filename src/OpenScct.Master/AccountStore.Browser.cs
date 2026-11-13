using System;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    /// <summary>Atomic compare-and-save for a browser tab's account profile.</summary>
    public bool TryPutBrowserProfile(long accountId, ProfileData profile, DateTimeOffset? expectedUpdated, out ProfileData? saved)
    {
        lock (_gate)
        {
            ProfileData? current = ProfileLocked(accountId) is { } stored ? WithAppearanceLocked(accountId, stored) : null;
            if (current?.Updated != expectedUpdated)
            {
                saved = current;
                return false;
            }
            // The master's clock controls browser revisions. A changed profile always gets a later revision.
            long now = _time.GetUtcNow().ToUnixTimeMilliseconds();
            long next = Math.Max(now, (current?.Updated.ToUnixTimeMilliseconds() ?? 0) + 1);
            Exec("INSERT INTO profiles (account_id, data, updated_ms) VALUES ($a, $d, $u) " +
                "ON CONFLICT (account_id) DO UPDATE SET data = excluded.data, updated_ms = excluded.updated_ms;",
                ("$a", accountId), ("$d", profile.Data), ("$u", next));
            AcceptLegacyAppearanceLocked(accountId, profile.Data);
            saved = WithAppearanceLocked(accountId, new ProfileData { Data = profile.Data, Updated = DateTimeOffset.FromUnixTimeMilliseconds(next) });
            return true;
        }
    }
}
