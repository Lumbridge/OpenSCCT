using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Physics;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Devices;

/// <summary>
/// Which gameplay-object mesh states a collider belongs to (the SVMap 1.1 <c>CSRC.variants</c> mask: bit 0 the
/// initial StaticMesh, bit 1 TriggeredMesh, bit 2 + i DamagedMeshes[i]). The map binding provides it once the
/// collision reader keeps that chunk.
/// </summary>
public interface IColliderVariants
{
    /// <summary>The variants mask of collider <paramref name="ColliderIndex"/>; false when the map has none for it.</summary>
    bool TryGetVariants(int ColliderIndex, out ushort Variants);
}

/// <summary>
/// The engine natives the device ports need (<see cref="IActorNatives"/>) over the OpenScct.Physics driver and its
/// collision world: collision flag and mesh changes, teleports, physics mode changes, FastTrace and the moving-brush
/// encroachment. Without a driver (synthetic levels with no collision) the natives only change the actor's fields.
/// <code>
/// DeviceNatives.Install(level, physicsWorld.Driver, animations);   // after PhysicsWorld.Bind
/// </code>
/// </summary>
public sealed class DeviceNatives : IActorNatives
{
    private readonly List<List<Actor>> _listPool = new();
    private readonly List<int> _colliders = new(8);
    private int _listDepth;

    public DeviceNatives(PhysicsDriver? driver = null, IColliderVariants? variants = null)
    {
        Driver = driver;
        Variants = variants;
    }

    /// <summary>The physics driver, or null when the level has no collision world.</summary>
    public PhysicsDriver? Driver { get; }

    /// <summary>Collider mesh-state masks, or null (then every collider of a mesh actor stays on while it has a mesh).</summary>
    public IColliderVariants? Variants { get; set; }

    /// <summary>
    /// Installs the device services on <paramref name="level"/>: these natives, the class index for spawning by
    /// class, and optionally the animation data and the hooks into other areas.
    /// </summary>
    public static DeviceNatives Install(Level level, PhysicsDriver? driver = null, IMeshAnimations? animations = null,
        IDeviceHooks? hooks = null, IColliderVariants? variants = null)
    {
        var natives = new DeviceNatives(driver, variants);
        level.SetService<IActorNatives>(natives);
        level.SetService(DeviceClasses.Index);
        if (animations is not null)
        {
            level.SetService(animations);
        }

        if (hooks is not null)
        {
            level.SetService(hooks);
        }

        return natives;
    }

    public void SetCollision(Actor Actor, bool NewColActors, bool NewBlockActors, bool NewBlockPlayers)
    {
        if (Driver is not null)
        {
            Driver.SetCollision(Actor, NewColActors, NewBlockActors, NewBlockPlayers);
            return;
        }

        Actor.bCollideActors = NewColActors;
        Actor.bBlockActors = NewBlockActors;
        Actor.bBlockPlayers = NewBlockPlayers;
    }

    /// <summary>
    /// The mesh changed: the actor's colliders that belong to the new mesh state are switched on and the others
    /// off. A None mesh has no collision. Without variant data every collider follows "has a mesh".
    /// </summary>
    [UcNative("AActor::execSetStaticMesh", Exe = 0x10a24ea0)]
    [PortOf("nat:Engine.Actor.SetStaticMesh", Spec = "spec/devices/gameplay-object.md#trigger-and-untrigger")]
    [Provenance(ProvenanceKind.Estimated, "Collision follows the swapped mesh (RE+ measured); variant selection from SVMap 1.1 CSRC")]
    public void SetStaticMesh(Actor Actor, AssetRef NewStaticMesh)
    {
        if (Driver is null)
        {
            return;
        }

        CollisionWorld cw = Driver.Collision;
        _colliders.Clear();
        if (cw.GetActorColliders(Actor, _colliders) == 0)
        {
            return;
        }

        int bit = NewStaticMesh.IsNone ? -1 : VariantBit(Actor, NewStaticMesh);
        foreach (int ci in _colliders)
        {
            bool on;
            if (bit < 0)
            {
                on = false;
            }
            else if (Variants is not null && Variants.TryGetVariants(ci, out ushort mask) && mask != 0)
            {
                on = (mask & (1 << bit)) != 0;
            }
            else
            {
                on = true;
            }

            cw.SetColliderEnabled(ci, on);
        }

        cw.NotifyCollisionChanged();
    }

