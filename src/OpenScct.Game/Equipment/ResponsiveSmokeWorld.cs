using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.Equipment;

/// <summary>Shared geometry, exposure and server-authored smoke state. Disabled matches allocate no voxel grids.</summary>
public sealed partial class ResponsiveSmokeWorld
{
    private const int GeometryQueriesPerTick = 1024;
    private const int FrontNodesPerTick = 2048;
    private const int ClassificationsPerCloudPerTick = 512;
    public const float ExposureThreshold = .25f;
    private readonly Level _level;
    private readonly Func<FVector, FVector, bool> _seedReachable;
    private readonly Dictionary<SSmokeEmitter, ResponsiveSmokeField> _fields = new();
    private readonly Dictionary<CellKey, GeometryCell> _geometry = new();
    private readonly List<CellKey> _invalidGeometryKeys = new();
    private readonly Dictionary<int, ColliderInfo> _colliders = new();
    private readonly List<int> _movingColliders = new();
    private readonly Dictionary<int, int> _blockingStates = new();
    private uint _geometryRevision;
    private readonly Queue<(ResponsiveSmokeField Field, int Index)> _dirtyCells = new();
    private readonly HashSet<(ResponsiveSmokeField Field, int Index)> _pendingDirtyCells = new();
    private readonly List<SSmokeEmitter> _gone = new();
    private readonly List<(float Start, float End)> _rayIntervals = new();
    private readonly List<ResponsiveSmokeField> _tickFields = new();
    private int _firstField;
    private float _lastTick = float.NegativeInfinity, _nextGeometryCheck;
    private float _clockOffset;
    private uint _sequence;
    private ICollisionWorld? _collision;
    private readonly record struct CellKey(int X, int Y, int Z);
    private readonly record struct GeometryCell(bool Free, ResponsiveSmokePlane[]? Planes);

    private ResponsiveSmokeWorld(Level level)
    {
        _level = level;
        _seedReachable = SeedReachable;
        // Capture the existing map identity before a baseline is installed. Its first query is not a map replacement.
        _collision = GadgetWorld.Collision(level.Info);
    }
    public static ResponsiveSmokeWorld For(Level level)
    {
        if (level.GetService<ResponsiveSmokeWorld>() is { } existing) return existing;
        var service = new ResponsiveSmokeWorld(level); level.SetService(service); return service;
    }

    public static bool IsEnabled(Level level)
    {
        if (level.Info.GRI is { bDeleteMe: false } rules) return rules.ResponsiveSmokeEnabled;
        if (level.Info.Game?.GameReplicationInfo is { bDeleteMe: false } gameRules) return gameRules.ResponsiveSmokeEnabled;
        foreach (GameReplicationInfo info in level.AllActors<GameReplicationInfo>())
            if (!info.bDeleteMe && info.ResponsiveSmokeEnabled) return true;
        return false;
    }

