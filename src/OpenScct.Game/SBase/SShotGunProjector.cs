using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SShotGunProjector: one shotgun pellet mark (spec/equipment/merc-guns.md). The gun that throws it sets its
/// range and size, abandons it for 10 s and destroys it; three marks in ten play an impact sound.
/// </summary>
public partial class SShotGunProjector
{
    private static readonly AssetRef ImpactSound = new("Armes_impacts.Impacts.SW_Impact_Shot");

    [Uc("SBase.SShotGunProjector.PostBeginPlay", Sha1 = "2f67f465d0b896ecc6c90226ab5d127eda74fb29")]
    [PortOf("uc:SBase.SShotGunProjector:PostBeginPlay", Spec = "spec/equipment/merc-guns.md#rules")]
    protected override void PostBeginPlay()
    {
        if (FRand() < K.ShotgunDecalSoundChance)
        {
            PlaySound(ImpactSound, ESoundSlot.SLOT_None, K.ShotgunDecalSoundVolume, false, K.ShotgunDecalSoundRadius);
        }
    }
}