    /// <summary>The CSRC variant bit of a mesh for a gameplay object; 0 (the initial mesh) for other actors.</summary>
    private static int VariantBit(Actor actor, AssetRef mesh)
    {
        if (actor is not SGamePlayObject gpo || mesh == gpo.InitialStaticMesh)
        {
            return 0;
        }

        if (mesh == gpo.TriggeredMesh)
        {
            return 1;
        }

        for (int i = 0; i < gpo.DamagedMeshes.Count && i < 14; i++)
        {
            if (gpo.DamagedMeshes[i].StaticMesh == mesh)
            {
                return 2 + i;
            }
        }

        return 0;
    }

    public void SetPhysics(Actor Actor, EPhysics NewPhysics)
    {
        if (Driver is not null)
        {
            Driver.setPhysics(Actor, NewPhysics);
        }
        else
        {
            Actor.Physics = NewPhysics;
        }
    }

    public bool SetLocation(Actor Actor, FVector NewLocation)
    {
        if (Driver is not null)
        {
            return Driver.SetLocation(Actor, NewLocation);
        }

        Actor.Location = NewLocation;
        return true;
    }

    public bool FastTrace(FVector End, FVector Start) => Driver?.Collision.FastTrace(End, Start) ?? true;

    /// <summary>
    /// Moves an encroacher with mesh or brush colliders: each colliding cylinder actor the new pose would overlap
    /// (and that does not ride on it) is first pushed by the step; one still overlapping goes to the encroacher's
    /// EncroachingOn (true refuses the step and restores the old pose), else gets EncroachedBy. Riders follow
    /// accepted steps. An encroacher without colliders moves freely.
    /// </summary>
    [UcNative("ULevel::CheckEncroachment")]
    [PortOf("nat:ULevel::CheckEncroachment", Spec = "spec/devices/mover.md#encroachment")]
    [Provenance(ProvenanceKind.Estimated, "Mover spec rule 20 (push first, then EncroachingOn), UE2 lineage")]
    public bool MoveEncroacher(Actor Actor, FVector Delta, FRotator NewRotation)
    {
        if (Delta.IsZero() && NewRotation == Actor.Rotation)
        {
            return true;
        }

        FVector oldLocation = Actor.Location;
        FRotator oldRotation = Actor.Rotation;
        Actor.Location = oldLocation + Delta;
        Actor.Rotation = NewRotation;
        if (Driver is not null && Actor.bCollideActors && Driver.Collision.HasColliders(Actor))
        {
            if (!Encroach(Driver, Actor, Delta))
            {
                if (!Actor.bDeleteMe)
                {
                    Actor.Location = oldLocation;
                    Actor.Rotation = oldRotation;
                }

                return false;
            }

            if (Actor.bDeleteMe)
            {
                return false;
            }
        }

        // The touch pass of CheckEncroachment: a mover running into a trigger (ClassProximityType Mover) touches it.
        if (Driver is not null && Actor.bCollideActors)
        {
            Driver.EncroachTouches(Actor);
            if (Actor.bDeleteMe)
            {
                return false;
            }
        }

        MoveRiders(Actor, oldLocation, oldRotation);
        return true;
    }

