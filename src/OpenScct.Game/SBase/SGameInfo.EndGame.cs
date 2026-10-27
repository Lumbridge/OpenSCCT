using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: win conditions and the end of the round. Written from spec/game/end-of-round.md.
public partial class SGameInfo
{
    private static readonly Name NAME_s_MatchOver = new("s_MatchOver");

    /// <summary>
    /// True when the round is over, with the winner and reason published. Only once the match has begun, in this
    /// order: time out (mercs win), Merc Deathmatch kill limit and survivors, offline both sides out of lives,
    /// network one side out of lives, then every objective taken (spies win). A network level without objectives
    /// ends with a spy win at the first check that reaches the objective test.
    /// </summary>
    [Uc("SBase.SGameInfo.sCheckEndGame", Sha1 = "2e66ff39356da30f621a72e1ff1acddf7161b39a")]
    [PortOf("uc:SBase.SGameInfo:sCheckEndGame", Spec = "spec/game/end-of-round.md#rules")]
    public bool sCheckEndGame()
    {
        UpdateTeamNumLives();
        WinnerTeam = K.NoResult;
        GameReplicationInfo? gri = GameReplicationInfo;
        if (gri is null || !gri.bMatchHasBegun)
        {
            return false;
        }

        if (gri.TimeLimit > 0 && gri.ElapsedTime >= gri.TimeLimit * 60)
        {
            return Decide(K.TimeOutWinner, EEndReason.END_TIMEOUT);
        }

        int numTeam0 = 0;
        int numTeam1 = 0;
        int nbPlayersSpy = 0;
        int nbPlayersSentry = 0;
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            // A missing PRI or team reads as zeros, as in script: such a controller counts as a spy without lives.
            PlayerReplicationInfo? pri = p.PlayerReplicationInfo;
            if (p is not PlayerController || (pri?.bOnlySpectator ?? false) || (pri?.bQuittedMatch ?? false))
            {
                continue;
            }

            int team = TeamIndexOf(p);
            int lives = pri?.NumLives ?? 0;
            if (team == 0)
            {
                nbPlayersSpy++;
                if (lives > 0)
                {
                    numTeam0++;
                }
            }
            else if (team == 1)
            {
                nbPlayersSentry++;
                if (lives > 0)
                {
                    numTeam1++;
                }
            }
        }

