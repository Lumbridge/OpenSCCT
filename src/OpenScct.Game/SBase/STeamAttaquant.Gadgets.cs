using OpenScct.Core.Uc;
using OpenScct.Game.Svm;

namespace OpenScct.Game.SBase;

public partial class STeamAttaquant
{
    public const int GadgetOptions = 10;
    public const byte AllAdditionalGadgets = 0b11;

    public byte AdditionalGadgets = AllAdditionalGadgets;

    public STeamAttaquant()
    {
        ClassInventory.Add(new ClassRef(CloneGadget.ClassPath));
        ClassInventory.Add(new ClassRef(MineHackGadget.ClassPath));
    }
}
