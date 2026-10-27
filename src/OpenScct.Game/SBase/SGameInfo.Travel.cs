using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: the next round and map travel. Written from spec/game/round-travel.md.
public partial class SGameInfo
{
    /// <summary>
    /// The legacy restart (every player asked to play again): a relative travel carrying the swapped team points of
    /// a revenge pair and one game less in the series, or "?Restart".
    /// </summary>
    [Uc("SBase.SGameInfo.RestartGame", Sha1 = "70fdcf296c3a7a71bd3576aca85572f9402d817b")]
    [PortOf("uc:SBase.SGameInfo:RestartGame", Spec = "spec/game/round-travel.md#rules")]
    public override void RestartGame()
    {
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            eventRestartPlayer(p);
        }

        string url = string.Empty;
        if (MatchInfo is { Revenge: true })
        {
            url += "?ScoreDEF=" + Int(Level.NbPointsAttack) + "?ScoreATT=" + Int(Level.NbPointsDefense);
        }

        if (MatchInfo is { GamesInARow: > 0 } m)
        {
            url += "?GamesInARow=" + Int(m.GamesInARow - 1);
        }

        if (url.Length == 0)
        {
            url = "?Restart";
        }

        Level.ServerTravel(url, false);
    }

    /// <summary>
    /// Sends every remote player after the server: its team is appended to the URL (the other side when the first
    /// GamesInARow of the URL is below 2 in a revenge game) and the URL grows with each client, as in the original;
    /// local players get PreClientTravel and the listen host's team goes into NextURL. Standalone travels at once.
    /// </summary>
    [Uc("SBase.SGameInfo.ProcessServerTravel", Sha1 = "86bbac280aa9150819a40ba45853edb9dd9c0fb0")]
    [PortOf("uc:SBase.SGameInfo:ProcessServerTravel", Spec = "spec/game/round-travel.md#rules")]
    public override void ProcessServerTravel(string URL, bool bItems)
    {
        bool bSwitchTeams = MatchInfo is { Revenge: true } && GetIntOption(URL, "GamesInARow", K.RevengeGamesInARowDefault) < 2;
        int nbClientsToTravel = 0;
        PlayerController? localPlayer = null;
        foreach (PlayerController p in XLevel.DynamicActors<PlayerController>())
        {
            if (p.IsRemotePlayer)
            {
                URL += TeamOption(p, bSwitchTeams);
                nbClientsToTravel++;
                Host.ClientCall(p, new GameCall("ClientTravel")
                {
                    Text = URL + "?IdentifiedTravel",
                    Int = (int)ETravelType.TRAVEL_Relative,
                    Bool = bItems,
                });
                continue;
            }

            localPlayer = p;
            Host.ClientCall(p, new GameCall("PreClientTravel"));
        }

        Level.waitedClients = nbClientsToTravel;
        if (Level.NetMode == ENetMode.NM_ListenServer && localPlayer is not null)
        {
            Level.NextURL += TeamOption(localPlayer, bSwitchTeams);
        }

        if (Level.NetMode != ENetMode.NM_DedicatedServer && Level.NetMode != ENetMode.NM_ListenServer)
        {
            Level.NextSwitchCountdown = 0f;
        }
    }

    /// <summary>Sends every SBase player back to the lobby page.</summary>
    [Uc("SBase.SGameInfo.ForceMenuLobby", Sha1 = "87a91b326631a40a64bb62257460b88ebfb14a42")]
    [PortOf("uc:SBase.SGameInfo:ForceMenuLobby", Spec = "spec/game/round-travel.md#rules")]
    public void ForceMenuLobby()
    {
        foreach (SPlayerController p in XLevel.DynamicActors<SPlayerController>())
        {
            p.CLientForceMenuLobby();
        }
    }

    /// <summary>Sends every SBase player but the one of <paramref name="PRI"/> back to the entry menu.</summary>
    [Uc("SBase.SGameInfo.ForceMenuClientEntry", Sha1 = "49bc1bb65bb2b3f1ca8715b37778cb5112d3b6ba")]
    [PortOf("uc:SBase.SGameInfo:ForceMenuClientEntry", Spec = "spec/game/round-travel.md#rules")]
    public void ForceMenuClientEntry(PlayerReplicationInfo? PRI)
    {
        foreach (SPlayerController p in XLevel.DynamicActors<SPlayerController>())
        {
            if (!ReferenceEquals(p.PlayerReplicationInfo, PRI))
            {
                p.ClientForceMenuEntry();
            }
        }
    }

    /// <summary>"?Team=n": the player's side, or the other side when the teams switch (a team-less player reads 0).</summary>
    private static string TeamOption(PlayerController p, bool bSwitchTeams)
    {
        int team = TeamIndexOf(p);
        if (bSwitchTeams)
        {
            return team == 1 ? "?Team=0" : "?Team=1";
        }

        return "?Team=" + Int(team);
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
