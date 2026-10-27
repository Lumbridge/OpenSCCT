using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Physics;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Traversal;

namespace OpenScct.Game.Movement;

/// <summary>Which side the rig's pawn plays.</summary>
public enum PawnTeam
{
    /// <summary>SPawnAttaque_1Mesh.</summary>
    Spy,

    /// <summary>SPawnDefense_1Mesh.</summary>
    Merc,
}

/// <summary>Options of a <see cref="PawnRig"/>.</summary>
public sealed record PawnRigOptions
{
    /// <summary>The match seed of the world.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>The level's net mode (a standalone game by default).</summary>
    public ENetMode NetMode { get; init; } = ENetMode.NM_Standalone;

    /// <summary>The pawns' animation sequences; null uses <see cref="CharacterContentDir"/> when set, else the built-in table.</summary>
    public IPawnAnimationSource? Animations { get; init; }

    /// <summary>The converted characters folder (<c>.content/shared/chars</c>) for <see cref="PawnAnimationSource.FromContent"/>.</summary>
    public string? CharacterContentDir { get; init; }

    /// <summary>Calls into other areas (camera, weapons, combat); null uses the local stand-in.</summary>
    public IMovementHooks? Hooks { get; init; }
}

/// <summary>
/// One frame of player input: the controller input bytes the input layer writes, a jump press and the one-frame
/// writes of the scenario format (tests/scenarios/README.md).
/// </summary>
public readonly record struct PlayerInputFrame
{
    public byte BT_Up { get; init; }

    public byte BT_Down { get; init; }

    public byte BT_Left { get; init; }

    public byte BT_Right { get; init; }

    public byte bCrouch { get; init; }

    public byte bRoll { get; init; }

    public byte bUsing { get; init; }

    public byte bFireMode { get; init; }

    public byte bUseBackToWall { get; init; }

    /// <summary>The Jump exec before this frame (bPressedJump).</summary>
    public bool Jump { get; init; }

    /// <summary>The co-op key (exec PerformCoopMvt) before this frame, after the jump.</summary>
    public bool CoopMove { get; init; }

    /// <summary>A view rotation written to the controller before this frame.</summary>
    public FRotator? View { get; init; }

    /// <summary>A bFastSpeed write before this frame.</summary>
    public bool? FastSpeed { get; init; }

    /// <summary>Mouse turn input this frame (aTurn).</summary>
    public float MouseX { get; init; }

    /// <summary>Mouse look input this frame (aLookUp).</summary>
    public float MouseY { get; init; }

    /// <summary>
    /// A frame from the scenario names: <paramref name="buttons"/> maps BT_Up, BT_Down, BT_Left, BT_Right, bCrouch,
    /// bRoll, bUsing, bFireMode and bUseBackToWall to their byte values (others are ignored).
    /// </summary>
    public static PlayerInputFrame FromButtons(IReadOnlyDictionary<string, int>? buttons, bool jump = false,
        (int Pitch, int Yaw)? view = null, bool? fastSpeed = null)
    {
        byte B(string name) => buttons is not null && buttons.TryGetValue(name, out int v) ? (byte)v : (byte)0;
        return new PlayerInputFrame
        {
            BT_Up = B("BT_Up"),
            BT_Down = B("BT_Down"),
            BT_Left = B("BT_Left"),
            BT_Right = B("BT_Right"),
            bCrouch = B("bCrouch"),
            bRoll = B("bRoll"),
            bUsing = B("bUsing"),
            bFireMode = B("bFireMode"),
            bUseBackToWall = B("bUseBackToWall"),
            Jump = jump,
            View = view is { } v ? new FRotator(v.Pitch, v.Yaw, 0) : null,
            FastSpeed = fastSpeed,
        };
    }
}

/// <summary>
/// The state after a frame, named by the svm.trace/1 frame keys (tests/scenarios/README.md).
/// </summary>
public readonly record struct PawnSample(
    int Frame,
    float T,
    float Dt,
    FVector Loc,
    FVector Vel,
    FVector Acc,
    FRotator Rot,
    FRotator CRot,
    EPhysics Phys,
    Name PawnState,
    Name ControllerState,
    bool Crouch,
    bool Roll,
    int Hp,
    float Gs,
    float Ch,
    float ZMax,
    int Jn,
    float Fwd,
    float Str,
    float Npm,
    int IMove,
    bool Fast,
    bool Cwr,
    bool Pj,
    Name Anim0,
    float Anim0Frame);

