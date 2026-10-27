using OpenScct.Provenance;

namespace OpenScctGame.Audio;

/// <summary>
/// Numbers of the original's sound playback (UNIAudio on DirectSound 3D), each with its evidence. The rules that use
/// them are in spec/native/audio.md; the content they apply to in docs/AUDIO.md.
/// </summary>
public static class AudioK
{
    /// <summary>Sources the subsystem mixes at once.</summary>
    [Provenance(ProvenanceKind.Decoded, "Default.ini [UNIAudio.UNIAudioSubsystem] Channels=32")]
    public const int Voices = 32;

    /// <summary>Listener roll-off of the inverse distance model and of the 2D ambient fall-off.</summary>
    [Provenance(ProvenanceKind.Decoded, "Default.ini [UNIAudio.UNIAudioSubsystem] Rolloff=0.5; read by UpdateAmbientSounds (editor 0x10f6d390)")]
    [Provenance(ProvenanceKind.Estimated, "That the DirectSound 3D listener uses the same factor (spec/native/audio.md open question 1)")]
    public const float Rolloff = 0.5f;

    /// <summary>A sound's reach (priority range, ambient range) is this many times its radius.</summary>
    [Provenance(ProvenanceKind.Decoded, "SoundPriority 0x10f6a1d0 and UpdateAmbientSounds 0x10f6d390: 100 x Radius (0x115beffc)")]
    public const float RangeFactor = 100f;

    /// <summary>Lowest distance factor of the priority.</summary>
    [Provenance(ProvenanceKind.Decoded, "SoundPriority 0x10f6a1d0: clamp(1 - d^2 / (100 R)^2, 0.01, 1)")]
    public const float PriorityFloor = 0.01f;

    /// <summary>Radius a hearer gets for a call with radius 0 (PlaySound, ClientHearSound, AnimNotify_Sound).</summary>
    [Provenance(ProvenanceKind.Decoded, "execPlaySound 0x11189d50, execClientHearSound 0x11189020, UAnimNotify_Sound::Notify 0x11103360 (0x115bf000)")]
    public const float DefaultHearRadius = 80f;

    /// <summary>Minimum distance of a source whose radius is 0.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlaySound 0x10f6c340: radius 0 stored as 10")]
    public const float ZeroRadiusMinDistance = 10f;

    /// <summary>Smallest minimum distance handed to DirectSound.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlaySound 0x10f6c340: radius at least 0.001")]
    public const float MinMinDistance = 0.001f;

    /// <summary>Source volume clamp.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlaySound and Update: clamp(volume, 1e-5, 1)")]
    public const float MinVolume = 1e-5f;

    /// <summary>Lowest pitch factor.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlaySound: pitch at least 0.5; Update: ambient pitch clamp(SoundPitch / 64, 0.5, 2)")]
    public const float MinPitch = 0.5f;

    /// <summary>Highest pitch factor.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::Update: ambient pitch at most 2")]
    [Provenance(ProvenanceKind.Estimated, "The same bound for one-shot sounds (spec/native/audio.md open question 3)")]
    public const float MaxPitch = 2f;

    /// <summary>SoundPitch value of pitch 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "Update 0x10f6e980: SoundPitch x 0.015625")]
    public const float PitchUnit = 64f;

    /// <summary>SoundVolume value of volume 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "GetAmbientVolume: SoundVolume / 255")]
    public const float VolumeUnit = 255f;

    /// <summary>A 2D ambient below this volume does not play.</summary>
    [Provenance(ProvenanceKind.Decoded, "UpdateAmbientSounds 0x10f6d390: 0.05")]
    public const float Ambient2DCut = 0.05f;

    /// <summary>Id offset of an actor's ambient sound (ActorIndex x 16 + 8).</summary>
    [Provenance(ProvenanceKind.Decoded, "UpdateAmbientSounds 0x10f6d390")]
    public const int AmbientSlotId = 8;

    /// <summary>Random list weights are out of this.</summary>
    [Provenance(ProvenanceKind.Decoded, "random pick 0x10f6a300: rand() % 10000")]
    public const int RandomTotal = 10000;

    /// <summary>A switch sound with no pending SetSwitch traces this far below its location...</summary>
    [Provenance(ProvenanceKind.Decoded, "GetSwitchSound 0x10f6a350: from 15 uu above to 150 uu below")]
    public const float SwitchTraceDown = 150f;

    /// <summary>...from this far above it.</summary>
    [Provenance(ProvenanceKind.Decoded, "GetSwitchSound 0x10f6a350")]
    public const float SwitchTraceUp = 15f;

    /// <summary>Sequence entry value bit: loop the entry for (value &amp; 0x1fff) times its duration.</summary>
    [Provenance(ProvenanceKind.Decoded, "sequence player 0x10f6e0c0")]
    public const int SequenceTimedLoop = 0x2000;

    /// <summary>Music plays at this radius (2D, so it only matters for priority).</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlayMusic 0x10f69b10: radius 1000")]
    public const float MusicRadius = 1000f;

    /// <summary>The previous music track fades out over at least this long.</summary>
    [Provenance(ProvenanceKind.Decoded, "UUNIAudioSubsystem::PlayMusic 0x10f69b10: max(FadeInTime, 2)")]
    public const float MusicMinFadeOut = 2f;

    /// <summary>Sources faster than this get no Doppler velocity.</summary>
    [Provenance(ProvenanceKind.Decoded, "Update 0x10f6e980: velocity above 300 is dropped")]
    public const float MaxDopplerSpeed = 300f;

    /// <summary>
    /// Audio zoom (SetAudioZoom, the scope and binoculars): a 3D sound's off-axis measure is this over the view FOV times
    /// its sideways share; 1 at about the edge of the view.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10b06280 (audio zoom gain): 112.5 / camera FOV x |projected xy|, clamped to 1")]
    [Provenance(ProvenanceKind.Estimated, "|projected xy| read as the sideways part of the unit direction to the sound (the projection's operands were not recovered)")]
    public const float AudioZoomFovScale = 112.5f;

    /// <summary>Audio zoom gain: 1 + zoom x (centre - edge x off-axis), so up to 1 + 3 x zoom on the aim line.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10b06280: (3 - 3.9 r) x zoom + 1, applied to each non-2D sound's volume by 0x10b06340 (SetAudioZoom, execSetAudioZoom 0x10a25b80)")]
    public const float AudioZoomCentre = 3f;

    [Provenance(ProvenanceKind.Decoded, "game 0x10b06280: (3 - 3.9 r) x zoom + 1")]
    public const float AudioZoomEdge = 3.9f;

    /// <summary>
    /// Radius of a gadget page's sound (the heart sensor's heartbeat and scan ping): the page natives call the
    /// subsystem's PlaySound with no actor, slot 0, volume 1, this radius, pitch 1 and the 2D flag.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "HUD sound helper game 0x10b26920: Audio->PlaySound(None, 0, Sound, loc, 1.0, 4096.0, 1.0, 0x10, ...)")]
    public const float HudSoundRadius = 4096f;

    /// <summary>Sound flags (spec/native/audio.md, Sound objects).</summary>
    public const int FlagLoop = 0x0002;
    public const int FlagStream = 0x0004;
    public const int FlagMusic = 0x0008;
    public const int Flag2D = 0x0010;
}
