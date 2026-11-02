using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SStickyCamEmitter: the sticky cam's sleeping gas (spec/equipment/sticky-cam.md rules 22 and 23). It lives
/// 6 s; every tick, on every machine, a pawn caught in front of the cam keeps getting GoInSmoke until it dies.
/// </summary>
public partial class SStickyCamEmitter
{
    /// <summary>Encroachment does nothing to the gas.</summary>
    [Uc("SBase.SStickyCamEmitter.EncroachedBy", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SStickyCamEmitter:EncroachedBy", Spec = "spec/equipment/sticky-cam.md#gas")]
    public override void EncroachedBy(Actor? Other)
    {
    }

    /// <summary>
    /// The gas point: 192 uu ahead of the emitter along its rotation. The helper 0x10aa3130 is the class's C++
    /// PostBeginPlay (the AActor vtable slot after the two SetVolumes and before setPhysics), so it runs once at spawn
    /// on every machine.
    /// </summary>
    [UcNative("ASStickyCamEmitter::PostBeginPlay", Exe = 0x10aa3130)]
    [PortOf("eng:ASStickyCamEmitter::Tick", Spec = "spec/equipment/sticky-cam.md#gas")]
    [Provenance(ProvenanceKind.Decoded, "0x10aa3130 is referenced only by the ASStickyCamEmitter vtable (0x10bfb968) at slot 0xf8, where AActor and AEmitter have the empty PostBeginPlay 0x10968f20 between SetVolumes (0xf4) and setPhysics (0xfc)")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        RefLocation = Location + Rotation.Vector() * K.StickyGasAhead;
    }

    /// <summary>
    /// The emitter's native tick (0x10aa31c0): after the emitter's own tick, the gas scan (0x10aa31f0) over the
    /// level's pawns.
    /// </summary>
    [UcNative("ASStickyCamEmitter::Tick", Exe = 0x10aa31c0)]
    [PortOf("eng:ASStickyCamEmitter::Tick", Spec = "spec/equipment/sticky-cam.md#gas")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        GasScan();
    }

    /// <summary>
    /// The gas scan: a gassed pawn's controller gets GoInSmoke every tick; a new pawn is gassed when awake, not
    /// shocked, lying or masked, within 128 uu of the gas point and seen from the emitter.
    /// </summary>
    [UcNative("ASStickyCamEmitter::GasScan", Exe = 0x10aa31f0)]
    [PortOf("eng:ASStickyCamEmitter::Tick", Spec = "spec/equipment/sticky-cam.md#gas")]
    [Provenance(ProvenanceKind.Decoded, "0x10aa31f0: PawnsGazes list, wake grace 2 s, 16384 squared radius, 0x286 sight line")]
    [Provenance(ProvenanceKind.Estimated, "The original does not bound PawnsGazes at 3; the port keeps at most 3")]
    public void GasScan()
    {
        float now = Level.TimeSeconds;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p.bDeleteMe)
            {
                continue;
            }

            if (IsGassed(p))
            {
                if (Role == ENetRole.ROLE_Authority && p.Controller is SPlayerController exposed)
                {
                    exposed.ObserveGasActivity(this, TimeBaseToCought, TimeBaseToFaint, stickyCamera: true);
                }

                (p.Controller as SPlayerController)?.eventGoInSmoke(TimeBaseToCought, TimeBaseToFaint, TimeBaseToJamming, ForceBaseToJamming);
                continue;
            }

            if (p.Controller is not SPlayerController { SBPawn: { } own } pc || own.bStickyShocking || own.bLyingOnGround ||
                own.bGasMask || !(now - pc.fLastTimeWakeUp > K.StickyGasWakeGrace))
            {
                continue;
            }

            if (!(FVector.DistSquared(p.Location, RefLocation) < K.StickyGasRadiusSquared) || !GadgetWorld.Sees(this, Location, p))
            {
                continue;
            }

            if (Role == ENetRole.ROLE_Authority)
            {
                pc.ObserveGasActivity(this, TimeBaseToCought, TimeBaseToFaint, stickyCamera: true);
            }

            pc.eventGoInSmoke(TimeBaseToCought, TimeBaseToFaint, TimeBaseToJamming, ForceBaseToJamming);
            if (iNbPawnsGazes < K.StickyGasVictims)
            {
                PawnsGazes[iNbPawnsGazes] = p;
                iNbPawnsGazes++;
            }
        }
    }

    private bool IsGassed(Pawn p)
    {
        for (int i = 0; i < iNbPawnsGazes && i < PawnsGazes.Length; i++)
        {
            if (ReferenceEquals(PawnsGazes[i], p))
            {
                return true;
            }
        }

        return false;
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < PawnsGazes.Length; i++)
        {
            PawnsGazes[i] = Live(PawnsGazes[i]);
        }
    }
}
