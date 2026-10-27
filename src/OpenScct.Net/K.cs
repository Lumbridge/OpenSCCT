using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Network constants taken from the original. Values from <c>System/Default.ini</c> are the shipped settings;
/// values from the executable are the ones spec/native/net-*.md decoded.
/// </summary>
public static class K
{
    /// <summary>Dedicated server tick cap (and replication rate) on the internet, per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] NetServerMaxTickRate")]
    public const int NetServerMaxTickRate = 20;

    /// <summary>Dedicated server tick cap with the lanplay switch, per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] LanServerMaxTickRate")]
    public const int LanServerMaxTickRate = 35;

    /// <summary>
    /// Replication rate of an Enhanced host (<see cref="NetProfile.Enhanced"/>): one network frame per world tick, so
    /// clients get every simulated state instead of one in three (docs/NETCODE.md, "Send rate and budget: Stock and
    /// Enhanced"). Also the tick cap its update frequency gate uses.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Rewrite choice (rl:net.rates): the 60 Hz sim rate, Sim.Hz")]
    public const int EnhancedServerTickRate = OpenScct.Core.Sim.Hz;

    /// <summary>GetMaxTickRate clamps a dedicated server's cap into this range.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 11, game 0x109ecb10")]
    public const int MinServerTickRate = 10;

    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 11, game 0x109ecb10")]
    public const int MaxServerTickRate = 120;

    /// <summary>Highest net speed a server grants one client, in bytes per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] MaxClientRate")]
    public const int MaxClientRate = 20000;

    /// <summary>Default client net speed on the internet, in bytes per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [Engine.Player] ConfiguredInternetSpeed")]
    public const int ConfiguredInternetSpeed = 10000;

    /// <summary>Default client net speed on a LAN, in bytes per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [Engine.Player] ConfiguredLanSpeed")]
    public const int ConfiguredLanSpeed = 20000;

    /// <summary>NETSPEED values below this are ignored by the server.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 10, game 0x109f7970")]
    public const int MinNetSpeed = 500;

    /// <summary>Seconds without a packet before an open connection is closed.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] ConnectionTimeout")]
    public const float ConnectionTimeout = 15f;

    /// <summary>Seconds a connection may stay pending (no handshake reply) before it is closed.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] InitialConnectTimeout")]
    public const float InitialConnectTimeout = 15f;

    /// <summary>Seconds after which an unacknowledged packet counts as lost (our packet layer; UE2 resends on NAK only).</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] AckTimeout")]
    public const float AckTimeout = 1f;

    /// <summary>Seconds of silence after which a connection sends an empty packet.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] KeepAliveTime")]
    public const float KeepAliveTime = 0.2f;

    /// <summary>Seconds an actor channel stays open after its actor stops being relevant.</summary>
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] RelevantTimeout")]
    public const float RelevantTimeout = 5f;

    /// <summary>A connection gets a replication pass only when it received a packet this recently.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 2, ServerTickClient game 0x10a30730")]
    public const float ReceiveWindow = 1.5f;

    /// <summary>Relevancy of an actor with a channel is checked again after this long.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 6")]
    public const float RelevancyRecheck = 1.0f;

    /// <summary>RelevantTime jitter: now plus this times FRand.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 6")]
    public const float RelevantTimeJitter = 0.5f;

    /// <summary>Within this distance of the camera an actor is always relevant.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-relevancy.md, game 0x10a8f760, 0x10ae41c0, 0x10aea530")]
    public const float AlwaysRelevantDistance = 2500f;

    /// <summary>An ambient sound is relevant within this times SoundRadius of the viewer.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-relevancy.md, game constant 0x10c71afc")]
    public const float AmbientSoundRelevancyFactor = 100f;

    /// <summary>Number of channel indexes per connection (index 0 is the control channel).</summary>
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 UnConn.h MAX_CHANNELS; the game uses 0..510 (spec/native/net-driver.md)")]
    public const int MAX_CHANNELS = 1023;

    /// <summary>Reliable bunch sequence numbers wrap at this value (a power of two).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md: channel sequence 0..1023, editor 0x11144f60")]
    public const int MAX_CHSEQUENCE = 1024;

    /// <summary>Unacknowledged reliable bunches one channel may have; at RELIABLE_BUFFER - 1 the channel is not ready.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 3, editor 0x1113e920")]
    public const int RELIABLE_BUFFER = 128;

    /// <summary>Statistics period of a connection, microseconds.</summary>
    [Provenance(ProvenanceKind.Estimated, "spec/native/net-driver.md: 1.0 is the UE2 default, read from the connection")]
    public const long StatisticsPeriodMicros = 1_000_000;

    /// <summary>More than this long between two connection ticks counts as a hitch (no timeout), microseconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 5.3, game 0x10a0e570")]
    public const long HitchGuardMicros = 1_000_000;

    /// <summary>A statistics period with more loss than this (percent) either way counts as lossy.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 5.2, game 0x10a0e570")]
    public const float LossyPeriodPercent = 20f;

    /// <summary>The adaptive rate is held at its maximum this long after a reset, microseconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 8, game 0x10a0eac0")]
    public const long NetSpeedHoldMicros = 2_000_000;

    /// <summary>The rate a reset sets before it is clamped to the row maximum.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 9, game 0x109ef0a0")]
    public const int NetSpeedResetRate = 999999;

    /// <summary>Frames of unused rate the send budget may save up.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 2, game 0x109eeff0")]
    public const int BudgetCarryFrames = 2;

    /// <summary>
    /// How many times the newest move goes out. The original resends each big move three times after the
    /// reliable send (counter 4, sends below 4); the M0 test pawn resends every move this many times.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController C_MaxBSMReSend 4")]
    public const int MoveResendCount = 4;
}

/// <summary>
/// The SCCT adaptive net speed settings (<c>CONF*</c> keys of <c>[IpDrv.TcpNetDriver]</c>): per side (client,
/// listen server, dedicated server) a step and, per network type, the rate bounds and lag thresholds.
/// </summary>
public sealed record NetSpeedRow(int StepUp, int InternetMin, int InternetMax, int LanMin, int LanMax, float LagToTakeAction,
    float MaxAllowedLag)
{
    public int Min(bool lan) => lan ? LanMin : InternetMin;

    public int Max(bool lan) => lan ? LanMax : InternetMax;
}

/// <summary>The three CONF* rows with the shipped values.</summary>
public sealed record NetSpeedConf(NetSpeedRow Client, NetSpeedRow ListenServer, NetSpeedRow DedicatedServer)
{
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] CONFClient* keys")]
    public static readonly NetSpeedRow DefaultClient = new(20, 5000, 8000, 10000, 11000, 0.250f, 0.400f);

    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] CONFListenServer* keys")]
    public static readonly NetSpeedRow DefaultListenServer = new(50, 7000, 9000, 15000, 40000, 0.200f, 0.350f);

    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] CONFDedicatedServer* keys")]
    public static readonly NetSpeedRow DefaultDedicatedServer = new(50, 8000, 15000, 15000, 40000, 0.150f, 0.300f);

    public static readonly NetSpeedConf Default = new(DefaultClient, DefaultListenServer, DefaultDedicatedServer);
}
