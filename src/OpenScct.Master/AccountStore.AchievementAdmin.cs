using System.Linq;
using OpenScct.Net.Cosmetics;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    public AdminResult RevokeAchievement(long accountId, string achievement, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return AdminResult.Refused("Enter a reason for this change.");
        if (!Achievements.All.Any(a => a.Id == achievement)) return AdminResult.Refused("Choose a known achievement.");
        lock (_gate)
        {
            if (FindById(accountId) is null) return AdminResult.Missing("That account does not exist.");
            if (!Earned(accountId).ContainsKey(achievement)) return AdminResult.Refused("This player has not earned that achievement.");
            return InTransaction(() =>
            {
                var before = new { Earned = Earned(accountId), Appearance = Appearance(accountId) };
                Exec("DELETE FROM achievements WHERE account_id = $a AND achievement_id = $id;", ("$a", accountId), ("$id", achievement));
                AuditChange("achievement.revoke", "account:" + accountId, reason, before,
                    new { Earned = Earned(accountId), Appearance = Appearance(accountId) });
                return AdminResult.Done("Achievement revoked. Its earned rewards were removed; stats and XP are unchanged. It can be earned again in a later match.");
            });
        }
    }
}
