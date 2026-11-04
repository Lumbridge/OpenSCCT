using OpenScct.Core.Uc;

namespace OpenScct.Game.Svm;

/// <summary>
/// The Svm package: map classes that are OpenSCCT's own rather than ports of the original's (spec/additions). Maps name
/// them <c>Svm.&lt;Class&gt;</c> in sim/gameplay.json like any other class.
/// </summary>
public static class SvmPackage
{
    public static readonly UcPackageInfo Info = new("Svm", [Conveyor.UcMeta.Class]);
}
