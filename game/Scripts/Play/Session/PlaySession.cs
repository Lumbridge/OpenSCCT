using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Bots;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Camera;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScctGame.Play.Session;

/// <summary>A camera result of one client tick: the view the calc view produced and the FOV.</summary>
public readonly record struct ViewSample(long Tick, FVector Location, FRotator Rotation, float Fov, bool Valid);

/// <summary>
/// An offline match through the online path: a listen <see cref="ServerHost"/> with the map (loaded by
/// <see cref="PlayMapLoader"/> through a <see cref="MapCatalog"/>) and the SBase game rules, and the local player as a
/// <see cref="ClientSession"/> over loopback. The server logs the player in through the ported game flow (Login,
/// team, spawn selection) when the connection joins, and a network match is started by its host as the lobby
/// does. The client runs the local controller with the input of each tick and the calc view after it.
/// <para>Godot-free: the game's play scene, the smoke tests and tools drive it the same way.</para>
/// </summary>
public sealed class PlaySession : IDisposable
{
    public const string HostAddress = ListenServer.HostAddress;

    /// <summary>When the caller falls further behind than this (a stall), the backlog is dropped.</summary>
    public const int MaxCatchUpTicks = 5;

    private readonly ListenServer? _listen;
    private readonly LoopbackNetwork? _network;
    private readonly bool _remote;
    private readonly long _start;
    private readonly List<string> _log = new();
    private readonly object _logGate = new();
    private long _ticks;
    private ViewSample _view;
    private ViewSample _previousView;
    private Pawn? _possessed;
    private double _alpha;
    private bool _disposed;
    private NetCombatBridge? _clientCombat;
    private static int s_offlineSessions;

    /// <summary>An offline session (one with its own host, not a round on a remote server) runs on this machine.</summary>
    public static bool OfflineSessionRunning => System.Threading.Volatile.Read(ref s_offlineSessions) > 0;

    public PlaySession(PlaySettings settings, IClock clock)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        var roots = new List<string>(settings.MapRoots);
        roots.AddRange(MapCatalog.CreateDefault().Roots);
        Catalog = new MapCatalog(roots.ToArray());
        string folder = Catalog.FindFolder(settings.Map)
            ?? throw new FileNotFoundException($"Map '{settings.Map}' not found in: {string.Join("; ", roots)}");
        MapFolder = folder;
        SvMap map = SvMap.Load(folder);
        Loader = new PlayMapLoader(map, settings.GameOptions(map.Gameplay), PlayMapLoader.FindCharsDir(folder),
            settings.HostSettings(map.Gameplay));
        Loader.Register(Catalog);

        var server = new ServerSettings
        {
            Map = map.Id,
            MaxPlayers = 16,
            NetRate = settings.NetRate,
            Profile = NetProfile.Enhanced,
            MatchSeed = settings.MatchSeed,
            NetMode = ENetMode.NM_ListenServer,
            Maps = Catalog,
            SpawnWalkers = false,
        };
        // The client's gameplay calls as online (other players' pawns, device events, objectives); the map loader
        // installs the gameplay services itself.
        var client = new ClientSettings { PlayerName = settings.PlayerName, Maps = Catalog, Gameplay = true, InstallGameplayServices = false };

        // The bots' navigation is built (or loaded from its cache) before the host starts ticking.
        AiNavCache? nav = null;
        if (settings.Bots > 0)
        {
            nav = new AiNavCache();
            nav.Prepare(Catalog, map.Id);
        }

