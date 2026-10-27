using OpenScct.Server.Online;

namespace OpenScctGame.Online;

/// <summary>
/// Which side the local player played in a round, read from the round's own results. The lobby keeps each player's
/// lobby side, which a Revenge round swaps for its second game (spec/game/lobby-and-match-creation.md), so the
/// "You Win" / "You Lose" banner must come from the local player's row of the results.
/// </summary>
public static class RoundResultSide
{
    /// <summary>The local player's team in the round (0 spies, 1 mercs): his results row, else his lobby side.</summary>
    public static byte LocalTeam(MatchResults results, int localId, byte? lobbyTeam)
    {
        foreach (ResultRow r in results.Rows)
        {
            if (r.Id == localId)
            {
                return r.Team;
            }
        }

        return lobbyTeam ?? 0;
    }
}
