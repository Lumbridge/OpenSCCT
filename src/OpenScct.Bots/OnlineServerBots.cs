using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Server;
using OpenScct.Server.Online;

namespace OpenScct.Bots;

/// <summary>
/// The bots of an online session's round (docs/ONLINE.md, "Bots"): <see cref="ServerBots"/> on the round's
/// <see cref="ServerHost"/>, one per bot row of the lobby, at the setup's skill. They log in through the game's Login
/// and PostLogin before the first world tick, report waiting like loaded clients, and play on the server's thread.
/// Online clients see them as any other player; they have no connection, so the voice relay never routes them.
/// </summary>
public sealed class OnlineServerBots : IOnlineBotRunner
{
    private readonly ServerBots _bots;
    private readonly IReadOnlyList<OnlineBot> _roster;

    private OnlineServerBots(ServerHost host, MapCatalog maps, IReadOnlyList<OnlineBot> roster, AiNavCache nav)
    {
        _roster = roster;
        var specs = new List<ServerBotSpec>(roster.Count);
        foreach (OnlineBot b in roster)
        {
            specs.Add(new ServerBotSpec(b.Name, b.Team, AiDifficulty.For(b.Skill)) { ExtraOptions = b.LoginOptions });
        }

        _bots = new ServerBots(host, new ServerBotSettings
        {
            Bots = specs,
            Nav = nav,
            Maps = maps,
        });
        try
        {
            // A cold map can spend tens of seconds building navigation. Finish that work as part of map
            // creation, before the online server accepts clients and starts their loading timeout.
            _bots.LoginAll();
        }
        catch
        {
            _bots.Dispose();
            throw;
        }
    }

    /// <summary>The bots' log (login failures).</summary>
    public IReadOnlyList<string> Log => _bots.Log;

    /// <summary>
    /// A factory for <see cref="OnlineServerSettings.Bots"/>; the navigation of each map is planned once and kept
    /// (in <paramref name="nav"/>, or a cache over the default folder) for the session's later rounds.
    /// </summary>
    public static OnlineBotFactory Factory(AiNavCache? nav = null)
    {
        AiNavCache cache = nav ?? new AiNavCache();
        return (host, maps, bots) =>
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(bots);
            return new OnlineServerBots(host, maps, bots, cache);
        };
    }

    /// <summary>
    /// Prepares navigation on a worker while the lobby continues ticking. Pass the same cache to
    /// <see cref="Factory"/> so map creation reuses the graphs before clients start loading.
    /// </summary>
    public static OnlineBotPreparation Preparation(AiNavCache nav)
    {
        ArgumentNullException.ThrowIfNull(nav);
        return (maps, map) => Task.Run(() => nav.Prepare(maps, map));
    }

    public PlayerController? ControllerOf(int id)
    {
        foreach (OnlineBot b in _roster)
        {
            if (b.Id == id)
            {
                return FindByName(b.Name);
            }
        }

        return null;
    }

    private PlayerController? FindByName(string name)
    {
        foreach (AiController bot in _bots.Bots)
        {
            if (bot.Controller.PlayerReplicationInfo is { } pri && string.Equals(pri.PlayerName, name, StringComparison.Ordinal))
            {
                return bot.Controller;
            }
        }

        return null;
    }

    public void Dispose() => _bots.Dispose();
}
