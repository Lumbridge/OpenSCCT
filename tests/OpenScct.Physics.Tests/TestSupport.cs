using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Traversal;

namespace OpenScct.Physics.Tests;

/// <summary>Repository locations.</summary>
internal static class TestPaths
{
    private static readonly Lazy<string> s_root = new(FindRoot);

    public static string RepoRoot => s_root.Value;

    public static string TestBox => Path.Combine(RepoRoot, "fixtures", "testbox");

    public static string SteeE => Path.Combine(RepoRoot, ".content", "svmap", "SteeE");

    private static string FindRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("The repository root (OpenSCCT.slnx) was not found above the test binaries.");
    }
}

/// <summary>
/// A spy-sized pawn (radius 35, half height 87.5) with the SBasePawn movement values, recording its events. Its Tick
/// sets GroundSpeed from <see cref="WalkSpeed"/> every frame, as the movement script does.
/// </summary>
public class TestPawn : Pawn
{
    public TestPawn()
    {
        CollisionRadius = 35f;
        CollisionHeight = 87.5f;
        CrouchHeight = 50f;
        CrouchRadius = 35f;
        bCollideActors = true;
        bBlockActors = true;
        bBlockPlayers = true;
        bCollideWorld = true;
        GroundSpeed = 423.5f;
        AccelRate = 2048f;
        JumpZ = 610f;
        AirControl = 2f;
        AirSpeed = 600f;
        MaxFallSpeed = 1200f;
        bCanWalkOffLedges = true;
    }

    /// <summary>The GroundSpeed the "script" sets every frame (0 leaves GroundSpeed alone).</summary>
    public float WalkSpeed = 423.5f;

    public int Bumps;
    public int HitWalls;
    public int Landeds;
    public int Fallings;
    public int Touches;
    public int UnTouches;
    public int Encroached;
    public Actor? LastBump;
    public FVector LastHitWallNormal;
    public FVector LastLandedNormal;

    protected override void Tick(float DeltaTime)
    {
        if (WalkSpeed > 0f)
        {
            GroundSpeed = WalkSpeed;
        }
    }

    protected override void Bump(Actor Other)
    {
        Bumps++;
        LastBump = Other;
    }

    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        HitWalls++;
        LastHitWallNormal = HitNormal;
    }

    protected override void Landed(FVector HitNormal)
    {
        Landeds++;
        LastLandedNormal = HitNormal;
    }

    protected override void Falling() => Fallings++;

    protected override void Touch(Actor Other) => Touches++;

    protected override void UnTouch(Actor Other) => UnTouches++;

    public override void EncroachedBy(Actor? Other) => Encroached++;
}

/// <summary>A root-motion mesh that hands out a fixed root delta per call.</summary>
public sealed class TestRootMotionMesh : IRootMotionMesh
{
    public int RootLockType { get; private set; }

    public FVector DeltaPerCall;

    public FRotator RotationPerCall;

    public int Calls;

    public void LockRootMotion(int LockType) => RootLockType = LockType;

    public void AdvanceRootMotion(out FVector DeltaLocation, out FRotator DeltaRotation)
    {
        Calls++;
        DeltaLocation = DeltaPerCall;
        DeltaRotation = RotationPerCall;
    }
}

