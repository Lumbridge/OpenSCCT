using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Physics.Conveyors;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Svm;

/// <summary>How a conveyor looks. The editor builds the look into the map; the game moves its belt, rollers and items.</summary>
public enum EConveyorLook : byte
{
    /// <summary>A generated frame: belt, side walls and, at an open path's ends, drums.</summary>
    CL_Frame,

    /// <summary>Nothing generated: the author's own meshes are the look and the floor (RE+ "driver" belts).</summary>
    CL_Driver,

    /// <summary>A flat belt surface with low edges (travelators).</summary>
    CL_Treadmill,

    /// <summary>A stepped belt with balustrades (escalators); riders stand on its slope.</summary>
    CL_Escalator,
}

/// <summary>
/// Which sides of a conveyor have a wall (left and right facing the direction of travel). The map builder builds the
/// walls; the game only stores the choice.
/// </summary>
public enum EConveyorWalls : byte
{
    CW_Both,
    CW_Left,
    CW_Right,
    CW_None,

    /// <summary>Both sides, open where another conveyor's end meets this one's side (joins and branches).</summary>
    CW_Auto,
}

/// <summary>What an event does to a conveyor.</summary>
public enum EConveyorTrigger : byte
{
    /// <summary>Starts a stopped conveyor and stops a running one.</summary>
    CT_Toggle,
    CT_TurnOn,
    CT_TurnOff,

    /// <summary>Runs the other way.</summary>
    CT_Reverse,
}

/// <summary>A point of the path on the riding surface, relative to the actor; Radius 0 is a sharp corner.</summary>
public struct FConveyorPoint
{
    public FVector Loc;
    public float Radius;
}

/// <summary>
/// A mesh of the map whose texture scrolls with the conveyor (driver looks): the placed actor's id, the uu one texture
/// repeat covers along the direction of travel, and the texture axis that runs along it (0 U, 1 V).
/// </summary>
public struct FConveyorScroll
{
    public string Target;
    public float UnitsPerTile;
    public byte Axis;
}

/// <summary>
/// Svm.Conveyor, an OpenSCCT-native map actor (spec/additions/conveyors.md): a belt along a path of straight runs and
/// arcs that carries walking pawns and dropped objects. The walking physics does the carrying
/// (<see cref="PhysicsDriver"/>), so the server and the owner's prediction move a rider the same way. Events start,
/// stop or reverse it; the state and the phase anchor replicate so every client draws the belt, rollers and items in
/// the same place.
/// </summary>
public sealed partial class Conveyor : Actor, IConveyor
{
    /// <summary>The path on the riding surface, relative to the actor's location and rotation.</summary>
    public List<FConveyorPoint> Path = new();

    /// <summary>The path closes from its last point back to its first.</summary>
    public bool bLoop;

    public float Width = 120f;

    /// <summary>uu a second along the path; negative runs backwards.</summary>
    public float Speed = 150f;

    public bool bOn = true;

    public bool bCarryPlayers = true;

    /// <summary>Dropped disks and data drives resting on the belt move with it (server).</summary>
    public bool bCarryObjects = true;

    public EConveyorLook Look;

    public EConveyorWalls Walls = EConveyorWalls.CW_Auto;

    public EConveyorTrigger TriggerAction;

    /// <summary>Meshes drawn riding the belt, picked per item from the seed; none draws no items.</summary>
    public List<AssetRef> ItemMeshes = new();

    /// <summary>Distance between items along the path.</summary>
    public float ItemSpacing = 160f;

    public float ItemScale = 1f;

    /// <summary>Items grow in over this distance from the start and shrink away over it before the end.</summary>
    public float ItemFade = 48f;

    public int ItemSeed;

    /// <summary>Largest random turn of an item about its vertical axis, in rotator units.</summary>
    public int ItemYawJitter = 65536;

    /// <summary>The map's own meshes whose texture moves with this conveyor (driver looks).</summary>
    public List<FConveyorScroll> ScrollTargets = new();

    /// <summary>Material of the belt surface the editor builds (frame, treadmill and escalator looks).</summary>
    public AssetRef BeltMaterial;

    /// <summary>Material of the frame and walls the editor builds (the belt, steps and drums use BeltMaterial).</summary>
    public AssetRef FrameMaterial;

    /// <summary>Distance travelled at <see cref="ChangeTick"/> (replicated): the belt's phase is continuous across changes.</summary>
    public float PhaseAtChange;

    /// <summary>Server tick of the last start, stop or reversal (replicated).</summary>
    public int ChangeTick;

    /// <summary>
    /// Surface speed before <see cref="ChangeTick"/> (replicated): clients draw a little in the past, so the phase
    /// before the change must stay what it was.
    /// </summary>
    public float SpeedBeforeChange;

    private ConveyorPath? _worldPath;

