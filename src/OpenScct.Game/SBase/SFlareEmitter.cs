using OpenScct.Core;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlareEmitter: the burning flare left where an SProjFlare caught (spec/equipment/flare.md rules 6 and 7). The
/// server spawns it and it replicates; on every machine it plays its loop and lights its zone (a steady dynamic point
/// light, radius 25) for its 20 s life, flickering at full strength for 15 s, then dying down.
/// </summary>
public partial class SFlareEmitter
{
    /// <summary>The burn loop, and the time the flare caught (on each machine, as it spawns there).</summary>
    [Uc("SBase.SFlareEmitter.PostBeginPlay", Sha1 = "d4ceadc6be19c3049e0df8cf5d84489e2ce33950")]
    [PortOf("uc:SBase.SFlareEmitter:PostBeginPlay", Spec = "spec/equipment/flare.md#rules")]
    protected override void PostBeginPlay()
    {
        AmbientSound = SoundEffect;
        fDateEmission = Level.TimeSeconds;
    }

    /// <summary>
    /// The native tick (game 0x10a863f0), after the emitter's own: once the clock has passed fDateEmission, the light's
    /// brightness flickers between 100 and 115 for the first 15 s, then falls 20 a second from 100 with a flicker of up
    /// to 7, never below 0.
    /// </summary>
    [UcNative("ASFlareEmitter::Tick", Exe = 0x10a863f0)]
    [Provenance(ProvenanceKind.Decoded, "0x10a863f0: AEmitter::Tick; TimeSeconds > fDateEmission (+0x368); age < 15 -> LightBrightness (+0xf8) = (int(age * 1234.567) & 15) + 100; else max(0, 100 - (age - 15) * 20 + (int(age * 1234.567) & 7))")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || !(Level.TimeSeconds > fDateEmission))
        {
            return;
        }

        LightBrightness = BrightnessAt(Level.TimeSeconds - fDateEmission);
    }

    /// <summary>The light's brightness <paramref name="age"/> seconds after the flare caught.</summary>
    public static float BrightnessAt(float age)
    {
        int flicker = SvmMath.Trunc(age * K.FlareFlickerRate);
        if (age < K.FlareFullBurn)
        {
            return (flicker & K.FlareFullFlicker) + K.FlareBrightness;
        }

        float b = K.FlareBrightness - ((age - K.FlareFullBurn) * K.FlareFadeRate) + (flicker & K.FlareFadeFlicker);
        return b > 0f ? b : 0f;
    }
}
