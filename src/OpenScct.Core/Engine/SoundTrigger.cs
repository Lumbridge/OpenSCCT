using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.SoundTrigger: plays its Sound at itself every time it is triggered (spec/devices/sound-music.md).</summary>
public partial class SoundTrigger
{
    /// <summary>Plays the sound with this trigger's own volume (255 = 1), radius and pitch (64 = 1); plays overlap.</summary>
    [Uc("Engine.SoundTrigger.Trigger", Sha1 = "af3d2eb61604831b22f0c1efd8d8b16456d2b893")]
    [PortOf("uc:Engine.SoundTrigger:Trigger", Spec = "spec/devices/sound-music.md#rules")]
    protected internal override void Trigger(Actor? Other, Pawn? EventInstigator) =>
        PlaySound(Sound, ESoundSlot.SLOT_None, SoundVolume / 255f, false, SoundRadius, SoundPitch / 64f);
}
