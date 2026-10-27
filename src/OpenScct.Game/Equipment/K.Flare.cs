using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the merc's flare (spec/equipment/flare.md).
public static partial class K
{
    /// <summary>Radius of the flare's launch sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "SProjFlare.PostBeginPlay: PlaySound(SoundLaunch, , 1.0, , 100.0)")]
    public const float FlareLaunchSoundRadius = 100f;

    /// <summary>Radius of the sound the flare makes when it catches (SLOT_Misc, attenuated).</summary>
    [Provenance(ProvenanceKind.Decoded, "SProjFlare.Explode: PlaySound(SoundExplode, SLOT_Misc, 1.0, , 500.0, , true)")]
    public const float FlareExplodeSoundRadius = 500f;

    /// <summary>The burning flare and its burn decal sit this far out of the surface along the hit normal.</summary>
    [Provenance(ProvenanceKind.Decoded, "SProjFlare.Explode: HitLocation + Normal(HitNormal) * 15.0 for SFlareEmitter and SFlareBurnProjector")]
    public const float FlareStandOff = 15f;

    /// <summary>The burn decal stays this long after its projector is gone.</summary>
    [Provenance(ProvenanceKind.Decoded, "SFlareBurnProjector.PostBeginPlay: AbandonProjector(30.0)")]
    public const float FlareBurnLifetime = 30f;

    /// <summary>The noise a hit wall makes.</summary>
    [Provenance(ProvenanceKind.Decoded, "SProjFlare HitWall game 0x10ae8cd5: MakeNoise(1.0) (event name 0x3ca)")]
    public const float FlareHitNoise = 1f;

    /// <summary>For this long after it catches the flare burns at full strength (brightness 100 to 115).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a86435: age < 15.0 (double 0x10c10f80)")]
    public const float FlareFullBurn = 15f;

    /// <summary>The flicker phase: int(age x 1234.567), masked to the flicker range.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a86442: FMUL 1234.567 (0x10c11038), then _ftol")]
    public const float FlareFlickerRate = 1234.567f;

    /// <summary>The flicker mask at full strength (0 to 15 added).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a8644d: AND EAX, 0xf")]
    public const int FlareFullFlicker = 0xf;

    /// <summary>The flicker mask while it dies down (0 to 7 added).</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a8648b: AND EAX, 0x7")]
    public const int FlareFadeFlicker = 0x7;

    /// <summary>The brightness at full strength, before the flicker.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a86450 (ADD 0x64) and 0x10a86478 (100.0 at 0x10c109e0)")]
    public const float FlareBrightness = 100f;

    /// <summary>The brightness lost each second after <see cref="FlareFullBurn"/>.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASFlareEmitter::Tick game 0x10a86472: FMUL 20.0 (0x10c10bac)")]
    public const float FlareFadeRate = 20f;
}
