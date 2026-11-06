using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;

namespace OpenScctGame.Render;

/// <summary>A published simulation boundary. Drawing never holds live emitters or mutable field arrays.</summary>
internal sealed record SmokeRenderFrame(bool Enabled, float CurrentTime, IReadOnlyList<SmokeRenderCloud> Clouds)
{
    public static SmokeRenderFrame Disabled { get; } = new(false, 0, Array.Empty<SmokeRenderCloud>());
    public static SmokeRenderFrame Empty { get; } = new(true, 0, Array.Empty<SmokeRenderCloud>());
}

internal sealed class SmokeRenderCloud
{
    private readonly byte[] _cells;
    private readonly ushort[] _activation;

    public SmokeRenderCloud(object identity, ResponsiveSmokeSnapshot snapshot, uint snapshotRevision)
    {
        Identity = identity;
        Revision = snapshot.Revision;
        SnapshotRevision = snapshotRevision;
        Origin = snapshot.Origin;
        Center = snapshot.Center;
        CreatedAt = snapshot.CreatedAt;
        // Capture owns these copies; only read-only access crosses into drawing.
        _cells = snapshot.Cells;
        _activation = snapshot.ActivationTicks;
        var carves = new SmokeRenderCarve[snapshot.Carves.Length];
        for (int i = 0; i < carves.Length; i++) carves[i] = new(snapshot.Carves[i]);
        Carves = Array.AsReadOnly(carves);
        var boundaries = new SmokeRenderBoundary[snapshot.Boundaries?.Length ?? 0];
        for (int i = 0; i < boundaries.Length; i++) boundaries[i] = new(snapshot.Boundaries![i]);
        Boundaries = Array.AsReadOnly(boundaries);
    }

    public object Identity { get; }
    public uint Revision { get; }
    public uint SnapshotRevision { get; }
    public (uint Revision, uint SnapshotRevision) ContentStamp => (Revision, SnapshotRevision);
    public FVector Origin { get; }
    public FVector Center { get; }
    public float CreatedAt { get; }
    public ReadOnlySpan<byte> Cells => _cells;
    public ReadOnlySpan<ushort> ActivationTicks => _activation;
    public ReadOnlyCollection<SmokeRenderCarve> Carves { get; }
    public ReadOnlyCollection<SmokeRenderBoundary> Boundaries { get; }
}

internal sealed class SmokeRenderBoundary(ResponsiveSmokeBoundaryCell boundary)
{
    private readonly ResponsiveSmokePlane[] _planes = boundary.Planes;
    public ushort Cell { get; } = boundary.Cell;
    public ReadOnlySpan<ResponsiveSmokePlane> Planes => _planes;
}

internal sealed class SmokeRenderCarve
{
    public SmokeRenderCarve(ResponsiveSmokeCarve carve)
    {
        Sequence = carve.Sequence; Kind = carve.Kind; Start = carve.Start; End = carve.End;
        Radius = carve.Radius; Feather = carve.Feather; CreatedAt = carve.CreatedAt; Hold = carve.Hold; Refill = carve.Refill;
        AffectedCells = carve.AffectedCells is { } mask ? new ReadOnlyMemory<byte>(mask) : null;
    }
    public uint Sequence { get; }
    public ResponsiveSmokeCarveKind Kind { get; }
    public FVector Start { get; }
    public FVector End { get; }
    public float Radius { get; }
    public float Feather { get; }
    public float CreatedAt { get; }
    public float Hold { get; }
    public float Refill { get; }
    public ReadOnlyMemory<byte>? AffectedCells { get; }
}

/// <summary>Called at the client tick boundary (or explicitly by render fixtures). Copies changed clouds only.</summary>
internal static class SmokeRenderCache
{
    private sealed class Cache
    {
        public readonly Dictionary<ResponsiveSmokeField, SmokeRenderCloud> Clouds = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<ResponsiveSmokeField> Live = new(ReferenceEqualityComparer.Instance);
        public readonly List<ResponsiveSmokeField> Gone = [];
    }
    private static readonly ConditionalWeakTable<Level, Cache> Levels = new();

    public static SmokeRenderFrame Capture(Level level)
    {
        if (!ResponsiveSmokeWorld.IsEnabled(level))
        {
            Levels.Remove(level);
            return SmokeRenderFrame.Disabled;
        }
        if (level.GetService<ResponsiveSmokeWorld>() is not { } world || world.Fields.Count == 0)
        {
            Levels.Remove(level);
            return SmokeRenderFrame.Empty;
        }
        Cache cache = Levels.GetValue(level, static _ => new());
        cache.Live.Clear();
        float time = world.CurrentTime;
        var clouds = new List<SmokeRenderCloud>(world.Fields.Count);
        foreach (var pair in world.Fields)
        {
            if (pair.Key.bDeleteMe) continue;
            ResponsiveSmokeField field = pair.Value;
            cache.Live.Add(field);
            if (!cache.Clouds.TryGetValue(field, out SmokeRenderCloud? cloud) ||
                cloud.ContentStamp != (field.Revision, field.SnapshotRevision))
            {
                cloud = new SmokeRenderCloud(cloud?.Identity ?? new object(), field.Capture(time), field.SnapshotRevision);
                cache.Clouds[field] = cloud;
            }
            clouds.Add(cloud);
        }
        foreach (ResponsiveSmokeField field in cache.Clouds.Keys)
            if (!cache.Live.Contains(field)) cache.Gone.Add(field);
        foreach (ResponsiveSmokeField field in cache.Gone) cache.Clouds.Remove(field);
        cache.Gone.Clear();
        return new(true, time, clouds.AsReadOnly());
    }
}