    public IReadOnlyDictionary<SSmokeEmitter, ResponsiveSmokeField> Fields => _fields;
    public float CurrentTime => _level.WorldTimeSeconds + (_level.Info.NetMode == ENetMode.NM_Client ? _clockOffset : 0f);
    public long GeometryQueries { get; private set; }
    public int PendingGeometryCells => _pendingDirtyCells.Count;
    public void SynchronizeTime(float serverSeconds)
    {
        if (float.IsFinite(serverSeconds)) _clockOffset = serverSeconds - _level.WorldTimeSeconds;
    }
    public bool TryGet(SSmokeEmitter emitter, out ResponsiveSmokeField field) => _fields.TryGetValue(emitter, out field!);
    public void Register(SSmokeEmitter emitter)
    {
        if (!IsEnabled(_level) || emitter.bDeleteMe || _fields.ContainsKey(emitter) || _level.Info.NetMode == ENetMode.NM_Client) return;
        long diagnosticStart = DiagnosticsStart(out long diagnosticAllocated);
        FVector center = emitter.Location;
        // Grounded smoke blooms into the air above its source. The real emitter still seeds connectivity;
        // moving the envelope must never teleport the release through a ceiling or nearby wall.
        ICollisionWorld? collision = GadgetWorld.Collision(_level.Info);
        if (collision is not null && collision.LineCheck(out FCheckResult floor, _level.Info,
            center - new FVector(0, 0, 50), center, default, GadgetWorld.SightFlags) && floor.Normal.Z > .5f)
            center += new FVector(0, 0, 140);
        _fields.Add(emitter, new ResponsiveSmokeField(center, CurrentTime, emitter.Location));
        DiagnosticsEnd(ref _registerDiagnostics, diagnosticStart, diagnosticAllocated);
    }
    public ResponsiveSmokeSnapshot Capture(SSmokeEmitter emitter)
    {
        long start = DiagnosticsStart(out long allocated);
        ResponsiveSmokeSnapshot snapshot = _fields[emitter].Capture(CurrentTime);
        DiagnosticsEnd(ref _captureDiagnostics, start, allocated);
        return snapshot;
    }
    public void Unregister(SSmokeEmitter emitter)
    {
        if (_fields.Remove(emitter, out ResponsiveSmokeField? removed))
        {
            int count = _dirtyCells.Count;
            for (int i = 0; i < count; i++)
            {
                var cell = _dirtyCells.Dequeue();
                if (ReferenceEquals(cell.Field, removed)) _pendingDirtyCells.Remove(cell);
                else _dirtyCells.Enqueue(cell);
            }
            _tickFields.Remove(removed);
        }
        if (_fields.Count == 0)
        { _geometry.Clear(); _colliders.Clear(); _movingColliders.Clear(); _blockingStates.Clear(); _dirtyCells.Clear(); _pendingDirtyCells.Clear(); }
    }
    public void ApplySnapshot(SSmokeEmitter emitter, ResponsiveSmokeSnapshot snapshot)
    {
        long start = DiagnosticsStart(out long allocated);
        ResponsiveSmokeCodec.Validate(snapshot);
        if (!_fields.TryGetValue(emitter, out ResponsiveSmokeField? field))
            _fields.Add(emitter, field = new ResponsiveSmokeField(snapshot.Center, snapshot.CreatedAt, emitter.Location));
        field.Apply(snapshot);
        DiagnosticsEnd(ref _applyDiagnostics, start, allocated);
    }
    public void ApplyCarves(SSmokeEmitter emitter, uint revision, float capturedAt, IReadOnlyList<ResponsiveSmokeCarve> carves)
    {
        long start = DiagnosticsStart(out long allocated);
        if (!_fields.TryGetValue(emitter, out ResponsiveSmokeField? field))
            _fields.Add(emitter, field = new ResponsiveSmokeField(emitter.Location, capturedAt));
        field.ApplyCarves(revision, carves);
        DiagnosticsEnd(ref _applyDiagnostics, start, allocated);
    }

