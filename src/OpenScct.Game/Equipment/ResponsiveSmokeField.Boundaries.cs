using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

public sealed partial class ResponsiveSmokeField
{
    public const int MaxBoundaryPlanes = 8;
    public const int MaxTotalBoundaryPlanes = 32768;
    public const float BoundaryEpsilon = .05f;
    private readonly Dictionary<int, ResponsiveSmokePlane[]> _boundaries = new();
    private int _boundaryPlanes;
    public IReadOnlyDictionary<int, ResponsiveSmokePlane[]> Boundaries => _boundaries;

    internal void ClearBoundaries() { _boundaries.Clear(); _boundaryPlanes = 0; }
    internal void SetBoundary(int cell, ResponsiveSmokePlane[]? planes)
    {
        if (_boundaries.Remove(cell, out var old)) _boundaryPlanes -= old.Length;
        if (planes is not { Length: > 0 } || _boundaryPlanes + planes.Length > MaxTotalBoundaryPlanes) return;
        _boundaries.Add(cell, planes); _boundaryPlanes += planes.Length;
    }
    private ResponsiveSmokeBoundaryCell[] CaptureBoundaries()
    {
        var result = new ResponsiveSmokeBoundaryCell[_boundaries.Count]; int i = 0;
        foreach (var pair in _boundaries) result[i++] = new((ushort)pair.Key, (ResponsiveSmokePlane[])pair.Value.Clone());
        Array.Sort(result, static (a, b) => a.Cell.CompareTo(b.Cell));
        return result;
    }

    private float BoundaryDensity(int cell, FVector point, float age, out int maskCell)
    {
        maskCell = -1;
        if (!_boundaries.TryGetValue(cell, out var planes)) return 0f;
        Coordinates(cell, out int x, out int y, out int z);
        float best = float.PositiveInfinity, density = 0f;
        for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            if (!Inside(x + dx, y + dy, z + dz)) continue;
            int donor = Index(x + dx, y + dy, z + dz);
            if (Cells[donor] == 0) continue;
            FVector target = CellCenter(donor);
            float distance = FVector.DistSquared(point, target);
            if (distance >= best || !BoundaryPathOpen(x, y, z, dx, dy, dz, point, target)) continue;
            best = distance;
            maskCell = donor;
        }
        if (maskCell < 0) return 0f;

        // Extrapolate through the boundary layer along its surface normals, preserving tangential position.
        // Nearest-column borrowing made the growing floor contact jump by an entire voxel at every seam.
        FVector sample = point, center = CellCenter(cell), nearest = CellCenter(maskCell);
        foreach (ResponsiveSmokePlane plane in planes)
        {
            float side = ((plane.Normal | (nearest - center)) - plane.Distance) > 0 ? 1 : -1;
            float distance = (plane.Normal | (sample - center)) - plane.Distance;
            float clearance = CellSize * (MathF.Abs(plane.Normal.X) + MathF.Abs(plane.Normal.Y) + MathF.Abs(plane.Normal.Z));
            sample += plane.Normal * (side * Math.Max(clearance, side * distance) - distance);
        }
        FVector p = (sample - Origin) / CellSize - new FVector(.5f, .5f, .5f);
        int bx = (int)MathF.Floor(p.X), by = (int)MathF.Floor(p.Y), bz = (int)MathF.Floor(p.Z);
        float fx = p.X - bx, fy = p.Y - by, fz = p.Z - bz, weight = 0;
        for (int iz = 0; iz <= 1; iz++) for (int iy = 0; iy <= 1; iy++) for (int ix = 0; ix <= 1; ix++)
        {
            int nx = bx + ix, ny = by + iy, nz = bz + iz;
            if (!Inside(nx, ny, nz)) continue;
            int donor = Index(nx, ny, nz);
            if (Cells[donor] == 0) continue;
            float w = (ix == 0 ? 1 - fx : fx) * (iy == 0 ? 1 - fy : fy) * (iz == 0 ? 1 - fz : fz);
            if (w <= 0 || !BoundaryPathOpen(x, y, z, nx - x, ny - y, nz - z, point, CellCenter(donor))) continue;
            weight += w;
            if (ActivationTicks[donor] != Unreached)
                density += w * Math.Clamp((age - ActivationTicks[donor] / TickRate) / GrowthFade, 0f, 1f);
        }
        if (weight > 0) return density / weight;
        return ActivationTicks[maskCell] == Unreached ? 0
            : Math.Clamp((age - ActivationTicks[maskCell] / TickRate) / GrowthFade, 0f, 1f);
    }

    private bool BoundaryPathOpen(int x, int y, int z, int dx, int dy, int dz, FVector point, FVector target)
    {
        // Check the complete cell supercover. This deliberately cannot borrow air diagonally through a corner.
        for (int iz = 0; iz <= Math.Abs(dz); iz++) for (int iy = 0; iy <= Math.Abs(dy); iy++) for (int ix = 0; ix <= Math.Abs(dx); ix++)
        {
            int cell = Index(x + ix * Math.Sign(dx), y + iy * Math.Sign(dy), z + iz * Math.Sign(dz));
            if (Cells[cell] != 0) continue;
            if (!_boundaries.TryGetValue(cell, out var planes)) return false;
            FVector relative = point - CellCenter(cell), donor = target - CellCenter(cell);
            foreach (ResponsiveSmokePlane plane in planes)
            {
                float p = (plane.Normal | relative) - plane.Distance;
                float d = (plane.Normal | donor) - plane.Distance;
                if (MathF.Abs(p) <= BoundaryEpsilon || p * d <= 0f) return false;
            }
        }
        return true;
    }
}
