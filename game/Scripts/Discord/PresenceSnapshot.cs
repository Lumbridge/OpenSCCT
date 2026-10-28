using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Online;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Discord;

/// <summary>
/// What the player's Discord status says (docs/DISCORD.md). Records compare by value, so the presence node sends an
/// update only when something shown changed.
/// </summary>
public sealed record PresenceState
{
    /// <summary>The first line ("Story on Steel Squat"). A new line restarts the elapsed timer.</summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>The second line ("Spy · Spies 2 – 1 Mercs").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>"spy", "merc" or null: the small image in the corner of the logo.</summary>
    public string? Side { get; init; }

    /// <summary>The party (players on the server); null when the player is not on one.</summary>
    public string? PartyId { get; init; }

    public int PartySize { get; init; }

    public int PartyMax { get; init; }

    /// <summary>The join secret for friends, or null when this game cannot be joined from Discord.</summary>
    public DiscordJoinSecret? Join { get; init; }
}

/// <summary>Reads the game's current state (the scene, the online visit, the hub, the match) into a <see cref="PresenceState"/>.</summary>
public static class PresenceSnapshot
{
    public static PresenceState Build(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Node? scene = tree.CurrentScene;
        var round = scene as PlayRoot;

        if (tree.Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName) is { Client: { } client } online)
        {
            return Online(online, client, round);
        }

        if (tree.Root.GetNodeOrNull<HubService>(HubService.NodeName) is { Client: { } hub } hubService)
        {
            return Hub(hubService, hub, round);
        }

        if (round?.Session is { } session)
        {
            return Offline(session);
        }

