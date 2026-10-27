using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// Natives the combat port depends on. The decoded ones say so; each stand-in still marked Estimated
/// sits behind <see cref="ICombatPorts"/>, so a decoded port replaces it in one place.
/// </summary>
public static class CombatNatives
{
    private static readonly Name NAME_s_PlayerWalkingSt0H = new("s_PlayerWalkingSt0H");
    private static readonly Name NAME_s_PlayerWalkingSt2H = new("s_PlayerWalkingSt2H");
    private static readonly Name NAME_s_PlayerWalkingCr0H = new("s_PlayerWalkingCr0H");
    private static readonly Name NAME_s_PlayerWalkingCr2H = new("s_PlayerWalkingCr2H");

    /// <summary>
    /// The merc's rifle FX bone from his eye at a frag's release, along his aim (MyDesiredRotation's axes: forward,
    /// right, up). Measured on the original's frag throws (FX + 40 along the aim, rounded): the same at pitches -30 to
    /// +3 degrees, standing and crouched; spec/equipment/grenade-throw.md rule 4a. The aimed rifle's FX bone is 80 uu
    /// further forward (<see cref="MercAimFxForward"/>, measured on the laser): at the release it is not where the aim
    /// holds it.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "sandbox capture of four merc frag throws (local/reticle/capture2.py): spawn 25.3 forward, 11.0 right, 12.9 down of the eye, minus the decoded 40 along the aim")]
    public const float MercFxForward = -14.7f;

    [Provenance(ProvenanceKind.Measured, "as MercFxForward")]
    public const float MercFxRight = 11f;

    [Provenance(ProvenanceKind.Measured, "as MercFxForward")]
    public const float MercFxUp = -12.9f;

