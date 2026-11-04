using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>Engine.GameReplicationInfo: game state every client sees. Only the core fields so far.</summary>
public partial class GameReplicationInfo : ReplicationInfo
{
    public readonly List<PlayerReplicationInfo> PRIArray = new();
    public string GameName = string.Empty;
    public string ServerName = string.Empty;
    public bool bTeamGame;
    public bool SidearmsEnabled;

    /// <summary>
    /// The player count this round's match-size rules use (spec/additions/match-size-rules.md), 0 when the map has
    /// none. Replicated so clients can show the layout and leave out gated client-only actors.
    /// </summary>
    public int MatchPlayers;
    public bool bMatchHasBegun;
    public int TimeLimit;
    public int RemainingTime;
    public int ElapsedTime;
    public byte WinningTeam;
    public byte WinningReason;
}