        if (MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch } m)
        {
            int killLimit = m.ScoreLimit < 1 ? USMatchInfo.DefaultKillLimit() : m.ScoreLimit;
            if (GetTopKills() >= killLimit)
            {
                return Decide(1, EEndReason.END_STORY);
            }

            int numStillIn = CountMercsStillIn();
            if (numStillIn <= 1 && numStillIn < nbPlayersSentry)
            {
                return Decide(1, EEndReason.END_TEAMDEATH);
            }

            return false;
        }

        if (Level.NetMode == ENetMode.NM_Standalone)
        {
            if (numTeam0 == 0 && numTeam1 == 0)
            {
                return Decide(nbPlayersSpy > 0 ? (byte)1 : (byte)0, EEndReason.END_TEAMDEATH);
            }

            if (XLevel.AllActors<SObjective>().Count() == 0)
            {
                return false;
            }
        }
        else if (((numTeam0 == 0 && nbPlayersSpy > 0) || (numTeam1 == 0 && nbPlayersSentry > 0)) &&
                 numTeam0 + numTeam1 < GetNumPlayers())
        {
            byte winner = numTeam0 == 0 && numTeam1 == 0
                ? (nbPlayersSpy > 0 ? (byte)1 : (byte)0)
                : numTeam0 == 0 ? (byte)1 : (byte)0;
            return Decide(winner, EEndReason.END_TEAMDEATH);
        }

        foreach (SObjective o in XLevel.AllActors<SObjective>())
        {
            if (!o.ObjectiveTaken)
            {
                return false;
            }
        }

        return Decide(K.StoryWinner, EEndReason.END_STORY);
    }

    private bool Decide(byte winner, EEndReason reason)
    {
        WinnerTeam = winner;
        ReplicateWinningReason(WinnerTeam, reason);
        return true;
    }

    /// <summary>Freezes the clock, marks the game ended, fires the EndGame event and enters s_MatchOver.</summary>
    [Uc("SBase.SGameInfo.sEndGame", Sha1 = "a80d0fa207bbfc0899be36d37e908e97f6130ea0")]
    [PortOf("uc:SBase.SGameInfo:sEndGame", Spec = "spec/game/end-of-round.md#rules")]
    public void sEndGame()
    {
        if (MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch } && GameReplicationInfo is not null)
        {
            GameReplicationInfo.NumDisks = GetTopKills();
        }

        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.bStopCountDown = true;
        }

        bGameEnded = true;
        TriggerEvent(K.EndGameEvent, this, null);
        GotoState(NAME_s_MatchOver);
    }

    /// <summary>Publishes the winning side (0 spies, 1 mercs) and the reason.</summary>
    [Uc("SBase.SGameInfo.ReplicateWinningReason", Sha1 = "4c14b9a7edfbeb74dfb6f36d55b5134c7de588fa")]
    [PortOf("uc:SBase.SGameInfo:ReplicateWinningReason", Spec = "spec/game/end-of-round.md#rules")]
    public void ReplicateWinningReason(byte Team, EEndReason Reason)
    {
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.WinningTeam = Team;
            GameReplicationInfo.WinningReason = (byte)Reason;
        }
    }

    /// <summary>Merc Deathmatch survivors: playing mercs with lives left or no death yet.</summary>
    [Uc("SBase.SGameInfo.CountMercsStillIn", Sha1 = "03d915549c8f857c1cb49c7c5572340ba9c10118")]
    [PortOf("uc:SBase.SGameInfo:CountMercsStillIn", Spec = "spec/game/end-of-round.md#rules")]
    public int CountMercsStillIn()
    {
        int count = 0;
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is PlayerController &&
                p.PlayerReplicationInfo is { Team.TeamIndex: 1, bOnlySpectator: false, bQuittedMatch: false } pri &&
                (pri.NumLives > 0 || pri.Deaths == 0))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The most kills of any playing player (0 without players).</summary>
    [Uc("SBase.SGameInfo.GetTopKills", Sha1 = "f7d14b1508273fa136ab0848440fff5376af88d0")]
    [PortOf("uc:SBase.SGameInfo:GetTopKills", Spec = "spec/game/end-of-round.md#rules")]
    public int GetTopKills()
    {
        int top = 0;
        bool found = false;
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is PlayerController && p.PlayerReplicationInfo is { bOnlySpectator: false } pri && (!found || pri.Kills > top))
            {
                top = pri.Kills;
                found = true;
            }
        }

        return top;
    }

    /// <summary>The number of SBase players on the merc side.</summary>
    [Uc("SBase.SGameInfo.GetNbSentries", Sha1 = "cacfb9bfc79d10132f8bc24464e2fa06bfe44cdb")]
    [PortOf("uc:SBase.SGameInfo:GetNbSentries", Spec = "spec/game/end-of-round.md#rules")]
    public int GetNbSentries()
    {
        int n = 0;
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is SPlayerController && TeamIndexOf(p) == 1)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The Engine end check is disabled in this game.</summary>
    [Uc("SBase.SGameInfo.CheckEndGame", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SGameInfo:CheckEndGame", Spec = "spec/game/end-of-round.md#rules")]
    public override bool CheckEndGame(PlayerReplicationInfo? Winner, string Reason) => false;

    /// <summary>The Engine end path is disabled in this game.</summary>
    [Uc("SBase.SGameInfo.EndGame", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGameInfo:EndGame", Spec = "spec/game/end-of-round.md#rules")]
    public override void EndGame(PlayerReplicationInfo? Winner, string Reason)
    {
    }
}
