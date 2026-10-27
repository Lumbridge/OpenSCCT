using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// PHYS_RootMotion (ASBasePawn::physRootMotion): once per game time step, the mesh's root delta moves the pawn,
    /// with collision off, or through the additional physics mode (None: two collision moves; Walking: physWalking
    /// with the delta as a velocity; Falling: physFallingInRootMotion; OnPipe and the other GE modes). Velocity is the
    /// displacement over the elapsed game time.
    /// </summary>
    [UcNative("ASBasePawn::physRootMotion", Exe = 0x10acac40, Editor = 0x112128c0)]
    [PortOf("eng:ASBasePawn::physRootMotion", Spec = "spec/native/physics-rootmotion-repositioning.md#physrootmotiondt")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md physRootMotion rules 1 to 8")]
    [Provenance(ProvenanceKind.Estimated, "Lock bit 2 read as the value 2; collision is switched by the flags only (no touch events), the native calls SetCollision")]
    public void physRootMotion(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb || sb.RootMotionMesh is not { } mesh)
        {
            p.Velocity = default;
            p.Acceleration = default;
            return;
        }

        if (mesh.RootLockType == 0)
        {
            mesh.LockRootMotion(sb.iLockRootMotionType);
        }

        float now = p.Level.TimeSeconds;
        if (now < sb.fLastRootMotionTimeCalled + K.RootMotion.TimeGuard)
        {
            return;
        }

        FVector oldLocation = p.Location;
        bool collideWorld = p.bCollideWorld;
        bool collideActors = p.bCollideActors;
        mesh.AdvanceRootMotion(out FVector rootDelta, out FRotator rootRot);
        sb.vDeltaMove = rootDelta;
        bool applyRot = (sb.iLockRootMotionType & K.RootMotion.LockRotationBit) != 0;
        FRotator withRot = applyRot ? p.Rotation + rootRot : p.Rotation;
        if (!p.bUseRootMotionAdditionnalPhysics)
        {
            p.bCollideActors = false;
            p.bCollideWorld = false;
            MoveActor(p, sb.vDeltaMove, withRot, out _);
            p.bCollideActors = collideActors;
            p.bCollideWorld = collideWorld;
        }
        else
        {
            switch (sb.RootMotionAdditionnalPhysics)
            {
                case EPhysics.PHYS_None:
                    MoveActor(p, new FVector(0f, 0f, sb.vDeltaMove.Z), p.Rotation, out _);
                    MoveActor(p, new FVector(sb.vDeltaMove.X, sb.vDeltaMove.Y, 0f), applyRot ? p.Rotation + rootRot : p.Rotation, out _);
                    break;
                case EPhysics.PHYS_Walking:
                    if (applyRot)
                    {
                        MoveActor(p, default, p.Rotation + rootRot, out _);
                    }

                    p.Rotation.Roll = 0;
                    sb.vDeltaMove.Z = 0f;
                    sb.vDeltaMove = sb.vDeltaMove * (1f / dt);
                    if (p.Controller is not null)
                    {
                        physWalking(p, dt, 0);
                    }
                    else
                    {
                        physWalkingSimulated(p, dt, 0);
                    }

                    break;
                case EPhysics.PHYS_Falling:
                    physFallingInRootMotion(p, dt, 0, applyRot ? rootRot : default);
                    break;
                case EPhysics.PHYS_OnPipe:
                    p.Acceleration = default;
                    physOnPipe(p, dt);
                    p.bCollideActors = false;
                    p.bCollideWorld = false;
                    MoveActor(p, sb.vDeltaMove, p.Rotation + rootRot, out _);
                    p.bCollideActors = collideActors;
                    p.bCollideWorld = collideWorld;
                    break;
                case EPhysics.PHYS_Linear:
                    physLinear(p, dt);
                    break;
                case EPhysics.PHYS_OnVerticalPlane:
                    physOnVerticalPlane(p, dt);
                    break;
                case EPhysics.PHYS_Ledge:
                    physLedge(p, dt);
                    break;
                case EPhysics.PHYS_Vandamme:
                    physVandamme(p, dt);
                    break;
                case EPhysics.PHYS_OnLadder:
                    physOnLadder(p, dt);
                    break;
                case EPhysics.PHYS_OnPipeH:
                    physOnPipeH(p, dt);
                    break;
            }
        }

        if (p.bDeleteMe)
        {
            return;
        }

        float elapsed = now - sb.fLastRootMotionTimeCalled;
        if (!p.bJustTeleported && (p.Role != ENetRole.ROLE_SimulatedProxy || p.bUseRootMotionAdditionnalPhysics) && elapsed > 0f)
        {
            p.Velocity = (p.Location - oldLocation) * (1f / elapsed);
        }

        sb.fLastRootMotionTimeCalled = now;
    }

    /// <summary>
    /// ASBasePawn::physFallingInRootMotion: the out-of-world test, the authority's drop-attack trigger with the
    /// root-motion bounds (the first trigger ends the call), the root delta as a vertical then a horizontal move, and a
    /// hit raises NotifyHitWall (Normal.Z up to 0.707) or MyNotifyLanded. The apex is raised.
    /// </summary>
    [UcNative("ASBasePawn::physFallingInRootMotion", Exe = 0x10ad38d0, Editor = 0x1120c440)]
    [PortOf("eng:ASBasePawn::physFallingInRootMotion", Spec = "spec/native/physics-rootmotion-repositioning.md#physfallinginrootmotiondt-iterations-r")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md physFallingInRootMotion rules 1 to 5")]
    [Provenance(ProvenanceKind.Estimated, "When both moves hit, the horizontal move's hit is the one reported")]
    internal void physFallingInRootMotion(Pawn p, float dt, int iterations, FRotator rootRot)
    {
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        Controller? ctrl = p.Controller;
        float death = p.Level.fDeathPawnHeight;
        if (death != 0f && p.Location.Z < death && IsControllingSide(ctrl))
        {
            (ctrl as IPhysicsEvents)?.eventFellOutOfWorld();
            if (p.bDeleteMe)
            {
                return;
            }
        }

        if (p.Role == ENetRole.ROLE_Authority && DropAttackScan(p, sb, rootMotion: true))
        {
            return;
        }

        MoveActor(p, new FVector(0f, 0f, sb.vDeltaMove.Z), p.Rotation, out FCheckResult vertical);
        if (p.bDeleteMe)
        {
            return;
        }

        MoveActor(p, new FVector(sb.vDeltaMove.X, sb.vDeltaMove.Y, 0f), p.Rotation + rootRot, out FCheckResult horizontal);
        if (p.bDeleteMe)
        {
            return;
        }

        FCheckResult hit = horizontal.bHit ? horizontal : vertical;
        if (hit.bHit && ctrl is IPhysicsEvents events)
        {
            if (hit.Normal.Z <= K.Fall.RootMotionLandZ)
            {
                events.eventNotifyHitWall(hit.Normal, hit.Actor);
            }
            else
            {
                events.eventMyNotifyLanded(hit.Normal, (1f - hit.Time) * dt, hit.Actor);
            }
        }

        if (!p.bDeleteMe && p.Location.Z > ZMaxFalling(p))
        {
            SetZMaxFalling(p, p.Location.Z);
        }
    }

    /// <summary>
    /// ASBasePawn::EndRootMotion, when the root-motion clip ends (Role at least AutonomousProxy): vSpeedAfterRM becomes
    /// the velocity, then the pawn takes its final yaw and is placed without collision at the start of the move plus
    /// vMoveAfterRM (or back at the start with bReverseRM).
    /// </summary>
    [UcNative("ASBasePawn::EndRootMotion", Exe = 0x10ad65e0, Editor = 0x111fd160)]
    [PortOf("eng:ASBasePawn::EndRootMotion", Spec = "spec/native/physics-rootmotion-repositioning.md#endrootmotion-virtual-called-when-the-root-motion-clip-ends")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md EndRootMotion rules 1 to 5")]
    public void EndRootMotion(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb || Pawn.Role < ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        if (!sb.vSpeedAfterRM.IsZero())
        {
            Pawn.Velocity = sb.vSpeedAfterRM;
            sb.vSpeedAfterRM = default;
        }

        if (sb.vMoveAfterRM.IsZero() && sb.rRotAfterRM == 0)
        {
            return;
        }

        FVector destination;
        bool move;
        if (sb.bReverseRM)
        {
            MoveActor(Pawn, default, new FRotator(0, Pawn.rRotBeforeRM, 0), out _);
            destination = Pawn.vLocationBeforeRM;
            move = true;
        }
        else
        {
            MoveActor(Pawn, default, new FRotator(0, Pawn.rRotBeforeRM + sb.rRotAfterRM, 0), out _);
            move = !sb.vMoveAfterRM.IsZero();
            destination = Pawn.vLocationBeforeRM + sb.vMoveAfterRM;
        }

        if (move && !Pawn.bDeleteMe)
        {
            FarMoveActor(Pawn, destination, bTest: false, bNoCheck: true, bAttachedMove: true);
        }

        sb.bReverseRM = false;
    }

    /// <summary>
    /// ASBasePawn::bServerTestRootMotion, the server's check of a client root-motion action: the ledge pull-up (113)
    /// needs TestTopLedge(false) to return 1, else the client gets ClientStopRM(113) and the action is refused. An
    /// accepted action stores the end move and yaw and enters PHYS_RootMotion.
    /// </summary>
    [UcNative("ASBasePawn::bServerTestRootMotion", Editor = 0x11215580)]
    [PortOf("eng:ASBasePawn::bServerTestRootMotion", Spec = "spec/native/physics-rootmotion-repositioning.md#bservertestrootmotionaction-vmove-yaw-server")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md bServerTestRootMotion")]
    public bool bServerTestRootMotion(Pawn Pawn, int Action, FVector vMove, float Yaw)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb)
        {
            return false;
        }

        if (Action == K.RootMotion.ServerTestedAction && sb.RootMotionMesh is not null && TestTopLedge(Pawn, false) != 1)
        {
            (Pawn.Controller as ISPlayerControllerPhysics)?.eventClientStopRM(Action);
            return false;
        }

        sb.vMoveAfterRM = vMove;
        sb.rRotAfterRM = SvmMath.Round(Yaw);
        setPhysics(Pawn, EPhysics.PHYS_RootMotion);
        return true;
    }

    /// <summary>
    /// PHYS_Repositionnal (ASBasePawn::physRepositionning): a straight line to vVandammePos at fPhysReposLinearSpeed
    /// (Z ignored within 5 uu or with bUseAdditionnalPhysicsForRepos), turning toward vWallJumpDir by 3000 per call;
    /// through the world with bCollideWorld off, or walking with collision. TopSynchro on arrival (within 1 uu with the
    /// yaw reached). Acceleration is cleared.
    /// </summary>
    [UcNative("ASBasePawn::physRepositionning", Exe = 0x10ad2220, Editor = 0x111fc7a0)]
    [PortOf("eng:ASBasePawn::physRepositionning", Spec = "spec/native/physics-rootmotion-repositioning.md#physrepositionningdt")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md physRepositionning rules 1 to 8")]
    public void physRepositionning(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        float now = p.Level.TimeSeconds;
        if (now < sb.fLastRootMotionTimeCalled)
        {
            return;
        }

        sb.fLastRootMotionTimeCalled = now + dt;
        FVector start = p.Location;
        FVector d = ReposOffset(p, sb);
        float step = sb.fPhysReposLinearSpeed * dt;
        float len = d.Size();
        FVector move = len <= step || len <= K.RootMotion.SnapTolerance ? d : d.SafeNormal() * step;
        int targetYaw = sb.vWallJumpDir.Rotation().Yaw;
        int newYaw = FixedTurn(p.Rotation.Yaw, targetYaw, K.RootMotion.TurnRate);
        var newRotation = new FRotator(0, newYaw, 0);
        if (!sb.bUseAdditionnalPhysicsForRepos)
        {
            p.bCollideWorld = false;
            MoveActor(p, move, newRotation, out _);
            p.bCollideWorld = true;
        }
        else
        {
            MoveActor(p, default, newRotation, out _);
            sb.vDeltaMove = new FVector(move.X, move.Y, 0f) * (1f / dt);
            p.Rotation.Roll = 0;
            physWalking(p, dt, 0);
        }

        if (p.bDeleteMe)
        {
            return;
        }

        p.Velocity = (p.Location - start) * (1f / dt);
        if (ReposOffset(p, sb).Size() < K.RootMotion.ArrivalDistance && SameYaw(newYaw, targetYaw) &&
            p.Controller is ISPlayerControllerPhysics pc)
        {
            pc.eventTopSynchro();
        }

        p.Acceleration = default;
    }

    private static FVector ReposOffset(Pawn p, ISBasePawnPhysics sb)
    {
        FVector d = sb.vVandammePos - p.Location;
        if (SvmMath.Abs(d.Z) < K.RootMotion.ReposDeadBand || sb.bUseAdditionnalPhysicsForRepos)
        {
            d.Z = 0f;
        }

        return d;
    }

    /// <summary>
    /// ASBasePawn::EndPhysRepositionning (setPhysics leaving repositioning on the authority): snaps the pawn through
    /// the world to vVandammePos with the vWallJumpDir yaw and stops it, unless bNoReposAtEnd (cleared once).
    /// </summary>
    [UcNative("ASBasePawn::EndPhysRepositionning", Editor = 0x111fcbc0)]
    [PortOf("eng:ASBasePawn::EndPhysRepositionning", Spec = "spec/native/physics-rootmotion-repositioning.md#endphysrepositionning-setphysics-leaving-mode-11-authority")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md EndPhysRepositionning")]
    public void EndPhysRepositionning(Pawn Pawn)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb)
        {
            return;
        }

        if (sb.bNoReposAtEnd)
        {
            sb.bNoReposAtEnd = false;
            return;
        }

        FVector d = sb.vVandammePos - Pawn.Location;
        if (sb.bUseAdditionnalPhysicsForRepos)
        {
            d.Z = 0f;
        }

        Pawn.bCollideWorld = false;
        MoveActor(Pawn, d, new FRotator(0, sb.vWallJumpDir.Rotation().Yaw, 0), out _);
        Pawn.bCollideWorld = true;
        Pawn.Velocity = default;
        Pawn.Acceleration = default;
    }

    /// <summary>
    /// PHYS_Vandamme (ASBasePawn::physVandamme): a straight line to vVandammePos at 87 uu/s, facing turned (3000 per
    /// call) to the forward axis with its vWallJumpDir part removed. Velocity is the displacement times dt, as in the
    /// original.
    /// </summary>
    [UcNative("ASBasePawn::physVandamme", Exe = 0x10ac8d20, Editor = 0x111fb380)]
    [PortOf("eng:ASBasePawn::physVandamme", Spec = "spec/native/physics-rootmotion-repositioning.md#physvandammedt-iterations")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md physVandamme rules 1 to 4 (the velocity scaling is open question 2)")]
    public void physVandamme(Pawn Pawn, float dt)
    {
        NotNull(Pawn);
        Pawn p = Pawn;
        if (p is not ISBasePawnPhysics sb)
        {
            return;
        }

        FVector start = p.Location;
        FVector d = sb.vVandammePos - p.Location;
        float step = K.RootMotion.VandammeSpeed * dt;
        float len = d.Size();
        FVector move = len <= step || len <= K.RootMotion.SnapTolerance ? d : d.SafeNormal() * step;
        FVector fwd = p.Rotation.Vector();
        FVector w = sb.vWallJumpDir;
        FVector u = (w ^ fwd).SafeNormal();
        FVector f = u ^ w;
        int newYaw = FixedTurn(p.Rotation.Yaw, f.Rotation().Yaw, K.RootMotion.TurnRate);
        MoveActor(p, move, new FRotator(p.Rotation.Pitch, newYaw, p.Rotation.Roll), out _);
        if (p.bDeleteMe)
        {
            return;
        }

        p.Velocity = (p.Location - start) * dt;
        p.Acceleration = default;
    }
}
