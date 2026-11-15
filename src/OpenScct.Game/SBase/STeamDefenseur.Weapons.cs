using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

public partial class STeamDefenseur
{
    public STeamDefenseur()
    {
        foreach (var rifle in MercRifles.All) ClassWeapon.Add(new ClassRef(rifle.ClassPath));
    }
}
