using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Traversal;

/// <summary>A GE search sphere in pawn space: an offset from the pawn's Location (turned by its yaw) and a radius.</summary>
public readonly record struct GEZone(FVector Offset, float Radius)
{
    /// <summary>
    /// The sphere's centre for a pawn at <paramref name="location"/> facing <paramref name="rotation"/>, turned by the
    /// yaw (the feet zones' rule; the arms zones turn with the full rotation, which equals it for an upright pawn,
    /// and add the PrePivot change: <see cref="Movement.PhysicsDriver.ArmsZonePoint"/>).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Zone points (spec/native/physics-ge-queries.md Data)")]
    public FVector CenterFor(FVector location, FRotator rotation)
    {
        float c = SvmMath.Cos(rotation.Yaw);
        float s = SvmMath.Sin(rotation.Yaw);
        return new FVector(
            location.X + Offset.X * c - Offset.Y * s,
            location.Y + Offset.X * s + Offset.Y * c,
            location.Z + Offset.Z);
    }
}

/// <summary>The GE search zones of SBasePawn (spec/movement/native-contracts.md, GE search zones).</summary>
public static class GEZones
{
    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_NormalArmsZone / Radius")]
    public static readonly GEZone NormalArms = new(new FVector(0f, 0f, 44.45f), 139.7f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_LedgeGrabArmsZone / Radius")]
    public static readonly GEZone LedgeGrabArms = new(new FVector(39f, 0f, 117f), 40f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_PipeArmsZone / Radius")]
    public static readonly GEZone PipeArms = new(new FVector(37f, 0f, 90.3f), 25.4f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn narrow ladder arms zone")]
    public static readonly GEZone NarrowLadderArms = new(new FVector(37.5f, 0f, 100.5f), 19.05f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn hand over hand arms zone")]
    public static readonly GEZone HandOverHandArms = new(new FVector(0f, 0f, 128f), 50f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_ZipLineArmsZone / Radius")]
    public static readonly GEZone ZipLineArms = new(new FVector(0f, 0f, 116f), 60f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn fence arms zone")]
    public static readonly GEZone FenceArms = new(new FVector(45.72f, 0f, 90f), 25f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_NormalFeetZone / Radius")]
    public static readonly GEZone NormalFeet = new(new FVector(39f, 2f, -85f), 60f);

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SBasePawn m_CrouchedFeetZone / Radius")]
    public static readonly GEZone CrouchedFeet = new(new FVector(0f, 0f, -63.5f), 76.2f);
}
