using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SInfirmary: a wall medkit that refills health and clears poison, for both teams (spec/devices/infirmary-ammo.md).</summary>
public partial class SInfirmary
{
    [Uc("SBase.SInfirmary.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SInfirmary:AcceptStickableGPO", Spec = "spec/devices/infirmary-ammo.md#rules")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>
    /// After the reuse delay: a hurt user counts a medkit use, poison drain is credited and cleared, health goes
    /// back to the class default, a spy's refillable points reset, and the heal sound plays.
    /// </summary>
    [Uc("SBase.SInfirmary.Use", Sha1 = "8fae1ff6ef67da400bf816341d54bd744f47ff66")]
    [PortOf("uc:SBase.SInfirmary:Use", Spec = "spec/devices/infirmary-ammo.md#rules")]
    public void Use(SPlayerController? Instigator)
    {
        using var clock = WorldEventTime();
        if (Instigator?.SBPawn is not { } pawn || !(LastUseTime + ReUseTime < Level.TimeSeconds))
        {
            return;
        }

        LastUseTime = Level.TimeSeconds;
        int defaultHealth = SHealMateInteraction.DefaultHealth(pawn);
        if (pawn.Health < defaultHealth && Instigator.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.MedkitsUsed++;
        }

        GameFlowServices.Ports(this).CountPoisonDrain(Instigator);
        pawn.bPoisonned = false;
        pawn.Health = defaultHealth;
        if (pawn is SPawnAttaque spy)
        {
            spy.NbPointsRefillable = 0;
        }

        PlayOwnedSound(HealSound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.HealSoundRadius);
    }
}
