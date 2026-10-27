using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.AnimNotify_StopSound: stops a sound the animated actor plays (a looping notify sound).</summary>
public partial class UAnimNotify_StopSound
{
    /// <summary>Native: stops Sound on the owner, on every machine that updates the animation.</summary>
    [UcNative("UAnimNotify_StopSound::Notify")]
    [PortOf("def:Engine.AnimNotify_StopSound", Spec = "spec/devices/animated-mesh.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Native notify body not decoded; stops the sound on the owner as PawnAnimNotifies does")]
    public override void Notify(Actor Owner)
    {
        if (!Sound.IsNone)
        {
            Owner.StopSound(Sound);
        }
    }
}
