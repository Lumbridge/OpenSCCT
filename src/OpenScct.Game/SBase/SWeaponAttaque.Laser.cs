using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// The spy pistol's native side (spec/equipment/spy-shocker.md rule 6 and "Laser drawing"): where the pistol hangs for
/// each WeaponLoaded, the laser dot (an SLaserProjector for every spy pistol) and the beam (an SLaserBeam for the local
/// player's own pistol) while bLaserOn, their placement once per frame from the local player's view, and the muzzle
/// mesh per replicated shot. None of it runs on a dedicated server.
/// </summary>
public partial class SWeaponAttaque
{
    private static readonly Name BoneHolster = new("HOLSTER");
    private static readonly ConditionalWeakTable<LevelInfo, StrongBox<float>> LastViewUpdate = new();

    /// <summary>The pistol's place in the other hand (WeaponLoaded 7): 2 forward, 8 left, 2 up, rolled half a turn.</summary>
    [Provenance(ProvenanceKind.Decoded, "execTickSWeaponAttaque (game 0x10ab7870): WeaponLoaded 7 -> AttachToBone 'B L Hand', RelativeLocation (2, -8, 2), RelativeRotation roll 0x8000")]
    public static readonly FVector OtherHandOffset = new(2f, -8f, 2f);

    [Provenance(ProvenanceKind.Decoded, "execTickSWeaponAttaque: RelativeRotation (0, 0, 0x8000) in the other hand")]
    public const int OtherHandRoll = 0x8000;

