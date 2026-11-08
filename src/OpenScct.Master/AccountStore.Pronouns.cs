using OpenScct.Core.Engine;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    public string Pronouns(long accountId)
    {
        lock (_gate)
            return PlayerPronouns.Normalize(One("SELECT pronouns FROM account_pronouns WHERE account_id = $id;", r => r.GetString(0), ("$id", accountId)));
    }

    public bool SetPronouns(long accountId, string? selection)
    {
        if (!PlayerPronouns.TryNormalize(selection, out string pronouns)) return false;
        lock (_gate)
            return Exec("""
                INSERT INTO account_pronouns (account_id, pronouns)
                SELECT id, $p FROM accounts WHERE id = $id
                ON CONFLICT(account_id) DO UPDATE SET pronouns = excluded.pronouns;
                """, ("$id", accountId), ("$p", pronouns)) > 0;
    }
}