    public void Tick()
    {
        float now = CurrentTime;
        if (_lastTick == _level.WorldTimeSeconds || _level.World.IsReplaying) return;
        _lastTick = _level.WorldTimeSeconds;
        foreach (var pair in _fields) if (pair.Key.bDeleteMe) _gone.Add(pair.Key);
        foreach (SSmokeEmitter dead in _gone) Unregister(dead);
        _gone.Clear();
        if (!IsEnabled(_level) || _level.Info.NetMode == ENetMode.NM_Client) return;
        foreach (SSmokeEmitter emitter in _level.DynamicActors<SSmokeEmitter>()) Register(emitter);
        if (_fields.Count == 0) return;
        ICollisionWorld? collision = GadgetWorld.Collision(_level.Info);
        if (!ReferenceEquals(collision, _collision))
        {
            _collision = collision; _geometry.Clear(); _colliders.Clear(); _movingColliders.Clear(); _blockingStates.Clear();
            _dirtyCells.Clear(); _pendingDirtyCells.Clear();
            foreach (ResponsiveSmokeField field in _fields.Values)
            {
                if (field.ClassifyCursor == 0) continue;
                Array.Clear(field.Cells); Array.Fill(field.ActivationTicks, ResponsiveSmokeField.Unreached);
                field.ClearBoundaries();
                field.ClassifyCursor = 0; field.InvalidateFront();
            }
        }
        if (now >= _nextGeometryCheck)
        {
            long start = DiagnosticsStart(out long allocated);
            RefreshGeometry(); _nextGeometryCheck = now + .05f;
            DiagnosticsEnd(ref _geometryDiagnostics, start, allocated);
        }
        long frontStart = DiagnosticsStart(out long frontAllocated);
        int budget = GeometryQueriesPerTick, dirtyVisits = 2048, frontBudget = FrontNodesPerTick;
        while (budget > 0 && dirtyVisits-- > 0 && _dirtyCells.TryDequeue(out var dirty))
        {
            _pendingDirtyCells.Remove(dirty);
            if (_fields.ContainsValue(dirty.Field)) Classify(dirty.Field, dirty.Index, ref budget);
        }
        _tickFields.Clear(); _tickFields.AddRange(_fields.Values);
        for (int n = 0; n < _tickFields.Count; n++)
        {
            ResponsiveSmokeField field = _tickFields[(_firstField + n) % _tickFields.Count];
            int visits = ClassificationsPerCloudPerTick;
            if (!field.HasPendingFront)
                while (budget > 0 && visits-- > 0 && field.ClassifyCursor < ResponsiveSmokeField.CellCount)
                    Classify(field, ResponsiveSmokeField.ClassifyIndex(field.ClassifyCursor++), ref budget);
            if (field.GeometryDirty) field.RebuildFront(now, _seedReachable);
            field.AdvanceFront(now, ref frontBudget);
            field.ExpireCarves(now);
        }
        _firstField = (_firstField + 1) % _tickFields.Count;
        DiagnosticsEnd(ref _frontDiagnostics, frontStart, frontAllocated);
        ApplyExposure(now);
    }

    /// <summary>Called by client emitters too: the local owner uses the exact received field for cough and KO.</summary>
    public void ApplyLocalExposure()
    {
        if (!IsEnabled(_level)) return;
        Tick();
        if (_level.Info.NetMode == ENetMode.NM_Client) ApplyExposure(CurrentTime);
    }

    private float _lastExposure = float.NegativeInfinity;
    private void ApplyExposure(float now)
    {
        if (_lastExposure == _level.WorldTimeSeconds) return;
        _lastExposure = _level.WorldTimeSeconds;
        long start = DiagnosticsStart(out long allocated);
        foreach (SPlayerController pc in _level.AllActors<SPlayerController>())
        {
            if (pc.Pawn is not SBasePawn pawn) continue;
            SSmokeEmitter? source = ExposureSource(pawn, now, out float density);
            if (source is null || density < ExposureThreshold) continue;
            if (source.Role == ENetRole.ROLE_Authority) pc.ObserveGasActivity(source, source.TimeBaseToCought, source.TimeBaseToFaint, false);
            if (!pawn.bGasMask && !pc.IsRemotePlayer && _level.Info.NetMode != ENetMode.NM_DedicatedServer)
                source.DetectedSPlayerController(pc);
        }
        DiagnosticsEnd(ref _exposureDiagnostics, start, allocated);
    }