    /// <summary>The beam's start on the hand: 30 forward, 4 left (minus Y) and 8 up (minus 8 in the other hand).</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80): start = hand origin + 30 X - 4 Y +/- 8 Z")]
    public const float LaserStartForward = 30f;

    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80)")]
    public const float LaserStartLeft = 4f;

    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80)")]
    public const float LaserStartUp = 8f;

    /// <summary>The beam's thickness scale (Y and Z of DrawScale3D).</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80): SetDrawScale3D(len + 1, 0.5, 0.5)")]
    public const float LaserBeamThickness = 0.5f;

    /// <summary>TickSWeaponAttaque (native 1287): the hand bone for the stance, the laser actors, the muzzle mesh.</summary>
    [Uc("SBase.SWeaponAttaque.TickSWeaponAttaque", Sha1 = "e8898aae6b9d7ffd92474a9ab1ed7ce1563ee8b3")]
    [UcNative("ASWeaponAttaque::execTickSWeaponAttaque")]
    [PortOf("nat:SBase.SWeaponAttaque.TickSWeaponAttaque", Spec = "spec/equipment/spy-shocker.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The muzzle mesh is raised as the SMuzzleSticky view event; the original also requires the pistol's bone-driven rotation to have no zero component, which the simulation does not pose")]
    public void TickSWeaponAttaque(float DeltaTime)
    {
        SBPawn ??= Instigator as SBasePawn;
        if (SBPawn is { } pawn && pawn.WeaponLoaded != OldWeaponLoaded)
        {
            AttachForStance(pawn);
            OldWeaponLoaded = pawn.WeaponLoaded;
        }

        if (!bLaserOn)
        {
            LaserProjector?.Destroy();
            LaserProjector = null;
            LaserBeam?.Destroy();
            LaserBeam = null;
        }
        else if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            LaserProjector ??= XLevel.SpawnActor(new SLaserProjector(), Location, Rotation, this, Instigator);
            if (LaserBeam is null && Level.GetLocalPlayerController() is { } local && ReferenceEquals(local.Pawn, Instigator))
            {
                LaserBeam = XLevel.SpawnActor(new SLaserBeam(), Location, Rotation, this, Instigator);
            }
        }

        // One muzzle mesh per tick while the replicated count is ahead.
        if (MyMuzzleCount < MuzzleCount)
        {
            MyMuzzleCount++;
            if (Level.NetMode != ENetMode.NM_DedicatedServer)
            {
                CombatFx.Notify(this, CombatFx.MuzzleSticky);
            }
        }
    }

    /// <summary>Where the pistol hangs: the holster at 0, the other hand at 7, else the right hand.</summary>
    private void AttachForStance(SBasePawn pawn)
    {
        RelativeRotation = default;
        RelativeLocation = default;
        switch (pawn.WeaponLoaded)
        {
            case 0:
                ObjectiveNatives.AttachToBone(pawn, this, BoneHolster);
                break;
            case 7:
                ObjectiveNatives.AttachToBone(pawn, this, BoneLeftHand);
                RelativeLocation = OtherHandOffset;
                RelativeRotation = new FRotator(0, 0, OtherHandRoll);
                break;
            default:
                ObjectiveNatives.AttachToBone(pawn, this, BoneRightHand);
                break;
        }
    }

    /// <summary>
    /// The view-dependent weapon update of NativePlayerCalcView (spec/native/camera.md rule 1.3): with bUpdate, once per
    /// Level.TimeSeconds, every spy pawn whose weapon slot holds an SWeaponAttaque places its laser.
    /// </summary>
    [UcNative("ASPlayerController::NativePlayerCalcView")]
    [PortOf("eng:ASPlayerController::NativePlayerCalcView", Spec = "spec/native/camera.md#1-calc-view-dispatch")]
    [Provenance(ProvenanceKind.Decoded, "NativePlayerCalcView (game 0x10aafe70): once per TimeSeconds, every SBasePawn with SPawnType 0 and an SWeaponAttaque WeaponDummy")]
    public static void ViewUpdate(SPlayerController PC, FVector CameraLocation)
    {
        if (PC.Level is not { } level || PC.XLevel is not { } xlevel)
        {
            return;
        }

        StrongBox<float> last = LastViewUpdate.GetOrCreateValue(level);
        if (last.Value == level.TimeSeconds)
        {
            return;
        }

        last.Value = level.TimeSeconds;
        foreach (SPawnAttaque spy in xlevel.DynamicActors<SPawnAttaque>())
        {
            if (!spy.bDeleteMe && spy.SPawnType == PawnType.SPAWNOFFENSE && spy.WeaponDummy is SWeaponAttaque pistol && !pistol.bDeleteMe)
            {
                pistol.PlaceLaser(CameraLocation);
            }
        }
    }

    /// <summary>
    /// The laser placement (game 0x10ab7c80) while bLaserOn: a line of ShootingRange from the hand along the hand's X
    /// axis, the first hit that is not bBulletGoTru (the line's end when none); the dot 10 uu (half its
    /// MaxTraceDistance) back from the hit along the line, turned along it, scaled by its distance from the local
    /// view (1/1500, at least 0.05), projected on the pawn it hit or on the world; the beam from the start to the hit.
    /// </summary>
    [UcNative("ASWeaponAttaque::UpdateLaser")]
    [PortOf("nat:SBase.SWeaponAttaque.TickSWeaponAttaque", Spec = "spec/equipment/spy-shocker.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The hand bone is placed as the shot's pistol point (CombatPorts.BoneCoords of the pistol): the simulation has no skeleton; the line flags are the shot's")]
    public void PlaceLaser(FVector ViewLocation)
    {
        if (!bLaserOn || SBPawn is not { } pawn)
        {
            return;
        }

        FCoords hand = CombatServices.Ports(this).BoneCoords(this, SWeaponDefense.FxBone);
        FVector start = LaserStartOn(hand, pawn.WeaponLoaded == 7);
        FVector end = start + hand.XAxis * ShootingRange;
        (FVector hitAt, Actor? hitActor) = LaserHit(pawn, start, end);
        FVector dir = (hitAt - start).SafeNormal();
        if (LaserProjector is { } dot)
        {
            dot.SetLocation(hitAt - dir * (0.5f * dot.MaxTraceDistance));
            dot.Rotation = dir.Rotation();
            dot.DrawScale = System.MathF.Max(FVector.Dist(hitAt, ViewLocation) / LaserProjectorDivider, LaserProjectorMinScale);
            bool onPawn = hitActor is Pawn;
            dot.bProjectOnBackfaces = !onPawn;
            dot.bProjectActor = onPawn;
            dot.bDynamicAttach = !onPawn;
            dot.SetBase(onPawn ? hitActor : null);
        }

        if (LaserBeam is { } beam)
        {
            FVector d = hitAt - start;
            beam.DrawScale3D = new FVector(d.Size() + 1f, LaserBeamThickness, LaserBeamThickness);
            beam.Rotation = d.Rotation();
            beam.SetLocation(start);
        }
    }

    /// <summary>
    /// Where the laser leaves the hand (game 0x10ab7c80): the hand bone's origin plus 30 along its X axis, 4 against
    /// its Y axis and 8 along its Z axis (8 against it in the other hand, WeaponLoaded 7). The drawn beam starts here on
    /// the drawn hand (game/Scripts/Fx/SpyLaserView.cs), the simulation's line on the measured pistol frame.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement helper (game 0x10ab7c80): start = hand origin + 30 X - 4 Y +/- 8 Z")]
    public static FVector LaserStartOn(FCoords hand, bool otherHand)
    {
        float up = otherHand ? -LaserStartUp : LaserStartUp;
        return hand.Origin + (hand.XAxis * LaserStartForward) - (hand.YAxis * LaserStartLeft) + (hand.ZAxis * up);
    }

    /// <summary>
    /// The first hit on the line that bullets do not pass (bBulletGoTru), or the end and no actor: the laser's line,
    /// which the client view also runs from the drawn hand (game/Scripts/Fx/SpyLaserView.cs). Reads the world only.
    /// </summary>
    public (FVector At, Actor? Actor) LaserHit(SBasePawn pawn, FVector start, FVector end)
    {
        if (TraceHitZones.CollisionOf(this) is not { } world)
        {
            return (end, null);
        }

        var hits = new List<FCheckResult>();
        world.MultiLineCheck(hits, pawn, end, start, FVector.Zero, ETraceFlags.TRACE_AllColliding);
        foreach (FCheckResult h in hits)
        {
            if (!h.bHit)
            {
                continue;
            }

            if (h.Actor is { bBulletGoTru: true } || ReferenceEquals(h.Actor, LaserProjector) || ReferenceEquals(h.Actor, LaserBeam))
            {
                continue;
            }

            return (h.Location, h.Actor);
        }

        return (end, null);
    }
}
