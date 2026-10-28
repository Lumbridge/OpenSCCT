using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// The result of Actor.TraceBone (native 1122): the actor hit, the collision pill of a pawn hit (1 to 15, 0 for
/// anything else), the hit point and normal, the surface material and the fake backdrop flag. A miss has
/// <see cref="bHit"/> false (the native returns None; the LevelInfo stands in so callers never see null).
/// </summary>
public struct BoneTraceResult
{
    /// <summary>The actor hit; the LevelInfo for BSP hits and misses.</summary>
    public Actor Actor;

    /// <summary>True when something was hit before the trace end.</summary>
    public bool bHit;

    /// <summary>The struck collision pill of a pawn (1 to 15), 0 when no pill was hit.</summary>
    public int PillTag;

    public FVector HitLocation;

    public FVector HitNormal;

    /// <summary>The surface type of the material hit (Core.Object.ESurfaceType).</summary>
    public ESurfaceType SurfaceType;

    /// <summary>
    /// True when the hit surface is a fake backdrop (BSP poly flag 0x80): no impact effect. The collision data has no
    /// such flag yet, so it stays false.
    /// </summary>
    public bool bIsAFakeBackdrop;
}

/// <summary>
/// Hit traces with pills (Actor.TraceBone, spec/native/hit-zones.md rule 1). Install another implementation with
/// <c>Level.SetService&lt;IHitZones&gt;(...)</c>; the default is <see cref="TraceHitZones"/>.
/// </summary>
public interface IHitZones
{
    /// <summary>
    /// TraceBone from <paramref name="TraceStart"/> to <paramref name="TraceEnd"/> on behalf of
    /// <paramref name="Shooter"/> (a weapon traces for its instigator). <paramref name="bRealFire"/> marks a weapon shot.
    /// </summary>
    BoneTraceResult TraceBone(Actor Shooter, FVector TraceEnd, FVector TraceStart, bool bRealFire);
}

/// <summary>
/// The pawns' pills at an earlier moment, for one shot: a server with lag compensation installs one so that a client's
/// shot is traced against the pawns where that client saw them (docs/NETCODE.md, "Lag compensation"). Outside such a
/// shot it holds nothing and the live pose applies.
/// </summary>
public interface IPillHistory
{
    /// <summary>
    /// Adds the pills <paramref name="Pawn"/> had at the shot's moment to <paramref name="Shapes"/> and gives where it
    /// stood then; false (nothing added) when the live pose applies.
    /// </summary>
    bool TryGetPills(SBasePawn Pawn, List<PillShape> Shapes, out FVector Origin);
}

/// <summary>One collision pill of a pawn's current pose in world space: a capsule from A to B, or a sphere at A.</summary>
public readonly record struct PillShape(int Pill, FVector A, FVector B, float Radius, int Priority, bool IsSphere);

/// <summary>
/// The world-space collision pills of a pawn for its current pose (the skeletal mesh's collision points moved by the
/// animation). The simulation has no skinned poses, so <see cref="EstimatedPillPose"/> places them on a reference
/// stance; a pose from the animation system replaces it through <c>Level.SetService&lt;IPillPose&gt;(...)</c>.
/// </summary>
public interface IPillPose
{
    /// <summary>Adds the pills of <paramref name="Pawn"/> to <paramref name="Shapes"/> (none when it has no pill mesh).</summary>
    void GetPills(SBasePawn Pawn, List<PillShape> Shapes);
}

/// <summary>
/// The pill table of the character meshes (spec/native/hit-zones.md, Data: 13 capsules and 2 spheres, every priority
/// 1): pill number, radius and the bones each shape joins.
/// </summary>
public static class PillTable
{
    /// <summary>One table row: pill, radius, sphere or capsule.</summary>
    public readonly record struct Row(int Pill, float Radius, bool IsSphere);

    [Provenance(ProvenanceKind.Decoded, "spec/native/hit-zones.md pill table (SPerso.ukx ATT_01 and DEF_01)")]
    public static readonly Row[] Rows =
    [
        new(1, 9.0f, false),
        new(2, 12.5f, false),
        new(3, 7.0f, false),
        new(4, 6.5f, false),
        new(5, 7.0f, true),
        new(6, 9.5f, false),
        new(7, 7.5f, false),
        new(8, 5.5f, false),
        new(9, 12.5f, false),
        new(10, 7.0f, false),
        new(11, 6.5f, false),
        new(12, 7.0f, true),
        new(13, 9.5f, false),
        new(14, 7.5f, false),
        new(15, 5.5f, false),
    ];

    /// <summary>Every shipped shape has priority 1 (package licensee version 0).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/hit-zones.md: licensee 0 meshes, priority 1")]
    public const int Priority = 1;
}

