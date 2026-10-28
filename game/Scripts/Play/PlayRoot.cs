using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Cameras;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Match;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Views;
using ViewSource = OpenScct.Game.Camera.PlayerCalcView.EViewSource;

namespace OpenScctGame.Play;

/// <summary>What <see cref="PlayRoot"/> plays.</summary>
public sealed record PlayConfig
{
    public PlaySettings Settings { get; init; } = new();

    /// <summary>Builds the map's render and the views (off for sim-only runs).</summary>
    public bool Render { get; init; } = true;

    /// <summary>Adds the HUD (when the UI content exists).</summary>
    public bool Hud { get; init; } = true;

    /// <summary>Plays the match's sound (when the map's audio is converted).</summary>
    public bool Audio { get; init; } = true;

    /// <summary>
    /// 0 runs the simulation by the wall clock (real play). Above 0, each frame runs that many ticks on a virtual
    /// clock whatever the frame took (smoke runs: fast and deterministic).
    /// </summary>
    public int TicksPerFrame { get; init; }

    /// <summary>A scripted local player instead of the keyboard and mouse, or null.</summary>
    public ScriptedPilot? Pilot { get; init; }

    /// <summary>The original game folder the key layouts come from, or null for the default.</summary>
    public string? GameRoot { get; init; }

    /// <summary>
    /// Makes the session instead of an offline match (an online round: OpenScctGame.Online gives the visit's session).
    /// </summary>
    public Func<IClock, PlaySession>? CreateSession { get; init; }

    /// <summary>A demo being watched (docs/DEMOS.md): its cameras, playback bar and keys replace the player's input.</summary>
    public Demo.DemoPlayer? Demo { get; init; }

    /// <summary>A play of <see cref="LaunchOptions.PlayMap"/> with the launch options' side, mode, rules and host.</summary>
    public static PlayConfig FromOptions(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PlaySide side = options.PlaySide is "merc" or "mercs" or "1" ? PlaySide.Merc : PlaySide.Spy;
        PlayRules rules = options.PlayRules is "playlevel" or "play-level" or "editor" ? PlayRules.PlayLevel : PlayRules.MapSettings;
        PlayHostMode host = options.PlayHost == "inline" ? PlayHostMode.Inline : PlayHostMode.Thread;
        var settings = new PlaySettings
        {
            Map = options.PlayMap ?? "SteeE",
            Side = side,
            Mode = options.PlayMode,
            Rules = rules,
            HostMode = host,
            MapRoots = [.. options.MapRoots],
            Bots = options.PlayBots,
            Difficulty = options.PlayDifficulty,
        };
        if (options.PlayNetRate > 0)
        {
            settings = settings with { NetRate = options.PlayNetRate };
        }

        // A simulated bad link needs the host in this thread: the simulated network delivers when the client polls.
        if (options.PlaySimLink is { } link)
        {
            settings = settings with { HostMode = PlayHostMode.Inline, SimLink = SimLinkProfile.Get(link) };
        }

        if (options.PlayRecord)
        {
            settings = settings with { DemoFolder = OpenScctGame.Play.Demo.DemoLibrary.SavedFolder };
        }

        return new PlayConfig { Settings = settings, GameRoot = options.GameRoot };
    }
}

/// <summary>Per-frame costs of the play scene, in milliseconds.</summary>
public sealed class PlayFrameStats
{
    private readonly List<double> _sim = [];
    private readonly List<double> _view = [];
    private readonly List<double> _frame = [];

    public int Frames => _frame.Count;

    public long Ticks { get; internal set; }

    /// <summary>Milliseconds the scripted pilot took (route planning), kept out of the simulation cost.</summary>
    public double PilotMs { get; internal set; }

    /// <summary>The simulation milliseconds of the last frame.</summary>
    public double LastSimMs { get; private set; }

    /// <summary>The view milliseconds of the last frame.</summary>
    public double LastViewMs { get; private set; }

    internal void Add(double simMs, double viewMs, double frameMs)
    {
        LastSimMs = simMs;
        LastViewMs = viewMs;
        _sim.Add(simMs);
        _view.Add(viewMs);
        _frame.Add(frameMs);
    }

