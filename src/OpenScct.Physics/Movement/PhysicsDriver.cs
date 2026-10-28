using System;
using System.Collections.Generic;
using OpenScct.Content.Actors;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

/// <summary>
/// The engine physics over a <see cref="CollisionWorld"/>: performPhysics and the mode switch, the SBasePawn modes
/// (walking, falling, root motion, repositioning, Vandamme and the GE traversal modes) and their natives, as decoded
/// in spec/native (docs/PHYSICS.md), plus the stock actor modes (falling, projectile, moving brush) for non-pawns.
/// Set it on <see cref="Level.PhysicsDriver"/>. A <see cref="IPhysicsModeHandler"/> can replace any mode.
/// Unported actor markers have no physics.
/// </summary>
public sealed partial class PhysicsDriver : IActorPhysics
{
    private readonly IPhysicsModeHandler?[] _handlers = new IPhysicsModeHandler?[32];
    private readonly List<List<Actor>> _listPool = new();
    private int _listDepth;
    private PawnPhysicsState?[] _states = new PawnPhysicsState?[64];

    public PhysicsDriver(CollisionWorld collision, GEIndex? ges = null)
    {
        ArgumentNullException.ThrowIfNull(collision);
        Collision = collision;
        GEs = ges ?? new GEIndex([]);
    }

    public CollisionWorld Collision { get; }

    /// <summary>The map's GE index, which the zone natives and the traversal modes search.</summary>
    public GEIndex GEs { get; }

    /// <summary>The physics driver of the level an actor is in, or null.</summary>
    public static PhysicsDriver? Of(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.XLevel?.PhysicsDriver as PhysicsDriver;
    }

    /// <summary>Installs (or with null removes) the implementation of one physics mode.</summary>
    public void SetModeHandler(EPhysics mode, IPhysicsModeHandler? handler) => _handlers[(int)mode] = handler;

    /// <summary>The physics state of a pawn: its own when it owns one, else one the driver keeps.</summary>
    public PawnPhysicsState GetState(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (pawn is IPawnPhysicsStateOwner owner)
        {
            return owner.PhysicsState;
        }

        int id = pawn.ActorId;
        if (id >= _states.Length)
        {
            Array.Resize(ref _states, Math.Max(id + 1, _states.Length * 2));
        }

        return _states[id] ??= new PawnPhysicsState();
    }

    /// <summary>
    /// The highest Location.Z of the pawn's airborne phase: its SPlayerController's fZMaxFalling, or the driver's copy
    /// for a pawn without one.
    /// </summary>
    public float ZMaxFalling(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        return pawn.Controller is ISPlayerControllerPhysics pc ? pc.fZMaxFalling : GetState(pawn).fZMaxFalling;
    }

    private void SetZMaxFalling(Pawn pawn, float z)
    {
        if (pawn.Controller is ISPlayerControllerPhysics pc)
        {
            pc.fZMaxFalling = z;
        }

        GetState(pawn).fZMaxFalling = z;
    }

    /// <summary>Gravity Z for a non-pawn actor: LevelInfo.DefaultGravity, or the decoded default when the level has none set.</summary>
    public static float GetGravityZ(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        float g = actor.Level?.DefaultGravity ?? 0f;
        return g != 0f ? g : K.DefaultGravity;
    }

    // ---- dispatch -----------------------------------------------------------------------------------------