/// <summary>
/// The pills on a reference stance scaled to the pawn's collision cylinder: the bone points are fractions of the
/// half height (up) and fixed side and forward offsets, turned by the pawn's yaw. Pills 2 to 8 are on the pawn's left
/// (B L bones and the left torso half), 9 to 15 on its right, as in the decoded table.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Reference stance for the pill bones; the decoded collision points stay in local/ and need the animated pose")]
public sealed class EstimatedPillPose : IPillPose
{
    public static readonly EstimatedPillPose Instance = new();

    // Bone points as (forward uu, left uu, up as a fraction of CollisionHeight), for the left side; the right side
    // mirrors the left offset.
    private static readonly FVector HeadLow = new(0f, 0f, 0.80f);
    private static readonly FVector HeadHigh = new(0f, 0f, 0.93f);
    private static readonly FVector Spine2 = new(0f, 6.2f, 0.52f);
    private static readonly FVector Pelvis = new(0f, 4.8f, 0.02f);
    private static readonly FVector UpperArm = new(2f, 15f, 0.62f);
    private static readonly FVector ForeArm = new(6f, 17f, 0.30f);
    private static readonly FVector Wrist = new(14f, 13f, 0.10f);
    private static readonly FVector Hand = new(17f, 12f, 0.05f);
    private static readonly FVector Thigh = new(0f, 8f, 0f);
    private static readonly FVector Calf = new(3f, 9f, -0.50f);
    private static readonly FVector Foot = new(0f, 9f, -0.90f);
    private static readonly FVector Toe = new(12f, 9f, -0.97f);

    // A rolling spy (bIsRolling, crouched cylinder) is tucked into a ball in the lower half of its cylinder: head down
    // by the knees, back rounded, hands at the shins. The pills top out about 5 uu above the cylinder's centre (about
    // 55 uu above the floor), so a beam higher than that passes over the roll while it still trips a crouched walk.
    private static readonly FVector RollHeadLow = new(16f, 0f, -0.20f);
    private static readonly FVector RollHeadHigh = new(10f, 0f, -0.10f);
    private static readonly FVector RollSpine2 = new(-6f, 6.2f, -0.15f);
    private static readonly FVector RollPelvis = new(-10f, 4.8f, -0.60f);
    private static readonly FVector RollUpperArm = new(2f, 15f, -0.20f);
    private static readonly FVector RollForeArm = new(10f, 15f, -0.45f);
    private static readonly FVector RollWrist = new(14f, 12f, -0.70f);
    private static readonly FVector RollHand = new(12f, 10f, -0.80f);
    private static readonly FVector RollThigh = new(-6f, 8f, -0.60f);
    private static readonly FVector RollCalf = new(12f, 9f, -0.35f);
    private static readonly FVector RollFoot = new(0f, 9f, -0.85f);
    private static readonly FVector RollToe = new(8f, 9f, -0.95f);

    private EstimatedPillPose()
    {
    }

    [Provenance(ProvenanceKind.Estimated, "Roll stance: a tucked ball in the crouched cylinder's lower half; the roll animation's real pose is not measured")]
    public void GetPills(SBasePawn Pawn, List<PillShape> Shapes)
    {
        float h = Pawn.CollisionHeight > 0f ? Pawn.CollisionHeight : 1f;
        new FRotator(0, Pawn.Rotation.Yaw, 0).GetAxes(out FVector fwd, out FVector right, out FVector up);
        FVector At(FVector p, bool left) =>
            Pawn.Location + fwd * p.X + right * (left ? -p.Y : p.Y) + up * (p.Z * h);

        bool roll = Pawn.bIsRolling;
        Add(Shapes, 1, At(roll ? RollHeadLow : HeadLow, true), At(roll ? RollHeadHigh : HeadHigh, true));
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            int o = left ? 0 : 7;
            Add(Shapes, 2 + o, At(roll ? RollSpine2 : Spine2, left), At(roll ? RollPelvis : Pelvis, left));
            Add(Shapes, 3 + o, At(roll ? RollUpperArm : UpperArm, left), At(roll ? RollForeArm : ForeArm, left));
            Add(Shapes, 4 + o, At(roll ? RollForeArm : ForeArm, left), At(roll ? RollWrist : Wrist, left));
            Add(Shapes, 5 + o, At(roll ? RollHand : Hand, left), At(roll ? RollHand : Hand, left));
            Add(Shapes, 6 + o, At(roll ? RollThigh : Thigh, left), At(roll ? RollCalf : Calf, left));
            Add(Shapes, 7 + o, At(roll ? RollCalf : Calf, left), At(roll ? RollFoot : Foot, left));
            Add(Shapes, 8 + o, At(roll ? RollFoot : Foot, left), At(roll ? RollToe : Toe, left));
        }
    }

    private static void Add(List<PillShape> shapes, int pill, FVector a, FVector b)
    {
        PillTable.Row row = PillTable.Rows[pill - 1];
        shapes.Add(new PillShape(pill, a, b, row.Radius, PillTable.Priority, row.IsSphere));
    }
}