/// <summary>A pawn with the SBasePawn fields and defaults (spy: SPawnAttaque ZCDecal), as the SBasePawn port exposes them.</summary>
public sealed class TestSBasePawn : TestPawn, ISBasePawnPhysics
{
    public FVector ZipP1;
    public FVector ZipP2;
    public FVector NewZipP1;
    public FVector NewZipP2;
    public FVector m_geoNormal;
    public FVector vBackFeetNormal;
    public FVector m_CurrentArmsZone = new(0f, 0f, 44.45f);
    public float m_CurrentArmsRadius = 139.7f;
    public FVector m_CurrentFeetZone = new(39f, 2f, -85f);
    public float m_CurrentFeetRadius = 60f;
    public FVector m_LedgeGrabArmsZone = new(39f, 0f, 117f);
    public float m_LedgeGrabArmsRadius = 40f;
    public FVector m_PipeArmsZone = new(37f, 0f, 90.3f);
    public FVector m_HandOverHandArmsZone = new(0f, 0f, 128f);
    public FVector m_NarrowLadderArmsZone = new(37.5f, 0f, 100.5f);
    public FVector m_ZipLineArmsZone = new(0f, 0f, 116f);
    public float m_ZipLineArmsRadius = 60f;
    public FVector m_FenceArmsZone = new(45.72f, 0f, 90f);
    public FVector m_NormalFeetZone = new(39f, 2f, -85f);
    public readonly float[] ZCDecal = new float[16];
    public byte ZoneConfig;
    public byte ZoneConfigForClient;
    public float ZipHeight = 45f;
    public bool bIsZipSpeed;
    public float Pipe4Height = 27.5f;
    public float Pipe4DecalCol;
    public bool bIsPipe4Decal;
    public byte TypeLedge;
    public bool bIsLeftPart;
    public FVector vDeltaMove;
    public FVector vVandammePos;
    public FVector vWallJumpDir;
    public FVector vSpeedAfterRM;
    public FVector vMoveAfterRM;
    public int rRotAfterRM;
    public bool bReverseRM;
    public int iLockRootMotionType;
    public float fLastRootMotionTimeCalled;
    public EPhysics RootMotionAdditionnalPhysics;
    public float fPhysReposLinearSpeed = 87f;
    public bool bUseAdditionnalPhysicsForRepos;
    public bool bNoReposAtEnd;
    public bool bKeepLocalVandammePos;
    public bool bActionsSendByServerMove;
    public TestRootMotionMesh? RootMesh;
    public Pawn? AttackTarget;
    public PawnType Team = PawnType.SPAWNOFFENSE;
    public bool StickySkinned;
    public float Fell_MinHeightForDmg = 600f;
    public float Fell_MaxHeightForDmg = 1500f;
    public int nJumpNumber;
    public int sAction;
    public bool UnCrouchSucceeds = true;
    public int UnCrouchCalls;

    public TestSBasePawn()
    {
        Health = 100;
        // SPawnAttaque ZCDecal (spec/movement/native-contracts.md, collision per stance).
        ZCDecal[1] = 37.5f;
        ZCDecal[2] = -42.5f;
        ZCDecal[3] = -60f;
        ZCDecal[4] = -37.5f;
        ZCDecal[8] = 42.5f;
        ZCDecal[10] = 42.5f;
        ZCDecal[12] = 60f;
        ZCDecal[14] = 60f;
    }

    ref FVector ISBasePawnPhysics.ZipP1 => ref ZipP1;

    ref FVector ISBasePawnPhysics.ZipP2 => ref ZipP2;

    ref FVector ISBasePawnPhysics.NewZipP1 => ref NewZipP1;

    ref FVector ISBasePawnPhysics.NewZipP2 => ref NewZipP2;

    ref FVector ISBasePawnPhysics.m_geoNormal => ref m_geoNormal;

    ref FVector ISBasePawnPhysics.vBackFeetNormal => ref vBackFeetNormal;

    ref FVector ISBasePawnPhysics.m_CurrentArmsZone => ref m_CurrentArmsZone;

    ref float ISBasePawnPhysics.m_CurrentArmsRadius => ref m_CurrentArmsRadius;

    ref FVector ISBasePawnPhysics.m_CurrentFeetZone => ref m_CurrentFeetZone;

    ref float ISBasePawnPhysics.m_CurrentFeetRadius => ref m_CurrentFeetRadius;

    ref FVector ISBasePawnPhysics.m_LedgeGrabArmsZone => ref m_LedgeGrabArmsZone;

    ref float ISBasePawnPhysics.m_LedgeGrabArmsRadius => ref m_LedgeGrabArmsRadius;

    ref FVector ISBasePawnPhysics.m_PipeArmsZone => ref m_PipeArmsZone;

    ref FVector ISBasePawnPhysics.m_HandOverHandArmsZone => ref m_HandOverHandArmsZone;

    ref FVector ISBasePawnPhysics.m_NarrowLadderArmsZone => ref m_NarrowLadderArmsZone;

    ref FVector ISBasePawnPhysics.m_ZipLineArmsZone => ref m_ZipLineArmsZone;

    ref float ISBasePawnPhysics.m_ZipLineArmsRadius => ref m_ZipLineArmsRadius;

    ref FVector ISBasePawnPhysics.m_FenceArmsZone => ref m_FenceArmsZone;

    ref FVector ISBasePawnPhysics.m_NormalFeetZone => ref m_NormalFeetZone;

