using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;

namespace OpenScct.Server.Online;

/// <summary>How the host adds bots to an online session (Create Session's "Bots" row).</summary>
public enum BotFill : byte
{
    /// <summary>No bots.</summary>
    Off = 0,

    /// <summary>A fixed number of bots on each side (<see cref="LobbySetup.BotsPerSide"/>), as long as the sides have room.</summary>
    PerSide = 1,

    /// <summary>Bots take the empty places: each side is filled to half the session's players.</summary>
    Fill = 2,
}

/// <summary>
/// A bot of the session as the round's map logs it in: its session id, name, lobby side, skill, and the login options
/// after its name and side (a revenge series' <c>?GamesInARow=</c>, so Login swaps it in the second game).
/// </summary>
public sealed record OnlineBot(int Id, string Name, byte Team, AiSkill Skill, string LoginOptions);

/// <summary>The bots of one round's map, playing on the server's thread with the game's own AI.</summary>
public interface IOnlineBotRunner : IDisposable
{
    /// <summary>The controller the bot with session id <paramref name="id"/> plays with, or null (not logged in yet, or failed).</summary>
    PlayerController? ControllerOf(int id);
}

/// <summary>
/// Makes the bots of a round's map (OpenScct.Bots provides one, <c>OnlineServerBots.Factory</c>). Called on the
/// server's thread right after the map's server was created, before any player joins it; the bots log in before the
/// first world tick so they play the round from its start.
/// </summary>
public delegate IOnlineBotRunner OnlineBotFactory(ServerHost host, MapCatalog maps, IReadOnlyList<OnlineBot> bots);

/// <summary>Who the session's bots are: how many on each side for the humans in the lobby, and their names.</summary>
public static class OnlineBotRoster
{
    /// <summary>Most bots on one side the page offers.</summary>
    public const int MaxPerSide = 4;

    /// <summary>
    /// Bots wanted on the spies (index 0) and the mercenaries (index 1) for <paramref name="setup"/> with
    /// <paramref name="spies"/> and <paramref name="mercs"/> human players, in a session of <paramref name="maxPlayers"/>
    /// players. Bots never take a human's place: humans plus bots stay within <paramref name="maxPlayers"/>, and
    /// in Merc Deathmatch every bot is a mercenary.
    /// </summary>
    public static (int Spies, int Mercs) Wanted(LobbySetup setup, int spies, int mercs, int maxPlayers)
    {
        ArgumentNullException.ThrowIfNull(setup);
        int room = Math.Max(0, maxPlayers - spies - mercs);
        bool mercsOnly = setup.Mode == EGameMode.GM_MercDeathMatch;
        int wantSpies;
        int wantMercs;
        switch (setup.Bots)
        {
            case BotFill.PerSide:
                wantSpies = mercsOnly ? 0 : setup.BotsPerSide;
                wantMercs = mercsOnly ? 2 * setup.BotsPerSide : setup.BotsPerSide;
                break;
            case BotFill.Fill when mercsOnly:
                wantSpies = 0;
                wantMercs = room;
                break;
            case BotFill.Fill:
                // Each side up to half the places (the spies take the odd one), never below its humans.
                int spySide = (maxPlayers + 1) / 2;
                int mercSide = maxPlayers / 2;
                wantSpies = Math.Max(0, spySide - spies);
                wantMercs = Math.Max(0, mercSide - mercs);
                break;
            default:
                return (0, 0);
        }

        // Too many for the places left: trim the larger side first, so the sides stay as even as they can.
        while (wantSpies + wantMercs > room)
        {
            if (wantSpies + spies > wantMercs + mercs && wantSpies > 0)
            {
                wantSpies--;
            }
            else if (wantMercs > 0)
            {
                wantMercs--;
            }
            else
            {
                wantSpies--;
            }
        }

        return (wantSpies, wantMercs);
    }

    /// <summary>The name of the <paramref name="n"/>th bot (from 1) of a side: SpyBot1, MercBot2.</summary>
    public static string NameOf(byte team, int n) =>
        (team == 0 ? "SpyBot" : "MercBot") + n.ToString(CultureInfo.InvariantCulture);

    /// <summary>The page's name of a bot setting.</summary>
    public static string FillName(BotFill fill) => FillNames[(int)fill];

    /// <summary>The "Bots" row's choices in page order.</summary>
    public static IReadOnlyList<string> FillNames { get; } = ["Off", "Per Side", "Fill Slots"];

    /// <summary>The "Bot Skill" row's choices in page order (AiSkill).</summary>
    public static IReadOnlyList<string> SkillNames { get; } = ["Easy", "Normal", "Hard", "Expert"];
}
