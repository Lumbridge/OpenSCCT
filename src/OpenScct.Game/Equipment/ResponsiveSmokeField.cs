using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

public enum ResponsiveSmokeCarveKind : byte { Bullet, Blast }

/// <summary>A server-authored displacement. Blast masks are indexed in the cloud's voxel grid.</summary>
public sealed record ResponsiveSmokeCarve(uint Sequence, ResponsiveSmokeCarveKind Kind, FVector Start, FVector End,
    float Radius, float Feather, float CreatedAt, float Hold, float Refill, byte[]? AffectedCells)
{
    public ResponsiveSmokeCarve Copy() => this with { AffectedCells = AffectedCells is null ? null : (byte[])AffectedCells.Clone() };
}

/// <summary>Complete, independently applicable cloud state; arrays belong to the snapshot.</summary>
public sealed record ResponsiveSmokeSnapshot(uint Revision, float CreatedAt, float CapturedAt, FVector Origin, FVector Center,
    byte[] Cells, ushort[] ActivationTicks, ResponsiveSmokeCarve[] Carves, ResponsiveSmokeBoundaryCell[]? Boundaries = null);

public readonly record struct ResponsiveSmokePlane(FVector Normal, float Distance);
public sealed record ResponsiveSmokeBoundaryCell(ushort Cell, ResponsiveSmokePlane[] Planes);

/// <summary>
/// A bounded smoke field. Geometry and arrival times are authoritative; density and displacement recovery are
/// analytic in server time, allowing the renderer and gas queries to agree without density traffic every frame.
/// </summary>
public sealed partial class ResponsiveSmokeField
{
    public const int Dimension = 25;
    public const int CellCount = Dimension * Dimension * Dimension;
    public const float CellSize = 25f;
    public const float Radius = 300f;
    public const float QueryRadius = 450f;
    public const float Lifetime = 10f;
    public const float TickRate = 20f;
    public const float GrowthFade = .10f;
    public const float FadeStart = 8f;
    // Short paths through the body must conceal surfaces too, not only a full cloud diameter.
    // Shared with SMOKE_EXTINCTION in svm_responsive_smoke.gdshaderinc (including self-shadowing).
    public const float Extinction = .060f;
    public const ushort Unreached = ushort.MaxValue;
    public const int MaxCarves = 512;
    private const int FaceCost = 1024;
    private const float GrowthSpeed = 1200f;
    private static readonly int[] ClassifyOrder = BuildClassifyOrder();
    private readonly List<ResponsiveSmokeCarve> _carves = new();
    private readonly int[] _distance = new int[CellCount];
    private readonly PriorityQueue<int, (int Distance, int Index)> _front = new();
    private readonly List<int> _openedCells = new();
    private readonly HashSet<int> _openedNeighbors = new();
    private int _frontTick;
    private bool _growthStarted, _topologyInvalidated;
    internal int ClassifyCursor;
    internal bool GeometryDirty;
    internal uint CarveRevision;
    private uint _snapshotRevision;

    public ResponsiveSmokeField(FVector center, float createdAt, FVector? sourcePosition = null)
    {
        Center = center;
        SourcePosition = sourcePosition ?? center;
        CreatedAt = createdAt;
        Origin = new FVector(MathF.Floor((center.X - Radius) / CellSize) * CellSize,
            MathF.Floor((center.Y - Radius) / CellSize) * CellSize,
            MathF.Floor((center.Z - Radius) / CellSize) * CellSize);
        Array.Fill(ActivationTicks, Unreached);
    }

    public uint Revision { get; internal set; } = 1;
    /// <summary>The applied geometry baseline can advance independently of newer fast-carve revisions.</summary>
    public uint SnapshotRevision => _snapshotRevision;
    public float CreatedAt { get; private set; }
    public FVector Origin { get; private set; }
    public FVector Center { get; private set; }
    /// <summary>The real emitter remains the growth seed even when a grounded cloud blooms upward.</summary>
    public FVector SourcePosition { get; }
    public byte[] Cells { get; } = new byte[CellCount];
    public ushort[] ActivationTicks { get; } = new ushort[CellCount];
    public IReadOnlyList<ResponsiveSmokeCarve> Carves => _carves;
    internal bool HasPendingFront => _front.Count > 0;
    internal void CellOpened(int index) => _openedCells.Add(index);
    internal void InvalidateFront() { _topologyInvalidated = true; GeometryDirty = true; }
    public int AgeTick(float now) => Math.Clamp((int)MathF.Floor((now - CreatedAt) * TickRate + .001f), 0, 65534);
    internal static int ClassifyIndex(int cursor) => ClassifyOrder[cursor];
    private static int[] BuildClassifyOrder()
    {
        var order = new int[CellCount];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        static int Distance(int i) { Coordinates(i, out int x, out int y, out int z); x -= Dimension / 2; y -= Dimension / 2; z -= Dimension / 2; return x * x + y * y + z * z; }
        Array.Sort(order, (a, b) => { int d = Distance(a).CompareTo(Distance(b)); return d != 0 ? d : a.CompareTo(b); });
        return order;
    }
    public FVector CellCenter(int index)
    {
        Coordinates(index, out int x, out int y, out int z);
        return Origin + new FVector((x + .5f) * CellSize, (y + .5f) * CellSize, (z + .5f) * CellSize);
    }

