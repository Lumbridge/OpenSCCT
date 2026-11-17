using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    public LiveAccountAppearance LiveAppearance(long accountId)
    {
        lock (_gate)
        {
            var appearance = Appearance(accountId);
            int bit = TitleCatalog.Find(appearance.Title)?.Bit ?? -1;
            return new(ProgressionLocked(accountId).Rank.WithTitle(bit).Pack(),
                CosmeticUnlocks.Of(Unlocked(accountId)).ToBytes(), appearance);
        }
    }
}