        return new PresenceState
        {
            Details = scene switch
            {
                ServerBrowserScreen => "Browsing servers",
                HostScreen => "Setting up a game",
                LobbyScreen => "Setting up an offline match",
                HubScreen => "Looking for a hub",
                Options.OptionsMenu => "In the options",
                ResultsScreen => "Looking at the results",
                _ => "In the menus",
            },
        };
    }

    private static PresenceState Online(OnlineService online, OnlineClient client, PlayRoot? round)
    {
        LobbyState lobby = client.Lobby;
        string server = lobby.ServerName.Length > 0 ? lobby.ServerName : client.Target.Name;
        string game = Game(lobby.Setup.Mode, MapTitles.Of(online.Catalog.Maps, client.CurrentMap ?? lobby.Setup.Map));
        DiscordJoinSecret? join = online.InHubMatch ? null : OnlineJoin(online, client);
        var state = new PresenceState
        {
            PartyId = PartyId(join?.ToString() ?? "server:" + server),
            PartySize = Math.Max(1, lobby.HumanCount),
            PartyMax = Math.Max(lobby.MaxPlayers, lobby.HumanCount),
            Join = join,
        };

        switch (client.State)
        {
            case OnlineClientState.Lobby:
                return state with
                {
                    Details = (online.Hosting is not null ? "Hosting " : "In the lobby of ") + server,
                    State = game,
                };
            case OnlineClientState.Match when round?.Session is { } session:
                (string? side, string line) = MatchLine(session);
                return state with { Details = game, State = line + " · " + server, Side = side };
            case OnlineClientState.Results:
                return state with { Details = "Match over on " + server, State = game };
            default:
                return state with { Details = "Joining " + server, State = game };
        }
    }

    private static PresenceState Hub(HubService service, HubClient hub, PlayRoot? round)
    {
        string name = hub.ServerName.Length > 0 ? hub.ServerName : service.Address;
        DiscordJoinSecret? join = service.Host is null && DiscordJoinSecret.Shareable(service.Address)
            ? new DiscordJoinSecret(DiscordJoinKind.Hub, service.Address)
            : null;
        int players = 1;
        if (hub.LocalPlayer?.GameReplicationInfo is { } gri)
        {
            players = Math.Max(1, CountPlayers(gri));
        }

        string? side = hub.Team switch
        {
            0 => "spy",
            1 => "merc",
            _ => null,
        };
        return new PresenceState
        {
            Details = "In the hub: " + name,
            State = round is null ? "Arriving" : side == "merc" ? "Mercenary" : side == "spy" ? "Spy" : "Looking around",
            Side = round is null ? null : side,
            PartyId = PartyId(join?.ToString() ?? "hub:" + name),
            PartySize = players,
            PartyMax = Math.Max(hub.MaxPlayers, players),
            Join = join,
        };
    }

    private static PresenceState Offline(PlaySession session)
    {
        PlaySettings settings = session.Settings;
        (string? side, string line) = MatchLine(session);
        return new PresenceState
        {
            Details = "Offline: " + Game(settings.GameMode, MapTitles.Of(session.Catalog, settings.Map)),
            State = line + (settings.Bots > 0 ? " · vs bots" : string.Empty),
            Side = side,
        };
    }

    /// <summary>The player's side and the team scores in a running match: ("spy", "Spy · Spies 2 – 1 Mercs").</summary>
    private static (string? Side, string Line) MatchLine(PlaySession session)
    {
        SPlayerController? pc = session.LocalController;
        PlayerReplicationInfo? own = pc?.PlayerReplicationInfo;
        if (own is { bOnlySpectator: true })
        {
            return (null, "Spectating");
        }

        int team = own?.Team?.TeamIndex ?? session.Settings.Team;
        string side = team == 1 ? "merc" : "spy";
        string line = team == 1 ? "Mercenary" : "Spy";
        if ((pc?.GameReplicationInfo ?? pc?.Level.GRI) is { } gri && gri.Teams[0] is { } spies && gri.Teams[1] is { } mercs)
        {
            line += string.Create(CultureInfo.InvariantCulture, $" · Spies {(int)spies.Score} – {(int)mercs.Score} Mercs");
        }

        return (side, line);
    }

    private static int CountPlayers(GameReplicationInfo gri)
    {
        int count = 0;
        foreach (PlayerReplicationInfo? pri in gri.PRIArray)
        {
            if (pri is { bDeleteMe: false, bOnlySpectator: false })
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// How friends join this visit: a listed server by its master id (as the browser joins), else the address the
    /// player typed, else for the player's own server its master id or public address. A server with a password says
    /// so in the secret (the password stays here: the friend's game asks for it).
    /// </summary>
    private static DiscordJoinSecret? OnlineJoin(OnlineService online, OnlineClient client)
    {
        if (online.Hosting is { } host)
        {
            bool locked = host.Request.Password is { Length: > 0 };
            MasterLink? master = host.Server.Master;
            if (master?.ServerId is { Length: > 0 } id)
            {
                return new DiscordJoinSecret(DiscordJoinKind.Master, id, locked);
            }

            return master?.PublicAddress is { } address && DiscordJoinSecret.Shareable(address)
                ? new DiscordJoinSecret(DiscordJoinKind.Address, address, locked)
                : null;
        }

        bool password = client.Settings.Password is { Length: > 0 };
        JoinTarget target = client.Target;
        if (target.MasterId is { Length: > 0 } masterId)
        {
            return new DiscordJoinSecret(DiscordJoinKind.Master, masterId, password);
        }

        return target.Attempts is [{ Kind: ConnectKind.Direct, IsLan: false } attempt] && DiscordJoinSecret.Shareable(attempt.Address)
            ? new DiscordJoinSecret(DiscordJoinKind.Address, attempt.Address, password)
            : null;
    }

    private static string Game(EGameMode mode, string map)
    {
        string name = mode switch
        {
            EGameMode.GM_DiskHunt => "Disk Hunt",
            EGameMode.GM_DeathMatch => "Deathmatch",
            EGameMode.GM_MercDeathMatch => "Merc Deathmatch",
            EGameMode.GM_Extraction => "Extraction",
            EGameMode.GM_Sabotage => "Sabotage",
            EGameMode.GM_Neutralization => "Neutralization",
            _ => "Story",
        };
        return map.Length > 0 ? name + " on " + map : name;
    }

    /// <summary>A party id that names the server without showing its address.</summary>
    private static string PartyId(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16).ToLowerInvariant();
}
