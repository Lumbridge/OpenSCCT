namespace OpenScct.Core.Engine;

/// <summary>Engine.PlayerReplicationInfo: per-player state every client sees. Only the core fields so far.</summary>
public partial class PlayerReplicationInfo : ReplicationInfo
{
    public string PlayerName = string.Empty;
    public string CharacterName = string.Empty;
    public int PlayerID;
    public int TeamID;
    public float Score;
    public int Kills;
    public int Deaths;
    public int Ping;

    /// <summary>Lives left in modes with limited respawns.</summary>
    public int NumLives;
    public bool bOutOfLives;
    public bool bIsSpectator;
    public bool bOnlySpectator;
    public bool bReadyToPlay;

    /// <summary>
    /// OpenSCCT's own: the player's cosmetic look (OpenScct.Net.Cosmetics.CosmeticLook, low and high halves), set by
    /// the server at login from its checked <c>?SvmLook=</c> option and replicated to every client.
    /// </summary>
    public int SvmLookLow;

    public int SvmLookHigh;

    /// <summary>The look as one value.</summary>
    public long SvmLook => ((long)SvmLookHigh << 32) | (uint)SvmLookLow;

    /// <summary>
    /// OpenSCCT's own: the player's level, prestige and shown title (OpenScct.Net.Cosmetics.PlayerRank packed; 0 for
    /// guests and bots), set by the server at login from its checked <c>?SvmRank=</c> option and replicated to every client.
    /// </summary>
    public int SvmRank;
}