        _start = clock.NowMicroseconds;
        if (settings.HostMode == PlayHostMode.Thread)
        {
            _listen = new ListenServer(clock, server);
            Host = _listen.Host;
            AddBots(nav);
            Host.PlayerJoined += OnPlayerJoined;
            Client = _listen.ConnectLocalPlayer(_start, client);
        }
        else if (settings.SimLink is { } link)
        {
            SimLink = new SimLinkNetwork(clock, settings.MatchSeed, link);
            Host = new ServerHost(SimLink.Bind(HostAddress), server);
            AddBots(nav);
            Host.PlayerJoined += OnPlayerJoined;
            Client = new ClientSession(SimLink.Bind("local-player-1"), HostAddress, _start, client, ownsTransport: true);
        }
        else
        {
            _network = new LoopbackNetwork();
            Host = new ServerHost(_network.Bind(HostAddress), server);
            AddBots(nav);
            Host.PlayerJoined += OnPlayerJoined;
            Client = new ClientSession(_network.Bind("local-player-1"), HostAddress, _start, client, ownsTransport: true);
        }

        Client.World.Level.Info.m_dT = Sim.DeltaTime;
        InstallCombatNet();
        if (settings.DemoFolder is { Length: > 0 } demos)
        {
            RecordDemo(demos, map.Id);
        }

