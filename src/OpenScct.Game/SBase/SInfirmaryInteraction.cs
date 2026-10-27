using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SInfirmaryInteraction: the medkit's use entry (spec/devices/infirmary-ammo.md).</summary>
public partial class SInfirmaryInteraction
{
    /// <summary>A living user heals at the owning medkit, then the usual interaction start.</summary>
    [Uc("SBase.SInfirmaryInteraction.InitInteract", Sha1 = "d804f666ef09795b2c6a2da4253e0ad0c101a23c")]
    [PortOf("uc:SBase.SInfirmaryInteraction:InitInteract", Spec = "spec/devices/infirmary-ammo.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Instigator.SBPawn is { Health: > 0 })
        {
            (Owner as SInfirmary)?.Use(Instigator);
        }

        base.InitInteract(Instigator, SubInteraction);
    }
}