    float[] ISBasePawnPhysics.ZCDecal => ZCDecal;

    ref byte ISBasePawnPhysics.ZoneConfig => ref ZoneConfig;

    ref byte ISBasePawnPhysics.ZoneConfigForClient => ref ZoneConfigForClient;

    ref float ISBasePawnPhysics.ZipHeight => ref ZipHeight;

    ref bool ISBasePawnPhysics.bIsZipSpeed => ref bIsZipSpeed;

    ref float ISBasePawnPhysics.Pipe4Height => ref Pipe4Height;

    ref float ISBasePawnPhysics.Pipe4DecalCol => ref Pipe4DecalCol;

    ref bool ISBasePawnPhysics.bIsPipe4Decal => ref bIsPipe4Decal;

    byte ISBasePawnPhysics.TypeLedge
    {
        get => TypeLedge;
        set => TypeLedge = value;
    }

    ref bool ISBasePawnPhysics.bIsLeftPart => ref bIsLeftPart;

    ref FVector ISBasePawnPhysics.vDeltaMove => ref vDeltaMove;

    ref FVector ISBasePawnPhysics.vVandammePos => ref vVandammePos;

    ref FVector ISBasePawnPhysics.vWallJumpDir => ref vWallJumpDir;

    ref FVector ISBasePawnPhysics.vSpeedAfterRM => ref vSpeedAfterRM;

    ref FVector ISBasePawnPhysics.vMoveAfterRM => ref vMoveAfterRM;

    ref int ISBasePawnPhysics.rRotAfterRM => ref rRotAfterRM;

    ref bool ISBasePawnPhysics.bReverseRM => ref bReverseRM;

    ref int ISBasePawnPhysics.iLockRootMotionType => ref iLockRootMotionType;

    ref float ISBasePawnPhysics.fLastRootMotionTimeCalled => ref fLastRootMotionTimeCalled;

    ref EPhysics ISBasePawnPhysics.RootMotionAdditionnalPhysics => ref RootMotionAdditionnalPhysics;

    ref float ISBasePawnPhysics.fPhysReposLinearSpeed => ref fPhysReposLinearSpeed;

    ref bool ISBasePawnPhysics.bUseAdditionnalPhysicsForRepos => ref bUseAdditionnalPhysicsForRepos;

    ref bool ISBasePawnPhysics.bNoReposAtEnd => ref bNoReposAtEnd;

    ref bool ISBasePawnPhysics.bKeepLocalVandammePos => ref bKeepLocalVandammePos;

    ref bool ISBasePawnPhysics.bActionsSendByServerMove => ref bActionsSendByServerMove;

    IRootMotionMesh? ISBasePawnPhysics.RootMotionMesh => RootMesh;

    Pawn? ISBasePawnPhysics.m_AttackTarget
    {
        get => AttackTarget;
        set => AttackTarget = value;
    }

    PawnType ISBasePawnPhysics.SPawnType => Team;

    bool ISBasePawnPhysics.bStickySkinned => StickySkinned;

    ref float ISBasePawnPhysics.Fell_MinHeightForDmg => ref Fell_MinHeightForDmg;

    ref float ISBasePawnPhysics.Fell_MaxHeightForDmg => ref Fell_MaxHeightForDmg;

    ref int ISBasePawnPhysics.nJumpNumber => ref nJumpNumber;

    ref int ISBasePawnPhysics.sAction => ref sAction;

    float ISBasePawnPhysics.DefaultCollisionRadius => 35f;

    float ISBasePawnPhysics.DefaultCollisionHeight => 87.5f;

    FVector ISBasePawnPhysics.DefaultPrePivot => default;

    int ISBasePawnPhysics.DefaultHealth => 100;

    bool ISBasePawnPhysics.UnCrouch(bool bClientSimulation)
    {
        UnCrouchCalls++;
        if (UnCrouchSucceeds)
        {
            bIsCrouched = false;
        }

        return UnCrouchSucceeds;
    }
}

