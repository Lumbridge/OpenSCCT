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
        if (!_boundaries.ContainsKey(cell)) return 0f;
        Coordinates(cell, out int x, out int y, out int z);
        float best = float.PositiveInfinity, density = 0f;
        for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            if (!Inside(x + dx, y + dy, z + dz)) continue;
            int donor = Index(x + dx, y + dy, z + dz);
            if (Cells[donor] == 0 || ActivationTicks[donor] == Unreached) continue;
            FVector target = CellCenter(donor);
            float distance = FVector.DistSquared(point, target);
            if (distance >= best || !BoundaryPathOpen(x, y, z, dx, dy, dz, point, target)) continue;
            best = distance;
            maskCell = donor;
            density = Math.Clamp((age - ActivationTicks[donor] / TickRate) / GrowthFade, 0f, 1f);
        }
        return density;
    }

    private bool BoundaryPathOpen(int x, int y, int z, int dx, int dy, int dz, FVector point, FVector target)
    {
        // Check the complete cell supercover. This deliberately cannot borrow air diagonally through a corner.
        for (int iz = 0; iz <= Math.Abs(dz); iz++) for (int iy = 0; iy <= Math.Abs(dy); iy++) for (int ix = 0; ix <= Math.Abs(dx); ix++)
        {
            int cell = Index(x + ix * dx, y + iy * dy, z + iz * dz);
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