    /// <summary>Simulation (server and client ticks) and view (camera, pawns, devices) costs: mean, 95th percentile and max.</summary>
    public string Summary()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"frames={Frames} ticks={Ticks} pilot ms={PilotMs:F0} sim ms/frame mean={Mean(_sim):F2} p95={Percentile(_sim, 0.95):F2} max={Max(_sim):F2} view ms/frame mean={Mean(_view):F2} p95={Percentile(_view, 0.95):F2} max={Max(_view):F2} frame ms mean={Mean(_frame):F2}");
    }

    private static double Mean(List<double> v)
    {
        double sum = 0;
        foreach (double x in v)
        {
            sum += x;
        }

        return v.Count > 0 ? sum / v.Count : 0;
    }

    private static double Max(List<double> v)
    {
        double max = 0;
        foreach (double x in v)
        {
            max = Math.Max(max, x);
        }

        return max;
    }

    private static double Percentile(List<double> v, double p)
    {
        if (v.Count == 0)
        {
            return 0;
        }

        var sorted = new List<double>(v);
        sorted.Sort();
        return sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    }
}

/// <summary>
/// The offline play scene: a <see cref="PlaySession"/> (listen server with the map and the game rules, the local
/// player over loopback, logged in through the game flow), the map built by <see cref="MapBuilder"/> and drawn by a
/// <see cref="FaithfulView"/> placed by the simulation's calc view (<see cref="PlayCamera"/>), the pawns, devices
/// and the merc's first-person arms drawn from the client world, the HUD, and the keyboard and mouse
/// (<see cref="PlayInput"/>) or a <see cref="ScriptedPilot"/> feeding the local player's input each tick.
/// </summary>
public partial class PlayRoot : Node3D
{
    public const string ScenePath = "res://Scenes/Play.tscn";

    private const long TickMicros = 1_000_000 / Sim.Hz;

    private VirtualClock? _virtualClock;
    private Label? _status;
    private Label? _prompt;
    private int _lastRendMap = -1;
    private bool _lastBinoculars;
    private int _lastRemanenceCaptures;
    private double _pilotMs;
    private readonly Stopwatch _watch = new();
    private readonly LookAhead _look = new();
    private readonly HudBinocularAim _aim = new();
    private readonly DrawnTime _time = new();
    private float _alpha = 1f;

    public PlayConfig Config { get; private set; } = new();

    public PlaySession? Session { get; private set; }

    public RenderMap? Map { get; private set; }

    public FaithfulView? View { get; private set; }

    public PlayCamera? Camera { get; private set; }

    public PawnViews? Pawns { get; private set; }

    public DeviceViews? Devices { get; private set; }

    public FirstPersonView? FirstPerson { get; private set; }

    /// <summary>Muzzle flashes, tracers, impact marks and sparks (game/Scripts/Fx).</summary>
    public OpenScctGame.Fx.CombatFxView? Fx { get; private set; }

    /// <summary>The merc torch light (game/Scripts/Render/FlashLightView.cs).</summary>
    public FlashLightView? FlashLight { get; private set; }

    /// <summary>The merc laser beams (game/Scripts/Render/MercLaserView.cs).</summary>
    public MercLaserView? MercLasers { get; private set; }

    /// <summary>The spy pistols' laser dots and beam (game/Scripts/Fx/SpyLaserView.cs).</summary>
    public OpenScctGame.Fx.SpyLaserView? SpyLasers { get; private set; }

    /// <summary>The map's laser tripwire beams (game/Scripts/Fx/LaserTriggerView.cs).</summary>
    public OpenScctGame.Fx.LaserTriggerView? LaserTriggers { get; private set; }

    /// <summary>The sticky shock sparks on shocked pawns (game/Scripts/Fx/ShockFxView.cs).</summary>
    public OpenScctGame.Fx.ShockFxView? ShockFx { get; private set; }

    public PlayInput? Controls { get; private set; }

    public HudLayer? Hud { get; private set; }

    /// <summary>The In-Game Menu (Escape).</summary>
    public PlayMenu? Menu { get; private set; }

    /// <summary>The match's sound, or null.</summary>
    public PlayAudio? Audio { get; private set; }

    public bool IsLoaded { get; private set; }

    public string? LoadError { get; private set; }

    public PlayFrameStats Stats { get; } = new();

    /// <summary>Seconds the map render and the session took to build.</summary>
    public double LoadSeconds { get; private set; }

