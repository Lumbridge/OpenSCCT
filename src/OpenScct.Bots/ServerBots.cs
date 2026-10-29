using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>One server bot: its name, team (0 spies, 1 mercs) and difficulty.</summary>
public sealed record ServerBotSpec(string Name, int Team, AiDifficulty Difficulty)
{
    /// <summary>More login options after the name and team (an online series' <c>?GamesInARow=</c>, for the revenge swap).</summary>
    public string ExtraOptions { get; init; } = string.Empty;
}

/// <summary>The bots a host adds, and where their map data comes from.</summary>
public sealed record ServerBotSettings
{
    public IReadOnlyList<ServerBotSpec> Bots { get; init; } = [];

    /// <summary>The navigation cache (shared by the processes' matches); null makes one over the default folder.</summary>
    public AiNavCache? Nav { get; init; }

    /// <summary>Finds the map's folder for its lighting (render/lighting.json); null uses the default catalog.</summary>
    public MapCatalog? Maps { get; init; }

    /// <summary>
    /// Terminals block players as the map says (default). False turns their player blocking off, for the converted
    /// terminal collision that keeps a walker short of the use spot (docs/NETCODE.md, "Bot matches").
    /// </summary>
    public bool TerminalsBlockPlayers { get; init; } = true;

    /// <summary>
    /// <paramref name="count"/> bots filling a 2v2 (or larger) match next to one human on <paramref name="humanTeam"/>:
    /// the human's side gets one bot fewer.
    /// </summary>
    public static IReadOnlyList<ServerBotSpec> Fill(int count, int humanTeam, AiDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(difficulty);
        var bots = new List<ServerBotSpec>();
        int[] onTeam = [humanTeam == 0 ? 1 : 0, humanTeam == 1 ? 1 : 0];
        int[] named = [0, 0];
        for (int i = 0; i < count; i++)
        {
            int team = onTeam[0] <= onTeam[1] ? 0 : 1;
            onTeam[team]++;
            named[team]++;
            string name = (team == 0 ? "SpyBot" : "MercBot") + named[team].ToString(CultureInfo.InvariantCulture);
            bots.Add(new ServerBotSpec(name, team, difficulty));
        }

        return bots;
    }

    /// <summary>Two spies and two mercs at <paramref name="spies"/> and <paramref name="mercs"/> difficulty.</summary>
    public static IReadOnlyList<ServerBotSpec> TwoVersusTwo(AiDifficulty spies, AiDifficulty mercs) =>
    [
        new ServerBotSpec("SpyBot1", 0, spies),
        new ServerBotSpec("SpyBot2", 0, spies),
        new ServerBotSpec("MercBot1", 1, mercs),
        new ServerBotSpec("MercBot2", 1, mercs),
    ];
}

/// <summary>
/// Server-side bots on a <see cref="ServerHost"/> with the game rules: each logs in through the game's own Login and
/// PostLogin as a player of this machine (an <see cref="AiViewport"/> player, never the level's local player), reports
/// waiting to play like a loaded client, and before every world tick its <see cref="AiController"/> writes the input
/// bytes, view and key presses on its controller. The pawn then moves and acts by the same rules as a human player's:
/// the input sampling, the movement states, the combat calls. Everything runs on the host's thread.
/// </summary>
public sealed class ServerBots : IDisposable
{
    private readonly List<AiController> _bots = new();
    private readonly List<string> _log = new();
    private bool _loggedIn;
    private bool _disposed;

    public ServerBots(ServerHost host, ServerBotSettings settings)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Nav = settings.Nav ?? new AiNavCache();
        Host.BeforeWorldTick += OnBeforeWorldTick;
    }

    public ServerHost Host { get; }

    public ServerBotSettings Settings { get; }

    public AiNavCache Nav { get; }

    /// <summary>The shared bot world, once made (first tick or <see cref="LoginAll"/>).</summary>
    public AiWorld? World { get; private set; }

    public IReadOnlyList<AiController> Bots => _bots;

    /// <summary>Login failures and other problems.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>
    /// Makes the bot world (navigation, light, noise) and logs every bot in now; on the host's thread. A listen host
    /// calls it before its own player's login starts the match, so the bots play the round rather than watch it.
    /// </summary>
    public void LoginAll()
    {
        if (_loggedIn)
        {
            return;
        }

        _loggedIn = true;

        // The host's own game rules, or those a map loader spawned into the level (the play session's).
        if ((Host.Game ?? Host.World.Level.Info.Game as SGameInfo) is not { } game || Host.Map.Physics is not { } physics)
        {
            _log.Add("no game rules or collision: bots not added");
            return;
        }

        if (!Settings.TerminalsBlockPlayers)
        {
            BotMatch.UnblockTerminalsOf(Host.World);
        }

        // The bots play with the host's ability switches as they are, like the people: the settings object starts
        // from InitPersistentData (USPersistentData.Init) and PostLogin copies it into the replicated game info.
        byte mask = Host.World.Level.Info.GEActivationMask;
        NavGraph spyNav = Nav.Get(Host.Map, spy: true, mask);
        NavGraph mercNav = Nav.Get(Host.Map, spy: false, mask);
        MapCatalog maps = Settings.Maps ?? MapCatalog.CreateDefault();
        AiLightField light = AiMapLight.Load(maps.FindFolder(Host.Map.Name), physics.Collision, Host.Map.Loaded);
        spyNav.ApplyLight(light);
        mercNav.ApplyLight(light);
        World = new AiWorld(Host.World.Level, physics.Collision, spyNav, mercNav, light);
        AiWorld.Install(World);
        for (int i = 0; i < Settings.Bots.Count; i++)
        {
            ServerBotSpec spec = Settings.Bots[i];
            if (Login(game, spec) is { } pc)
            {
                _bots.Add(new AiController(World, pc, spec.Difficulty, i));
            }
        }
    }

    private SPlayerController? Login(SGameInfo game, ServerBotSpec spec)
    {
        string error = string.Empty;
        string options = "?Name=" + spec.Name + "?Team=" + spec.Team.ToString(CultureInfo.InvariantCulture) + spec.ExtraOptions;
        PlayerController? pc = game.Login(string.Empty, options, ref error);
        if (pc is not SPlayerController spc)
        {
            _log.Add($"{spec.Name}: login failed: {error}");
            pc?.Destroy();
            return null;
        }

        // ULevel::SpawnPlayActor for a player of this machine, without making it the level's local player.
        spc.Player = new AiViewport();
        spc.Role = ENetRole.ROLE_Authority;
        spc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        game.PostLogin(spc);

        // The loading page's report of a loaded client (the dedicated start gate waits for it).
        spc.PlayerWaitingToPlayToServer(true);
        return spc;
    }

    private void OnBeforeWorldTick(ServerHost host)
    {
        if (!_loggedIn)
        {
            LoginAll();
        }

        if (!Settings.TerminalsBlockPlayers)
        {
            BotMatch.UnblockTerminalsOf(host.World);
        }

        foreach (AiController bot in _bots)
        {
            bot.Tick();
        }
    }

    /// <summary>The bot of <paramref name="controller"/>, or null.</summary>
    public AiController? Of(PlayerController controller)
    {
        foreach (AiController b in _bots)
        {
            if (ReferenceEquals(b.Controller, controller))
            {
                return b;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Host.BeforeWorldTick -= OnBeforeWorldTick;
    }
}
