using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

public sealed partial class CollisionWorld : IZoneRegions
{
    /// <summary>
    /// UModel::PointRegion for actor zone tracking: the point's zone actor (zone 0 and unbound zones give the
    /// LevelInfo) and its zone number. The leaf index is not kept (-1).
    /// </summary>
    [UcNative("UModel::PointRegion")]
    [PortOf("nat:UModel::PointRegion", Spec = "spec/devices/zone-info.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Zone numbers are the SVMap ZONE record indices, assumed equal to the original zone numbers")]
    public FPointRegion PointRegion(FVector Location)
    {
        if (_zones.Length == 0)
        {
            return new FPointRegion { Zone = _level?.Info, iLeaf = -1, ZoneNumber = 0 };
        }

        int index = GetZoneIndex(Location);
        ZoneInfo? zone = _zoneActors[index] ?? _zoneActors[0] ?? _level?.Info;
        return new FPointRegion { Zone = zone, iLeaf = -1, ZoneNumber = (byte)index };
    }

    /// <summary>Number of zones (ZONE records).</summary>
    public int ZoneCount => _zones.Length;

    /// <summary>
    /// Zone of a point: through the world BSP when the map has nodes (walk to a leaf, front when the plane
    /// distance is not negative, and take that side's zone), else the first zone from 1 whose bounds hold the
    /// point, else zone 0.
    /// </summary>
    [UcNative("UModel::PointRegion")]
    [PortOf("nat:UModel::PointRegion", Spec = "spec/svmap/1.0/collision.md#zone-zones-stride-32")]
    [Provenance(ProvenanceKind.Estimated, "UE2 BSP point region walk; the SVMap BSPN layout is provisional")]
    public int GetZoneIndex(FVector Location)
    {
        if (_bspRoot >= 0)
        {
            int node = _bspRoot;
            int parent = -1;
            bool front = false;
            for (int guard = 0; node >= 0 && guard <= _bsp.Length; guard++)
            {
                parent = node;
                CollisionBspNode n = _bsp[node];
                front = n.Plane.PlaneDot(Location) >= 0f;
                node = front ? n.Front : n.Back;
            }

            if (parent >= 0)
            {
                int zone = front ? _bsp[parent].ZoneFront : _bsp[parent].ZoneBack;
                return zone < _zones.Length ? zone : 0;
            }

            return 0;
        }

        for (int i = 1; i < _zones.Length; i++)
        {
            if (_zones[i].Contains(Location))
            {
                return i;
            }
        }

        return 0;
    }

    public ZoneInfo? GetZoneInfo(FVector Location)
    {
        if (_zones.Length == 0)
        {
            return _level?.Info;
        }

        return _zoneActors[GetZoneIndex(Location)] ?? _zoneActors[0];
    }
}
