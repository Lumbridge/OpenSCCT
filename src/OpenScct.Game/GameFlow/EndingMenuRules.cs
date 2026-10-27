using System.Globalization;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// The round travel decided by the host's end menu (GUI.Ending_Menu and its GUIComponent helpers): once every
/// player confirmed, the next game of a series on the same map (revenge points carried swapped, one game less),
/// or the lobby when the series is over or a side is empty. Only the decision is ported; random maps and the stats
/// pages are UI.
/// </summary>
public static class EndingMenuRules
{
    /// <summary>True when every PRI of the list is ready to play.</summary>
    [PortOf("uc:GUI.GUIComponent:AllPlayerReady", Spec = "spec/game/round-travel.md#rules")]
    public static bool AllPlayerReady(GameReplicationInfo gri)
    {
        foreach (PlayerReplicationInfo pri in gri.PRIArray)
        {
            if (!pri.bReadyToPlay)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Players of one side in the PRI list, skipping the first entry when the host does not play; a team-less PRI
    /// counts as side 0.
    /// </summary>
    [PortOf("uc:GUI.GUIComponent:HowManyPlayersInTeam", Spec = "spec/game/round-travel.md#rules")]
    public static int HowManyPlayersInTeam(GameReplicationInfo gri, int NumTeam)
    {
        int count = 0;
        for (int i = gri.ServerOnly ? 1 : 0; i < gri.PRIArray.Count; i++)
        {
            if ((gri.PRIArray[i].Team?.TeamIndex ?? 0) == NumTeam)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The URL of the next round. The lobby (GamesInARow set back to <paramref name="lobbyGamesInARow"/>) when the
    /// series is over or a side is empty; otherwise the host's map with the match options, the swapped team points
    /// in a revenge pair, and GamesInARow one lower (so the URL holds GamesInARow twice).
    /// </summary>
    [PortOf("uc:GUI.Ending_Menu:Timer", Spec = "spec/game/round-travel.md#rules")]
    public static string NextRoundUrl(SGameInfo game, int lobbyGamesInARow)
    {
        USMatchInfo m = game.MatchInfo ?? new USMatchInfo();
        GameReplicationInfo? gri = game.GameReplicationInfo;
        bool sideEmpty = gri is not null && (HowManyPlayersInTeam(gri, 0) == 0 || HowManyPlayersInTeam(gri, 1) == 0);
        if (m.GamesInARow <= 1 || sideEmpty)
        {
            m.GamesInARow = lobbyGamesInARow;
            if (gri is not null)
            {
                gri.GamesInARow = lobbyGamesInARow;
            }

            return "Lobby" + m.GetOptionsString();
        }

        string url = game.PersistentData.LevelName + m.GetOptionsString();
        if (m.Revenge)
        {
            url += "?ScoreDEF=" + Int(game.Level.NbPointsAttack) + "?ScoreATT=" + Int(game.Level.NbPointsDefense);
        }

        return url + "?GamesInARow=" + Int(m.GamesInARow - 1);
    }

    /// <summary>
    /// The host's end menu timer: when every player is ready, travel to <see cref="NextRoundUrl"/>. Returns true
    /// when the travel started. The native travel handshake (canStartTravelOnServer) is taken as allowing it.
    /// </summary>
    [PortOf("uc:GUI.Ending_Menu:Timer", Spec = "spec/game/round-travel.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "canStartTravelOnServer and refuseClientsConnections are natives of the travel handshake, not ported")]
    public static bool TryTravel(SGameInfo game, PlayerController host, int lobbyGamesInARow)
    {
        if (game.GameReplicationInfo is not { } gri || !AllPlayerReady(gri))
        {
            return false;
        }

        host.SwitchLevel(NextRoundUrl(game, lobbyGamesInARow));
        return true;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
