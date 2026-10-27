using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The merc's sound arrows: where a noise came from, as SHud keeps it for the reticle driver.
public partial class SHud
{
    private static readonly AssetRef EchoIndicatorSound = new("Armes_impacts.Gadgets.echo_indic1");

    /// <summary>Beyond this many uu from the pawn a noise is not heard on the HUD.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/hud/merc-reticle.md rule 8")]
    public const float HotSoundRange = 1600f;

    /// <summary>Within this many uu of the pawn a noise gives no arrow.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/hud/merc-reticle.md rule 8")]
    public const float HotSoundMinDistance = 50f;

    /// <summary>A noise this far above or below the pawn lights the top or bottom arrow.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/hud/merc-reticle.md rule 9")]
    public const float HotSoundHeight = 240f;

    /// <summary>How long an arrow stays lit after a noise, in seconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/hud/merc-reticle.md rule 10")]
    public const float HotSoundArrowLife = 1f;

    /// <summary>
    /// A noise of <paramref name="NoiseLoudness"/> at <paramref name="NoisyActor"/>: lights the arrows toward it
    /// (above or below, and one of forward, behind, right or left) for a second, with the echo cue when an arrow was
    /// off for a second.
    /// </summary>
    [Uc("SBase.SHud.NotifyHotSound", Sha1 = "eb69306f83298c86954d0c99eba0e2a8cc6a9c1c")]
    [PortOf("uc:SBase.SHud:NotifyHotSound", Spec = "spec/hud/merc-reticle.md#rules")]
    public void NotifyHotSound(Actor? NoisyActor, float NoiseLoudness)
    {
        if (NoisyActor is null || PlayerOwner?.Pawn is not { } pawn)
        {
            return;
        }

        float distance = (pawn.Location - NoisyActor.Location).Size();
        float loudness = 0f;
        if (distance > HotSoundMinDistance)
        {
            loudness = System.Math.Clamp(NoiseLoudness * (HotSoundRange - distance) / HotSoundRange, 0f, 1f);
        }

        if (loudness <= 0.01f)
        {
            return;
        }

        float dz = NoisyActor.Location.Z - pawn.Location.Z;
        if (dz > HotSoundHeight)
        {
            Light(ref TopArrowTime, ref TopArrowInt, loudness);
        }
        else if (dz < -HotSoundHeight)
        {
            Light(ref BottomArrowTime, ref BottomArrowInt, loudness);
        }

        int yaw = ((NoisyActor.Location - pawn.Location).Rotation() - pawn.Rotation).Normalize().Yaw;
        if (System.Math.Abs(yaw) < 8192)
        {
            Light(ref ForwardArrowTime, ref ForwardArrowInt, loudness);
        }
        else if (yaw > 24576 || yaw < -24576)
        {
            Light(ref BackwardArrowTime, ref BackwardArrowInt, loudness);
        }
        else if (yaw >= 8192 && yaw <= 24576)
        {
            Light(ref RightArrowTime, ref RightArrowInt, loudness);
        }
        else if (yaw <= -8192 && yaw >= -24576)
        {
            Light(ref LeftArrowTime, ref LeftArrowInt, loudness);
        }
    }

    private void Light(ref float time, ref float intensity, float loudness)
    {
        float now = Level.TimeSeconds;
        if (now >= time || loudness > intensity)
        {
            if (now >= time + HotSoundArrowLife)
            {
                PlayerOwner?.PlaySound(EchoIndicatorSound, ESoundSlot.SLOT_Misc, 0.3f, Radius: 1f, Pitch: 1f);
            }

            time = now + HotSoundArrowLife;
            intensity = loudness;
        }
    }
}
