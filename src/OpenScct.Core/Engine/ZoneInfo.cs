using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.ZoneInfo: one BSP zone's settings (fog, ambient light, ambient sound, reverb, location name, sky). The
/// simulation keeps actor zones (<see cref="ZoneRegions"/>) and sends the zone events; fog, light, sound and reverb
/// are read by the render and audio layers. See spec/devices/zone-info.md.
/// </summary>
public partial class ZoneInfo : Info
{
    public Name ZoneTag;
    public string LocationName = string.Empty;

    /// <summary>Links the sky zone, then the parent's PreBeginPlay.</summary>
    [Uc("Engine.ZoneInfo.PreBeginPlay", Sha1 = "61797a917a85bef677466f82f2907bde991eabf7")]
    [PortOf("uc:Engine.ZoneInfo:PreBeginPlay", Spec = "spec/devices/zone-info.md#rules")]
    protected internal override void PreBeginPlay()
    {
        base.PreBeginPlay();
        LinkToSkybox();
    }

    /// <summary>
    /// SkyZone: the forced sky zone when set, else the last SkyZoneInfo in the level, then the last one allowed by the
    /// detail mode (low: neither high nor super-high detail; high: not super-high; super-high: any).
    /// </summary>
    [Uc("Engine.ZoneInfo.LinkToSkybox", Sha1 = "6dbf3050902a8a812c6d0ff41d49c0a1ca495ed8")]
    [PortOf("uc:Engine.ZoneInfo:LinkToSkybox", Spec = "spec/devices/zone-info.md#rules")]
    public void LinkToSkybox()
    {
        if (ForcedSkyZone is not null)
        {
            SkyZone = ForcedSkyZone;
            return;
        }

        foreach (SkyZoneInfo sky in XLevel.AllActors<SkyZoneInfo>())
        {
            SkyZone = sky;
        }

        EDetailMode mode = Level.DetailMode;
        if (mode == EDetailMode.DM_Low)
        {
            foreach (SkyZoneInfo sky in XLevel.AllActors<SkyZoneInfo>())
            {
                if (!sky.bHighDetail && !sky.bSuperHighDetail)
                {
                    SkyZone = sky;
                }
            }
        }
        else if (mode == EDetailMode.DM_High)
        {
            foreach (SkyZoneInfo sky in XLevel.AllActors<SkyZoneInfo>())
            {
                if (!sky.bSuperHighDetail)
                {
                    SkyZone = sky;
                }
            }
        }
        else if (mode == EDetailMode.DM_SuperHigh)
        {
            foreach (SkyZoneInfo sky in XLevel.AllActors<SkyZoneInfo>())
            {
                SkyZone = sky;
            }
        }
    }

    /// <summary>An actor entered this zone (sent by <see cref="ZoneRegions.SetActorZone"/>). Empty here.</summary>
    [Uc("Engine.ZoneInfo.ActorEntered", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.ZoneInfo:ActorEntered", Spec = "spec/devices/zone-info.md#rules")]
    protected internal virtual void ActorEntered(Actor Other)
    {
    }

    /// <summary>An actor is leaving this zone. Empty here.</summary>
    [Uc("Engine.ZoneInfo.ActorLeaving", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.ZoneInfo:ActorLeaving", Spec = "spec/devices/zone-info.md#rules")]
    protected internal virtual void ActorLeaving(Actor Other)
    {
    }

    /// <summary>Dispatches ActorEntered (no ZoneInfo class has states, so it goes straight to the class version).</summary>
    public void eventActorEntered(Actor Other) => ActorEntered(Other);

    /// <summary>Dispatches ActorLeaving.</summary>
    public void eventActorLeaving(Actor Other) => ActorLeaving(Other);

    /// <summary>
    /// The live actors of <typeparamref name="T"/> whose zone is this one, in actor list order (ZoneInfo.ZoneActors,
    /// native 308).
    /// </summary>
    [UcNative("AZoneInfo::execZoneActors")]
    [PortOf("nat:Engine.ZoneInfo.ZoneActors", Spec = "spec/devices/zone-info.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 iterator: every actor of the class whose Region.Zone is this zone; not decoded")]
    public IEnumerable<T> ZoneActors<T>()
        where T : Actor
    {
        foreach (T a in XLevel.AllActors<T>())
        {
            if (ReferenceEquals(a.Region.Zone, this))
            {
                yield return a;
            }
        }
    }
}
