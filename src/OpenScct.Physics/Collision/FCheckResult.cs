using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Result of a collision query (UE2 FCheckResult). <see cref="Time"/> is the fraction of the trace travelled
/// before the hit (1 when nothing was hit); <see cref="Location"/> is where the trace stopped and
/// <see cref="Normal"/> the unit surface normal facing the trace start.
/// </summary>
public struct FCheckResult
{
    /// <summary>Fraction of the trace before the hit, 0 to 1; 1 means no hit.</summary>
    public float Time;

    /// <summary>Where the trace (or the extent box centre) stopped.</summary>
    public FVector Location;

    /// <summary>Unit normal of the surface hit, facing the trace start.</summary>
    public FVector Normal;

    /// <summary>The actor hit: the collider's owner, the LevelInfo for level geometry, or a cylinder actor.</summary>
    public Actor? Actor;

    /// <summary>Index of the collider hit (COLL), or -1 for actor cylinders and no hit.</summary>
    public int Collider;

    /// <summary>Source triangle (TRIS) or hull (HULL) index of the primitive hit, or -1.</summary>
    public int Item;

    /// <summary>Collision material (TFLG) index, or -1.</summary>
    public int Material;

    /// <summary>The material's surface type (footsteps, impacts).</summary>
    public byte SurfaceType;

    /// <summary>The material's gameplay flags (climbable, noSplitJump...).</summary>
    public CollisionMaterialFlags MaterialFlags;

    /// <summary>The original UE2 PolyFlags of the world BSP surface hit (0 for every other primitive or without TFLX).</summary>
    public uint PolyFlags;

    /// <summary>A world BSP hit on a fake backdrop surface (PF_FakeBackdrop, 0x80).</summary>
    public readonly bool bFakeBackdrop => (PolyFlags & 0x80u) != 0;

    /// <summary>True when the trace started overlapping what it hit and was moving deeper into it.</summary>
    public bool bStartPenetrating;

    /// <summary>True when something was hit.</summary>
    public readonly bool bHit => Time < 1f;

    /// <summary>A result for a trace that reached <paramref name="end"/>.</summary>
    public static FCheckResult NoHit(FVector end) => new()
    {
        Time = 1f,
        Location = end,
        Collider = -1,
        Item = -1,
        Material = -1,
    };

    public override readonly string ToString() =>
        bHit ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"hit t={Time:R} at {Location} n={Normal} actor={Actor?.ToString() ?? "none"} collider={Collider} item={Item}")
            : "no hit";
}
