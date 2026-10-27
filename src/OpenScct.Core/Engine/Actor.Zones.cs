using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public abstract partial class Actor
{
    /// <summary>The actor moved into another zone (sent by <see cref="ZoneRegions.SetActorZone"/>). Empty here.</summary>
    [Uc("Engine.Actor.ZoneChange", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Actor:ZoneChange", Spec = "spec/devices/zone-info.md#rules")]
    protected internal virtual void ZoneChange(ZoneInfo NewZone)
    {
    }

    /// <summary>
    /// Dispatches ZoneChange. No SCCT state overrides it, so it goes to the class version directly.
    /// </summary>
    public void eventZoneChange(ZoneInfo NewZone) => ZoneChange(NewZone);
}