/// <summary>
/// A controller that receives the SCCT physics events and records them. Like the movement script, MyFalling plays
/// the fall (PHYS_Falling) and MyNotifyLanded the landing (PHYS_Walking), unless <see cref="bScriptModes"/> is off.
/// </summary>
public sealed class TestController : Controller, ISPlayerControllerPhysics
{
    public bool bScriptModes = true;
    public bool NotifyHitWallResult;
    public bool CanBeDropAttacked;
    public float fZMaxFalling;
    public float fLastTimeOnAMover = -3f;
    public bool bTestBackFeetGrab;
    public float fDistanceForContextualJump = 500f;

    public int MyFallings;
    public int MyLandeds;
    public int HitWalls;
    public int Bumps;
    public int FellOut;
    public int DropAttacks;
    public int StickyBlasts;
    public int MayFalls;
    public int TopSynchros;
    public int NoGEs;
    public int Ledges;
    public int WaistLedges;
    public int AutoFeetLedges;
    public int FeetLedges;
    public int Pipes;
    public int TopPipes;
    public int TopPipeCrouches;
    public int NarrowLadders;
    public int TopLadders;
    public int HandOverHands;
    public int OrthoPipeHs;
    public int ZipLines;
    public int ChangeLedges;
    public int FenceTops;
    public int ClientStopRMs;
    public int Fallings;
    public float LastLeftTime = -1f;
    public float LastLandedTimeLeft;
    public float ZMaxAtLanding;
    public FVector LastLandedNormal;
    public Actor? LastLandedOn;
    public Pawn? LastDropAttack;
    public bool LastWaistCanStand;
    public int LastChangeLedgeType = -1;
    public FVector LastEventLocation;
    public FRotator LastEventRotation;
    public bool LastSide;

    public PhysicsDriver Driver => PhysicsDriver.Of(this)!;

    ref float ISPlayerControllerPhysics.fZMaxFalling => ref fZMaxFalling;

    ref float ISPlayerControllerPhysics.fLastTimeOnAMover => ref fLastTimeOnAMover;

    bool ISPlayerControllerPhysics.bTestBackFeetGrab => bTestBackFeetGrab;

    float ISPlayerControllerPhysics.fDistanceForContextualJump => fDistanceForContextualJump;

    public void eventMyFalling(float LeftTime)
    {
        MyFallings++;
        LastLeftTime = LeftTime;
        if (bScriptModes && Pawn is { } p)
        {
            Driver.setPhysics(p, EPhysics.PHYS_Falling);
        }
    }

    public bool eventMyNotifyLanded(FVector HitNormal, float fTimeLeft, Actor? LandedOn)
    {
        MyLandeds++;
        LastLandedNormal = HitNormal;
        LastLandedTimeLeft = fTimeLeft;
        LastLandedOn = LandedOn;
        ZMaxAtLanding = fZMaxFalling;
        if (bScriptModes && Pawn is { } p)
        {
            Driver.setPhysics(p, EPhysics.PHYS_Walking, LandedOn, HitNormal);
        }

        return true;
    }

    public bool eventNotifyHitWall(FVector HitNormal, Actor? Wall)
    {
        HitWalls++;
        return NotifyHitWallResult;
    }

    public bool eventNotifyBump(Actor Other)
    {
        Bumps++;
        return false;
    }

    public void eventFellOutOfWorld() => FellOut++;

    public void eventDropAttack(Pawn Target)
    {
        DropAttacks++;
        LastDropAttack = Target;
    }

    public bool eventCanBeDropAttacked() => CanBeDropAttacked;

    public void eventStickyBlast(Actor? Other) => StickyBlasts++;

    public void eventMayFall() => MayFalls++;

    public void eventTopSynchro() => TopSynchros++;

    public void eventNoGE() => NoGEs++;

    public void eventLedge() => Ledges++;

    public void eventWaistLedge(bool bCanGoNoCrouch)
    {
        WaistLedges++;
        LastWaistCanStand = bCanGoNoCrouch;
    }

    public void eventAutoFeetLedge() => AutoFeetLedges++;

    public void eventFeetLedge() => FeetLedges++;

    public void eventPipe(FVector locationEnd, FRotator orientationEnd)
    {
        Pipes++;
        LastEventLocation = locationEnd;
        LastEventRotation = orientationEnd;
    }

    public void eventTopPipe(bool bLeft)
    {
        TopPipes++;
        LastSide = bLeft;
    }

    public void eventTopPipeCrouch(bool bLeft)
    {
        TopPipeCrouches++;
        LastSide = bLeft;
    }

    public void eventNarrowLadder() => NarrowLadders++;