/// <summary>
/// A spy or merc pawn with its SPlayerController in a level with physics attached, stepped one frame at a time
/// from per-frame input (the differential tests' sim runner builds on it). See src/OpenScct.Game/Movement/README.md.
/// </summary>
public sealed class PawnRig
{
    /// <summary>The scenario format's waitStill: 10 still walking frames.</summary>
    private const int StillFrames = 10;

    private const int StanceFramesMax = 120;

    private PawnRig(World world, PhysicsDriver driver, PawnTeam team, PawnRigOptions options)
    {
        World = world;
        Driver = driver;
        Team = team;
        Options = options;
        Level level = world.Level;
        level.PhysicsDriver = driver;
        DeviceNatives.Install(level, driver);
        IPawnAnimationSource animations = options.Animations ??
            (options.CharacterContentDir is { } dir ? PawnAnimationSource.FromContent(dir) : PawnAnimationSource.Default);
        level.SetService(new MovementService(animations, options.Hooks));
        MovementNotifies.Install(level);
        level.SetService<IGameFlowPorts>(new MovementGameFlowPorts(level.GetService<IGameFlowPorts>()));

        SBasePawn pawn = team == PawnTeam.Spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh();
        Pawn = level.SpawnActor(pawn, default, default)
            ?? throw new InvalidOperationException("The rig's pawn could not be spawned.");
        var pc = new SPlayerController();
        Controller = level.SpawnActor(pc, default, default)
            ?? throw new InvalidOperationException("The rig's controller could not be spawned.");
        Controller.Player = new SimViewport();
        Controller.Role = ENetRole.ROLE_Authority;
        Controller.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        level.Info.LocalPlayerController = Controller;

        // A fresh level's clock starts at 0, where the default fTimeWhenHit (0) would read as a hit in the last
        // AdrenalineTime seconds; a player in a running match is long past that.
        Controller.fTimeWhenHit = MovementK.AdrenalineReset;

        // The side as the login sets it (SGameInfo.Login: team 0 spies, team 1 mercs); the ladder and fence states read
        // the controller's copy.
        Controller.SPawnType = team == PawnTeam.Spy ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE;
        Controller.Possess(Pawn);
        Collision?.NotifyCollisionChanged();
    }

    private readonly List<RigMate> _mates = [];

    /// <summary>The world (one level).</summary>
    public World World { get; }

    public Level Level => World.Level;

    /// <summary>The level's physics driver.</summary>
    public PhysicsDriver Driver { get; }

    /// <summary>The collision world.</summary>
    public CollisionWorld? Collision => Driver.Collision;

    /// <summary>The physics of the map, when the rig was made from one.</summary>
    public PhysicsWorld? Physics { get; private init; }

    /// <summary>The map's load result, when the rig was made from one.</summary>
    public MapLoadResult? Map { get; private init; }

    public PawnTeam Team { get; }

    public PawnRigOptions Options { get; }

    public SBasePawn Pawn { get; }

    public SPlayerController Controller { get; }

    /// <summary>Frames stepped since creation.</summary>
    public int Frame { get; private set; }

    /// <summary>The other players added with <see cref="AddMate"/>.</summary>
    public IReadOnlyList<RigMate> Mates => _mates;

    // ---- creation -------------------------------------------------------------------------------------------

    /// <summary>A standalone level over synthetic collision (and optional GEs), the pawn spawned at the origin.</summary>
    public static PawnRig Create(PawnTeam team, CollisionData collision, TraversalDoc? traversal = null, PawnRigOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(collision);
        options ??= new PawnRigOptions();
        var world = new World(options.Seed, options.NetMode);
        CollisionWorld cw = CollisionWorld.Build(collision);
        cw.BindLevel(world.Level);
        var ges = traversal is null ? new GEIndex([]) : new GEIndex(traversal);
        return new PawnRig(world, new PhysicsDriver(cw, ges), team, options);
    }