        // The zone hints (SZoneTips) show for the local player, as in the original's standalone game.
        System.Threading.Interlocked.Increment(ref s_offlineSessions);
    }

    /// <summary>The demo file this match records into (<see cref="PlaySettings.DemoFolder"/>), or null.</summary>
    public string? DemoPath { get; private set; }

    /// <summary>
    /// Records the match as a demo (docs/DEMOS.md) from the host's first tick that has the game rules, on the host's
    /// thread; the host finishes the file when it is disposed.
    /// </summary>
    private void RecordDemo(string folder, string mapId)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        string path = Path.Combine(folder, start.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) +
            "-" + mapId + DemoFile.Extension);
        DemoPath = path;
        bool started = false;
        Host.BeforeWorldTick += host =>
        {
            if (started || Loader.Game is not { } game)
            {
                return;
            }

            started = true;
            DemoWriter? writer = null;
            try
            {
                writer = new DemoWriter(path, new DemoHeader
                {
                    GameVersion = OpenScct.Net.Discovery.BuildInfo.Version,
                    ProtocolHash = host.Driver.ProtocolHash,
                    ContentHash = host.Map.ContentHash,
                    Map = mapId,
                    GameOptions = Loader.GameOptions,
                    ServerName = "Offline",
                    StartUnixMs = start.ToUnixTimeMilliseconds(),
                    TickRate = Sim.Hz,
                    NetRate = host.NetRate,
                });
                host.StartRecording(writer, host.Driver.Time, game);
                AddLog("recording demo " + path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                writer?.Dispose();
                DemoPath = null;
                AddLog("demo not recorded: " + e.Message);
            }
        };
    }

    /// <summary>
    /// The combat and equipment calls between the host and the local player (inventory, gadgets, damage). The host's
    /// game rules come from the map loader rather than the server's own options, so neither session installed the
    /// bridge: it is added on both machines here and pumped once a tick. The server's is installed on its own first
    /// tick (a listen server thread is already running), before any player can log in.
    /// </summary>
    private void InstallCombatNet()
    {
        NetCombatBridge? server = null;
        Host.BeforeWorldTick += host =>
        {
            if (server is null && host.Combat is null && host.World.Level.GetService<NetCombatBridge>() is null)
            {
                server = NetCombatBridge.Install(host.World.Level, host.Driver);
            }

            server?.Pump();

            // The server's sounds to the players who hear them (ClientHearSound), as the game rules' host would install.
            NetHearSound.InstallServer(host.World, host.Driver).RelayLate();
        };

        _clientCombat = Client.World.Level.GetService<NetCombatBridge>() ?? NetCombatBridge.Install(Client.World.Level, Client.Driver);
        InstallClientCalls(Client);
    }

    /// <summary>
    /// The server's calls to the local controller that the play client runs itself: ClientSetHUD gives the player
    /// its SHud (the gadget views, HUD messages and perturbation live on it). Other calls go on to the fallback.
    /// </summary>
    private static void InstallClientCalls(ClientSession client)
    {
        NetFunctionHandler? previous = client.Driver.Functions.Fallback;
        client.Driver.Functions.Fallback = (self, function, args, from) =>
        {
            if (!client.Driver.IsServer && self is SPlayerController pc && function.Name == "ClientSetHUD")
            {
                GameCall call = NetGameHost.ToGameCall(function, args);
                pc.ClientSetHUD(call.Class, call.Class2);
                return;
            }

            previous?.Invoke(self, function, args, from);
        };
    }

    /// <summary>
    /// A round on a remote server (online play): <paramref name="client"/> is the online visit's session of the map in
    /// <paramref name="mapFolder"/>, loaded through <paramref name="catalog"/>. This session ticks it with the local
    /// input and the calc view as for an offline match, has no host of its own, and leaves the client session to the
    /// visit when disposed.
    /// </summary>
    public PlaySession(PlaySettings settings, IClock clock, ClientSession client, MapCatalog catalog, string mapFolder, PlayMapLoader loader)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        MapFolder = mapFolder ?? throw new ArgumentNullException(nameof(mapFolder));
        Loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _remote = true;
        _start = clock.NowMicroseconds;
        Client.World.Level.Info.m_dT = Sim.DeltaTime;
        InstallClientCalls(Client);
    }

    public PlaySettings Settings { get; }

    /// <summary>The simulated network of <see cref="PlaySettings.SimLink"/>, or null.</summary>
    public SimLinkNetwork? SimLink { get; }

    /// <summary>True for a round on a remote server (no host here).</summary>
    public bool IsRemote => _remote;

    public IClock Clock { get; }

    public MapCatalog Catalog { get; }

    public string MapFolder { get; }

    public PlayMapLoader Loader { get; }

    /// <summary>
    /// The server. With <see cref="PlayHostMode.Thread"/> it belongs to its thread: read it only through
    /// <see cref="ServerSnapshot"/> or after <see cref="Stop"/>. Null for a remote round.
    /// </summary>
    public ServerHost Host { get; } = null!;

    public ClientSession Client { get; }

    /// <summary>The input applied to the local controller at the next tick (the input layer fills it).</summary>
    public LocalInput Input { get; } = new();

    public MouseLook Mouse { get; set; } = new();

    /// <summary>The viewport size the input mapping scales the mouse by.</summary>
    public (int Width, int Height) ViewportSize { get; set; } = (1280, 720);

    /// <summary>Called on the client's thread before each client tick, before the input is applied (scripted input).</summary>
    public Action<PlaySession, long>? BeforeClientTick { get; set; }

    /// <summary>Called on the client's thread after each client tick and its calc view (views take their tick state here).</summary>
    public Action<PlaySession, long>? AfterClientTick { get; set; }

    /// <summary>How far the clock is into the next tick after <see cref="Update"/> (0 to 1), for the render interpolation.</summary>
    public float Alpha => (float)_alpha;

    /// <summary>The local player's controller on the client, once replicated.</summary>
    public SPlayerController? LocalController => Client.World.Level.Info.LocalPlayerController as SPlayerController;

    /// <summary>The local pawn on the client, once replicated and possessed.</summary>
    public SBasePawn? LocalPawn => LocalController?.Pawn as SBasePawn;

    /// <summary>Client ticks run so far.</summary>
    public long TicksRun => _ticks;

    /// <summary>
    /// Replaces the calc view after each client tick when set (demo playback's cameras, docs/DEMOS.md): it gets the
    /// calc view (invalid when there is no local controller) and returns the view to draw.
    /// </summary>
    public Func<ViewSample, ViewSample>? ViewOverride { get; set; }

    /// <summary>
    /// When set, the pawn drawn as seen from inside (body hidden) instead of the local first-person pawn: demo
    /// playback's first-person view of another player. Returns null for none.
    /// </summary>
    public Func<SBasePawn?>? FirstPersonOverride { get; set; }

    /// <summary>Draws <paramref name="view"/> from now on without waiting for a tick (a paused demo's free camera).</summary>
    public void SetViewNow(ViewSample view)
    {
        _previousView = view;
        _view = view;
    }

    /// <summary>The view after the last client tick.</summary>
    public ViewSample View => _view;

    /// <summary>The view after the tick before it (the render interpolates between the two).</summary>
    public ViewSample PreviousView => _previousView;

    /// <summary>The server's login of the local player failed with this reason, or null.</summary>
    public string? LoginError { get; private set; }

    /// <summary>The server-side controller of the local player (server thread data).</summary>
    public SPlayerController? ServerController { get; private set; }

    public bool IsStopped { get; private set; }

    /// <summary>The listen server's failure, if its thread stopped.</summary>
    public Exception? ServerFailure => _listen?.Failure;

    /// <summary>Commands the input asked for that the port does not have.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_logGate)
            {
                return _log.ToArray();
            }
        }
    }

    /// <summary>Runs the ticks due by the clock (server and client inline, the client only with a server thread).</summary>
    public int Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStopped)
        {
            return 0;
        }

        long now = Clock.NowMicroseconds;
        long due = (now - _start) * Sim.Hz / 1_000_000;
        if (due - _ticks > MaxCatchUpTicks)
        {
            _ticks = due - MaxCatchUpTicks;
        }

        int run = 0;
        while (_ticks < due)
        {
            Step(now);
            run++;
        }

        double exact = (now - _start) * (double)Sim.Hz / 1_000_000.0;
        _alpha = Math.Clamp(exact - _ticks, 0.0, 1.0);
        return run;
    }

    /// <summary>One tick now (inline server first), whatever the clock says. For tests and tools.</summary>
    public void Step(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listen is null && !_remote)
        {
            Host.World.Level.Info.m_dT = Sim.DeltaTime * Host.World.Level.Info.TimeDilation;
            Host.Tick(nowMicros);
        }

        TickClient(nowMicros);
        _ticks++;
        _alpha = 1.0;
    }

    /// <summary>Reads server state on the server's thread: at once inline, or between ticks after <see cref="Stop"/>.</summary>
    public T ServerSnapshot<T>(Func<ServerHost, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_remote)
        {
            throw new InvalidOperationException("A remote round has no server in this process.");
        }

        if (_listen is not null && !IsStopped)
        {
            throw new InvalidOperationException("The listen server runs on its own thread; stop it first or use the inline host.");
        }

        return read(Host);
    }

    /// <summary>Stops the server thread (inline: no further server ticks).</summary>
    public void Stop()
    {
        if (IsStopped)
        {
            return;
        }

        IsStopped = true;
        _listen?.Stop();
    }

    private void TickClient(long now)
    {
        Level level = Client.World.Level;
        level.Info.m_dT = Sim.DeltaTime * level.Info.TimeDilation;
        BeforeClientTick?.Invoke(this, Client.LocalTick);
        if (LocalController is { } pc)
        {
            Input.ApplyButtons(pc);
            Mouse.Apply(pc, Input.MouseX, Input.MouseY, ViewportSize.Width, ViewportSize.Height);
            pc.aUp = Input.Up;
            if (pc.FreeCameraFlying != 0)
            {
                // The cinematic free camera: the wheel (or the pad's shoulder buttons) steps its speed, Shift and Alt
                // scale it, the pad's triggers fly it up and down.
                pc.StepFreeCamSpeed(Input.Wheel);
                pc.FreeCamSpeedFactor = Input.Fast ? CinematicCameraK.FastFactor : Input.Slow ? CinematicCameraK.SlowFactor : 1f;
                pc.FreeCamPadUp = Input.PadUp;
            }

            foreach (InputCommand command in Input.Commands)
            {
                if (!PlayerCommands.Execute(pc, command))
                {
                    AddLog("unsupported command: " + command);
                }
            }
        }

        Input.EndTick();
        Client.Tick(now);
        if (!ReferenceEquals(_clientCombat, Client.Combat))
        {
            _clientCombat?.Pump();
        }
        RestoreBehindView();
        _previousView = _view;
        if (LocalController is { } viewer)
        {
            PlayerCalcView.FrameView(viewer, out _, out FVector location, out FRotator rotation);
            _view = new ViewSample(Client.LocalTick, location, rotation, viewer.FovAngle, true);
        }

        if (ViewOverride is { } viewOverride)
        {
            _view = viewOverride(LocalController is null ? default : _view);
        }

        AfterClientTick?.Invoke(this, Client.LocalTick);
    }

    /// <summary>
    /// The view mode of a new possession, as ClientRestart sets it (bBehindView = Pawn.PointOfView()). Over loopback
    /// the restart call reaches the client before the pawn's channel, so the controller waits for its pawn and
    /// WaitingForPawn.EndState clears the flag right after the late restart set it; the spy would then be drawn in
    /// first person. Applied once per possession on the client.
    /// </summary>
    private void RestoreBehindView()
    {
        if (LocalController is not { } pc || pc.Pawn is not { } pawn || ReferenceEquals(pawn, _possessed))
        {
            if (LocalController?.Pawn is null)
            {
                _possessed = null;
            }

            return;
        }

        if (pc.IsInState("WaitingForPawn"))
        {
            return;
        }

        _possessed = pawn;
        pc.bBehindView = pawn.PointOfView();
    }

    /// <summary>
    /// The server-side bots of <see cref="PlaySettings.Bots"/> (docs/AI.md): they log in before the local player,
    /// whose login starts the match, and play through the same input path. Server thread data.
    /// </summary>
    public ServerBots? Bots { get; private set; }

    private void AddBots(AiNavCache? nav)
    {
        if (Settings.Bots <= 0)
        {
            return;
        }

        AiDifficulty difficulty = AiDifficulty.TryParse(Settings.Difficulty, out AiSkill skill) ? AiDifficulty.For(skill) : AiDifficulty.For(AiSkill.Normal);
        Bots = new ServerBots(Host, new ServerBotSettings
        {
            Bots = ServerBotSettings.Fill(Settings.Bots, Settings.Team, difficulty),
            Nav = nav,
            Maps = Catalog,
            TerminalsBlockPlayers = Settings.TerminalsBlockPlayers ?? true,
        });
    }

    /// <summary>Server thread: the connection joined, so the player logs in (UE2 SpawnPlayActor) and the host starts.</summary>
    private void OnPlayerJoined(PlayerSlot slot)
    {
        Bots?.LoginAll();
        SGameInfo? game = Loader.Game;
        if (game is null)
        {
            LoginError = "the map has no game rules";
            slot.Connection.Close(LoginError);
            return;
        }

        string error = string.Empty;
        PlayerController? pc = game.Login(string.Empty, Settings.LoginOptions, ref error);
        if (pc is null)
        {
            LoginError = string.IsNullOrEmpty(error) ? "login refused" : error;
            slot.Connection.Close(LoginError);
            return;
        }

        // ULevel::SpawnPlayActor: the connection's player, the roles, the connection's actor, then PostLogin.
        pc.Player = new SimNetConnection();
        pc.Role = ENetRole.ROLE_Authority;
        pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        slot.Connection.Actor = pc;
        game.PostLogin(pc);
        ServerController = pc as SPlayerController;

        // A network match from the lobby: the host launches it (StartMatchOnServer). Play Level started at login.
        if (Settings.Rules == PlayRules.MapSettings && ServerController is { } host && !(game.GameReplicationInfo?.bMatchHasBegun ?? false))
        {
            game.bDelayedStart = false;
            host.StartMatchOnServer();
        }
    }

    private void AddLog(string line)
    {
        lock (_logGate)
        {
            if (_log.Count < 256)
            {
                _log.Add(line);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        if (_remote)
        {
            return;
        }

        System.Threading.Interlocked.Decrement(ref s_offlineSessions);
        Bots?.Dispose();
        Client.Dispose();
        if (_listen is not null)
        {
            _listen.Dispose();
        }
        else
        {
            Host.Dispose();
        }
    }
}