    public void eventTopLadder() => TopLadders++;

    public void eventHandOverHand() => HandOverHands++;

    public void eventOrthoPipeH(bool bToLeft)
    {
        OrthoPipeHs++;
        LastSide = bToLeft;
    }

    public void eventZipLine(FVector locationEnd, FRotator orientationEnd)
    {
        ZipLines++;
        LastEventLocation = locationEnd;
        LastEventRotation = orientationEnd;
    }

    public void eventChangeLedge(int iType, FVector P1, FVector P2)
    {
        ChangeLedges++;
        LastChangeLedgeType = iType;
    }

    public void eventFenceTop(FVector locationEnd, FRotator orientationEnd)
    {
        FenceTops++;
        LastEventLocation = locationEnd;
        LastEventRotation = orientationEnd;
    }

    public void eventClientStopRM(int TheAction) => ClientStopRMs++;

    protected override void Falling() => Fallings++;
}

/// <summary>A touch-only cylinder (a trigger).</summary>
public sealed class TestTrigger : Actor
{
    public TestTrigger()
    {
        bCollideActors = true;
        CollisionRadius = 64f;
        CollisionHeight = 64f;
        bBlockZeroExtentTraces = false;
        bBlockNonZeroExtentTraces = false;
    }

    public int Touches;
    public int UnTouches;

    protected override void Touch(Actor Other) => Touches++;

    protected override void UnTouch(Actor Other) => UnTouches++;
}

/// <summary>A two-key mover with a Mover encroach type (the generated EMoverEncroachType).</summary>
public sealed class TestMover : Actor, IMovingBrush, IEncroacher
{
    public TestMover()
    {
        bCollideActors = true;
        bBlockActors = true;
        bBlockPlayers = true;
        bIsAMover = true;
        Physics = EPhysics.PHYS_MovingBrush;
    }

    public readonly FVector[] KeyPos = new FVector[8];
    public readonly FRotator[] KeyRot = new FRotator[8];
    public EMoverEncroachType MoverEncroachType = EMoverEncroachType.ME_StopWhenEncroach;
    public readonly List<Actor> Crushed = new();
    public readonly List<Actor> Encroached = new();
    public int KeysReached;

    public float PhysAlpha { get; set; }

    public float PhysRate { get; set; } = 1f;

    public int KeyNum { get; set; }

    public FVector OldPos { get; set; }

    public FRotator OldRot { get; set; }

    public FVector BasePos { get; set; }

    public FRotator BaseRot { get; set; }

    public EMoverGlideType MoverGlideType { get; set; } = EMoverGlideType.MV_MoveByTime;

    public FVector GetKeyPos(int Index) => KeyPos[Index];

    public FRotator GetKeyRot(int Index) => KeyRot[Index];

    public void eventKeyFrameReached() => KeysReached++;

    /// <summary>Starts a leg to <paramref name="key"/> lasting <paramref name="seconds"/> (Mover.InterpolateTo, simplified).</summary>
    public void InterpolateTo(int key, float seconds)
    {
        OldPos = Location;
        OldRot = Rotation;
        PhysAlpha = 0f;
        KeyNum = key;
        PhysRate = 1f / seconds;
        bInterpolating = true;
    }

    public bool eventEncroachingOn(Actor Other)
    {
        Encroached.Add(Other);
        switch (MoverEncroachType)
        {
            case EMoverEncroachType.ME_CrushWhenEncroach:
                Crushed.Add(Other);
                return false;
            case EMoverEncroachType.ME_IgnoreWhenEncroach:
                return false;
            default:
                return true;
        }
    }
}

/// <summary>A world with a collision world and the physics driver installed.</summary>
internal sealed class Scene
{
    private Scene(World world, CollisionWorld collision, GEIndex ges)
    {
        World = world;
        Collision = collision;
        Driver = new PhysicsDriver(collision, ges);
        world.Level.PhysicsDriver = Driver;
    }

    public World World { get; }

    public Level Level => World.Level;

    public CollisionWorld Collision { get; }

    public PhysicsDriver Driver { get; }