    /// <summary>A play scene for <paramref name="config"/>, not yet in the tree.</summary>
    public static PlayRoot Create(PlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var root = GD.Load<PackedScene>(ScenePath).Instantiate<PlayRoot>();
        root.Config = config;
        return root;
    }

    /// <summary>Replaces the current scene with a play scene for <paramref name="config"/>.</summary>
    public static PlayRoot Enter(SceneTree tree, PlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(tree);
        PlayRoot root = Create(config);
        Node? previous = tree.CurrentScene;
        tree.Root.AddChild(root);
        tree.CurrentScene = root;
        previous?.QueueFree();
        return root;
    }

    public override void _Ready()
    {
        var hud = new CanvasLayer { Name = "PlayStatus", Layer = HudLayer.DefaultLayer + 1 };
        _status = new Label { Name = "Status", Position = new Vector2(16f, 12f) };
        _prompt = new Label { Name = "Prompt", Visible = false };
        hud.AddChild(_status);
        hud.AddChild(_prompt);
        AddChild(hud);
        var watch = Stopwatch.StartNew();
        try
        {
            Load();
            IsLoaded = true;
            _status.Text = "";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or SvMapFormatException or IOException
            or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            LoadError = ex.Message;
            GD.PushError($"Could not start play on '{Config.Settings.Map}': {ex.Message}");
            _status.Text = $"Could not start play on '{Config.Settings.Map}': {ex.Message}";
        }

        LoadSeconds = watch.Elapsed.TotalSeconds;
    }

    private void Load()
    {
        PlaySettings settings = Config.Settings;
        IClock clock;
        if (Config.TicksPerFrame > 0)
        {
            _virtualClock = new VirtualClock();
            clock = _virtualClock;
        }
        else
        {
            clock = new StopwatchClock();
        }

        Session = Config.CreateSession?.Invoke(clock) ?? new PlaySession(settings, clock);
        CrashLog.Context = settings.Map + " " + settings.Mode;
        if (settings.Bots > 0)
        {
            GD.Print($"SVM_PLAY bots: {settings.Bots} at {settings.Difficulty} ({(settings.HostMode == PlayHostMode.Inline ? "status every 10 s" : "host thread")})");
        }

        bool headless = DisplayServer.GetName() == "headless";
        if (Config.Render)
        {
            Map = MapBuilder.Build(Session.MapFolder);
            AddChild(Map.Root);
            View = new FaithfulView();
            AddChild(View);
            View.Attach(Map);
            View.Camera.MakeCurrent();
            GameplayObjects.ShowMode(Map, settings.Mode);
            Camera = new PlayCamera(View);
            Pawns = new PawnViews { Map = Map, SharedRoot = Map.SharedRoot };
            AddChild(Pawns);
            FirstPerson = new FirstPersonView { Map = Map, SharedRoot = Map.SharedRoot };
            AddChild(FirstPerson);
            Fx = new OpenScctGame.Fx.CombatFxView { Pawns = Pawns, FirstPerson = FirstPerson, SharedRoot = Map.SharedRoot };
            AddChild(Fx);
            FlashLight = new FlashLightView { Name = "FlashLight", Map = Map, Pawns = Pawns };
            AddChild(FlashLight);
            MercLasers = new MercLaserView { Name = "MercLasers", Map = Map, Pawns = Pawns, FirstPerson = FirstPerson };
            AddChild(MercLasers);
            SpyLasers = new OpenScctGame.Fx.SpyLaserView { Name = "SpyLasers", Map = Map, Pawns = Pawns, SharedRoot = Map.SharedRoot };
            AddChild(SpyLasers);
            LaserTriggers = new OpenScctGame.Fx.LaserTriggerView(Map);
            ShockFx = new OpenScctGame.Fx.ShockFxView { Name = "ShockFx", Pawns = Pawns };
            AddChild(ShockFx);
            if (Map.Warnings.Count > 0)
            {
                GD.Print($"SVM_PLAY {Map.MapId} render warnings={Map.Warnings.Count} first: {Map.Warnings[0]}");
            }
        }

        _bindingsSide = settings.Side;
        _seedInputMap = !headless;
        InputBindings.Rebound += OnKeysRebound;
        Controls = new PlayInput
        {
            Bindings = InputBindings.Load(settings.Side, Config.GameRoot, seedInputMap: !headless),
            Target = Session.Input,
            Enabled = Config.Pilot is null && !headless,
            CapturePointer = !headless,
        };
        AddChild(Controls);
        if (Config.Demo is { } demo)
        {
            // A demo's keys are the playback bar's; the spectator the recorder was takes no input.
            Controls.Enabled = false;
            AddChild(new Demo.DemoHud(demo));
        }

        if (Config.Audio)
        {
            // Before the HUD: the sound takes the client world's event sink first and the HUD's tap wraps it.
            Audio = PlayAudio.Attach(this, Session, ListenerView);
        }

        Session.BeforeClientTick = BeforeTick;
        Session.AfterClientTick = AfterTick;
        if (Config.Hud && Config.Render)
        {
            Hud = HudLayer.Attach(this, () => Session?.LocalController, out string? error);
            if (Hud is null)
            {
                GD.Print($"SVM_PLAY no HUD: {error}");
            }
            else
            {
                Hud.Net.Source = ReadNet;
                if (Hud.GadgetViews is { } gadgets && Audio is { } audio)
                {
                    // The heart sensor's heartbeat and scan ping (spec/equipment/gadget-hud.md rules 24 and 27).
                    gadgets.Heart.PlaySound = s => audio.PlayHudSound(s.Sound);
                }
            }

            if (Hud is not null && View is not null)
            {
                // The scope sights follow the aim point drawn between ticks, as the camera is.
                Hud.DrawnAimLocation = () => _aim.At(_alpha);

                // The HUD's fades and page time at the drawn moment, as the map's time.
                Hud.DrawnTime = () => _time.At(_alpha);
                Camera3D cam = View.Camera;
                Hud.Projector = p =>
                {
                    Vector3 g = UeSpace.ToGodot(p);
                    return cam.IsInsideTree() && !cam.IsPositionBehind(g) ? cam.UnprojectPosition(g) : null;
                };
            }
        }

        // Calls from the server land between client ticks (a ClientDamage's flash): their HUD events go in before the step.
        Session.Client.BeforeWorldTick += PostLateHudEvents;

        // Server messages and chat for the HUD, the chat line, the HUD's keys and the player's settings; the In-Game
        // Menu on the ShowMenu key (Escape).
        Menu = PlayMenu.Attach(this, Session, Controls, PlayHud.Attach(this, Session, Controls, Hud));
    }