    public int CellIndex(FVector point)
    {
        FVector p = (point - Origin) / CellSize;
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        return Inside(x, y, z) ? Index(x, y, z) : -1;
    }

    public static int Index(int x, int y, int z) => x + Dimension * (y + Dimension * z);
    public static void Coordinates(int index, out int x, out int y, out int z)
    {
        x = index % Dimension; y = index / Dimension % Dimension; z = index / (Dimension * Dimension);
    }
    private static bool Inside(int x, int y, int z) => (uint)x < Dimension && (uint)y < Dimension && (uint)z < Dimension;

    /// <summary>Rebuilds only after topology changes, retaining smoke already released into disconnected rooms.</summary>
    internal void RebuildFront(float now, Func<FVector, FVector, bool> seedReachable)
    {
        if (_growthStarted && !_topologyInvalidated)
        {
            // Initial geometry discovery only adds air. Continue its original sub-tick path distance instead
            // of restarting from quantized activation ticks, which would cap growth at one cell per 50 ms.
            foreach (int cell in _openedCells)
            {
                Coordinates(cell, out int x, out int y, out int z);
                for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (!Inside(x + dx, y + dy, z + dz)) continue;
                    int neighbor = Index(x + dx, y + dy, z + dz);
                    if (Cells[neighbor] != 0 && _distance[neighbor] != int.MaxValue) _openedNeighbors.Add(neighbor);
                }
            }
            // An opened supercover cell can also enable a shorter diagonal between two already known cells.
            // Recheck each adjacent source once; only genuinely improved destinations enter the work queue.
            foreach (int neighbor in _openedNeighbors) RelaxNeighbors(neighbor, _distance[neighbor]);
            _openedNeighbors.Clear();
            _openedCells.Clear(); GeometryDirty = false; Revision++;
            return;
        }
        int tick = AgeTick(now);
        _frontTick = tick;
        Array.Fill(_distance, int.MaxValue);
        _front.Clear();
        bool seeded = false;
        for (int i = 0; i < CellCount; i++)
        {
            if (Cells[i] == 0) { ActivationTicks[i] = Unreached; continue; }
            if (ActivationTicks[i] <= tick)
            {
                _distance[i] = 0; seeded = true;
            }
            else ActivationTicks[i] = Unreached;
        }
        if (seeded)
        {
            // Interior cells never need to enter the heap. Only the edge of smoke already released can extend it.
            for (int i = 0; i < CellCount; i++)
            {
                if (_distance[i] != 0) continue;
                Coordinates(i, out int x, out int y, out int z);
                bool open(int nx, int ny, int nz) => Inside(nx, ny, nz) && Cells[Index(nx, ny, nz)] != 0 && _distance[Index(nx, ny, nz)] != 0;
                if (open(x - 1, y, z) || open(x + 1, y, z) || open(x, y - 1, z) || open(x, y + 1, z) || open(x, y, z - 1) || open(x, y, z + 1))
                    _front.Enqueue(i, (0, i));
            }
        }
        if (!seeded)
        {
            int seed = -1;
            float best = CellSize * CellSize * 9f;
            for (int i = 0; i < CellCount; i++)
            {
                if (Cells[i] == 0) continue;
                float d = FVector.DistSquared(CellCenter(i), SourcePosition);
                if (d < best && seedReachable(SourcePosition, CellCenter(i))) { best = d; seed = i; }
            }
            if (seed < 0) { _openedCells.Clear(); GeometryDirty = false; Revision++; return; }
            ActivationTicks[seed] = (ushort)tick;
            _distance[seed] = 0; _front.Enqueue(seed, (0, seed));
        }
        _growthStarted = true; _topologyInvalidated = false; _openedCells.Clear();
        GeometryDirty = false;
        Revision++;
    }

    /// <summary>Fixed work budget prevents a grenade or a door change monopolizing a simulation frame.</summary>
    internal void AdvanceFront(float now, ref int budget)
    {
        bool changed = false;
        int publishedTick = AgeTick(now);
        while (budget > 0 && _front.TryDequeue(out int current, out var priority))
        {
            budget--;
            if (_distance[current] != priority.Distance) continue;
            // Budgeted classification can reveal a cell after the physical front would have arrived. Preserve
            // the path's sub-tick distance, but give newly published air its local fade instead of popping dense.
            ushort activation = (ushort)Math.Min(65534, Math.Max(publishedTick,
                _frontTick + (int)MathF.Ceiling(BloomArrival(priority.Distance * CellSize / FaceCost) * TickRate)));
            if (ActivationTicks[current] > publishedTick && ActivationTicks[current] > activation)
            {
                ActivationTicks[current] = activation;
                changed = true;
            }
            RelaxNeighbors(current, priority.Distance);
        }
        if (changed) Revision++;
    }

    private void RelaxNeighbors(int current, int currentDistance)
    {
        Coordinates(current, out int x, out int y, out int z);
        for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            int axes = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
            if (axes == 0 || !Inside(x + dx, y + dy, z + dz)) continue;
            int next = Index(x + dx, y + dy, z + dz);
            int distance = currentDistance + StepCost(axes);
            if (Cells[next] == 0 || distance >= _distance[next] || !DiagonalOpen(x, y, z, dx, dy, dz)) continue;
            _distance[next] = distance; _front.Enqueue(next, (distance, next));
        }
    }

    private static int StepCost(int axes) => axes == 1 ? FaceCost : axes == 2 ? 1448 : 1774;

    private bool DiagonalOpen(int x, int y, int z, int dx, int dy, int dz)
    {
        // The entire supercover must be free: diagonals cannot cut a wall's edge or corner.
        for (int ix = 0; ix <= Math.Abs(dx); ix++) for (int iy = 0; iy <= Math.Abs(dy); iy++) for (int iz = 0; iz <= Math.Abs(dz); iz++)
            if (Cells[Index(x + ix * dx, y + iy * dy, z + iz * dz)] == 0) return false;
        return true;
    }

    public float SampleDensity(FVector point, float now)
    {
        int nearest = CellIndex(point);
        float age = now - CreatedAt;
        if (nearest < 0 || age < 0f || age >= Lifetime) return 0f;
        float radial = Envelope(point - Center, age, ShapeExtents);
        if (radial <= 0f) return 0f;
        FVector p = (point - Origin) / CellSize - new FVector(.5f, .5f, .5f);
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        Coordinates(nearest, out int nx, out int ny, out int nz);
        float fx = p.X - x, fy = p.Y - y, fz = p.Z - z, density = 0f, weight = 0f;
        int maskCell = nearest;
        if (Cells[nearest] == 0) density = BoundaryDensity(nearest, point, age, out maskCell);
        else for (int iz = 0; iz <= 1; iz++) for (int iy = 0; iy <= 1; iy++) for (int ix = 0; ix <= 1; ix++)
        {
            if (!Inside(x + ix, y + iy, z + iz)) continue;
            int cell = Index(x + ix, y + iy, z + iz);
            if (Cells[cell] == 0) continue;
            int dx = x + ix - nx, dy = y + iy - ny, dz = z + iz - nz;
            if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > 1 &&
                !BoundaryPathOpen(nx, ny, nz, dx, dy, dz, point, CellCenter(cell))) continue;
            float w = (ix == 0 ? 1f - fx : fx) * (iy == 0 ? 1f - fy : fy) * (iz == 0 ? 1f - fz : fz);
            weight += w;
            if (ActivationTicks[cell] == Unreached) continue;
            float value = Math.Clamp((age - ActivationTicks[cell] / TickRate) / GrowthFade, 0f, 1f);
            density += value * w;
        }
        if (Cells[nearest] != 0 && weight > 0f) density /= weight;
        density *= radial * (1f - Smooth(FadeStart, Lifetime, age));
        foreach (ResponsiveSmokeCarve carve in _carves)
        {
            float elapsed = now - carve.CreatedAt;
            if (elapsed < 0f || elapsed >= carve.Hold + carve.Refill || carve.AffectedCells is { } mask && (maskCell < 0 || mask[maskCell] == 0)) continue;
            float distance = DistanceToSegment(point, carve.Start, carve.End);
            float spatial = 1f - Smooth(carve.Radius, carve.Radius + carve.Feather, distance);
            density *= 1f - spatial * (1f - Smooth(carve.Hold, carve.Hold + carve.Refill, elapsed));
        }
        return Math.Clamp(density, 0f, 1f);
    }

    internal void AddCarve(ResponsiveSmokeCarve carve)
    {
        // This is above the maximum simultaneously live holes from six players' normal weapon rates.
        if (_carves.Count >= MaxCarves) _carves.RemoveAt(0);
        _carves.Add(carve); Revision++; CarveRevision = Revision;
    }

    internal void ExpireCarves(float now)
    {
        if (_carves.RemoveAll(c => now >= c.CreatedAt + c.Hold + c.Refill) > 0) { Revision++; CarveRevision = Revision; }
    }

    public ResponsiveSmokeSnapshot Capture(float now)
    {
        var carves = new ResponsiveSmokeCarve[_carves.Count];
        for (int i = 0; i < carves.Length; i++) carves[i] = _carves[i].Copy();
        return new(Revision, CreatedAt, now, Origin, Center, (byte[])Cells.Clone(), (ushort[])ActivationTicks.Clone(), carves, CaptureBoundaries());
    }

    internal void Apply(ResponsiveSmokeSnapshot snapshot)
    {
        if (snapshot.Revision < _snapshotRevision) return;
        CreatedAt = snapshot.CreatedAt; Origin = snapshot.Origin; Center = snapshot.Center;
        _front.Clear(); _openedCells.Clear(); _growthStarted = false; _topologyInvalidated = false; GeometryDirty = false;
        snapshot.Cells.CopyTo(Cells, 0); snapshot.ActivationTicks.CopyTo(ActivationTicks, 0);
        ClearBoundaries();
        foreach (ResponsiveSmokeBoundaryCell boundary in snapshot.Boundaries ?? [])
            SetBoundary(boundary.Cell, (ResponsiveSmokePlane[])boundary.Planes.Clone());
        ApplyCarves(snapshot.Revision, snapshot.Carves);
        Revision = Math.Max(Revision, snapshot.Revision); ClassifyCursor = CellCount;
        _snapshotRevision = snapshot.Revision;
    }

    internal void ApplyCarves(uint revision, IReadOnlyList<ResponsiveSmokeCarve> carves)
    {
        if (revision < CarveRevision) return;
        _carves.Clear();
        for (int i = 0; i < carves.Count; i++) _carves.Add(carves[i].Copy());
        CarveRevision = revision;
        Revision = Math.Max(Revision, revision);
    }

    /// <summary>Conservative grid visibility for blast pressure, with no additional physics query per voxel.</summary>
    public bool GridLineOpen(FVector start, FVector end)
    {
        float distance = FVector.Dist(start, end);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / (CellSize * .2f)));
        for (int i = 0; i <= steps; i++)
        {
            int cell = CellIndex(start + (end - start) * (i / (float)steps));
            if (cell >= 0 && Cells[cell] == 0) return false;
        }
        return true;
    }

    public static float DistanceToSegment(FVector point, FVector start, FVector end)
    {
        FVector direction = end - start;
        float length = direction.SizeSquared();
        float t = length > 0f ? Math.Clamp(((point - start) | direction) / length, 0f, 1f) : 0f;
        return FVector.Dist(point, start + direction * t);
    }

    public static float Smooth(float start, float end, float value)
    {
        float t = Math.Clamp((value - start) / (end - start), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Maps cloud-relative air into one coherently rolling material pattern. Ordered shears are invertible and
    /// preserve volume; geometry and clearings remain evaluated at the original world position.
    /// </summary>
    public static FVector MaterialCoordinate(FVector relative, float age)
    {
        relative.X += 24f * SvmMath.Sin(relative.Z / 85f - 1.2f * age);
        relative.Z += 22f * SvmMath.Sin(relative.X / 90f + .9f * age + 1.7f);
        relative.Y += 18f * SvmMath.Sin(relative.Z / 100f - .7f * age + 3.1f);
        relative.Z -= 18f * age;
        return relative;
    }

    /// <summary>A broad rounded body and rolling rim; lighting quality never changes gas or visibility.</summary>
    public static float Envelope(FVector relative, float age) => Envelope(relative, age, DefaultShapeExtents);

    public static float Envelope(FVector relative, float age, FVector extents)
    {
        float radiusSquared = relative.SizeSquared();
        if (radiusSquared <= 150f * 150f) return 1f;
        if (radiusSquared >= QueryRadius * QueryRadius) return 0f;
        float radius = MathF.Sqrt(radiusSquared);
        FVector material = MaterialCoordinate(relative, age);
        float macro = Smooth(.28f, .72f, ResponsiveSmokeNoise.Sample(material / 135f + new FVector(11.17f, 7.31f, 19.83f)));
        float fine = ResponsiveSmokeNoise.Sample(material / 55f + new FVector(23.41f, 3.71f, 13.29f));
        float taper = 1 - .16f * Smooth(-.1f, 1, relative.Z / extents.Z);
        FVector p = new(MathF.Abs(relative.X / (extents.X * taper)), MathF.Abs(relative.Y / (extents.Y * taper)), MathF.Abs(relative.Z / extents.Z));
        float body = MathF.Cbrt(p.X * p.X * p.X + p.Y * p.Y * p.Y + p.Z * p.Z * p.Z);
        float edge = .90f + .08f * macro + .02f * fine;
        float shell = 1f + (.70f + .30f * macro - 1f) * Smooth(.55f, .88f, body);
        float density = shell * (1f - Smooth(edge - .07f, edge, body));
        return Math.Max(1f - Smooth(150f, 180f, radius), density);
    }
}
