using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Point-to-zone lookup of a level's world BSP (UModel::PointRegion). The physics layer installs its collision world
/// as this level service; without it actors keep the zone they have.
/// </summary>
public interface IZoneRegions
{
    /// <summary>The zone of <paramref name="Location"/>: its ZoneInfo (the LevelInfo for zone 0) and zone number.</summary>
    FPointRegion PointRegion(FVector Location);
}

/// <summary>
/// Actor zone tracking (ULevel::SetActorZone): keeps <see cref="Actor.Region"/> up to date as actors spawn and move and
/// sends the zone events (old zone ActorLeaving, actor ZoneChange, new zone ActorEntered). See
/// spec/devices/zone-info.md rules 1 to 4.
/// </summary>
public static class ZoneRegions
{
    /// <summary>
    /// Sets <paramref name="Actor"/>'s zone from its location. A changed zone sends the old zone's ActorLeaving, the
    /// actor's ZoneChange and the new zone's ActorEntered unless <paramref name="bTest"/>.
    /// <paramref name="bForceRefresh"/> first leaves the current zone and puts the actor in the LevelInfo's zone (spawn
    /// and map start), so a real zone is entered with its events. The LevelInfo is always in its own zone.
    /// </summary>
    [UcNative("ULevel::SetActorZone")]
    [PortOf("eng:ULevel::SetActorZone", Spec = "spec/devices/zone-info.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 SetActorZone without the physics volume part (SCCT has no PhysicsVolume); not decoded in the game exe")]
    public static void SetActorZone(Actor Actor, bool bTest = false, bool bForceRefresh = false)
    {
        System.ArgumentNullException.ThrowIfNull(Actor);
        if (Actor.bDeleteMe || Actor.XLevel is not { } level)
        {
            return;
        }

        LevelInfo info = level.Info;
        if (ReferenceEquals(Actor, info))
        {
            Actor.Region = new FPointRegion { Zone = info, iLeaf = -1, ZoneNumber = 0 };
            return;
        }

        if (level.GetService<IZoneRegions>() is not { } regions)
        {
            return;
        }

        if (bForceRefresh)
        {
            if (Actor.Region.Zone is { } previous && !bTest)
            {
                previous.eventActorLeaving(Actor);
            }

            Actor.Region = new FPointRegion { Zone = info, iLeaf = -1, ZoneNumber = 0 };
        }

        FPointRegion next = regions.PointRegion(Actor.Location);
        next.Zone ??= info;
        if (!ReferenceEquals(next.Zone, Actor.Region.Zone))
        {
            ZoneInfo? old = Actor.Region.Zone;
            if (!bTest)
            {
                old?.eventActorLeaving(Actor);
                Actor.eventZoneChange(next.Zone);
            }

            Actor.Region = next;
            if (!bTest && !Actor.bDeleteMe)
            {
                next.Zone.eventActorEntered(Actor);
            }
        }
        else
        {
            Actor.Region = next;
        }
    }

    /// <summary>Puts every live actor of <paramref name="level"/> in its zone with the zone events (map start).</summary>
    public static void InitAllZones(Level level)
    {
        System.ArgumentNullException.ThrowIfNull(level);
        IReadOnlyList<Actor> actors = level.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (!a.bDeleteMe)
            {
                SetActorZone(a, bTest: false, bForceRefresh: true);
            }
        }
    }
}
