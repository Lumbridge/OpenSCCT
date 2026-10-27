using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the merc taser and its beam (spec/equipment/tazer.md).
public static partial class K
{
    // ---- UseBulletSet (native 1301, game 0x10aa0b50) ---------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0b80: every player hears FireSound at the merc, volume 1.5")]
    public const float TazerSoundVolume = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0b8c: sound radius 150")]
    public const float TazerSoundRadius = 150f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0c51: range and cone measured from 50 uu behind the FX bone")]
    public const float TazerAimPullBack = 50f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0cc5: the sight line starts 40 uu behind the FX bone")]
    public const float TazerTracePullBack = 40f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0c7f and STazerEmitter Tick 0x10a94b6b: 15 uu box traces, beam width 15")]
    public const float TazerTraceExtent = 15f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0eb6 and STazerEmitter Tick 0x10a94b35: trace flags 0x86 (movers, level, world geometry)")]
    public const int TazerTraceFlags = 0x86;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0d6e: a pawn lying on the ground is aimed at Location.Z - 0.95 CollisionHeight")]
    public const float TazerLyingAimHeight = 0.95f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0e87: then raised by 20 for the sight line and the hit")]
    public const float TazerLyingAimRaise = 20f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0d87: a standing pawn is aimed 25 uu above its Location")]
    public const float TazerStandingAimRaise = 25f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0e50: cone half angle cos((range - dist) x 0.01 x 0.345 + 0.35)")]
    public const float TazerConeScale = 0.01f * 0.345f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa0e66: the cone's half angle at full range, 0.35 rad")]
    public const float TazerConeMin = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa1001: radians to rotator units, 10430.21875, then truncated (_ftol)")]
    public const float TazerRadToRotator = 10430.21875f;

    [Provenance(ProvenanceKind.Decoded, "execUseBulletSet 0x10aa11d0: ForceRotateTo step 3")]
    public const int TazerForceRotateStep = 3;

    // ---- STazerEmitter Tick (game 0x10a94920) ----------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "STazerEmitter Tick 0x10a94b1c: the miss trace starts 30 uu behind the FX bone")]
    public const float TazerBeamPullBack = 30f;
}