    /// <summary>
    /// Builds the scene. <paramref name="bound"/> spawns actors that own colliders (by actor id) before the level
    /// is bound.
    /// </summary>
    public static Scene Create(CollisionData data, Func<Level, Dictionary<string, Actor>>? bound = null, ulong seed = 1,
        IReadOnlyList<GERecord>? ges = null)
    {
        var world = new World(seed);
        Dictionary<string, Actor> owners = bound?.Invoke(world.Level) ?? new Dictionary<string, Actor>();
        CollisionWorld collision = CollisionWorld.Build(data);
        collision.BindLevel(world.Level, id => owners.TryGetValue(id, out Actor? a) ? a : null);
        return new Scene(world, collision, new GEIndex(ges ?? []));
    }

    /// <summary>Spawns a pawn, by default with a <see cref="TestController"/> (none with <paramref name="noController"/>).</summary>
    public T Spawn<T>(T pawn, FVector location, EPhysics physics = EPhysics.PHYS_Falling, TestController? controller = null,
        bool noController = false, FRotator rotation = default)
        where T : Pawn
    {
        T p = Level.SpawnActor(pawn, location, rotation)!;
        p.Physics = physics;
        if (!noController)
        {
            controller ??= new TestController();
            Level.SpawnActor(controller, location, default);
            controller.Pawn = p;
            p.Controller = controller;
        }

        Collision.NotifyCollisionChanged();
        return p;
    }

    public TestPawn SpawnPawn(FVector location, EPhysics physics = EPhysics.PHYS_Falling, TestController? controller = null,
        bool noController = false) =>
        Spawn(new TestPawn(), location, physics, controller, noController);

    public TestSBasePawn SpawnSpy(FVector location, EPhysics physics = EPhysics.PHYS_Falling, TestController? controller = null,
        FRotator rotation = default) =>
        Spawn(new TestSBasePawn(), location, physics, controller, rotation: rotation);

    /// <summary>Steps until the pawn walks (or the tick budget runs out).</summary>
    public void Settle(Pawn pawn, int maxTicks = 240)
    {
        for (int i = 0; i < maxTicks && pawn.Physics != EPhysics.PHYS_Walking; i++)
        {
            World.Step();
        }

        // Two more ticks for the floor band to settle the height.
        World.Step(2);
    }

    public void Step(int ticks) => World.Step(ticks);

    public static TestController ControllerOf(Pawn p) => (TestController)p.Controller!;
}

/// <summary>Small geometry helpers.</summary>
internal static class Geo
{
    public static FVector V(float x, float y, float z) => new(x, y, z);

    /// <summary>A builder with a floor slab whose top is at z = 0.</summary>
    public static CollisionDataBuilder Floor(float half = 4000f)
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-half, -half, -64f), V(half, half, 0f));
        return b;
    }

    /// <summary>Location of a standing spy whose feet are at <paramref name="floorZ"/>.</summary>
    public static float StandZ(float floorZ = 0f) => floorZ + 87.5f;

    /// <summary>
    /// The gap a walking pawn keeps above a BSP floor: the floor band lifts it to 1.405 by the probe's reading, which
    /// is 0.1 uu short of the contact (the BSP box pull-back of the 36 uu probe).
    /// </summary>
    public const float BspRestGap = 1.405f + 0.1f;

    /// <summary>The same above a static mesh floor (the mesh pull-back of the 36 uu probe is 1 uu).</summary>
    public const float MeshRestGap = 1.405f + 1f;

    /// <summary>Location of a spy walking on a BSP floor at <paramref name="floorZ"/> (the middle of the band).</summary>
    public static float WalkZ(float floorZ = 0f) => floorZ + 87.5f + BspRestGap;

    /// <summary>
    /// A walking spy on a BSP floor at <paramref name="floorZ"/>: the floor band keeps the probe reading in
    /// [1.39, 1.42], so the gap is that plus the 0.1 uu pull-back.
    /// </summary>
    public static void AssertWalkHeight(float floorZ, float z, float pullback = 0.1f)
    {
        float low = floorZ + 87.5f + 1.39f + pullback - 1e-3f;
        float high = floorZ + 87.5f + 1.42f + pullback + 1e-3f;
        Xunit.Assert.InRange(z, low, high);
    }

    public static GERecord GE(int index, GEKind kind, params FVector[] points) =>
        new(index, "GE" + index, kind, points, 0, GEPlatform.Common, GESource.Synthetic());

    public static void Near(FVector expected, FVector actual, float tolerance = 1e-5f) =>
        Xunit.Assert.True(FVector.Dist(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
}
