using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.AnimNotify_Sound: plays a sound at the animated actor (spec/devices/animated-mesh.md rule 12).</summary>
public partial class UAnimNotify_Sound
{
    /// <summary>Native: plays Sound at the owner (at BoneName when set) with Volume and Radius, on every machine.</summary>
    [UcNative("UAnimNotify_Sound::Notify")]
    [PortOf("def:Engine.AnimNotify_Sound", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Native notify body not decoded; volume and radius as the notify stores them")]
    public override void Notify(Actor Owner) =>
        Owner.PlaySound(Sound, ESoundSlot.SLOT_None, Volume, false, Radius, 1f, true, BoneName);
}