    /// <summary>The local player's link totals for the network readout (main thread, between client ticks).</summary>
    private NetReadout? ReadNet()
    {
        if (Session is not { IsStopped: false } session || session.Client.Connection?.Stats is not { } stats)
        {
            return null;
        }

        long corrections = session.LocalController is { } pc
            ? session.Client.Movement.PeekEndpoint(pc)?.Client?.CorrectionsApplied ?? 0
            : 0;
        // A listen host's own player is on loopback: its line is the other players' pings (their PRI.Ping, set by the
        // server from each connection's round trip; bots and the host have none).
        bool host = session.Client.Connection.IsLocal;
        int clients = 0;
        int best = int.MaxValue;
        int worst = 0;
        if (host && (session.LocalController?.GameReplicationInfo ?? session.Client.World.Level.Info.GRI) is { } gri)
        {
            foreach (OpenScct.Core.Engine.PlayerReplicationInfo? pri in gri.PRIArray)
            {
                if (pri is { bDeleteMe: false, Ping: > 0 })
                {
                    int ms = pri.Ping * OpenScctGame.Hud.HudK.PingUnit;
                    clients++;
                    best = Math.Min(best, ms);
                    worst = Math.Max(worst, ms);
                }
            }
        }

        return new NetReadout(stats.HasRtt, stats.RttMicros, stats.JitterMicros, stats.InBytes, stats.OutBytes,
            stats.InPackets, stats.InPacketsLost, stats.OutPackets, stats.OutPacketsLost, corrections, session.Client.ServerNetRate,
            host, clients, clients > 0 ? best : 0, worst);
    }

