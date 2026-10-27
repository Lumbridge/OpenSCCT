using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>Engine.GameReplicationInfo: game state every client sees. Only the core fields so far.</summary>
public partial class GameReplicationInfo : ReplicationInfo
{
    public readonly List<PlayerReplicationInfo> PRIArray = new();
    public string GameName = string.Empty;
    public string ServerName = string.Empty;
    public bool bTeamGame;
    public bool bMatchHasBegun;
    public int TimeLimit;
    public int RemainingTime;
    public int ElapsedTime;
    public byte WinningTeam;
    public byte WinningReason;
}
