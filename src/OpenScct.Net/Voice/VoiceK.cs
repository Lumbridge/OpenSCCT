using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>
/// Voice chat constants (docs/VOICE.md). Values Reloaded's code fixes are decoded (spec/native/reloaded-features-from-code.md);
/// values its config file ships are decoded from the config; the rewrite's own choices follow spec/reloaded/FEATURES.md
/// section 5 and are marked estimated.
/// </summary>
public static class VoiceK
{
    /// <summary>Reloaded's Opus encoder rate. The rewrite codes at <see cref="SampleRate"/>; this is the quality floor.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1006ba60 (reloaded-features-from-code.md rule 2)")]
    public const int ReloadedSampleRate = 16000;

    /// <summary>Reloaded's target bitrate in bits per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1006ba60 (reloaded-features-from-code.md rule 2)")]
    public const int ReloadedBitrate = 16000;

    /// <summary>Opus complexity, as Reloaded sets it.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1006ba60 (reloaded-features-from-code.md rule 2)")]
    public const int Complexity = 10;

    /// <summary>One Opus frame: 320 samples at 16 kHz in Reloaded, the same 20 ms here.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1006bb50 (reloaded-features-from-code.md rule 3)")]
    public const int FrameMilliseconds = 20;

    /// <summary>Largest Opus packet Reloaded lets the encoder write (about 24.8 kbit/s at 20 ms). Our encoder has the same cap.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1006bb50 (reloaded-features-from-code.md rule 3)")]
    public const int MaxOpusPacketBytes = 62;

    /// <summary>Coding rate: 48 kHz mono (FEATURES rl:voice.codec).</summary>
    [Provenance(ProvenanceKind.Estimated, "spec/reloaded/FEATURES.md rl:voice.codec (rewrite choice)")]
    public const int SampleRate = 48000;

    /// <summary>Samples in one frame at <see cref="SampleRate"/> (960).</summary>
    public const int FrameSamples = SampleRate / 1000 * FrameMilliseconds;

    /// <summary>Frames per second (50).</summary>
    public const int FramesPerSecond = 1000 / FrameMilliseconds;

    /// <summary>
    /// The wire's talker id of a player: its PlayerReplicationInfo.PlayerID plus one, because GameInfo hands out ids from
    /// 0 (the first player to log in, a listen host's own player, has PlayerID 0) and talker 0 means nobody. 0 for an id
    /// the wire cannot carry.
    /// </summary>
    public static ushort TalkerOf(int playerId) => playerId is >= 0 and < ushort.MaxValue ? (ushort)(playerId + 1) : (ushort)0;

    /// <summary>Target bitrate, constrained VBR; <see cref="MaxOpusPacketBytes"/> caps every packet.</summary>
    [Provenance(ProvenanceKind.Estimated, "spec/reloaded/FEATURES.md rl:voice.codec: VBR around 20 to 24 kbps (rewrite choice)")]
    public const int Bitrate = 20000;

    /// <summary>Loss the encoder plans in-band FEC for until the client reports its own uplink loss.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: Opus only codes FEC when the expected loss is above 0")]
    public const int DefaultPacketLossPercent = 5;

    /// <summary>Smallest jitter buffer setting, in frames.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT_Versus.config voice_jitter_frames comment: 1 to 12 frames")]
    public const int MinJitterFrames = 1;

    /// <summary>Largest jitter buffer setting, in frames.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT_Versus.config voice_jitter_frames comment: 1 to 12 frames")]
    public const int MaxJitterFrames = 12;

    /// <summary>Frames banked before playback starts and after a dropout.</summary>
    [Provenance(ProvenanceKind.Estimated, "spec/reloaded/FEATURES.md rl:voice.jitter: rewrite default 3 (Reloaded ships 4)")]
    public const int DefaultJitterFrames = 3;

    /// <summary>Level (frame RMS, 0 to 1) that opens the activation gate. 0 always transmits, 1 never.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT_Versus.config voice_activation_threshold")]
    public const float ActivationThreshold = 0.02f;

    /// <summary>Milliseconds the gate stays open after the level falls below the close level.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT_Versus.config voice_activation_hold_ms")]
    public const int ActivationHoldMs = 300;

    /// <summary>The gate closes when the level falls to this share of the threshold.</summary>
    [Provenance(ProvenanceKind.Decoded, "SCCT_Versus.config voice_activation_threshold comment: stays open until 60% of it")]
    public const float ActivationCloseRatio = 0.6f;

    /// <summary>Talkers one listener receives at once; a further talker waits for a free slot.</summary>
    [Provenance(ProvenanceKind.Estimated, "spec/reloaded/FEATURES.md rl:voice.routing: cap per recipient, for example 4")]
    public const int MaxTalkersPerListener = 4;

    /// <summary>Milliseconds without a packet after which a talker's slot on a listener is free again.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: the activation hold plus one frame")]
    public const int TalkerSlotReleaseMs = ActivationHoldMs + FrameMilliseconds;

    /// <summary>Milliseconds without a packet after which a talker counts as silent (the speaking indicator goes off).</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice")]
    public const int SpeakingTimeoutMs = 250;

    /// <summary>Distance at which a downed player and an enemy can talk (stock SPlayerController default; 0 in Team mode).</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController defaults fDistToTalkWhenOnGround")]
    public const float DistToTalkWhenOnGround = 300f;

    /// <summary>Audio packets one talker may send per second before the server drops them (50 nominal).</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: 50 frames per second plus slack for bursts after a stall")]
    public const int MaxPacketsPerSecond = 60;

    /// <summary>Burst allowance of the per-talker packet rate limit.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice")]
    public const int PacketBurst = 15;

    /// <summary>A client repeats its join message (voice on, mute list) this often.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice")]
    public const int JoinRefreshMs = 2000;

    /// <summary>A listener whose join message is older than this gets no voice.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: three missed refreshes")]
    public const int JoinExpiryMs = 3 * JoinRefreshMs;

    /// <summary>How often the server rebuilds its routing roster from the game.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: ten times a second")]
    public const int RosterRefreshMs = 100;

    /// <summary>Talkers a client keeps muted on the server (per-player mute list in the join message).</summary>
    public const int MaxMutedTalkers = 32;
}