    private bool Encroach(PhysicsDriver driver, Actor encroacher, FVector delta)
    {
        CollisionWorld cw = driver.Collision;
        List<Actor> candidates = RentList();
        try
        {
            IReadOnlyList<Actor> actors = encroacher.XLevel.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (!a.bDeleteMe && a.bCollideActors && !ReferenceEquals(a, encroacher) && !cw.HasColliders(a))
                {
                    candidates.Add(a);
                }
            }

            return EncroachCandidates(driver, encroacher, delta, candidates);
        }
        finally
        {
            ReturnList(candidates);
        }
    }

    private static bool EncroachCandidates(PhysicsDriver driver, Actor encroacher, FVector delta, List<Actor> candidates)
    {
        CollisionWorld cw = driver.Collision;
        for (int i = 0; i < candidates.Count; i++)
        {
            Actor other = candidates[i];
            if (other.bDeleteMe || IsBasedOn(other, encroacher))
            {
                continue;
            }

            cw.GetCylinder(other, out float r, out float h, out bool collide, out bool otherBlockActors, out bool otherBlockPlayers);
            bool encroacherBlocks = other.bIsAPawn ? encroacher.bBlockPlayers : encroacher.bBlockActors;
            if (!collide || !encroacherBlocks || !(otherBlockActors || otherBlockPlayers))
            {
                continue;
            }

            var extent = new FVector(r, r, h);
            if (!cw.OverlapsActorColliders(encroacher, other.Location, extent))
            {
                continue;
            }

            if (other.Physics != EPhysics.PHYS_None && !delta.IsZero())
            {
                // Push one skin further than the step so the pushed actor ends clear of the encroacher.
                driver.MoveActor(other, delta + delta.SafeNormal() * OpenScct.Physics.K.TraceSkin, other.Rotation, out _, Ignore: encroacher);
                if (other.bDeleteMe || !cw.OverlapsActorColliders(encroacher, other.Location, extent))
                {
                    continue;
                }
            }

            // Only movers decide; any other encroacher is refused, as the physics driver does without a handler.
            bool refuse = encroacher is not Mover mover || mover.eventEncroachingOn(other);
            if (encroacher.bDeleteMe)
            {
                return true;
            }

            if (refuse)
            {
                return false;
            }

            if (!other.bDeleteMe)
            {
                other.EncroachedBy(encroacher);
            }
        }

        return true;
    }

    private static bool IsBasedOn(Actor actor, Actor baseActor)
    {
        for (Actor? b = actor.Base; b is not null; b = b.Base)
        {
            if (ReferenceEquals(b, baseActor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves the actors based on <paramref name="baseActor"/> by its move, turning their yaw with it.</summary>
    private void MoveRiders(Actor baseActor, FVector oldLocation, FRotator oldRotation)
    {
        if (baseActor.Attached.Count == 0)
        {
            return;
        }

        List<Actor> riders = RentList();
        try
        {
            riders.AddRange(baseActor.Attached);
            MoveRiderList(baseActor, riders, oldLocation, oldRotation);
        }
        finally
        {
            ReturnList(riders);
        }
    }

    private void MoveRiderList(Actor baseActor, List<Actor> riders, FVector oldLocation, FRotator oldRotation)
    {
        FVector delta = baseActor.Location - oldLocation;
        bool rotated = baseActor.Rotation != oldRotation;
        FCoords oldAxes = rotated ? FCoords.FromRotator(oldRotation) : default;
        FCoords newAxes = rotated ? FCoords.FromRotator(baseActor.Rotation) : default;
        for (int i = 0; i < riders.Count; i++)
        {
            Actor rider = riders[i];
            if (rider.bDeleteMe || !ReferenceEquals(rider.Base, baseActor))
            {
                continue;
            }

            FVector move = delta;
            FRotator rotation = rider.Rotation;
            if (rotated)
            {
                FVector rel = rider.Location - oldLocation;
                var local = new FVector(rel | oldAxes.XAxis, rel | oldAxes.YAxis, rel | oldAxes.ZAxis);
                FVector moved = newAxes.XAxis * local.X + newAxes.YAxis * local.Y + newAxes.ZAxis * local.Z;
                move = baseActor.Location + moved - rider.Location;
                rotation.Yaw += baseActor.Rotation.Yaw - oldRotation.Yaw;
            }

            if (Driver is not null)
            {
                Driver.MoveActor(rider, move, rotation, out _, Ignore: baseActor);
            }
            else
            {
                rider.Location += move;
                rider.Rotation = rotation;
            }
        }
    }

    // Scratch lists by nesting depth: a push or an encroach handler can move another encroacher.
    private List<Actor> RentList()
    {
        if (_listDepth == _listPool.Count)
        {
            _listPool.Add(new List<Actor>(32));
        }

        return _listPool[_listDepth++];
    }

    private void ReturnList(List<Actor> list)
    {
        list.Clear();
        _listDepth--;
    }
}
