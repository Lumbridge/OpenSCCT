using System;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

public sealed partial class ResponsiveSmokeField
{
    public static readonly FVector DefaultShapeExtents = new(280, 280, 190);
    private uint _shapeRevision, _shapeSnapshotRevision;
    private FVector _shapeExtents;

    /// <summary>Geometry redirects the bloom into available axes, using the same published cells on clients.</summary>
    public FVector ShapeExtents
    {
        get
        {
            if (_shapeRevision != Revision || _shapeSnapshotRevision != SnapshotRevision)
            {
                _shapeExtents = GeometryShapeExtents(Cells, ActivationTicks, Origin, Center);
                _shapeRevision = Revision; _shapeSnapshotRevision = SnapshotRevision;
            }
            return _shapeExtents;
        }
    }

    public static FVector GeometryShapeExtents(ReadOnlySpan<byte> cells, ReadOnlySpan<ushort> activation, FVector origin, FVector center)
    {
        FVector local = (center - origin) / CellSize;
        int x = (int)MathF.Floor(local.X), y = (int)MathF.Floor(local.Y), z = (int)MathF.Floor(local.Z);
        if (!Inside(x, y, z)) return DefaultShapeExtents;
        if (cells[Index(x, y, z)] == 0)
        {
            // A grounded bloom's raised center can lie in a low ceiling. Measure confinement from the nearest
            // reached air, never from an unconnected room above the ceiling or across a wall.
            int best = -1, distance = int.MaxValue;
            for (int dz = -4; dz <= 4; dz++) for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
            {
                if (!Inside(x + dx, y + dy, z + dz)) continue;
                int cell = Index(x + dx, y + dy, z + dz), d = dx * dx + dy * dy + dz * dz;
                if (cells[cell] == 0 || activation[cell] == Unreached || d >= distance) continue;
                best = cell; distance = d;
            }
            if (best < 0) return DefaultShapeExtents;
            Coordinates(best, out x, out y, out z);
        }
        float px = Confinement(cells, x, y, z, 1, 0, 0);
        float py = Confinement(cells, x, y, z, 0, 1, 0);
        float pz = Confinement(cells, x, y, z, 0, 0, 1);
        return new(Math.Min(300, 280 * (1 + .25f * py + .20f * pz)),
            Math.Min(300, 280 * (1 + .25f * px + .20f * pz)),
            Math.Min(275, 190 * (1 + .60f * Math.Max(px, py))));
    }

    private static float Confinement(ReadOnlySpan<byte> cells, int x, int y, int z, int dx, int dy, int dz)
    {
        int open = 0;
        for (int sign = -1; sign <= 1; sign += 2)
            for (int step = 1; step <= 6; step++)
            {
                int nx = x + dx * step * sign, ny = y + dy * step * sign, nz = z + dz * step * sign;
                if (!Inside(nx, ny, nz) || cells[Index(nx, ny, nz)] == 0) break;
                open++;
            }
        return 1 - open / 12f;
    }

    // Fast central release easing into a slower outer bloom. Evaluate the complete path once, without rounding
    // each voxel step; openings and detours retain their real arrival delay.
    internal static float BloomArrival(float distance) => distance / GrowthSpeed + .25f * (distance / Radius) * (distance / Radius);
}