/// <summary>The pill line check of a skeletal mesh (spec/native/hit-zones.md rule 2).</summary>
public static class PillLineCheck
{
    [Provenance(ProvenanceKind.Decoded, "spec/native/hit-zones.md rule 2.5: a pill hit needs t < 1.001")]
    public const float Acceptance = 1.001f;

    /// <summary>
    /// The best pill along Start to End: highest priority, then the smallest entry time. Returns false when no pill
    /// is crossed.
    /// </summary>
    [PortOf("eng:USkeletalMesh::LineCheck", Spec = "spec/native/hit-zones.md#2-pill-line-check-uskeletalmeshlinecheck-editor-0x111090a0")]
    public static bool Check(List<PillShape> Shapes, FVector Start, FVector End, out float Time, out FVector Location,
        out FVector Normal, out int Pill)
    {
        Time = 0f;
        Location = End;
        Normal = default;
        Pill = 0;
        int bestPriority = int.MinValue;
        float bestT = float.MaxValue;
        PillShape best = default;
        foreach (PillShape s in Shapes)
        {
            if (!EntryTime(s, Start, End, out float t) || !(t < Acceptance))
            {
                continue;
            }

            if (s.Priority > bestPriority || (s.Priority == bestPriority && t < bestT))
            {
                bestPriority = s.Priority;
                bestT = t;
                best = s;
            }
        }

        if (bestPriority == int.MinValue)
        {
            return false;
        }

        Time = bestT;
        Location = Start + (End - Start) * bestT;
        FVector centre = best.IsSphere ? best.A : ClosestOnSegment(best.A, best.B, Location);
        Normal = (Location - centre).SafeNormal();
        Pill = best.Pill;
        return true;
    }

    /// <summary>
    /// The trace's entry time (0 to 1 along Start to End) into a capsule (the set of points within Radius of the
    /// segment A to B) or a sphere; false when the trace misses it.
    /// </summary>
    public static bool EntryTime(in PillShape Shape, FVector Start, FVector End, out float t)
    {
        t = 0f;
        FVector d = End - Start;
        float len2 = d.SizeSquared();
        if (len2 <= 0f)
        {
            return false;
        }

        float r = Shape.Radius;
        FVector a = Shape.A;
        FVector ab = Shape.IsSphere ? default : Shape.B - Shape.A;
        float ab2 = ab.SizeSquared();
        if (ab2 <= 1e-6f)
        {
            return SphereEntry(a, r, Start, d, out t);
        }

        // Infinite cylinder around the axis, then the end caps as spheres.
        FVector m = Start - a;
        float md = m | ab;
        float nd = d | ab;
        float aq = len2 * ab2 - nd * nd;
        float bq = ab2 * (m | d) - md * nd;
        float cq = ab2 * (m | m) - md * md - r * r * ab2;
        float best = float.MaxValue;
        if (aq > 1e-6f)
        {
            float disc = bq * bq - aq * cq;
            if (disc >= 0f)
            {
                float tc = (-bq - SvmMath.Sqrt(disc)) / aq;
                if (tc < 0f && cq <= 0f)
                {
                    tc = 0f;
                }

                float along = md + tc * nd;
                if (tc >= 0f && tc <= 1f && along >= 0f && along <= ab2)
                {
                    best = tc;
                }
            }
        }

        if (SphereEntry(a, r, Start, d, out float ta) && ta < best)
        {
            best = ta;
        }

        if (SphereEntry(Shape.B, r, Start, d, out float tb) && tb < best)
        {
            best = tb;
        }

        if (best == float.MaxValue)
        {
            return false;
        }

        t = best;
        return true;
    }

    private static bool SphereEntry(FVector c, float r, FVector start, FVector d, out float t)
    {
        t = 0f;
        FVector m = start - c;
        float a = d | d;
        float b = m | d;
        float cc = (m | m) - r * r;
        if (cc <= 0f)
        {
            return true;
        }

        float disc = b * b - a * cc;
        if (b > 0f || disc < 0f)
        {
            return false;
        }

        t = (-b - SvmMath.Sqrt(disc)) / a;
        return t <= 1f;
    }

    /// <summary>The closest point of the segment A to B to P.</summary>
    public static FVector ClosestOnSegment(FVector A, FVector B, FVector P)
    {
        FVector ab = B - A;
        float ab2 = ab.SizeSquared();
        if (ab2 <= 0f)
        {
            return A;
        }

        float u = SvmMath.Clamp(((P - A) | ab) / ab2, 0f, 1f);
        return A + ab * u;
    }

