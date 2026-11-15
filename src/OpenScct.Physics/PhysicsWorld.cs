using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Traversal;
using OpenScct.Provenance;

namespace OpenScct.Physics;

/// <summary>
/// The physics of one map: its collision world, GE index and queries, and the driver the level calls.
/// <code>
/// SvMap map = SvMap.Load(path);
/// MapLoadResult loaded = new MapLoader().Load(world, map);
/// PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);   // sets loaded.Level.PhysicsDriver
/// </code>
/// </summary>
public sealed class PhysicsWorld
{
    /// <summary>The box half sizes the mover test tries, smallest first (2 is the reach within which two GEs connect).</summary>
    private static readonly float[] s_moverReach = [2f, 4f, 8f, 16f];

    private const ETraceFlags OtherGeometry = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Movers;

    /// <summary>The GEs <see cref="Bind"/> found lying on a mover, with the mover's map id (for logs and diagnostics).</summary>
    public IReadOnlyList<(GERecord GE, string Mover)> GEsOnMovers { get; private set; } = [];

    /// <summary>
    /// Ties every GE that lies on a mover to that mover, so it travels with it (<see cref="GEIndex.Attach"/>). A map
    /// in the original's format stores its GEs in world space with nothing that names an actor, so the tie is read
    /// from where the map's actors stand when it loads: a GE belongs to a mover when both its ends and its middle are
    /// within a small box of the mover's collision, and nothing else (the level, a static mesh, another mover) comes
    /// as near to any of the three. A ledge on a wall beside a door, or a floor edge level with a lift, stays where it
    /// is. GEs a map already attaches (<c>attachedTo</c>) are left alone.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: the original keeps every GE in the level's static partition (spec/native/physics-ge-queries.md rule 1) and none moves")]
    private void AttachGEsToMovers(MapLoadResult loaded)
    {
        var found = new List<(GERecord, string)>();
        if (GEs.Count != 0)
        {
            foreach ((Actor actor, string id) in loaded.PlacedActors())
            {
                if (!actor.bIsAMover || actor.bDeleteMe || !Collision.HasColliders(actor)) continue;
                for (int i = 0; i < GEs.Count; i++)
                {
                    GERecord ge = GEs.Stored(i);
                    if (ge.AttachedTo is not null || !LiesOn(ge, actor)) continue;
                    GEs.Attach(i, id, actor);
                    found.Add((ge, id));
                }
            }
        }

        GEsOnMovers = found;
    }

    private bool LiesOn(GERecord ge, Actor mover)
    {
        FVector a = ge.P0, b = ge.P1, mid = (a + b) * 0.5f;
        foreach (float reach in s_moverReach)
        {
            var box = new FVector(reach, reach, reach);
            if (!Collision.OverlapsActorColliders(mover, a, box) || !Collision.OverlapsActorColliders(mover, b, box) ||
                !Collision.OverlapsActorColliders(mover, mid, box)) continue;
            return !Collision.Overlap(out _, null, mover, -1, a, box, OtherGeometry, bruteForce: false) &&
                   !Collision.Overlap(out _, null, mover, -1, b, box, OtherGeometry, bruteForce: false) &&
                   !Collision.Overlap(out _, null, mover, -1, mid, box, OtherGeometry, bruteForce: false);
        }

        return false;
    }

    public PhysicsWorld(CollisionData collision, TraversalDoc? traversal = null)
        : this(CollisionWorld.Build(collision ?? throw new ArgumentNullException(nameof(collision))), traversal)
    {
    }

    private PhysicsWorld(CollisionWorld collision, TraversalDoc? traversal)
    {
        Collision = collision;
        GEs = traversal is null ? new GEIndex([]) : new GEIndex(traversal);
        Traversal = new GEQueries(GEs, Collision);
        Driver = new PhysicsDriver(Collision, GEs);
    }

    public CollisionWorld Collision { get; }

    public GEIndex GEs { get; }

    public GEQueries Traversal { get; }

    public PhysicsDriver Driver { get; }

    /// <summary>Builds the physics of <paramref name="map"/>, binds it to the loaded level and installs the driver.</summary>
    public static PhysicsWorld Attach(SvMap map, MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(loaded);
        // Shares the map's static collision with its other worlds (server, client, render zones, bot navigation).
        var physics = new PhysicsWorld(CollisionWorld.Build(map), map.Traversal);
        physics.Bind(loaded);
        return physics;
    }

    /// <summary>Binds to a loaded level and installs the driver as its physics.</summary>
    public void Bind(MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        Collision.BindLevel(loaded);
        GEs.BindActors(loaded.FindActor);
        AttachGEsToMovers(loaded);
        loaded.Level.PhysicsDriver = Driver;

        // Actor zones (Region) and the zone events, then kept up to date by moves and spawns (spec/devices/zone-info.md).
        loaded.Level.SetService<IZoneRegions>(Collision);
        ZoneRegions.InitAllZones(loaded.Level);
    }
}