    /// <summary>
    /// One tick of an actor's physics. Pawns (ASBasePawn::performPhysics): OldZ, the mode switch, bSimulateGravity
    /// for walking and falling, the controller's MoveTimer, OldAcceleration and the AvgPhysicsTime filter. Other
    /// actors: the mode switch. Then the KillZ fallback.
    /// </summary>
    [UcNative("ASBasePawn::performPhysics", Exe = 0x10ace9f0, Editor = 0x11207b70)]
    [UcNative("AActor::performPhysics", Editor = 0x111803f0)]
    [PortOf("eng:ASBasePawn::performPhysics", Spec = "spec/native/physics-dispatch.md#performphysicsdeltatime-once-per-pawn-tick")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-dispatch.md performPhysics rules 1 to 5")]
    [Provenance(ProvenanceKind.Estimated, "physicsRotation (rule 4) and PendingTouch (rule 6) are not run: physicsRotation is not decoded and the script sets Rotation itself")]
    public void performPhysics(Actor Actor, float DeltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(Actor);
        if (Actor is UnportedActor || Actor.bDeleteMe)
        {
            return;
        }

        if (Actor is Pawn pawn)
        {
            pawn.OldZ = pawn.Location.Z;
            startNewPhysics(pawn, DeltaSeconds, 0);
            if (pawn.bDeleteMe)
            {
                return;
            }

            pawn.bSimulateGravity = pawn.Physics is EPhysics.PHYS_Walking or EPhysics.PHYS_Falling;
            if (pawn.Controller is { } controller)
            {
                controller.MoveTimer -= DeltaSeconds;
            }

            pawn.OldAcceleration = pawn.Acceleration;
            pawn.AvgPhysicsTime = K.Dispatch.AvgPhysicsTimeNew * DeltaSeconds + K.Dispatch.AvgPhysicsTimeOld * pawn.AvgPhysicsTime;
        }
        else
        {
            startNewPhysics(Actor, DeltaSeconds, 0);
            ActorRotation(Actor, DeltaSeconds);
        }

        if (!Actor.bDeleteMe)
        {
            CheckKillZ(Actor);
        }
    }

    /// <summary>Runs <paramref name="DeltaTime"/> of the actor's current physics mode (startNewPhysics with Iterations 0).</summary>
    public void StartNewPhysics(Actor Actor, float DeltaTime) => startNewPhysics(Actor, DeltaTime, 0);

    /// <summary>
    /// The mode switch (ASBasePawn::startNewPhysics). Returns at once below 0.0003 s or above 7 iterations. Only
    /// walking, falling, FollowParent and flying see the iteration count; the traversal modes start at zero.
    /// </summary>
    [UcNative("ASBasePawn::startNewPhysics", Exe = 0x10acaaf0, Editor = 0x112157c0)]
    [PortOf("eng:ASBasePawn::startNewPhysics", Spec = "spec/native/physics-dispatch.md#startnewphysicsdeltatime-iterations")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-dispatch.md mode table")]
    [Provenance(ProvenanceKind.Estimated, "Flying (16) is not decoded and does nothing here; non-pawn actors keep the stock AActor modes")]
    public void startNewPhysics(Actor Actor, float DeltaTime, int Iterations)
    {
        ArgumentNullException.ThrowIfNull(Actor);
        if (DeltaTime < K.Dispatch.MinStep || Iterations > K.Dispatch.MaxIterations || Actor.bDeleteMe)
        {
            return;
        }

        if (_handlers[(int)Actor.Physics] is { } handler)
        {
            handler.performPhysics(this, Actor, DeltaTime);
            return;
        }

        if (Actor is not Pawn pawn)
        {
            switch (Actor.Physics)
            {
                case EPhysics.PHYS_Falling:
                    physFallingActor(Actor, DeltaTime, Iterations);
                    break;
                case EPhysics.PHYS_Projectile:
                    physProjectile(Actor, DeltaTime);
                    break;
                case EPhysics.PHYS_MovingBrush:
                    physMovingBrush(Actor, DeltaTime);
                    break;
            }

            return;
        }

        switch (pawn.Physics)
        {
            case EPhysics.PHYS_Walking:
                physWalking(pawn, DeltaTime, Iterations);
                break;
            case EPhysics.PHYS_Falling:
                physFalling(pawn, DeltaTime, Iterations);
                break;
            case EPhysics.PHYS_RootMotion:
                physRootMotion(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_OnPipe:
                physOnPipe(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_Linear:
                physLinear(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_OnVerticalPlane:
                physOnVerticalPlane(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_Ledge:
                physLedge(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_Vandamme:
                physVandamme(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_Repositionnal:
                physRepositionning(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_OnLadder:
                physOnLadder(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_FollowParent:
                physFollow(pawn, DeltaTime);
                break;
            case EPhysics.PHYS_OnPipeH:
                physOnPipeH(pawn, DeltaTime);
                break;
        }
    }

    /// <summary>
    /// Changes an actor's physics mode (ASBasePawn::setPhysics over AActor::setPhysics). The same mode changes nothing
    /// (a RootMotion call still sets bActionsSendByServerMove). Leaving repositioning on the authority snaps to its
    /// target first; entering falling from another mode starts the fall apex at Location.Z. Walking, None and
    /// Rotating take <paramref name="NewFloor"/> as the base (or look for one under the actor); every other mode
    /// leaves the base; None and Rotating also stop the actor. Then bActionsSendByServerMove is set and
    /// bKeepLocalVandammePos cleared.
    /// </summary>
    [UcNative("ASBasePawn::setPhysics", Exe = 0x10ad29a0, Editor = 0x111fd020)]
    [UcNative("AActor::setPhysics", Editor = 0x1117bd30)]
    [PortOf("eng:ASBasePawn::setPhysics", Spec = "spec/native/physics-dispatch.md#setphysicsnewphysics-newfloor-newfloorv")]
    [PortOf("nat:Engine.Actor.SetPhysics", Spec = "spec/native/physics-dispatch.md#setphysicsnewphysics-newfloor-newfloorv")]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-dispatch.md setPhysics rules 1 to 5")]
    public void setPhysics(Actor Actor, EPhysics NewPhysics, Actor? NewFloor = null, FVector NewFloorV = default)
    {
        ArgumentNullException.ThrowIfNull(Actor);
        var pawn = Actor as Pawn;
        var sb = Actor as ISBasePawnPhysics;
        EPhysics old = Actor.Physics;
        if (old == NewPhysics)
        {
            if (sb is not null && NewPhysics == EPhysics.PHYS_RootMotion)
            {
                sb.bActionsSendByServerMove = true;
            }

            return;
        }

        if (sb is not null && pawn is not null && old == EPhysics.PHYS_Repositionnal && Actor.Role == ENetRole.ROLE_Authority)
        {
            EndPhysRepositionning(pawn);
            if (Actor.bDeleteMe)
            {
                return;
            }
        }

        if (pawn is not null && NewPhysics == EPhysics.PHYS_Falling)
        {
            SetZMaxFalling(pawn, pawn.Location.Z);
        }

        // AActor::setPhysics.
        if (old == EPhysics.PHYS_RootMotion && sb?.RootMotionMesh is { } mesh)
        {
            mesh.LockRootMotion(0);
        }

        Actor.Physics = NewPhysics;
        if (NewPhysics is EPhysics.PHYS_Walking or EPhysics.PHYS_None or EPhysics.PHYS_Rotating)
        {
            if (NewFloor is null)
            {
                FindBase(Actor);
            }
            else if (!ReferenceEquals(Actor.Base, NewFloor))
            {
                Actor.SetBase(NewFloor);
                if (pawn is not null && !NewFloorV.IsZero())
                {
                    pawn.Floor = NewFloorV;
                }
            }
        }
        else if (Actor.Base is not null)
        {
            Actor.SetBase(null);
        }

        if (NewPhysics is EPhysics.PHYS_None or EPhysics.PHYS_Rotating)
        {
            Actor.Velocity = default;
            Actor.Acceleration = default;
        }

        if (sb is not null)
        {
            sb.bActionsSendByServerMove = true;
            sb.bKeepLocalVandammePos = false;
        }
    }

    /// <summary>
    /// Bases an actor on what is just under it (AActor::findBase): a box trace of the collision cylinder 8 uu down with
    /// the floor flags 0xdf, then SetBase when the hit actor (or none) differs from the current base. Floor is not
    /// written here.
    /// </summary>
    [UcNative("AActor::findBase", Exe = 0x10a2ae30, Editor = 0x1117a470)]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a2ae30: extent (R, R, H), end Location - (0, 0, 8), flags 0xdf, SetBase(hit actor, hit normal, notify) on change")]
    public void FindBase(Actor Actor)
    {
        ArgumentNullException.ThrowIfNull(Actor);
        FVector start = Actor.Location;
        Collision.LineCheck(out FCheckResult hit, Actor, start + new FVector(0f, 0f, -8f), start, GetCylinderExtent(Actor),
            K.Flags.Floor);
        if (!ReferenceEquals(Actor.Base, hit.Actor))
        {
            Actor.SetBase(hit.Actor);
        }
    }

    /// <summary>
    /// SBasePawn.MyDoJump, on the controlling side only (a Controller whose RemoteRole is not AutonomousProxy), by
    /// physics mode: fence, pipe or ladder (or while SA_LEDGEFALL plays) push off backward, Velocity = -200 x the
    /// facing plus 50 up, then falling; zip line: falling with the velocity kept; falling with nJumpNumber 1: the
    /// second jump, Velocity = 250 vWallJumpDir with Z 700 and nJumpNumber 2 (still falling); Vandamme: Velocity.Z 0,
    /// nJumpNumber 0, falling; walking: Velocity.Z = JumpZ (plus the base's Velocity.Z when the base is not world
    /// geometry), nJumpNumber 1, falling. Any other mode, the ledge hang among them, is left alone: the drop from a
    /// ledge falls from rest through SA_LEDGEFALL's own mode change. setPhysics starts the fall apex.
    /// </summary>
    [UcNative("ASBasePawn::MyDoJump", Exe = 0x10ae00b0, Editor = 0x111f9cf0)]
    [PortOf("nat:SBase.SBasePawn.MyDoJump", Spec = "spec/native/ANSWERS-fidelity.md#f2-mydojump")]
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::MyDoJump editor 0x111f9cf0, game 0x10adff00")]
    public void MyDoJump(Pawn Pawn)
    {
        ArgumentNullException.ThrowIfNull(Pawn);
        if (Pawn.Controller is not { } controller || controller.RemoteRole == ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        var sb = Pawn as ISBasePawnPhysics;
        EPhysics mode = Pawn.Physics;
        if (mode is EPhysics.PHYS_OnVerticalPlane or EPhysics.PHYS_OnPipe or EPhysics.PHYS_OnLadder
            || (sb is not null && sb.sAction == K.Jump.SA_LEDGEFALL))
        {
            Pawn.Velocity = Pawn.Rotation.Vector() * K.Jump.PushOffFacing;
            Pawn.Velocity.Z += K.Jump.PushOffUp;
            setPhysics(Pawn, EPhysics.PHYS_Falling);
            return;
        }

        switch (mode)
        {
            case EPhysics.PHYS_Linear:
                setPhysics(Pawn, EPhysics.PHYS_Falling);
                return;
            case EPhysics.PHYS_Falling:
                if (sb is not null && sb.nJumpNumber == 1)
                {
                    Pawn.Velocity = sb.vWallJumpDir * K.Jump.WallJumpSpeed;
                    Pawn.Velocity.Z = K.Jump.WallJumpZ;
                    sb.nJumpNumber = 2;
                }

                return;
            case EPhysics.PHYS_Vandamme:
                Pawn.Velocity.Z = 0f;
                if (sb is not null)
                {
                    sb.nJumpNumber = 0;
                }

                setPhysics(Pawn, EPhysics.PHYS_Falling);
                return;
            case EPhysics.PHYS_Walking:
                Pawn.Velocity.Z = Pawn.JumpZ;
                if (sb is not null)
                {
                    sb.nJumpNumber = 1;
                }

                if (Pawn.Base is { bWorldGeometry: false } moving)
                {
                    Pawn.Velocity.Z = Pawn.JumpZ + moving.Velocity.Z;
                }

                setPhysics(Pawn, EPhysics.PHYS_Falling);
                return;
            default:
                return;
        }
    }

    // ---- events -------------------------------------------------------------------------------------------

    private static IPhysicsEvents? EventsOf(Actor actor) =>
        actor is Pawn p ? p.Controller as IPhysicsEvents ?? p as IPhysicsEvents : actor as IPhysicsEvents;

    /// <summary>True on the side that controls the pawn: its Controller's RemoteRole is not AutonomousProxy.</summary>
    private static bool IsControllingSide(Controller? controller) =>
        controller is not null && controller.RemoteRole != ENetRole.ROLE_AutonomousProxy;

    private static void NotifyBump(Actor actor, Actor other)
    {
        if (actor.bDeleteMe || other.bDeleteMe)
        {
            return;
        }

        if (EventsOf(actor) is { } events && events.eventNotifyBump(other))
        {
            return;
        }

        actor.eventBump(other);
    }

    private void CheckKillZ(Actor actor)
    {
        bool below = actor.Location.Z < Collision.KillZ;
        if (actor is Pawn pawn)
        {
            PawnPhysicsState st = GetState(pawn);
            if (below && !st.bBelowKillZ)
            {
                st.bBelowKillZ = true;
                if (pawn.Level.fDeathPawnHeight == 0f)
                {
                    EventsOf(pawn)?.eventFellOutOfWorld();
                }
            }
            else if (!below)
            {
                st.bBelowKillZ = false;
            }
        }
        else if (below && actor.Physics is EPhysics.PHYS_Falling or EPhysics.PHYS_Projectile)
        {
            actor.Destroy();
        }
    }

    /// <summary>A scratch actor list; nested moves (pushes, riders, touch events) each get their own.</summary>
    private List<Actor> RentList()
    {
        if (_listDepth == _listPool.Count)
        {
            _listPool.Add(new List<Actor>(32));
        }

        List<Actor> list = _listPool[_listDepth++];
        list.Clear();
        return list;
    }

    private void ReturnList(List<Actor> list)
    {
        list.Clear();
        _listDepth--;
    }

    private static bool IsBasedOn(Actor actor, Actor other)
    {
        for (Actor? b = actor.Base; b is not null; b = b.Base)
        {
            if (ReferenceEquals(b, other))
            {
                return true;
            }

            if (ReferenceEquals(b, actor))
            {
                return false;
            }
        }

        return false;
    }

    // ---- small maths --------------------------------------------------------------------------------------

    /// <summary>Moves a 16-bit yaw toward <paramref name="target"/> by at most <paramref name="rate"/> the short way, snapping when closer.</summary>
    [UcNative("FixedTurn", Editor = 0x10fdeef0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/physics-rootmotion-repositioning.md FixedTurn")]
    public static int FixedTurn(int current, int target, int rate)
    {
        int cur = current & 0xFFFF;
        int dest = target & 0xFFFF;
        if (rate <= 0 || cur == dest)
        {
            return rate <= 0 ? cur : dest;
        }

        int delta = (dest - cur) & 0xFFFF;
        if (delta > 32768)
        {
            delta -= 65536;
        }

        if (delta > rate)
        {
            return (cur + rate) & 0xFFFF;
        }

        if (delta < -rate)
        {
            return (cur - rate) & 0xFFFF;
        }

        return dest;
    }

    /// <summary>A 16-bit yaw compare (the natives compare yaws as 16-bit values).</summary>
    private static bool SameYaw(int a, int b) => ((a ^ b) & 0xFFFF) == 0;

    /// <summary>Unit horizontal direction of a rotator's forward axis.</summary>
    private static FVector YawForward(FRotator r) => new FRotator(0, r.Yaw, 0).Vector();

    /// <summary>Projection of <paramref name="p"/> on the infinite line through a and b (a when they coincide).</summary>
    private static FVector ProjectOnLine(FVector p, FVector a, FVector b)
    {
        FVector ab = b - a;
        float len2 = ab.SizeSquared();
        return len2 <= 0f ? a : a + ab * (((p - a) | ab) / len2);
    }

    /// <summary>
    /// The traversal hold move: toward <paramref name="target"/>, whole when within 500 dt (or 0.1), else 500 dt
    /// along the direction. <paramref name="arrived"/> is true when the move was whole.
    /// </summary>
    private static FVector Snap(FVector delta, float dt, out bool arrived)
    {
        float len = delta.Size();
        float step = K.Traversal.SnapSpeed * dt;
        if (len <= step || len <= K.Traversal.SnapTolerance)
        {
            arrived = true;
            return delta;
        }

        arrived = false;
        return delta.SafeNormal() * step;
    }
}