    /// <summary>
    /// Where the player hears from (spec/native/audio.md rule 23): UNIAudio's Update listens at the viewport's
    /// player controller's Location, turned by the drawn view (game 0x10b05260). The following camera's solver moves
    /// the controller to the camera spot, so there the drawn (interpolated) camera is the same place. The object
    /// views (sticky camera, camera network, Mirador and the move to a sticky camera) leave the controller where the
    /// following camera put it, by the pawn: the player keeps hearing from there, not from the remote camera.
    /// First person keeps the camera (where the original's controller sits then is not decoded).
    /// </summary>
    private (FVector Location, FRotator Rotation)? ListenerView()
    {
        (FVector Location, FRotator Rotation)? view = null;
        if (Camera is { } camera)
        {
            (FVector location, FRotator rotation, float _) = camera.Current;
            view = (location, rotation);
        }
        else if (Session is { View.Valid: true } s)
        {
            view = (s.View.Location, s.View.Rotation);
        }

        if (view is { } v && Session?.LocalController is { } pc &&
            OpenScct.Game.Camera.PlayerCalcView.SourceOf(pc) is ViewSource.StickyCam or ViewSource.StickyCamSmoothToPosition
                or ViewSource.Mirador)
        {
            return (pc.Location, v.Rotation);
        }

        return view;
    }

    private void PostLateHudEvents(OpenScct.Server.ClientSession client)
    {
        if (Hud is { } hud)
        {
            OpenScctGame.Hud.HudEventTap.TakeLate(client.World, hud.Effects);
        }
    }

    private void BeforeTick(PlaySession session, long tick)
    {
        Config.Demo?.BeforeTick();
        if (Config.Pilot is { } pilot)
        {
            long start = Stopwatch.GetTimestamp();
            pilot.Tick(session);
            _pilotMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        else
        {
            Controls?.FillTick(session.Input);
        }

        // The mouse counts this tick takes, for the drawn view's look-ahead.
        _look.BeforeTick(session.LocalController, session.Mouse, session.Input.MouseX, session.Input.MouseY, session.ViewportSize);
    }

    private void AfterTick(PlaySession session, long tick)
    {
        Config.Demo?.AfterTick(session);
        Stats.Ticks++;
        _time.OnTick(session.Client.World.Level.Info.TimeSeconds);
        _look.AfterTick(session.LocalController, session.View);
        FirstPerson?.OnTick(session.LocalController);
        _aim.OnTick(session.LocalController);
        if (Devices is null && Map is not null && session.Client.Map?.Loaded is { } loaded)
        {
            Devices = new DeviceViews(Map, loaded, Map.Root, Map.SharedRoot);
        }

        Devices?.OnTick(session.Client.World.Level);
        Map?.LightBeams?.OnTick(session.Client.Map?.Loaded);
        TriggerMapEmitters(session);
        Pawns?.OnTick(session.Client.World.Level);
        Fx?.OnTick(session);
        FollowSide(session);
    }

    private PlaySide _bindingsSide;
    private bool _seedInputMap;

    /// <summary>
    /// The keys follow the local pawn's side: a hub side change (docs/HUB.md) gives the player the other side's pawn
    /// in the same round, so the layout loaded for the side he entered with is swapped for the new side's (a merc on
    /// the spy layout had no fire key and threw the spy's gadgets).
    /// </summary>
    private void FollowSide(PlaySession session)
    {
        if (Controls is not { } controls || session.LocalPawn is not { } pawn)
        {
            return;
        }

        PlaySide side = pawn.SPawnType == PawnType.SPAWNDEFENSE ? PlaySide.Merc : PlaySide.Spy;
        if (side == _bindingsSide)
        {
            return;
        }

        _bindingsSide = side;
        controls.Bindings = InputBindings.Load(side, Config.GameRoot, _seedInputMap);
        controls.ClearHeld(session.Input);
        GD.Print($"SVM_PLAY keys: {(side == PlaySide.Merc ? "merc" : "spy")} layout for the new side");
    }

    /// <summary>
    /// A rebind in the options (in-game menu or the hub) saved a side's keys: when it is the side being played, the
    /// round's layout is reloaded so the change applies at once instead of on the next round.
    /// </summary>
    private void OnKeysRebound(PlaySide side)
    {
        if (Controls is not { } controls || side != _bindingsSide)
        {
            return;
        }

        controls.Bindings = InputBindings.Load(side, Config.GameRoot, _seedInputMap);
        if (Session is { } session)
        {
            controls.ClearHeld(session.Input);
        }
    }

    /// <summary>
    /// Emitter.ReplicateTrigger on this machine: every map Emitter the event graph triggered this tick (its own
    /// notify, or the server's relayed by NetDeviceEvents) fires its drawn particle systems.
    /// </summary>
    private void TriggerMapEmitters(PlaySession session)
    {
        if (Map?.Emitters is not { } emitters || session.Client.Map?.Loaded is not { } loaded)
        {
            return;
        }

        OpenScct.Core.World world = session.Client.World;
        foreach (SimEvent e in world.Events.Current)
        {
            if (e.Kind == SimEventKind.Notify && e.What == OpenScct.Core.Engine.Emitter.NAME_ReplicateTrigger &&
                world.Level.FindActor(e.ActorId) is OpenScct.Core.Engine.Emitter actor && loaded.RecordOf(actor)?.Id is { } id)
            {
                emitters.TriggerActor(id);
            }
        }
    }

    private long _botsPrintedAt;

    /// <summary>The server bots' status every 10 s of play, when the host runs inline (its state is safe to read here).</summary>
    private void PrintBots(PlaySession session)
    {
        if (session.Settings.HostMode != PlayHostMode.Inline || session.Bots is not { } bots || session.TicksRun - _botsPrintedAt < 10 * Sim.Hz)
        {
            return;
        }

        _botsPrintedAt = session.TicksRun;
        foreach (string line in bots.Log)
        {
            GD.Print($"SVM_PLAY bots: {line}");
        }

        foreach (OpenScct.Game.AI.AiController bot in bots.Bots)
        {
            GD.Print($"SVM_PLAY bot {bot.Controller.PlayerReplicationInfo?.PlayerName}: {bot.Status} at {bot.Pawn?.Location}");
        }
    }

    public override void _Process(double delta)
    {
        if (Session is not { } session || session.IsStopped)
        {
            return;
        }

        _watch.Restart();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X >= 1f && size.Y >= 1f)
        {
            session.ViewportSize = ((int)size.X, (int)size.Y);
        }

        float alpha;
        if (_virtualClock is { } clock)
        {
            for (int i = 0; i < Config.TicksPerFrame; i++)
            {
                clock.Advance(TickMicros);
                session.Step(clock.NowMicroseconds);
            }

            alpha = 1f;
        }
        else
        {
            session.Update();
            alpha = session.Alpha;
        }

        PrintBots(session);

        // The scripted pilot's route planning runs inside the ticks; it is a test driver, so it is counted apart.
        double simMs = _watch.Elapsed.TotalMilliseconds - _pilotMs;
        Stats.PilotMs += _pilotMs;
        _pilotMs = 0;
        _watch.Restart();

        // With a fixed step the views advance by the simulated time of the frame, so they keep pace with the ticks.
        float viewDt = _virtualClock is not null ? Config.TicksPerFrame * Sim.DeltaTime : (float)delta;
        DrawViews(session, alpha, viewDt);
        double viewMs = _watch.Elapsed.TotalMilliseconds;
        Stats.Add(simMs, viewMs, delta * 1000.0);
        if (session.ServerFailure is { } failure && _status is not null)
        {
            _status.Text = "Server stopped: " + failure.Message;
        }
    }

