using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;

namespace OpenScct.Server.Online;

/// <summary>One scoreboard line: the replicated score, kills, deaths and lives of a player, with its ping.</summary>
public sealed record ScoreboardRow(string Name, byte Team, int Score, int Kills, int Deaths, int Lives, int PingMs, bool IsLocal);

/// <summary>
/// The in-match scoreboard (FEATURES rl:qol.scoreboard): every player's PRI from the client's world (score, kills,
/// deaths, lives), with the ping the session reports, sides in order and best score first. The HUD draws it; this
/// gathers it.
/// </summary>
public static class Scoreboard
{
    /// <summary>The rows of <paramref name="session"/>'s world, with pings from <paramref name="lobby"/> when given.</summary>
    public static List<ScoreboardRow> Build(ClientSession session, LobbyState? lobby = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var rows = new List<ScoreboardRow>();
        PlayerReplicationInfo? local = session.LocalPlayer?.PlayerReplicationInfo;
        foreach (PlayerReplicationInfo pri in session.World.Level.AllActors<PlayerReplicationInfo>())
        {
            if (pri.bOnlySpectator || pri.bDeleteMe)
            {
                continue;
            }

            int ping = pri.Ping;
            if (lobby is not null)
            {
                foreach (LobbyPlayer p in lobby.Players)
                {
                    if (string.Equals(p.Name, pri.PlayerName, StringComparison.Ordinal))
                    {
                        ping = p.PingMs;
                    }
                }
            }

            rows.Add(new ScoreboardRow(pri.PlayerName, (byte)(pri.Team?.TeamIndex ?? 0), (int)pri.Score, pri.Kills, pri.Deaths,
                pri.NumLives >= K.UnlimitedLives ? -1 : pri.NumLives, ping, ReferenceEquals(pri, local)));
        }

        rows.Sort((a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Name, b.Name));
        return rows;
    }
}
