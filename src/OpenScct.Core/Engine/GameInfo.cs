namespace OpenScct.Core.Engine;

/// <summary>Engine.GameInfo: the server-side game rules. Only the core fields so far.</summary>
public partial class GameInfo : Info
{
    public GameReplicationInfo? GameReplicationInfo;
    public string GameName = string.Empty;
    public bool bTeamGame;
    public bool bGameEnded;
    public bool bWaitingToStartMatch;
    public int NumPlayers;
    public int MaxPlayers;
    public int NumSpectators;
    public int MaxSpectators;
    public int TimeLimit;
    public float GameSpeed = 1f;
    public float StartTime;

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        GameReplicationInfo = Live(GameReplicationInfo);
    }
}