    private void DrawViews(PlaySession session, float alpha, float dt)
    {
        SPlayerController? pc = session.LocalController;
        _alpha = alpha;

        // The mouse turn not yet simulated is drawn now (no frame of delay, as the original's per-frame controller).
        (float pendingX, float pendingY) = Controls?.PendingMouse ?? (0f, 0f);
        FRotator look = session.ViewOverride is not null ? default
            : _look.Offset(pc, session.Mouse, pendingX + session.Input.MouseX, pendingY + session.Input.MouseY, session.ViewportSize, alpha);
        if (Camera is not null && View is not null && Camera.Apply(session.PreviousView, session.View, alpha, look, _look.OrbitDistance(pc)))
        {
            // Materials, light types and the map's emitters at the drawn moment between ticks (they moved only at the
            // 60 Hz ticks before, so they held still in most frames above 60 fps).
            Map?.SetTime(_time.At(alpha));
            int rendMap = pc is null || pc.RendMap == 0 ? RenderK.RendMapNormal : pc.RendMap;
            if (rendMap != _lastRendMap)
            {
                _lastRendMap = rendMap;
                View.RendMap = rendMap;
            }

            // The motion tracker's clean view rectangles (HudMotionTracking, from the last HUD frame) as parts of the picture.
            View.MotionCleanRects = Hud is { } hud
                ? OpenScctGame.Render.Post.DisplayPostSettings.Normalize(hud.Motion.CleanRects, hud.DrawnScreen)
                : [];

            // The flash-bang after-image: a capture on each bCaptureRemanence, drawn by RemanenceBlendFactor.
            if (Hud is { } flashHud)
            {
                if (flashHud.Effects.RemanenceCaptures != _lastRemanenceCaptures)
                {
                    _lastRemanenceCaptures = flashHud.Effects.RemanenceCaptures;
                    View.Remanence.RequestCapture();
                }

                View.Remanence.Factor = flashHud.Effects.Sfx.RemanenceBlendFactor;
            }

            bool binoculars = session.LocalPawn?.bBinocularsOn ?? false;
            if (binoculars != _lastBinoculars)
            {
                _lastBinoculars = binoculars;
                View.BinocularsOn = binoculars;
            }
        }

        SBasePawn? firstPerson = session.FirstPersonOverride is { } fpOverride ? fpOverride()
            : pc is { bBehindView: false, FreeCameraFlying: 0 } ? session.LocalPawn : null;
        // The local spy's arm aims at the view drawn this frame (its controller's rotation plus the mouse turn not yet
        // simulated), as the camera does, so the pistol and its laser keep up with the mouse.
        SBasePawn? aimer = session.LocalPawn is { SPawnType: not PawnType.SPAWNDEFENSE } spy && pc is not null && spy.Controller == pc ? spy : null;
        FRotator drawnView = default;
        if (aimer is not null)
        {
            FRotator pending = session.ViewOverride is null && _look.Active ? _look.Pending : default;
            drawnView = new FRotator(pc!.Rotation.Pitch + pending.Pitch, pc.Rotation.Yaw + pending.Yaw, pc.Rotation.Roll);
        }

        Pawns?.Draw(alpha, dt, firstPerson, aimer, drawnView);
        Devices?.Draw(alpha, dt);
        Map?.SoftBodies?.Update(dt, session.Client.World.Level, Audio?.Sound, session.LocalPawn);
        if (Camera is not null)
        {
            (FVector location, FRotator rotation, float _) = Camera.Current;
            FirstPerson?.Draw(pc, location, rotation, dt, alpha);
            Fx?.Draw(dt, location, rotation, pc);
            FlashLight?.Draw(session.Client.World.Level, session.LocalPawn, location);
            MercLasers?.Draw(session.Client.World.Level, session.LocalPawn);
            SpyLasers?.Draw(session.Client.World.Level);
            LaserTriggers?.Draw(session.Client.World.Level);
            ShockFx?.Draw(session.Client.World.Level, dt);
        }

        UpdatePrompt(pc);
    }