    /// <summary>A level loaded from a converted map, with its actors and the device ports.</summary>
    public static PawnRig Create(PawnTeam team, SvMap map, PawnRigOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        options ??= new PawnRigOptions();
        var world = new World(options.Seed, options.NetMode);
        ActorClassRegistry registry = ActorClassRegistry.CreateDefault();
        DeviceClasses.Register(registry);
        MapLoadResult loaded = new MapLoader(registry).Load(world, map);
        PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);
        return new PawnRig(world, new PhysicsDriver(physics.Collision, physics.GEs), team, options)
        {
            Physics = physics,
            Map = loaded,
        };
    }

    /// <summary>Loads <c>.content/svmap/&lt;Map&gt;</c> (the folder with manifest.json) and calls <see cref="Create(PawnTeam, SvMap, PawnRigOptions)"/>.</summary>
    public static PawnRig ForMap(PawnTeam team, string svmapDir, PawnRigOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(svmapDir);
        if (options is null || (options.Animations is null && options.CharacterContentDir is null))
        {
            string chars = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(svmapDir).TrimEnd('\\', '/')) ?? ".", "..", "shared", "chars");
            if (Directory.Exists(chars))
            {
                options = (options ?? new PawnRigOptions()) with { CharacterContentDir = chars };
            }
        }

        return Create(team, SvMap.Load(svmapDir), options);
    }

    /// <summary>
    /// A second player in the level (a co-op mate or a target), placed standing or crouched on <paramref name="floor"/>
    /// facing <paramref name="yaw"/> in its resting state, stepped with the rig from its own <see cref="RigMate.Input"/>.
    /// </summary>
    public RigMate AddMate(PawnTeam team, FVector floor, int yaw, bool crouched = false)
    {
        SBasePawn pawn = team == PawnTeam.Spy ? new SPawnAttaque_1Mesh() : new SPawnDefense_1Mesh();
        var at = new FVector(floor.X, floor.Y, floor.Z + pawn.CollisionHeight + 2f);
        pawn = Level.SpawnActor(pawn, at, new FRotator(0, yaw, 0))
            ?? throw new InvalidOperationException("The mate's pawn could not be spawned.");
        SPlayerController pc = Level.SpawnActor(new SPlayerController(), at, default)
            ?? throw new InvalidOperationException("The mate's controller could not be spawned.");
        pc.Player = new SimViewport();
        pc.Role = ENetRole.ROLE_Authority;
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pc.fTimeWhenHit = MovementK.AdrenalineReset;
        pc.SPawnType = team == PawnTeam.Spy ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE;
        pc.Possess(pawn);
        Collision?.NotifyCollisionChanged();
        float h = crouched ? pawn.CrouchHeight : ((ISBasePawnPhysics)pawn).DefaultCollisionHeight;
        PlacePlayer(pawn, pc, new FVector(floor.X, floor.Y, floor.Z + h + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched);
        var mate = new RigMate(pawn, pc);
        _mates.Add(mate);
        return mate;
    }

    // ---- setup (the scenario steps) -------------------------------------------------------------------------

    /// <summary>
    /// The scenario <c>teleport</c>: the pawn to <paramref name="floor"/> with Z raised by its CollisionHeight plus
    /// <paramref name="clearance"/>, pawn and view turned to <paramref name="yaw"/>, no velocity.
    /// </summary>
    public void Teleport(FVector floor, int yaw, float clearance = 2f)
    {
        MovementNatives.FarMoveActor(Pawn, new FVector(floor.X, floor.Y, floor.Z + Pawn.CollisionHeight + clearance));
        Pawn.Rotation = new FRotator(0, yaw, 0);
        Pawn.Velocity = default;
        Pawn.Acceleration = default;
        Controller.SetViewRotation(new FRotator(0, yaw, 0));
        Controller.Location = Pawn.Location;
    }

    /// <summary>The scenario <c>stance</c>: presses crouch (a toggle) until bIsCrouched is <paramref name="crouched"/>.</summary>
    public bool SetCrouched(bool crouched)
    {
        for (int i = 0; i < StanceFramesMax && Pawn.bIsCrouched != crouched; i += 2)
        {
            Step(new PlayerInputFrame { bCrouch = 1 });
            Step(default);
        }

        for (int i = 0; i < StanceFramesMax && !IsResting(); i++)
        {
            Step(default);
        }

        return Pawn.bIsCrouched == crouched;
    }

    /// <summary>The scenario <c>fastSpeed</c>.</summary>
    public void SetFastSpeed(bool fast) => Controller.bFastSpeed = fast;

    /// <summary>The scenario <c>view</c>.</summary>
    public void SetView(int pitch, int yaw) => Controller.SetViewRotation(new FRotator(pitch, yaw, 0));

    /// <summary>The scenario <c>write</c> of the pawn's Health.</summary>
    public void SetHealth(int health) => Pawn.Health = health;

    /// <summary>
    /// The scenario <c>waitStill</c>: steps until the pawn walks, still, in a resting state (not a landing) for 10
    /// frames.
    /// </summary>
    public bool WaitStill(int maxFrames)
    {
        int still = 0;
        for (int i = 0; i < maxFrames; i++)
        {
            Step(default);
            still = Pawn.Physics == EPhysics.PHYS_Walking && Pawn.Velocity.IsZero() && IsResting() ? still + 1 : 0;
            if (still >= StillFrames)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A settled start in one call (the frame -1 of a reference run): location, pawn and view rotation, the stance
    /// through the crouch actions, and the resting state for the stance with its action forced. No frame runs.
    /// </summary>
    public void Place(FVector location, FRotator rotation, FRotator controllerRotation, bool crouched) =>
        PlacePlayer(Pawn, Controller, location, rotation, controllerRotation, crouched);

    private static void PlacePlayer(SBasePawn pawn, SPlayerController controller, FVector location, FRotator rotation,
        FRotator controllerRotation, bool crouched)
    {
        bool merc = pawn is SPAWNDEFENSE;
        if (crouched != pawn.bIsCrouched)
        {
            // The rig's own pawn spawns at the origin, inside the floor, so its first state crouches it and a stand-up
            // there finds no room. Stand up at the destination instead: the crouched cylinder on the floor under the
            // standing one the caller gives, which the stand-up raises into place.
            if (!crouched)
            {
                float standing = ((ISBasePawnPhysics)pawn).DefaultCollisionHeight;
                MovementNatives.FarMoveActor(pawn, location - new FVector(0f, 0f, standing - pawn.CollisionHeight));
            }

            pawn.ChangeEnumAction(crouched ? SBasePawn.SA_WANTS_TO_CROUCH : SBasePawn.SA_WANTS_TO_UNCROUCH, true);

            // The start is a recorded (or chosen) standing spot; the stand-up's box room test can refuse it next to a
            // wall the cylinder clears (the zip-line-29 platform). Stand up without the test then.
            if (!crouched && pawn.bIsCrouched)
            {
                float adjust = ((ISBasePawnPhysics)pawn).DefaultCollisionHeight - pawn.CollisionHeight;
                pawn.UnCrouchNoCheck();
                pawn.eventEndCrouch(adjust);
            }
        }

        MovementNatives.FarMoveActor(pawn, location);
        pawn.Rotation = rotation;
        pawn.Velocity = default;
        pawn.Acceleration = default;
        controller.Rotation = controllerRotation;
        controller.Location = location;
        controller.LocalMyDesiredRotation = controllerRotation;
        controller.MyDesiredRotation = controllerRotation;
        controller.bCrouchWithRoll = crouched;
        int action = merc || pawn.WeaponLoaded > 0
            ? (crouched ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_ST_2H)
            : (crouched ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_ST_0H);
        Name state = merc || pawn.WeaponLoaded > 0
            ? (crouched ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingSt2H)
            : (crouched ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingSt0H);
        pawn.ChangeEnumAction(action, true);
        controller.GotoState(state);
        controller.fZMaxFalling = pawn.Location.Z;
    }

    // ---- frames ---------------------------------------------------------------------------------------------

    /// <summary>Applies the input, then runs one fixed 60 Hz tick.</summary>
    public void Step(in PlayerInputFrame input)
    {
        Apply(input);
        ApplyMates();
        Level.Info.m_dT = Sim.DeltaTime * Level.Info.TimeDilation;
        World.Step();
        Frame++;
    }

    /// <summary>Applies the input, then runs one tick of <paramref name="dt"/> seconds (time dilation carries the step).</summary>
    public void Step(in PlayerInputFrame input, float dt)
    {
        if (!(dt > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), "A step must be longer than zero.");
        }

        Apply(input);
        ApplyMates();
        float dilation = Level.Info.TimeDilation;
        Level.Info.TimeDilation = dt / Sim.DeltaTime;
        Level.Info.m_dT = dt;
        try
        {
            World.Step();
        }
        finally
        {
            Level.Info.TimeDilation = dilation;
        }

        Frame++;
    }

    /// <summary>The state after the last frame, with the svm.trace/1 field names.</summary>
    public PawnSample Sample()
    {
        SBasePawn p = Pawn;
        SPlayerController c = Controller;
        PawnAnimChannel ch0 = p.PawnAnim[0];
        return new PawnSample(
            Frame - 1,
            Level.TimeSeconds,
            Level.DeltaTime,
            p.Location,
            p.Velocity,
            p.Acceleration,
            p.Rotation,
            c.Rotation,
            p.Physics,
            p.GetStateName(),
            c.GetStateName(),
            p.bIsCrouched,
            p.bIsRolling,
            p.Health,
            p.GroundSpeed,
            p.CollisionHeight,
            c.fZMaxFalling,
            p.nJumpNumber,
            c.aForward,
            c.aStrafe,
            c.fNormePadMove,
            c.iMove,
            c.bFastSpeed,
            c.bCrouchWithRoll,
            c.bPressedJump,
            ch0.Sequence,
            ch0.Frame);
    }

    private void Apply(in PlayerInputFrame input) => Apply(Controller, input);

    /// <summary>Each mate's held input; a jump press counts once.</summary>
    private void ApplyMates()
    {
        foreach (RigMate mate in _mates)
        {
            Apply(mate.Controller, mate.Input);
            mate.Input = mate.Input with { Jump = false, CoopMove = false };
        }
    }

    private static void Apply(SPlayerController c, in PlayerInputFrame input)
    {
        c.BT_Up = input.BT_Up;
        c.BT_Down = input.BT_Down;
        c.BT_Left = input.BT_Left;
        c.BT_Right = input.BT_Right;
        c.bCrouch = input.bCrouch;
        c.bRoll = input.bRoll;
        c.bUsing = input.bUsing;
        c.bFireMode = input.bFireMode;
        c.bUseBackToWall = input.bUseBackToWall;
        c.aTurn = input.MouseX;
        c.aLookUp = input.MouseY;
        if (input.View is { } view)
        {
            // A merc's view is his aim (UpdateRotationAttaquant_Mouse sets the view to it every frame).
            c.SetViewRotation(view);
        }

        if (input.FastSpeed is { } fast)
        {
            c.bFastSpeed = fast;
        }

        if (input.Jump)
        {
            c.eventJump();
        }

        if (input.CoopMove)
        {
            c.eventPerformCoopMvt();
        }
    }

    private bool IsResting() =>
        Controller.IsInState(MovementStateNames.s_PlayerWalkingSt0H) || Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H) ||
        Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H) || Controller.IsInState(MovementStateNames.s_PlayerWalkingCr2H);
}

/// <summary>Another player in a <see cref="PawnRig"/>'s level (<see cref="PawnRig.AddMate"/>).</summary>
public sealed class RigMate
{
    internal RigMate(SBasePawn pawn, SPlayerController controller)
    {
        Pawn = pawn;
        Controller = controller;
    }

    public SBasePawn Pawn { get; }

    public SPlayerController Controller { get; }

    /// <summary>The input each following frame applies (held until changed; a jump or co-op key press counts once).</summary>
    public PlayerInputFrame Input { get; set; }
}
