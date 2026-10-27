using System;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Physics.Traversal;

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
    public PhysicsWorld(CollisionData collision, TraversalDoc? traversal = null)
    {
        ArgumentNullException.ThrowIfNull(collision);
        Collision = CollisionWorld.Build(collision);
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
        var physics = new PhysicsWorld(map.Collision, map.Traversal);
        physics.Bind(loaded);
        return physics;
    }

    /// <summary>Binds to a loaded level and installs the driver as its physics.</summary>
    public void Bind(MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        Collision.BindLevel(loaded);
        loaded.Level.PhysicsDriver = Driver;

        // Actor zones (Region) and the zone events, then kept up to date by moves and spawns (spec/devices/zone-info.md).
        loaded.Level.SetService<IZoneRegions>(Collision);
        ZoneRegions.InitAllZones(loaded.Level);
    }
}
