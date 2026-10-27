using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGETrigger: switches traversal elements on and off by editing LevelInfo.GEActivationMask
/// (spec/devices/ge-trigger.md).
/// </summary>
public partial class SGETrigger
{
    /// <summary>Sets the GEMask bits, or with bToggle clears them when they are all set already.</summary>
    [Uc("SBase.SGETrigger.Trigger", Sha1 = "3dd0b3de834d4f772a7fd0b9b830e5ad0fd73204")]
    [PortOf("uc:SBase.SGETrigger:Trigger", Spec = "spec/devices/ge-trigger.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        int mask = Level.GEActivationMask;
        if (!bToggle || (mask & GEMask) != GEMask)
        {
            Level.GEActivationMask = (byte)(mask | GEMask);
        }
        else
        {
            Level.GEActivationMask = (byte)(mask - (mask & GEMask));
        }
    }

    /// <summary>Clears the GEMask bits.</summary>
    [Uc("SBase.SGETrigger.UnTrigger", Sha1 = "c60155a7a0298783479d35d88701a428eaff190b")]
    [PortOf("uc:SBase.SGETrigger:UnTrigger", Spec = "spec/devices/ge-trigger.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        int mask = Level.GEActivationMask;
        Level.GEActivationMask = (byte)(mask - (mask & GEMask));
    }
}