    /// <summary>A one-line Use prompt when there is no HUD to draw the interaction list.</summary>
    private void UpdatePrompt(SPlayerController? pc)
    {
        if (_prompt is null)
        {
            return;
        }

        SInteractObject? current = Hud is null ? pc?.IManager?.GetCurrentInteraction() : null;
        bool show = current is not null && (pc!.IManager!.HasUsableInteractions());
        _prompt.Visible = show;
        if (show)
        {
            string text = string.IsNullOrEmpty(current!.Description) ? "Use" : current.Description;
            _prompt.Text = "[Shift] " + text;
            Vector2 size = GetViewport().GetVisibleRect().Size;
            _prompt.Position = new Vector2((size.X * 0.5f) - 60f, size.Y * 0.62f);
        }
    }

    public override void _ExitTree()
    {
        // Whatever ends the round (results, the lobby, a disconnect, a map change), the pointer it took goes back.
        OpenScctGame.Controls.Pointer.Release();
        InputBindings.Rebound -= OnKeysRebound;
        CrashLog.Context = "";
        Session?.Dispose();
        Session = null;
        Config.Demo?.Dispose();
        Devices = null;
        Pawns = null;
        FirstPerson = null;
        Fx = null;
        FlashLight = null;
        MercLasers = null;
        SpyLasers = null;
        LaserTriggers = null;
        ShockFx = null;
        Camera = null;
        Map = null;
        View = null;
        Hud = null;
        Controls = null;
        Menu = null;
        Audio = null;

        // Let the wrappers of the meshes, clips and materials this scene made be finalized now, while the engine
        // still runs, rather than at process exit after the servers are gone.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
