using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the presence detector (spec/equipment/presence-detector.md).
public static partial class K
{
    [Provenance(ProvenanceKind.Decoded, "SPresDetector scan 0x10abd710: every player hears Snd_ScanPresDec at volume 1, radius fDetectionRadius x 2")]
    public const float PresScanSoundRadiusScale = 2f;

    [Provenance(ProvenanceKind.Decoded, "SPresDetector tick 0x10abd500: AddMessageOnHud(1) for each newly detected spy's controller")]
    public const int PresDetectedHudMessage = 1;

    [Provenance(ProvenanceKind.Decoded, "SPresDetector tick 0x10abd500: BroadcastTeamLocalizedMessage(1, SGameMessages, 254) (Intruder)")]
    public const byte PresWarnTeam = 1;

    [Provenance(ProvenanceKind.Decoded, "SPresDetector tick 0x10abd500: switch 254 of SGameMessages (Intruder)")]
    public const int PresWarnMessage = 254;
}