    protected override void SetDefaults()
    {
        base.SetDefaults();
        bHidden = true;
        DrawType = EDrawType.DT_None;
        bNoDelete = true;
        bAlwaysRelevant = true;
        bReplicateMovement = false;
        RemoteRole = ENetRole.ROLE_SimulatedProxy;
        bCollideActors = false;
        bBlockActors = false;
        bBlockPlayers = false;
        bBlockZeroExtentTraces = false;
        bBlockNonZeroExtentTraces = false;
        bBlockCamera = false;
    }

    /// <summary>The path in world space (built at begin play; the actor never moves).</summary>
    public ConveyorPath WorldPath => _worldPath ??= BuildWorldPath();

    public float HalfWidth => Width * 0.5f;

    public float SurfaceSpeed => bOn ? Speed : 0f;

    public bool Carries(ConveyorCargo cargo) => cargo == ConveyorCargo.Pawns ? bCarryPlayers : bCarryObjects;

    /// <summary>
    /// Distance the belt has travelled at server tick <paramref name="tick"/> (fractional for drawing between ticks):
    /// rollers, treads and items are placed from it. Before the last change it runs at the speed it had then, so a
    /// client drawing behind the server sees no jump when a change arrives.
    /// </summary>
    public double PhaseAt(double tick) =>
        PhaseAtChange + (tick >= ChangeTick ? SurfaceSpeed : SpeedBeforeChange) * (tick - ChangeTick) * Sim.DeltaTime;

    /// <summary>The world path of the stored points (the actor's location and rotation applied).</summary>
    public ConveyorPath BuildWorldPath()
    {
        FCoords axes = FCoords.FromRotator(Rotation);
        var points = new List<ConveyorPathPoint>(Path.Count);
        foreach (FConveyorPoint p in Path)
        {
            FVector world = Location + axes.XAxis * p.Loc.X + axes.YAxis * p.Loc.Y + axes.ZAxis * p.Loc.Z;
            points.Add(new ConveyorPathPoint(world, p.Radius));
        }

        return ConveyorPath.Build(points, bLoop);
    }

    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        _worldPath = BuildWorldPath();
        ConveyorField.Install(XLevel).Add(this);
    }

    /// <summary>Starts, stops or reverses the conveyor as <see cref="TriggerAction"/> says (server).</summary>
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: spec/additions/conveyors.md#events")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        switch (TriggerAction)
        {
            case EConveyorTrigger.CT_Toggle:
                SetRunning(!bOn, Speed);
                break;
            case EConveyorTrigger.CT_TurnOn:
                SetRunning(true, Speed);
                break;
            case EConveyorTrigger.CT_TurnOff:
                SetRunning(false, Speed);
                break;
            case EConveyorTrigger.CT_Reverse:
                SetRunning(bOn, -Speed);
                break;
        }
    }

    /// <summary>Changes the running state, keeping the drawn phase continuous (server).</summary>
    public void SetRunning(bool on, float speed)
    {
        if (on == bOn && speed == Speed)
        {
            return;
        }

        PhaseAtChange = (float)PhaseAt(XLevel.TickCount);
        ChangeTick = (int)XLevel.TickCount;
        SpeedBeforeChange = SurfaceSpeed;
        bOn = on;
        Speed = speed;
    }

    /// <summary>
    /// The server moves dropped objects resting on running conveyors; their positions replicate as usual. The conveyor
    /// that began play first does it for all of them, so the level's actors are walked once a tick.
    /// </summary>
    protected override void Tick(float DeltaTime)
    {
        base.Tick(DeltaTime);
        if (Level.NetMode == ENetMode.NM_Client || XLevel.PhysicsDriver is not PhysicsDriver driver ||
            XLevel.GetService<ConveyorField>() is not { } field || !ReferenceEquals(field.Conveyors[0], this))
        {
            return;
        }

        IReadOnlyList<Actor> actors = XLevel.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (!IsDroppedCargo(a))
            {
                continue;
            }

            FVector feet = a.Location - new FVector(0f, 0f, a.CollisionHeight);
            if (field.FindCarrier(feet, a.CollisionRadius, ConveyorCargo.Objects, out FVector velocity) is null)
            {
                continue;
            }

            driver.CarryOnConveyor(a, velocity * DeltaTime);
            if (!a.bDeleteMe && !driver.HasFloorBelow(a, ConveyorK.ObjectFloorProbe))
            {
                a.SetPhysics(EPhysics.PHYS_Falling);
            }
        }
    }

    /// <summary>
    /// A dropped disk or data drive lying still and not held: the objects players drop, whose positions replicate to
    /// every client. One still at home (where the map placed it, as SDisk.Touch tests) has not been dropped.
    /// </summary>
    private static bool IsDroppedCargo(Actor a)
    {
        FVector home;
        switch (a)
        {
            case SFlag flag:
                home = flag.InitialLocation;
                break;
            case SDisk disk:
                home = disk.InitialLocation;
                break;
            default:
                return false;
        }

        return !a.bDeleteMe && !a.bHidden && a.Physics == EPhysics.PHYS_None && a.bCollideWorld && a.Base is not Pawn &&
               (a.Location.X != home.X || a.Location.Y != home.Y);
    }
}