    /// <summary>The distance from P to the segment A to B.</summary>
    public static float DistToSegment(FVector A, FVector B, FVector P) => FVector.Dist(ClosestOnSegment(A, B, P), P);
}

/// <summary>
/// TraceBone over the level's collision world (spec/native/hit-zones.md rule 1): the world, movers and other actors
/// in a zero-extent line check from the caller, then every pawn near the segment through its pills; the nearest hit
/// wins. Without a collision world only pawns can be hit.
/// </summary>
public sealed class TraceHitZones : IHitZones
{
    public static readonly TraceHitZones Instance = new(EstimatedPillPose.Instance);

    private readonly List<PillShape> _shapes = [];

    public TraceHitZones(IPillPose pose)
    {
        Pose = pose;
    }

    /// <summary>The pill pose used when the level has no <see cref="IPillPose"/> service.</summary>
    public IPillPose Pose { get; }

    /// <summary>
    /// The first pass: movers, the level, other actors and level geometry, with the material (0x40097 without the
    /// pawn bit; pawns answer through their pills in the second pass), plus the SVM bullet channel.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "hit-zones.md open question 1: pawns found by their pills only (pill bit forwarded)")]
    public const ETraceFlags WorldFlags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Others |
        ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Material | ETraceFlags.TRACE_Bullet;

    [Provenance(ProvenanceKind.Decoded, "spec/native/hit-zones.md rule 1.4: pawns within 3 x CollisionHeight of the segment")]
    public const float PawnPassScale = 3f;

    [UcNative("AActor::execTraceBone", Exe = 0x10a25270)]
    [PortOf("nat:Engine.Actor.TraceBone", Spec = "spec/native/hit-zones.md#1-tracebone-native-1122")]
    public BoneTraceResult TraceBone(Actor Shooter, FVector TraceEnd, FVector TraceStart, bool bRealFire)
    {
        var result = new BoneTraceResult
        {
            Actor = Shooter.Level,
            HitLocation = TraceEnd,
        };
        Actor source = Shooter is Inventory { Instigator: { } owner } ? owner : Shooter;
        float bestT = float.MaxValue;
        ICollisionWorld? world = CollisionOf(Shooter);
        if (world is not null && world.LineCheck(out FCheckResult hit, source, TraceEnd, TraceStart, FVector.Zero, WorldFlags))
        {
            result.bHit = true;
            result.Actor = hit.Actor ?? Shooter.Level;
            result.HitLocation = hit.Location;
            result.HitNormal = hit.Normal;
            result.SurfaceType = (ESurfaceType)hit.SurfaceType;
            result.bIsAFakeBackdrop = hit.bFakeBackdrop;
            bestT = hit.Time;
        }

        if (Shooter.XLevel is not { } level)
        {
            return result;
        }

        IPillPose pose = level.GetService<IPillPose>() ?? Pose;
        IPillHistory? past = level.GetService<IPillHistory>();
        foreach (Pawn p in level.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn pawn || ReferenceEquals(pawn, source) || IsBaseOf(pawn, source))
            {
                continue;
            }

            // A rewound shot (lag compensation) sees the pawn where the shooter saw it; otherwise the live pose.
            _shapes.Clear();
            FVector origin = pawn.Location;
            bool rewound = past is not null && past.TryGetPills(pawn, _shapes, out origin);
            if (!rewound)
            {
                origin = pawn.Location;
            }

            if (PillLineCheck.DistToSegment(TraceStart, TraceEnd, origin) >= PawnPassScale * pawn.CollisionHeight)
            {
                continue;
            }

            if (!rewound)
            {
                pose.GetPills(pawn, _shapes);
            }

            if (_shapes.Count == 0 ||
                !PillLineCheck.Check(_shapes, TraceStart, TraceEnd, out float t, out FVector loc, out FVector n, out int pill) ||
                !(t < bestT))
            {
                continue;
            }

            bestT = t;
            result.bHit = true;
            result.Actor = pawn;
            result.HitLocation = loc;
            result.HitNormal = n;
            result.PillTag = pill;
            result.SurfaceType = default;
            result.bIsAFakeBackdrop = false;
        }

        return result;
    }

    /// <summary>True when <paramref name="Candidate"/> is in the Base chain of <paramref name="Source"/>.</summary>
    private static bool IsBaseOf(Actor Candidate, Actor Source)
    {
        for (Actor? b = Source.Base; b is not null; b = b.Base)
        {
            if (ReferenceEquals(b, Candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The collision queries of an actor's level, or null.</summary>
    public static ICollisionWorld? CollisionOf(Actor Actor) =>
        Actor.XLevel?.GetService<ICollisionWorld>() ?? (Actor.XLevel?.PhysicsDriver as PhysicsDriver)?.Collision;
}
