using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using CK = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPawnAttaque engine events of the combat and equipment ports: the spawn state of the health refill pool and
// the energy bar, and the health regeneration tick. Written from spec/combat/health.md and spec/equipment/spy-energy.md.
// The energy rules themselves are in Equipment/SpyEnergy.cs.
public partial class SPawnAttaque
{
    /// <summary>
    /// A spy starts with an empty refill pool and a full energy bar (SBasePawn.PostBeginPlay, health rule 1, energy
    /// rule 1). The goggle glow is spawned first (goggle-glow.md rule 1).
    /// </summary>
    [Uc("SBase.SPawnAttaque.PostBeginPlay", Sha1 = "201580c45a704a2f61d45d41e048aa60bd8e90a2")]
    [Uc("SBase.SBasePawn.PostBeginPlay", Sha1 = "08637dbcdf555ce83c7b957ee2dfd6ca4a66196a")]
    [PortOf("uc:SBase.SBasePawn:PostBeginPlay", Spec = "spec/combat/health.md#rules")]
    [PortOf("uc:SBase.SBasePawn:PostBeginPlay", Spec = "spec/equipment/spy-energy.md#rules")]
    protected override void PostBeginPlay()
    {
        PawnVision.SpawnGoggleBeam(this);
        base.PostBeginPlay();
        NbPointsRefillable = 0;
        EnergyLevel = EnergyLevelMax;
    }

    /// <summary>The spy's tick: TickNative with the default health, then the pawn tick (where the health refill runs).</summary>
    [Uc("SBase.SPawnAttaque.Tick", Sha1 = "fb2f8216d60724cc5500a39b7e9cc1a7a73d0d36")]
    [PortOf("uc:SBase.SPawnAttaque:Tick", Spec = "spec/combat/health.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        TickNative(CK.DefaultHealth);
        base.Tick(DeltaTime);
    }

    /// <summary>
    /// TickNative (native 1414, stock 0x10af5cf0, replaced by Reloaded): the argument is read and ignored and no
    /// health changes here; the native runs the screen glass effect only (natives2-damage.md rule 9), which the cloak
    /// port (Equipment/PawnVision.cs) owns. The health refill is SBasePawn.NativeTick (SBasePawn.HealthTick.cs).
    /// </summary>
    [UcNative("ASPawnAttaque::execTickNative", Exe = 0x10af5cf0)]
    [PortOf("nat:SBase.SPawnAttaque.TickNative", Spec = "spec/combat/health.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execTickNative 0x10af5cf0: no health rule, the glass effect only")]
    public void TickNative(int DefaultHealth)
    {
    }
}
