using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.STazerEmitter: the taser's beam (spec/equipment/tazer.md rules 6 to 8). Spawned by the server for 0.3 s and
/// replicated with its two pawns; every machine's tick shows it and stretches its mesh from the merc's FX bone to the
/// pawn hit, or along the bone for a miss.
/// </summary>
public partial class STazerEmitter
{
    /// <summary>
    /// The native tick (game 0x10a94920, every machine): after the actor tick, with both pawns set, the beam is shown,
    /// moved to the FX bone, scaled to (length, 15, 15) and turned along the beam.
    /// </summary>
    [UcNative("ASTazerEmitter::Tick", Exe = 0x10a94920)]
    [PortOf("eng:ASTazerEmitter::Tick", Spec = "spec/equipment/tazer.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "tazer.md rules 6 to 8: bHidden cleared, FX bone of MyWeapon (local merc) or WeaponDummy, miss trace 30 back to fDefaultLength, scale (L, 15, 15)")]
    [Provenance(ProvenanceKind.Estimated, "The FX bone is the weapon's estimated pose (CombatPorts.BoneCoords); without a weapon the merc's own axes")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || PawnOwner is not { } merc || PawnTarget is not { } target)
        {
            return;
        }

        bHidden = false;
        FCoords fx = BeamStart(merc);
        MovementNatives.FarMoveActor(this, fx.Origin);
        if (ReferenceEquals(target, merc))
        {
            if (LaserLenghtWithoutPawn == 0f)
            {
                LaserLenghtWithoutPawn = MissLength(fx);
            }

            DrawScale3D = new FVector(LaserLenghtWithoutPawn, K.TazerTraceExtent, K.TazerTraceExtent);
            Rotation = fx.XAxis.Rotation();
            return;
        }

        FVector end = AimPoint(target);
        DrawScale3D = new FVector((fx.Origin - end).Size(), K.TazerTraceExtent, K.TazerTraceExtent);
        Rotation = (end - fx.Origin).Rotation();
    }

    /// <summary>
    /// The beam's start: the FX bone of the first-person weapon for a merc played on this machine, else of his
    /// third-person dummy, else his location.
    /// </summary>
    private FCoords BeamStart(SPAWNDEFENSE Merc)
    {
        ICombatPorts ports = CombatServices.Ports(this);
        if (Merc.Controller is PlayerController { Player: UViewport } && Merc.MyWeapon is { } weapon)
        {
            return ports.BoneCoords(weapon, SWeaponDefense.FxBone);
        }

        if (Merc.WeaponDummy is { } dummy)
        {
            return ports.BoneCoords(dummy, SWeaponDefense.FxBone);
        }

        // The original leaves the axes unset here; the merc's own facing stands in.
        FCoords own = FCoords.FromRotator(Merc.Rotation);
        own.Origin = Merc.Location;
        return own;
    }

    /// <summary>A miss: fDefaultLength, or up to what the box line from 30 uu behind the bone hits (0 behind the bone).</summary>
    private float MissLength(in FCoords Fx)
    {
        FVector end = Fx.Origin + Fx.XAxis * fDefaultLength;
        if (!SGadTazerLauncher.LineHit(this, end, Fx.Origin - Fx.XAxis * K.TazerBeamPullBack, out FCheckResult hit))
        {
            return fDefaultLength;
        }

        FVector d = hit.Location - Fx.Origin;
        return (d | Fx.XAxis) >= 0f ? d.Size() : 0f;
    }

    /// <summary>The pawn's location, or its floor point (Location - CollisionHeight on Z) while it lies on the ground.</summary>
    private static FVector AimPoint(SBasePawn Target) =>
        Target.bLyingOnGround ? Target.Location - new FVector(0f, 0f, Target.CollisionHeight) : Target.Location;
}
