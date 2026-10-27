using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Numbers of the sound calls between the server and the players (spec/native/audio.md rules 4, 5 and 29).</summary>
public static class SoundK
{
    /// <summary>A sound call with radius 0 reaches its hearers with this radius.</summary>
    [Provenance(ProvenanceKind.Decoded, "execPlaySound 0x11189d50, execClientHearSound 0x11189020 (0x115bf000)")]
    public const float HearRadiusForZero = 80f;

    /// <summary>ClientHearSound sends the volume and the pitch as thousandths (Parameters.X and Parameters.Z).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/audio.md rule 5: execPlaySound builds (Volume x 1000, Radius, Pitch x 1000)")]
    public const float HearParameterScale = 1000f;

    /// <summary>ClientPlaySound's volume when the call does not set one.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PlayerController.ClientPlaySound: atten = 0.9")]
    public const float ClientPlaySoundVolume = 0.9f;

    /// <summary>
    /// How far from the hearer's view a server sound is still sent to that player, in multiples of its radius (the
    /// range past which UNIAudio no longer ranks a sound, rule 16).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "APlayerController::HearSound's reach test is not decoded (spec/native/audio.md open question 4); the client's 100 x radius range is used")]
    public const float HearRangeFactor = 100f;

    /// <summary>ClientSetMusic's fade-in for every transition but MTRAN_None.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PlayerController.ClientSetMusic: FadeTime = 3.0")]
    public const float MusicFadeSeconds = 3f;
}