    public SSmokeEmitter? ExposureSource(SBasePawn pawn, float now, out float density)
    {
        FVector eye = AiSight.Eye(pawn);
        Span<FVector> points = stackalloc FVector[7] { eye, eye + new FVector(10, 0, 0), eye - new FVector(10, 0, 0),
            eye + new FVector(0, 10, 0), eye - new FVector(0, 10, 0), eye + new FVector(0, 0, 10), eye - new FVector(0, 0, 10) };
        Span<float> combined = stackalloc float[7];
        combined.Clear();
        SSmokeEmitter? source = null;
        float strongest = 0f, distance = float.PositiveInfinity;
        density = 0f;
        foreach (var pair in _fields)
        {
            if (pair.Key.bDeleteMe) continue;
            float contribution = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                float value = pair.Value.SampleDensity(points[i], now);
                combined[i] += value; contribution += value;
            }
            contribution /= points.Length;
            float d = FVector.DistSquared(pair.Key.Location, eye);
            if (contribution > strongest || contribution == strongest && contribution > 0f &&
                (d < distance || d == distance && pair.Key.ActorId < source!.ActorId))
            { strongest = contribution; distance = d; source = pair.Key; }
        }
        foreach (float value in combined) density += Math.Min(1f, value) / points.Length;
        return source;
    }

    public static float BreathingDensity(ResponsiveSmokeField field, FVector eye, float now) =>
        (field.SampleDensity(eye, now) + field.SampleDensity(eye + new FVector(10, 0, 0), now) +
         field.SampleDensity(eye - new FVector(10, 0, 0), now) + field.SampleDensity(eye + new FVector(0, 10, 0), now) +
         field.SampleDensity(eye - new FVector(0, 10, 0), now) + field.SampleDensity(eye + new FVector(0, 0, 10), now) +
         field.SampleDensity(eye - new FVector(0, 0, 10), now)) / 7f;

    public float SampleDensity(FVector point)
    {
        float density = 0f;
        foreach (var pair in _fields) if (!pair.Key.bDeleteMe) density += pair.Value.SampleDensity(point, CurrentTime);
        return Math.Min(1f, density);
    }
    public bool InSmoke(FVector point, float extra = 0f)
    {
        if (SampleDensity(point) >= ExposureThreshold) return true;
        if (extra <= 0f) return false;
        foreach (var pair in _fields)
        {
            FVector toward = pair.Key.Location - point;
            float distance = toward.Size();
            if (distance > 0f && SampleDensity(point + toward * (Math.Min(extra, distance) / distance)) >= ExposureThreshold) return true;
        }
        return false;
    }
    public bool SmokeBetween(FVector start, FVector end) => OpticalDepth(start, end, 1.05f) >= 1.05f;

    /// <summary>Integrates the shared density along a ray, optionally stopping once enough opacity is reached.</summary>
    public float OpticalDepth(FVector start, FVector end, float stopAt = float.PositiveInfinity)
    {
        float length = FVector.Dist(start, end);
        if (length <= 0f || stopAt <= 0f) return 0f;
        FVector direction = (end - start) / length;
        _rayIntervals.Clear();
        foreach (var pair in _fields)
        {
            if (pair.Key.bDeleteMe) continue;
            FVector toCenter = pair.Value.Center - start;
            float projection = toCenter | direction;
            float discriminant = ResponsiveSmokeField.QueryRadius * ResponsiveSmokeField.QueryRadius - (toCenter.SizeSquared() - projection * projection);
            if (discriminant < 0f) continue;
            float half = MathF.Sqrt(discriminant), from = Math.Max(0f, projection - half), to = Math.Min(length, projection + half);
            if (to > from) _rayIntervals.Add((from, to));
        }
        _rayIntervals.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        float opticalDepth = 0f;
        for (int interval = 0; interval < _rayIntervals.Count; interval++)
        {
            (float from, float to) = _rayIntervals[interval];
            while (interval + 1 < _rayIntervals.Count && _rayIntervals[interval + 1].Start <= to)
                to = Math.Max(to, _rayIntervals[++interval].End);
            int steps = Math.Max(1, (int)MathF.Ceiling((to - from) / 10f));
            float step = (to - from) / steps;
            for (int i = 0; i < steps; i++)
            {
                opticalDepth += SampleDensity(start + direction * (from + (i + .5f) * step)) * step * ResponsiveSmokeField.Extinction;
                if (opticalDepth >= stopAt) return opticalDepth;
            }
        }
        return opticalDepth;
    }

    public static void Bullet(Actor source, FVector start, FVector end)
    {
        if (!IsEnabled(source.XLevel) || source.XLevel.Info.NetMode == ENetMode.NM_Client ||
            source.Role != ENetRole.ROLE_Authority || source.XLevel.World.IsReplaying) return;
        ResponsiveSmokeWorld world = For(source.XLevel); world.Tick();
        float now = world.CurrentTime;
        foreach (var pair in world._fields)
        {
            if (pair.Key.bDeleteMe || ResponsiveSmokeField.DistanceToSegment(pair.Value.Center, start, end) > ResponsiveSmokeField.QueryRadius + 18f) continue;
            pair.Value.AddCarve(new(++world._sequence, ResponsiveSmokeCarveKind.Bullet, start, end, 12f, 6f, now, .10f, .40f, null));
        }
    }

    public static void Blast(Actor source, FVector center, float damagingRange)
    {
        if (!IsEnabled(source.XLevel) || source.XLevel.Info.NetMode == ENetMode.NM_Client ||
            source.Role != ENetRole.ROLE_Authority || source.XLevel.World.IsReplaying || damagingRange <= 0f) return;
        ResponsiveSmokeWorld world = For(source.XLevel); world.Tick();
        float now = world.CurrentTime, radius = Math.Min(damagingRange, 300f);
        if (world._fields.Count == 0) return;
        if (!world.BlastVisibilityOrigin(source, center, out FVector visibilityOrigin)) return;
        foreach (var pair in world._fields)
        {
            ResponsiveSmokeField field = pair.Value;
            if (pair.Key.bDeleteMe || FVector.Dist(center, field.Center) > radius + ResponsiveSmokeField.QueryRadius + 25f) continue;
            var mask = new byte[ResponsiveSmokeField.CellCount];
            bool any = false;
            for (int i = 0; i < mask.Length; i++)
            {
                if (field.Cells[i] == 0 || FVector.Dist(field.CellCenter(i), center) > radius + 25f) continue;
                if (world.GeometryLineOpen(visibilityOrigin, field.CellCenter(i))) { mask[i] = 1; any = true; }
            }
            if (any) field.AddCarve(new(++world._sequence, ResponsiveSmokeCarveKind.Blast, center, center, radius, 25f, now, .60f, 1.40f, mask));
        }
    }

    private bool BlastVisibilityOrigin(Actor source, FVector center, out FVector origin)
    {
        origin = center;
        CellKey cell = Key(center);
        int budget = 343;
        if (CellFree(cell, ref budget)) return true;
        if (_collision is not null && _collision.PointCheck(out _, source, center, default, GadgetWorld.SightFlags)) return false;
        // A resting frag or wall mine sits in air inside a voxel touching its support. Project only to a nearby
        // free voxel reached by a real unobstructed trace. The blast shape retains its unmodified source center.
        float best = ResponsiveSmokeField.CellSize * ResponsiveSmokeField.CellSize * 9f;
        bool found = false;
        for (int z = -3; z <= 3; z++) for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++)
        {
            var key = new CellKey(cell.X + x, cell.Y + y, cell.Z + z);
            FVector candidate = new((key.X + .5f) * ResponsiveSmokeField.CellSize,
                (key.Y + .5f) * ResponsiveSmokeField.CellSize, (key.Z + .5f) * ResponsiveSmokeField.CellSize);
            float distance = FVector.DistSquared(candidate, center);
            // Strict comparison and fixed z/y/x iteration give deterministic distance/index ties.
            if (distance >= best || !CellFree(key, ref budget)) continue;
            if (_collision is not null && _collision.LineCheck(out _, source, candidate, center, default, GadgetWorld.SightFlags)) continue;
            best = distance; origin = candidate; found = true;
        }
        return found;
    }

    private static CellKey Key(FVector point) => new((int)MathF.Floor(point.X / ResponsiveSmokeField.CellSize),
        (int)MathF.Floor(point.Y / ResponsiveSmokeField.CellSize), (int)MathF.Floor(point.Z / ResponsiveSmokeField.CellSize));
    private bool SeedReachable(FVector source, FVector target) => _collision is null ||
        !_collision.PointCheck(out _, _level.Info, source, default, GadgetWorld.SightFlags) &&
        !_collision.LineCheck(out _, _level.Info, target, source, default, GadgetWorld.SightFlags);
    private bool CellFree(CellKey key, ref int budget)
    {
        return CellGeometry(key, ref budget).Free;
    }
    private GeometryCell CellGeometry(CellKey key, ref int budget)
    {
        if (_geometry.TryGetValue(key, out GeometryCell known)) return known;
        if (budget <= 0) return default;
        budget--; GeometryQueries++;
        float size = ResponsiveSmokeField.CellSize;
        FVector center = new((key.X + .5f) * size, (key.Y + .5f) * size, (key.Z + .5f) * size);
        // Overlap adjacent cell boxes slightly: a zero-thickness wall exactly on a grid plane must block both.
        var extent = new FVector(12.55f, 12.55f, 12.55f);
        GeometryCell result;
        if (_collision is CollisionWorld collision)
        {
            Span<CollisionBoundaryPlane> planes = stackalloc CollisionBoundaryPlane[ResponsiveSmokeField.MaxBoundaryPlanes];
            bool blocked = collision.BoxBoundaryPlanes(_level.Info, center, extent, GadgetWorld.SightFlags, planes, out int count);
            ResponsiveSmokePlane[]? boundary = null;
            if (count > 0)
            {
                boundary = new ResponsiveSmokePlane[count];
                for (int i = 0; i < count; i++) boundary[i] = new(planes[i].Normal, planes[i].Distance);
            }
            result = new(!blocked, boundary);
        }
        else result = new(_collision is null || !_collision.PointCheck(out _, _level.Info, center, extent, GadgetWorld.SightFlags), null);
        _geometry.Add(key, result); return result;
    }
    private void Classify(ResponsiveSmokeField field, int index, ref int budget)
    {
        FVector center = field.CellCenter(index);
        GeometryCell geometry = FVector.DistSquared(center, field.Center) > 472f * 472f ? default : CellGeometry(Key(center), ref budget);
        byte cell = geometry.Free ? (byte)1 : (byte)0;
        field.Boundaries.TryGetValue(index, out var priorPlanes);
        if (!ReferenceEquals(priorPlanes, geometry.Planes)) { field.SetBoundary(index, geometry.Planes); field.GeometryDirty = true; }
        if (field.Cells[index] != cell)
        {
            field.Cells[index] = cell; field.GeometryDirty = true;
            if (cell != 0) field.CellOpened(index); else field.InvalidateFront();
        }
    }
    private bool GeometryLineOpen(FVector start, FVector end)
    {
        FVector delta = end - start;
        CellKey cell = Key(start), last = Key(end);
        int sx = Math.Sign(delta.X), sy = Math.Sign(delta.Y), sz = Math.Sign(delta.Z);
        float stepX = sx == 0 ? float.PositiveInfinity : ResponsiveSmokeField.CellSize / MathF.Abs(delta.X);
        float stepY = sy == 0 ? float.PositiveInfinity : ResponsiveSmokeField.CellSize / MathF.Abs(delta.Y);
        float stepZ = sz == 0 ? float.PositiveInfinity : ResponsiveSmokeField.CellSize / MathF.Abs(delta.Z);
        static float Boundary(float origin, float direction, int coordinate, int sign) => sign == 0 ? float.PositiveInfinity :
            ((coordinate + (sign > 0 ? 1 : 0)) * ResponsiveSmokeField.CellSize - origin) / direction;
        float tx = Boundary(start.X, delta.X, cell.X, sx), ty = Boundary(start.Y, delta.Y, cell.Y, sy), tz = Boundary(start.Z, delta.Z, cell.Z, sz);
        int budget = 2048;
        for (int iteration = 0; iteration < 1024; iteration++)
        {
            if (!CellFree(cell, ref budget)) return false;
            if (cell == last) return true;
            float next = Math.Min(tx, Math.Min(ty, tz));
            if (next > 1f) return true;
            bool x = tx <= next + .000001f, y = ty <= next + .000001f, z = tz <= next + .000001f;
            // A ray exactly on an edge checks all touched cells instead of tunnelling diagonally through it.
            for (int ix = 0; ix <= (x ? 1 : 0); ix++) for (int iy = 0; iy <= (y ? 1 : 0); iy++) for (int iz = 0; iz <= (z ? 1 : 0); iz++)
                if (!CellFree(new CellKey(cell.X + ix * sx, cell.Y + iy * sy, cell.Z + iz * sz), ref budget)) return false;
            cell = new CellKey(cell.X + (x ? sx : 0), cell.Y + (y ? sy : 0), cell.Z + (z ? sz : 0));
            if (x) tx += stepX; if (y) ty += stepY; if (z) tz += stepZ;
        }
        return false;
    }

    private void RefreshGeometry()
    {
        if (_collision is not CollisionWorld collision) return;
        bool scanAll = _colliders.Count != collision.ColliderCount || _geometryRevision != collision.GeometryRevision;
        bool first = _colliders.Count == 0;
        int count = scanAll ? collision.ColliderCount : _movingColliders.Count;
        for (int n = 0; n < count; n++)
        {
            int i = scanAll ? n : _movingColliders[n];
            ColliderInfo current = collision.GetCollider(i);
            if ((current.IsDynamic || current.Actor is not null) && (first || scanAll && !_movingColliders.Contains(i))) _movingColliders.Add(i);
            Actor? owner = current.Actor;
            int state = owner is null ? 0 : (owner.bDeleteMe ? 1 : 0) | (owner.bCollideActors ? 2 : 0) |
                (owner.bBlockActors ? 4 : 0) | (owner.bBlockPlayers ? 8 : 0) | (owner.bBlockZeroExtentTraces ? 16 : 0) | (owner.bBlockNonZeroExtentTraces ? 32 : 0);
            bool known = _colliders.TryGetValue(i, out ColliderInfo old);
            if (known &&
                (old.Location != current.Location || old.Rotation != current.Rotation || old.Enabled != current.Enabled || old.Flags != current.Flags ||
                 old.Min != current.Min || old.Max != current.Max || !ReferenceEquals(old.Actor, current.Actor) || _blockingStates.GetValueOrDefault(i) != state))
            { Invalidate(old.Min, old.Max); Invalidate(current.Min, current.Max); }
            else if (!known && !first) Invalidate(current.Min, current.Max);
            _colliders[i] = current;
            _blockingStates[i] = state;
        }
        _geometryRevision = collision.GeometryRevision;
    }
    private void Invalidate(FVector min, FVector max)
    {
        const float pad = ResponsiveSmokeField.CellSize;
        // Blast visibility may have cached cells outside every cloud's box. Those cells must follow doors too.
        CellKey low = Key(min - new FVector(pad, pad, pad)), high = Key(max + new FVector(pad, pad, pad));
        _invalidGeometryKeys.Clear();
        foreach (CellKey key in _geometry.Keys)
            if (key.X >= low.X && key.X <= high.X && key.Y >= low.Y && key.Y <= high.Y && key.Z >= low.Z && key.Z <= high.Z)
                _invalidGeometryKeys.Add(key);
        foreach (CellKey key in _invalidGeometryKeys) _geometry.Remove(key);
        _invalidGeometryKeys.Clear();
        foreach (ResponsiveSmokeField field in _fields.Values)
        {
            FVector lower = field.Origin, upper = field.Origin + new FVector(625, 625, 625);
            if (upper.X < min.X - pad || lower.X > max.X + pad || upper.Y < min.Y - pad || lower.Y > max.Y + pad || upper.Z < min.Z - pad || lower.Z > max.Z + pad) continue;
            for (int i = 0; i < ResponsiveSmokeField.CellCount; i++)
            {
                FVector p = field.CellCenter(i);
                if (p.X < min.X - pad || p.X > max.X + pad || p.Y < min.Y - pad || p.Y > max.Y + pad || p.Z < min.Z - pad || p.Z > max.Z + pad) continue;
                _geometry.Remove(Key(p));
                // Unknown cells immediately stop density and propagation while their replacement is classified.
                field.Cells[i] = 0; field.InvalidateFront();
                field.SetBoundary(i, null);
                if (_pendingDirtyCells.Add((field, i))) _dirtyCells.Enqueue((field, i));
            }
        }
    }
}
