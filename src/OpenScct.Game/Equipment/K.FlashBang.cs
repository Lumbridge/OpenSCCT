using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the spy's flash-bang (spec/equipment/flash-bang.md) and of the pawn-touch pop the smoke grenade shares.
public static partial class K
{
    /// <summary>The flash's sight line from the grenade to each merc's location: level, movers, world geometry, backdrop skip.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjFlashBang::Explode game 0x10ab8610: PUSH 0x80086 into SingleLineCheck")]
    public const int FlashSightFlags = 0x80086;

    /// <summary>The flash reaches a merc only when that line is clear to its end.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjFlashBang::Explode game 0x10ab8625: FCheckResult.Time == 1.0")]
    public const float FlashClearTime = 1f;

    /// <summary>Volume of the explosion sound every player hears at the grenade.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjFlashBang::Explode game 0x10ab8698: Parameters (1.0, 100.0, 1.0) for CheckHearSound")]
    public const float FlashSoundVolume = 1f;

    /// <summary>Radius of that sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjFlashBang::Explode game 0x10ab8688: radius 100.0")]
    public const float FlashSoundRadius = 100f;

    /// <summary>Volume of the whistle the flashed player hears (Snd_FlashBangSiffle, his own machine only).</summary>
    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect game 0x10ab0a57: audio PlaySound(pc, 1, Snd_FlashBangSiffle, pc.Location, 1.0, 100.0, 1.0)")]
    public const float FlashWhistleVolume = 1f;

    /// <summary>Radius of the whistle.</summary>
    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect game 0x10ab0a5c: radius 100.0")]
    public const float FlashWhistleRadius = 100f;

    /// <summary>A flash-bang that touches a pawn goes off this far back along its flight from where it is.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjFlashBang::ProcessTouch game 0x10ab883b: Location - 100.0 x SafeNormal(Velocity)")]
    public const float FlashTouchPullBack = 100f;
}
