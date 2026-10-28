using System;
using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.ERainVolume: a box of rain. The views draw it (render/fx.json); the simulation only asks whether a pawn
/// stands in it (ASBasePawn::Tick, SBasePawn.Rain.cs). The map's volumes are kept by the level's
/// <see cref="RainVolumeList"/>, not spawned: they never tick and are never replicated (bNoDelete, render-only class).
/// </summary>
public partial class ERainVolume
{
    /// <summary>
    /// RebuildRainVolume's box (m_RainVolume): (-X, -Y, 0)..(X, Y, Z) of m_RainSize x DrawScale x DrawScale3D from the
    /// volume's Location, which is the floor of the box. The volume's small rotations are left out.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The box as docs/RENDERING.md#rain-volumes draws it (floor origin read from OrphD's BSP; rotation left out)")]
    public void RebuildRainVolume()
    {
        FVector scale = DrawScale3D * DrawScale;
        var half = new FVector(m_RainSize.X * scale.X, m_RainSize.Y * scale.Y, m_RainSize.Z * scale.Z);
        m_RainVolume = new FBox
        {
            Min = new FVector(Location.X - half.X, Location.Y - half.Y, Location.Z),
            Max = new FVector(Location.X + half.X, Location.Y + half.Y, Location.Z + half.Z),
        };
    }

    /// <summary>UERainPrimitive::CheckPointCollision: true when <paramref name="Point"/> lies inside the volume's box.</summary>
    [UcNative("UERainPrimitive::CheckPointCollision")]
    [Provenance(ProvenanceKind.Estimated, "A point-in-box test of m_RainVolume, edges included (the primitive's test is not decoded)")]
    public bool CheckPointCollision(FVector Point)
    {
        FBox box = m_RainVolume;
        return Point.X >= box.Min.X && Point.X <= box.Max.X &&
               Point.Y >= box.Min.Y && Point.Y <= box.Max.Y &&
               Point.Z >= box.Min.Z && Point.Z <= box.Max.Z;
    }
}

public abstract partial class Actor
{
    /// <summary>
    /// GetRainVolume (native 1789): the rain volume whose box holds <paramref name="_Location"/>, or None. Mines,
    /// beams and the spy's marker halve their heat when they start in one.
    /// </summary>
    [UcNative("AActor::execGetRainVolume", Editor = 0x11189b00)]
    [PortOf("nat:Engine.Actor.GetRainVolume", Spec = "spec/equipment/rain.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The same test as the pawn tick's (the first volume of the level's list whose box holds the point); the native is not decoded")]
    public Actor? GetRainVolume(FVector _Location) => XLevel?.GetService<RainVolumeList>()?.At(_Location);
}

/// <summary>
/// The level's rain volumes (ULevel's rain list that ASBasePawn::Tick walks), in level order. A level service: the map
/// loader installs it from the map's sim/rain.json (or render/fx.json rain); a level without one has no rain.
/// </summary>
public sealed class RainVolumeList
{
    public RainVolumeList(IReadOnlyList<ERainVolume> volumes)
    {
        Volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
    }

    public IReadOnlyList<ERainVolume> Volumes { get; }

    /// <summary>The first volume whose box holds <paramref name="point"/>, or null.</summary>
    public ERainVolume? At(FVector point)
    {
        foreach (ERainVolume v in Volumes)
        {
            if (v.CheckPointCollision(point))
            {
                return v;
            }
        }

        return null;
    }
}
