using OpenScct.Core.Uc;

namespace OpenScct.Game.Svm;

/// <summary>
/// The Svm package: map classes that are OpenSCCT's own rather than ports of the original's (spec/additions). Maps name
/// them <c>Svm.&lt;Class&gt;</c> in sim/gameplay.json like any other class.
/// </summary>
public static class SvmPackage
{
    public static readonly UcPackageInfo Gadgets = new("Svm", [CloneGadget.ClassInfo, MineHackGadget.ClassInfo,
        M4A1S.ClassInfo, AK47.ClassInfo, Galil.ClassInfo, Famas.ClassInfo,
        M4A1SDummy.ClassInfo, AK47Dummy.ClassInfo, GalilDummy.ClassInfo, FamasDummy.ClassInfo]);

    public static readonly UcPackageInfo Info = new("Svm", [Conveyor.UcMeta.Class]);
}
