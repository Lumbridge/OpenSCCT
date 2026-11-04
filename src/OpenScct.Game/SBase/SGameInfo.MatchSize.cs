using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;

namespace OpenScct.Game.SBase;

// OpenSCCT's match-size rules at the round start (spec/additions/match-size-rules.md).
public partial class SGameInfo
{
    /// <summary>The URL's SvmMatchPlayers: a player count the rules use instead of the roster (editor Play), 0 for none.</summary>
    private int _matchPlayersOption;

    /// <summary>
    /// Lays the round out by the map's match-size rules, when it has any: every player in the match at the round start
    /// counts, bots included and spectators not, unless the URL names a count. Clients learn the count through
    /// <see cref="GameReplicationInfo.MatchPlayers"/>.
    /// </summary>
    private void ApplyMatchSizeRules()
    {
        if (MatchSizeRules.For(XLevel) is not { } rules)
        {
            return;
        }

        int players = _matchPlayersOption > 0 ? _matchPlayersOption : CountMatchPlayers();
        rules.Apply(players);
        if (GameReplicationInfo is { } gri)
        {
            gri.MatchPlayers = players;
        }
    }

    private int CountMatchPlayers()
    {
        int players = 0;
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c.PlayerReplicationInfo is { bOnlySpectator: false, bIsSpectator: false })
            {
                players++;
            }
        }

        return players;
    }
}
