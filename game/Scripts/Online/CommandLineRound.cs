using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.Play;

namespace OpenScctGame.Online;

/// <summary>
/// An online round started from the command line with no menus, for test runs with a host and a connected client
/// (<c>--svm-lan-host=PORT</c> with <c>--svm-play=Map</c> and <c>--mode</c>, or <c>--svm-join=ADDRESS</c>): the host
/// serves the map loopback only and unlisted with the lobby's rules for the mode and no bots, the client joins; each
/// takes <c>--side</c>'s team and gets ready, and the host launches once the lobby holds the players it waits for. Both
/// print what happens as <c>SVM_NET</c> lines: hosting, joined, the lobby, the round loaded, then every 10 s the pawns
/// their own world holds (so a client shows what it was sent). These runs use the default settings, signed out
/// (<see cref="OpenScctGame.Options.SettingsStore.Isolated"/>): no player name, account or demo recording of the
/// person at the machine.
/// </summary>
public partial class CommandLineRound : Node
{
    public const string Prefix = "SVM_NET";

    private const double PawnReportSeconds = 10;

    private bool _started;
    private bool _launched;
    private bool _loaded;
    private OpenScct.Server.Online.OnlineClientState? _state;
    private string? _lobby;
    private double _sincePawns;

    /// <summary>The launch options the round is set up from.</summary>
    public LaunchOptions Options { get; init; } = LaunchOptions.Empty;

    private bool IsHost => Options.NetHostPort is not null;

    private int Team => Options.PlaySide is "merc" or "mercs" or "1" ? 1 : 0;

    /// <summary>Players the host waits for before launching: <c>--players</c>, else 2 (the host and one client).</summary>
    private int Players => Options.PlayPlayers > 0 ? Options.PlayPlayers : 2;

    public override void _Process(double delta)
    {
        OnlineService online = OnlineService.Get(GetTree());
        if (!_started)
        {
            _started = true;
            Start(online);
            return;
        }

        if (online.Client is not { } client)
        {
            return;
        }

        if (client.State != _state)
        {
            _state = client.State;
            Report($"state {client.State}" + (client.State == OpenScct.Server.Online.OnlineClientState.Failed ? $": {client.Error}" : ""));
        }

        if (client.State == OpenScct.Server.Online.OnlineClientState.Lobby && client.Lobby.Local is { } local)
        {
            string lobby = string.Join(", ", client.Lobby.Players.Select(p => $"{p.Name} ({(p.Team == 0 ? "spy" : "merc")}{(p.Ready ? ", ready" : "")})"));
            if (lobby != _lobby)
            {
                // One request per lobby change: the host's answer changes the lobby again.
                _lobby = lobby;
                Report($"lobby: {lobby}");
                if (local.Team != Team)
                {
                    client.SetTeam(Team);
                }
                else if (!IsHost && !local.Ready)
                {
                    client.SetReady(true);
                }
            }

            if (IsHost && !_launched && local.Team == Team && client.Lobby.Players.Count >= Players &&
                client.Lobby.Players.Where(p => p.Id != local.Id).All(p => p.Ready))
            {
                Report($"launching {client.Lobby.Setup.Map} with {client.Lobby.Players.Count} players");
                client.Launch();
                _launched = true;
            }
        }

        if (GetTree().CurrentScene is PlayRoot { IsLoaded: true, Session: { } session })
        {
            if (!_loaded)
            {
                _loaded = true;
                _sincePawns = PawnReportSeconds;
                Report($"loaded {client.Lobby.Setup.Map} as {(Team == 0 ? "spy" : "merc")}");
            }

            _sincePawns += delta;
            if (_sincePawns >= PawnReportSeconds)
            {
                _sincePawns = 0;
                var pawns = new List<SBasePawn>();
                foreach (SBasePawn pawn in session.Client.World.Level.AllActors<SBasePawn>())
                {
                    pawns.Add(pawn);
                }

                Report($"pawns {pawns.Count}: " + string.Join("; ", pawns.Select(p =>
                    $"{p.PlayerReplicationInfo?.PlayerName ?? p.GetType().Name} ({(p is SPawnAttaque ? "spy" : "merc")}) at " +
                    string.Create(CultureInfo.InvariantCulture, $"({p.Location.X:0},{p.Location.Y:0},{p.Location.Z:0})"))));
            }
        }
    }

    private void Start(OnlineService online)
    {
        if (Options.NetHostPort is { } port)
        {
            if (Options.PlayMap is not { } map)
            {
                Report("could not host: --svm-lan-host needs --svm-play=Map");
                return;
            }

            if (OpenScct.Server.MapCatalog.ModeOfKey(Options.PlayMode) is not { } mode)
            {
                Report($"could not host: unknown mode '{Options.PlayMode}'");
                return;
            }

            int capacity = Math.Max(Players, LobbySetup.MinMaxPlayers);
            LobbySetup setup = LobbySetup.ForMap(map, mode, online.Catalog.Maps.GetMapSettings(map), capacity) with { TimeLimit = 0, Bots = BotFill.Off };
            bool ok = online.Host(new HostRequest
            {
                Name = "Test round", Port = port, MaxPlayers = capacity, Listed = false, Upnp = false, LoopbackOnly = true, LanPort = 0,
                Setup = setup,
            });
            Report(ok ? $"hosting {map} on port {port}, waiting for {Players} players" : $"could not host: {online.LastError}");
        }
        else if (Options.NetJoin is { } address)
        {
            online.Join(JoinTarget.FromAddress(address));
            Report($"joining {address}");
        }
    }

    private static void Report(string line) => GD.Print($"{Prefix} {line}");
}