    /// <summary>
    /// The aimed merc rifle's FX bone from his eye along his aim (forward, right, up): his laser beam starts there
    /// pulled back 50 uu along the bone (merc-laser.md rule 9), measured at 15.4 forward, 12.0 right, 12.7 down of the
    /// eye at pitches -25 to +14 degrees, standing and crouched (spec/equipment/merc-laser.md "Where the beam starts").
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "sandbox capture local/lasers/capture.py --side merc: SLaserBeam Location + 50 along its Rotation, four aims, within 0.5 uu")]
    public const float MercAimFxForward = 65.4f;

    [Provenance(ProvenanceKind.Measured, "as MercAimFxForward")]
    public const float MercAimFxRight = 11.1f;

    [Provenance(ProvenanceKind.Measured, "as MercAimFxForward")]
    public const float MercAimFxUp = -13.0f;

    /// <summary>The aimed FX bone's direction against the aim: a little down and left (the beam's Rotation).</summary>
    [Provenance(ProvenanceKind.Measured, "local/lasers/capture.py --side merc: beam pitch 82 to 98 units under MyDesiredRotation's, yaw 185 to 205 units left")]
    public const int MercAimFxPitch = -95;

    [Provenance(ProvenanceKind.Measured, "as MercAimFxPitch")]
    public const int MercAimFxYaw = -190;

    /// <summary>
    /// The merc's rifle FX bone as the aimed rifle holds it (laser, muzzle clearance, tracers, the laser's fire start),
    /// from his eye along <paramref name="aim"/>.
    /// </summary>
    [UcNative("AActor::execGetBoneCoords")]
    [Provenance(ProvenanceKind.Measured, "local/lasers/capture.py --side merc (MercAimFx constants)")]
    public static FCoords MercAimFxCoords(Pawn merc, FRotator aim)
    {
        FCoords a = FCoords.FromRotator(aim);
        FCoords c = FCoords.FromRotator(new FRotator(aim.Pitch + MercAimFxPitch, aim.Yaw + MercAimFxYaw, aim.Roll));
        c.Origin = merc.Location + new FVector(0f, 0f, merc.EyeHeight) + (a.XAxis * MercAimFxForward) + (a.YAxis * MercAimFxRight) +
            (a.ZAxis * MercAimFxUp);
        return c;
    }

    /// <summary>
    /// The merc's rifle FX bone at a frag's release (grenade-throw.md rule 4a): from his eye along <paramref name="aim"/>,
    /// with the aim's axes. UseGren throws from here.
    /// </summary>
    [UcNative("AActor::execGetBoneCoords")]
    [Provenance(ProvenanceKind.Measured, "local/reticle/capture2.py (MercFx constants)")]
    public static FCoords MercThrowFxCoords(Pawn merc, FRotator aim)
    {
        FCoords c = FCoords.FromRotator(aim);
        c.Origin = merc.Location + new FVector(0f, 0f, merc.EyeHeight) + (c.XAxis * MercFxForward) + (c.YAxis * MercFxRight) +
            (c.ZAxis * MercFxUp);
        return c;
    }

    /// <summary>
    /// The drawn spy pistol (attached to B R Hand) in the original, against the controller's view rotation: the pistol's
    /// pitch is this share of the view pitch plus a stance offset (its yaw is the view's, its roll under 45 units).
    /// Measured on a crouched and a standing spy at view pitches -4639 to +2694 (spec/equipment/spy-shocker.md rule 14a).
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "sandbox capture local/spyhand/capture.py (SteeE lane, 72 frames): pistol pitch = 0.8498 view pitch + 1.7 crouched, + 22.1 standing; residual under 23 units")]
    public const float SpyPistolPitchShare = 0.85f;

    [Provenance(ProvenanceKind.Measured, "as SpyPistolPitchShare")]
    public const float SpyPistolCrouchedPitchOffset = 1.7f;

    [Provenance(ProvenanceKind.Measured, "as SpyPistolPitchShare")]
    public const float SpyPistolStandingPitchOffset = 22.1f;

    /// <summary>
    /// The pistol's place from the pawn's Location in the view's yaw frame (forward, right, up), each a + b * view pitch
    /// (65536 a turn), crouched and standing. Residuals under 0.5 uu over the measured pitches.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "local/spyhand/capture.py: crouched forward -2.558 - 0.000808 p, right 16.243, up 38.407 + 0.001584 p")]
    public static readonly (float A, float B) SpyPistolCrouchedForward = (-2.558f, -0.000808f);

    [Provenance(ProvenanceKind.Measured, "as SpyPistolCrouchedForward")]
    public const float SpyPistolCrouchedRight = 16.243f;

    [Provenance(ProvenanceKind.Measured, "as SpyPistolCrouchedForward")]
    public static readonly (float A, float B) SpyPistolCrouchedUp = (38.407f, 0.001584f);

    [Provenance(ProvenanceKind.Measured, "local/spyhand/capture.py: standing forward -3.668 - 0.000977 p, right 14.705, up 46.344 + 0.001410 p")]
    public static readonly (float A, float B) SpyPistolStandingForward = (-3.668f, -0.000977f);

    [Provenance(ProvenanceKind.Measured, "as SpyPistolStandingForward")]
    public const float SpyPistolStandingRight = 14.705f;

    [Provenance(ProvenanceKind.Measured, "as SpyPistolStandingForward")]
    public static readonly (float A, float B) SpyPistolStandingUp = (46.344f, 0.001410f);

    /// <summary>
    /// The drawn spy pistol's world placement (the pistol actor at B R Hand, which the shot and the laser start from;
    /// spy-shocker.md rules 9 and 14): measured offsets from the pawn in the view's yaw frame, turned to
    /// <see cref="SpyPistolPitchShare"/> of the view pitch. The simulation has no skeleton, so the pose the aim
    /// animations give is replaced by these fits (still pose, drawn, WeaponLoaded 2).
    /// </summary>
    [UcNative("AActor::execGetBoneCoords")]
    [Provenance(ProvenanceKind.Measured, "local/spyhand/capture.py: pistol Location and Rotation of a drawn spy, crouched and standing, 12 aims")]
    public static FCoords SpyPistolCoords(Pawn spy, FRotator view)
    {
        int pitch = (short)view.Pitch;
        bool crouched = spy.bIsCrouched;
        (float A, float B) fwd = crouched ? SpyPistolCrouchedForward : SpyPistolStandingForward;
        (float A, float B) up = crouched ? SpyPistolCrouchedUp : SpyPistolStandingUp;
        float right = crouched ? SpyPistolCrouchedRight : SpyPistolStandingRight;
        FCoords yawFrame = FCoords.FromRotator(new FRotator(0, view.Yaw, 0));
        FCoords c = FCoords.FromRotator(new FRotator(SvmMath.Round((SpyPistolPitchShare * pitch) + (crouched ? SpyPistolCrouchedPitchOffset : SpyPistolStandingPitchOffset)), view.Yaw, 0));
        c.Origin = spy.Location + (yawFrame.XAxis * (fwd.A + (fwd.B * pitch))) + (yawFrame.YAxis * right) +
            (yawFrame.ZAxis * (up.A + (up.B * pitch)));
        return c;
    }

    /// <summary>Weapon muzzle offset in front of the eye for the estimated FX bone.</summary>
    [Provenance(ProvenanceKind.Estimated, "FX bone of the first-person weapon, about 60 uu in front of the eye")]
    public const float EstimatedMuzzleForward = 60f;

    /// <summary>
    /// The downed flags the native action handler sets for the KO actions (execute-action.md cases 320 and 11):
    /// SA_REMOVE_COLLISION lays the pawn down (no attacker or target, finishable and lying, sleeping while alive,
    /// collision (1, 0, 0)); SA_STANDUP clears the finishable and sleeping flags (the rest of case 11 and the wake time
    /// are in <see cref="Animation.CombatActionAnimation"/>).
    /// </summary>
    [UcNative("ASBasePawn::execExecuteAction", Exe = 0x10ad6800)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 320 and 11 (ANSWERS-actions 11): no team test on bCanBeTerminated, bSleeping only while Health > 0")]
    public static void ApplyActionFlags(SBasePawn Pawn, int Action)
    {
        switch (Action)
        {
            case SBasePawn.SA_REMOVE_COLLISION:
                Pawn.m_AttackedBy = null;
                Pawn.m_AttackTarget = null;
                Pawn.bCanBeTerminated = true;
                Pawn.bLyingOnGround = true;
                if (Pawn.Health > 0)
                {
                    Pawn.bSleeping = true;
                }

                Pawn.SetCollision(true, false, false);
                break;
            case SBasePawn.SA_STANDUP:
                Pawn.bSleeping = false;
                Pawn.bCanBeTerminated = false;
                break;
        }
    }

    /// <summary>The walking state GotoWaitingState picks: by stance, and by side and weapon for the hands.</summary>
    [Provenance(ProvenanceKind.Estimated, "GotoWaitingState walking state by side, stance and weapon (movement spec owns it)")]
    public static Name WaitingStateFor(SPlayerController PC)
    {
        SBasePawn? pawn = PC.SBPawn;
        bool crouched = pawn?.bIsCrouched ?? false;
        bool armed = pawn is not null && (pawn.SPawnType == PawnType.SPAWNDEFENSE ? pawn.WeaponLoaded != 0 : pawn.WeaponLoaded > 1);
        if (crouched)
        {
            return armed ? NAME_s_PlayerWalkingCr2H : NAME_s_PlayerWalkingCr0H;
        }

        return armed ? NAME_s_PlayerWalkingSt2H : NAME_s_PlayerWalkingSt0H;
    }

    /// <summary>
    /// ChooseDeathAnimation (native 1405, game 0x10acd3e0, editor 0x11212f30): a zero momentum dies in place; else the
    /// normalised momentum against the pawn's forward axis picks forward (at least 0.7), backward (at most -0.7) or the
    /// side of <c>M.X F.Y - M.Y F.X</c> (left when not negative), then <see cref="ValidDeathDirection"/> looks for room.
    /// </summary>
    [UcNative("ASBasePawn::execChooseDeathAnimation", Exe = 0x10acd390, Editor = 0x11212f30)]
    [PortOf("nat:SBase.SBasePawn.ChooseDeathAnimation", Spec = "spec/combat/damage-pipeline.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::ChooseDeathAnimation game 0x10acd3e0: thresholds 0.7, side test >= 0, then GetValidDeathDirection")]
    public static EDeathDirection ChooseDeathAnimation(SBasePawn Pawn, FVector Momentum)
    {
        if (Momentum.X == 0f && Momentum.Y == 0f && Momentum.Z == 0f)
        {
            return EDeathDirection.DIE_InPlace;
        }

        return ValidDeathDirection(Pawn, DeathDirectionOf(Pawn.Rotation, Momentum));
    }

    /// <summary>The direction ChooseDeathAnimation asks for, before the room check (momentum not zero).</summary>
    public static EDeathDirection DeathDirectionOf(FRotator PawnRotation, FVector Momentum)
    {
        FVector m = Momentum.SafeNormal();
        PawnRotation.GetAxes(out FVector f, out _, out _);
        float a = f.X * m.X + m.Y * f.Y + f.Z * m.Z;
        if (a <= 0f)
        {
            if (a <= -K.DeathFrontBack)
            {
                return EDeathDirection.DIE_Backward;
            }
        }
        else if (K.DeathFrontBack <= a)
        {
            return EDeathDirection.DIE_Forward;
        }

        return 0f <= m.X * f.Y - m.Y * f.X ? EDeathDirection.DIE_Left : EDeathDirection.DIE_Right;
    }

    /// <summary>
    /// GetValidDeathDirection (game 0x10acd4d0, editor 0x11206c00): up to four directions are tried, the asked one, its
    /// opposite, then the other axis (left for forward and backward, else forward) and its opposite; the first with
    /// room wins, none dies in place.
    /// </summary>
    [UcNative("ASBasePawn::GetValidDeathDirection", Exe = 0x10acd4d0, Editor = 0x11206c00)]
    [PortOf("nat:SBase.SBasePawn.ChooseDeathAnimation", Spec = "spec/combat/damage-pipeline.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "GetValidDeathDirection: the try order and the room test of each direction")]
    public static EDeathDirection ValidDeathDirection(SBasePawn Pawn, EDeathDirection Asked)
    {
        ICollisionWorld? world = TraceHitZones.CollisionOf(Pawn);
        if (world is null)
        {
            return Asked;
        }

        EDeathDirection dir = Asked;
        for (int tries = 0; tries <= 3; tries++)
        {
            if (dir is >= EDeathDirection.DIE_Forward and <= EDeathDirection.DIE_Right && HasRoomToFall(world, Pawn, dir))
            {
                return dir;
            }

            if (tries is 0 or 2)
            {
                dir = dir switch
                {
                    EDeathDirection.DIE_Forward => EDeathDirection.DIE_Backward,
                    EDeathDirection.DIE_Backward => EDeathDirection.DIE_Forward,
                    EDeathDirection.DIE_Left => EDeathDirection.DIE_Right,
                    EDeathDirection.DIE_Right => EDeathDirection.DIE_Left,
                    _ => dir,
                };
            }
            else if (tries == 1)
            {
                dir = Asked is EDeathDirection.DIE_Forward or EDeathDirection.DIE_Backward ? EDeathDirection.DIE_Left : EDeathDirection.DIE_Forward;
            }
        }

        return EDeathDirection.DIE_InPlace;
    }

    /// <summary>
    /// The room test of one direction D (unit, from the pawn's rotation axes: forward, -forward, -right, right): the box
    /// (R + 20, R + 20, H - 15) at Location + 2R D clear of the level's BSP, and a floor under Location + 1.5R D and
    /// Location + 2.5R D (a line 1.25 H down, flags 0x2df, from the pawn).
    /// </summary>
    private static bool HasRoomToFall(ICollisionWorld World, SBasePawn Pawn, EDeathDirection Dir)
    {
        Pawn.Rotation.GetAxes(out FVector x, out FVector y, out _);
        FVector d = Dir switch
        {
            EDeathDirection.DIE_Forward => x,
            EDeathDirection.DIE_Backward => -x,
            EDeathDirection.DIE_Left => -y,
            _ => y,
        };

        float r = Pawn.CollisionRadius;
        FVector loc = Pawn.Location;
        FVector extent = new(r + K.DeathRoomExtentGrow, r + K.DeathRoomExtentGrow, Pawn.CollisionHeight - K.DeathRoomExtentShrinkZ);
        FVector down = new(0f, 0f, Pawn.CollisionHeight * -K.DeathFloorDepth);
        FVector box = new(d.X * r + d.X * r + loc.X, d.Y * r + d.Y * r + loc.Y, d.Z * r + d.Z * r + loc.Z);
        if (World.PointCheck(out _, null, box, extent, ETraceFlags.TRACE_Level))
        {
            return false;
        }

        return HasFloor(World, Pawn, d * r * K.DeathFloorNear + loc, down) && HasFloor(World, Pawn, d * r * K.DeathFloorFar + loc, down);
    }

    private static bool HasFloor(ICollisionWorld World, SBasePawn Pawn, FVector Start, FVector Down) =>
        World.LineCheck(out _, Pawn, Start + Down, Start, default, (ETraceFlags)K.DeathFloorTraceFlags);

    /// <summary>
    /// The armed spy's pistol points at what his behind camera looks at: the first world surface or pawn on the camera's
    /// line (as last drawn, the camera is not solved again), or null without a camera (see <see cref="TurnTowardCameraTarget"/>).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The pistol bone follows the aim animation toward the reticle; here it is turned straight at the camera's target")]
    public static FRotator? AimedAtCameraTarget(SPlayerController pc, Pawn owner, FVector muzzle)
    {
        if (!pc.bBehindView || PlayerCamera.Of(pc) is not { } camera || camera.m_PrevPosition.IsZero())
        {
            return null;
        }

        PlayerCalcView.NativePlayerCalcView(pc, false, out _, out FVector from, out FRotator look);
        FVector end = from + look.Vector() * Equipment.K.TraceLength;
        FVector target = GadgetWorld.Line(owner, end, from, ETraceFlags.TRACE_World | ETraceFlags.TRACE_Pawns, out FCheckResult hit)
            ? hit.Location
            : end;
        return TurnTowardCameraTarget(muzzle, look, target);
    }

    /// <summary>
    /// The turn toward the camera's target: null (the camera's own direction is kept) when the target is less than
    /// <see cref="MinCameraTargetDistance"/> ahead of the muzzle along the camera's look, as when a pawn stands between
    /// the camera and the estimated muzzle point; turning toward it would point the pistol back at the camera.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The animated pistol never points behind the reticle's direction; a target behind or at the estimated muzzle keeps the camera's direction")]
    public static FRotator? TurnTowardCameraTarget(FVector muzzle, FRotator look, FVector target)
    {
        FVector d = target - muzzle;
        return (d | look.Vector()) < MinCameraTargetDistance ? null : d.Rotation();
    }

    /// <summary>Below this the camera's target is too close to turn the pistol toward.</summary>
    [Provenance(ProvenanceKind.Estimated, "Keeps the camera's own direction for targets at the muzzle")]
    public const float MinCameraTargetDistance = 16f;

    /// <summary>
    /// The placement of a weapon's FX bone without the skeletal data: a player merc's aimed rifle and a spy's pistol where
    /// the original holds them (measured, <see cref="MercAimFxCoords"/> and <see cref="SpyPistolCoords"/>), any other weapon in
    /// front of the owner's eye along the aim (the merc's desired rotation, the spy's camera), else the actor's own
    /// placement. The spy's pistol used to be the eye point plus 60 uu along the camera turned at its target, so the
    /// crouched spy's shots rose and fell with his eye height.
    /// </summary>
    [UcNative("AActor::execGetBoneCoords")]
    [Provenance(ProvenanceKind.Estimated, "FX bone placement without the skeletal mesh: eye point plus 60 uu along the aim")]
    public static FCoords EstimatedBoneCoords(Actor Actor)
    {
        if (Actor is Inventory { Instigator: { } owner })
        {
            // A merc's mouse moves his desired rotation (the rifle's aim); a spy's moves only his camera, which the
            // armed spy's pistol follows.
            FRotator aim = owner.Controller is SPlayerController pc
                ? owner is SPawnAttaque ? pc.Rotation : pc.MyDesiredRotation
                : owner.Rotation;
            if (owner.Controller is null)
            {
                // Another machine's spy: his view comes packed in iYawPitchFromView (replicated to simulated proxies,
                // SBasePawn.TurnHead), which also aims his drawn arms; the pawn's own rotation has no pitch.
                aim = owner is SPawnAttaque { iYawPitchFromView: not 0 } remoteSpy
                    ? new FRotator((short)(remoteSpy.iYawPitchFromView >> 16), remoteSpy.iYawPitchFromView & 0xffff, 0)
                    : owner.Rotation;
            }

            FCoords c = FCoords.FromRotator(aim);
            c.Origin = owner.Location + new FVector(0f, 0f, owner.EyeHeight) + c.XAxis * EstimatedMuzzleForward;
            if (owner is SPAWNDEFENSE && owner.Controller is SPlayerController)
            {
                // The merc's aimed rifle as the original places its FX bone (his laser's start, measured).
                return MercAimFxCoords(owner, aim);
            }

            if (owner is SPawnAttaque spyPawn && Actor is SWeaponAttaque)
            {
                return SpyPistolCoords(spyPawn, aim);
            }

            return c;
        }

        FCoords coords = FCoords.FromRotator(Actor.Rotation);
        coords.Origin = Actor.Location;
        return coords;
    }
}
