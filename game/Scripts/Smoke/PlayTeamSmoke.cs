using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Content;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Net.Moves;
using OpenScct.Physics.Collision;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-team</c>: the interactions between team mates, played the way the launchers run a match
/// (SteeE, Play Level rules, the listen server on its own thread, wall clock, HUD) with the player and one bot on his
/// team (and one bot on the other side, held still). Every player action is a Godot input event for the key the
/// side's layout binds (<see cref="KeyPilot"/>): Use (Shift) opens the Use menu, S moves its selection, releasing Use
/// acts; Use at a downed mate revives him; the co-op key (T) starts a boost or a hoist. The scenario setup (a mate
/// placed in front, hurt, marked or knocked down) runs on the server thread between ticks, through the game's own
/// functions. Then the bot is let go and must help the player: heal him, take his mark off, revive him, climb on his
/// boost and boost him. The checks read what the player sees and gets: the HUD's menu rows and coop icon, the client's
/// copies of both pawns (health, mark, state, the clips their channels play), and the server's result. Legs:
/// SVM_TEAM_LEGS (spy, merc; default both). With a window and SVM_PLAY_SHOTS naming a folder, every step is saved.
/// </summary>
public sealed class PlayTeamSmoke : SmokeTest
{
    public const string TestName = "play-team";

    private const int TeardownFrames = 6;

    private readonly string[] _legs;
    private readonly List<string> _report = new();
    private int _leg;
    private TeamLeg? _current;
    private int _teardown = -1;
    private SmokeResult? _final;

    public PlayTeamSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_TEAM_LEGS");
        _legs = string.IsNullOrWhiteSpace(only) ? ["spy", "merc"] : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            if (++_teardown < TeardownFrames)
            {
                return SmokeResult.Running;
            }

            _teardown = -1;
            if (_final is { } done)
            {
                return done;
            }
        }

        if (_current is null)
        {
            _current = _legs[_leg] == "merc" ? TeamLeg.Merc(context) : TeamLeg.Spy(context);
            return SmokeResult.Running;
        }

        SmokeResult? result = _current.Step(context);
        if (result is null)
        {
            return SmokeResult.Running;
        }

        _current.Dispose();
        _current = null;
        _teardown = 0;
        if (result.Value.State == SmokeState.Failed)
        {
            _final = SmokeResult.Fail($"{_legs[_leg]}: {result.Value.Detail} | done: {string.Join("; ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        if (++_leg >= _legs.Length)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }
}

/// <summary>What the server thread publishes after each of its ticks for the client-side checks.</summary>
internal readonly record struct TeamServerView(
    bool Valid,
    string PlayerState,
    int PlayerHealth,
    bool PlayerMarked,
    FVector PlayerLocation,
    string MateState,
    int MateHealth,
    int MateDefaultHealth,
    bool MateMarked,
    FVector MateLocation,
    EPhysics MatePhysics,
    string MateStatus,
    bool MateReadyBoost,
    int MateYaw,
    FVector? BoostLoc,
    FVector BoostDir,
    int BoostSpots);

/// <summary>
/// Work for the listen server's thread: one-shot operations, a per-tick hold, and the published view. Everything runs
/// in <see cref="ServerHost.BeforeWorldTick"/>, after the bots have written their input and before the world steps.
/// </summary>
internal sealed class TeamServerOps
{
    private readonly object _gate = new();
    private readonly List<Func<TeamServer, bool>> _ops = new();
    private Action<TeamServer>? _every;
    private TeamServerView _view;
    private string? _error;
    private int _traceTick;

    /// <summary>SVM_TEAM_TRACE=1 prints the bot mate and the player twice a second (server side).</summary>
    private static readonly bool Trace = System.Environment.GetEnvironmentVariable("SVM_TEAM_TRACE") == "1";

    private static string s(SPlayerController? pc) => pc?.GetStateName().ToString() ?? "-";

    public TeamServerView View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    public string? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    /// <summary>Runs <paramref name="op"/> on the server thread each tick until it returns true.</summary>
    public void Run(Func<TeamServer, bool> op)
    {
        lock (_gate)
        {
            _ops.Add(op);
        }
    }

    /// <summary>Runs <paramref name="hold"/> on the server thread every tick (null stops it).</summary>
    public void Every(Action<TeamServer>? hold)
    {
        lock (_gate)
        {
            _every = hold;
        }
    }

    public void OnTick(TeamServer server)
    {
        lock (_gate)
        {
            try
            {
                _every?.Invoke(server);
                for (int i = 0; i < _ops.Count;)
                {
                    if (_ops[i](server))
                    {
                        _ops.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }

                _view = server.Publish();
                if (Trace && ++_traceTick % 30 == 0 && server.MatePawn is { } m && server.PlayerPawn is { } p && server.Mate is { } bot)
                {
                    System.Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"SVM_TEAM trace t={server.Host.World.Level.TimeSeconds:F1} mate {bot.Controller.GetStateName()} at {m.Location} yaw {m.Rotation.Yaw & 0xFFFF} crouched {m.bIsCrouched} keys up={bot.LastInput.BT_Up} use={bot.LastInput.bUsing} | player {s(server.Player)} at {p.Location} yaw {p.Rotation.Yaw & 0xFFFF} | reach {SMateInteraction.MateInReach(m, p)} | {bot.Status}"));
                }
            }
            catch (Exception e)
            {
                _error ??= e.ToString();
            }
        }
    }
}

/// <summary>The server-side actors of the scenario (server thread only).</summary>
internal sealed class TeamServer
{
    public TeamServer(PlaySession session, ServerHost host)
    {
        Session = session;
        Host = host;
    }

    public PlaySession Session { get; }

    public ServerHost Host { get; }

    public SPlayerController? Player => Session.ServerController;

    public SBasePawn? PlayerPawn => Player?.Pawn as SBasePawn;

    /// <summary>The bot on the player's side.</summary>
    public AiController? Mate => Session.Bots?.Bots.FirstOrDefault(b => Player is { } p && b.Controller.SPawnType == p.SPawnType);

    public SBasePawn? MatePawn => Mate?.Controller.Pawn as SBasePawn;

    /// <summary>The bots of the other side.</summary>
    public IEnumerable<AiController> Enemies => Session.Bots?.Bots.Where(b => Player is { } p && b.Controller.SPawnType != p.SPawnType) ?? [];

    /// <summary>A boost spot (CheckIfCanStartSHL's place and its direction away from the wall), once found.</summary>
    public (FVector Loc, FVector Dir)? BoostSpot { get; private set; }

    /// <summary>Boost spots seen by the search (diagnostics).</summary>
    public int BoostSpotsFound { get; private set; }

    /// <summary>
    /// Looks for boost spots under the level's ledges: for points along every LG, a floor 500 to 515 under it on
    /// either side, the held mate's pawn set there and asked CheckIfCanStartSHL (then put back). The spot nearest the
    /// player wins.
    /// </summary>
    public void FindBoostSpot()
    {
        if (Host.Map.Physics is not { } physics || MatePawn is not { } probe || PlayerPawn is not { } me)
        {
            return;
        }

        FVector home = probe.Location;
        float best = float.MaxValue;
        for (int i = 0; i < physics.GEs.Count; i++)
        {
            GERecord ge = physics.GEs[i];
            if (ge.Kind != GEKind.LG || !ge.IsActive(Host.World.Level.Info.GEActivationMask))
            {
                continue;
            }

            FVector d = ge.P1 - ge.P0;
            float len = new FVector(d.X, d.Y, 0f).Size();
            FVector n = new FVector(-d.Y, d.X, 0f).SafeNormal();
            for (float along = 16f; along < len; along += 48f)
            {
                FVector q = ge.P0 + (d * (along / len));
                foreach (float side in new[] { 1f, -1f })
                {
                    FVector xy = q + (n * (side * 60f));
                    if (!GadgetWorld.Line(probe, new FVector(xy.X, xy.Y, q.Z - 700f), new FVector(xy.X, xy.Y, q.Z - 380f), ETraceFlags.TRACE_World, out FCheckResult floor) ||
                        q.Z - floor.Location.Z is < 480f or > 540f)
                    {
                        continue;
                    }

                    probe.SetLocation(new FVector(xy.X, xy.Y, floor.Location.Z + probe.CollisionHeight + 2f));
                    if (probe.CheckIfCanStartSHL(out FVector loc, out FVector dir))
                    {
                        BoostSpotsFound++;
                        float dist = (loc - me.Location).SizeSquared();
                        if (dist < best)
                        {
                            best = dist;
                            BoostSpot = (loc, dir);
                        }
                    }
                }
            }
        }

        probe.SetLocation(home);
    }

    /// <summary>Puts the player's pawn at <paramref name="at"/> on the server and reinitialises the client there, as a respawn does.</summary>
    public bool PlacePlayer(FVector at, int yaw)
    {
        if (Player is not { Pawn: SBasePawn pawn } spc || Host.World.Level.GetService<IMovementNet>() is not NetMovementBridge bridge ||
            bridge.EndpointOf(spc)?.Server is not { } moves)
        {
            return false;
        }

        MovementNatives.FarMoveActor(pawn, at);
        pawn.Velocity = default;
        pawn.Acceleration = default;
        pawn.Rotation = new FRotator(0, yaw, 0);
        moves.ReinitPawn();
        return true;
    }

    /// <summary>Puts the mate's pawn (a bot, played on the server) at <paramref name="at"/>.</summary>
    public void PlaceMate(FVector at, int yaw)
    {
        if (MatePawn is not { } m || Mate is not { } bot)
        {
            return;
        }

        // A bot still hanging from the ledge it was boosted to (or inside any other move) is put back on its feet
        // first, as the reinit of a respawn does: walking physics and its waiting state.
        if (m.Physics != EPhysics.PHYS_Walking)
        {
            new SBasePawnMoves(bot.Controller).ReinitBecauseOfDivergences();
            bot.Controller.GotoWaitingState();
        }

        MovementNatives.FarMoveActor(m, at);
        m.Velocity = default;
        m.Acceleration = default;
        m.Rotation = new FRotator(0, yaw, 0);
        bot.Controller.Rotation = m.Rotation;
    }

    /// <summary>
    /// Puts the mate on open floor about <paramref name="distance"/> from the player (the first of 16 directions with
    /// a clear line and a floor at the player's height), so a bot step starts with the bot in reach of a walk.
    /// </summary>
    public bool PlaceMateNear(float distance)
    {
        if (PlayerPawn is not { } me || MatePawn is not { } m)
        {
            return false;
        }

        for (int k = 0; k < 16; k++)
        {
            FVector dir = new FRotator(0, me.Rotation.Yaw + (k * 4096), 0).Vector();
            FVector at = me.Location + (dir * distance);
            if (GadgetWorld.Line(me, at, me.Location, ETraceFlags.TRACE_World, out _) ||
                !GadgetWorld.Line(me, at - new FVector(0f, 0f, 200f), at, ETraceFlags.TRACE_World, out FCheckResult floor) ||
                MathF.Abs(floor.Location.Z - (me.Location.Z - me.CollisionHeight)) > 20f)
            {
                continue;
            }

            // On the bots' walking graph (a node right there), so the bot can walk from it.
            if (Session.Bots?.World is { } world)
            {
                OpenScct.Game.AI.NavGraph graph = world.NavFor(m is SPawnAttaque);
                int node = graph.NearestNode(floor.Location + new FVector(0f, 0f, 1f));
                if (node < 0 || NavFollower.Horizontal(graph.Node(node).Floor - floor.Location) > 40f ||
                    MathF.Abs(graph.Node(node).Floor.Z - floor.Location.Z) > 20f)
                {
                    continue;
                }
            }

            FVector home = m.Location;
            PlaceMate(new FVector(at.X, at.Y, floor.Location.Z + m.CollisionHeight + 2f), me.Rotation.Yaw & 0xFFFF);

            // And with a route on the bot's graph to the player's feet; otherwise back and the next direction.
            if (Session.Bots?.World is { } w && !new NavFollower(w, m is SPawnAttaque).SetGoal(m, NavFollower.Feet(me)))
            {
                PlaceMate(home, m.Rotation.Yaw & 0xFFFF);
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Prints the route the bot's graph has from the mate to the player (SVM_TEAM route) as a bot step starts, so a
    /// step the bot fails says where it was sent.
    /// </summary>
    public void TraceRoute(bool placed)
    {
        if (Session.Bots?.World is not { } world || MatePawn is not { } m || PlayerPawn is not { } p)
        {
            return;
        }

        var follower = new NavFollower(world, m is SPawnAttaque);
        bool ok = follower.SetGoal(m, NavFollower.Feet(p));
        OpenScct.Game.AI.NavGraph g = world.NavFor(m is SPawnAttaque);
        int a = g.NearestNode(m.Location, m.CollisionHeight);
        int b = g.NearestNode(NavFollower.Feet(p) + new FVector(0f, 0f, 1f));
        System.Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"SVM_TEAM route ({(placed ? "mate placed" : "no spot for the mate, left where it was")}) mate {m.Location} node {a} {(a >= 0 ? g.Node(a).Floor : default)} -> player {p.Location} node {b} {(b >= 0 ? g.Node(b).Floor : default)}: {(ok ? $"path {follower.Path.Count}" : "no path")}, line {world.LineOpen(m.Location, p.Location)}, " +
            $"steps [{string.Join("; ", follower.Path.Select(st => $"{st.Kind} {st.Point}"))}]"));
    }

    public TeamServerView Publish()
    {
        SBasePawn? me = PlayerPawn;
        SBasePawn? mate = MatePawn;
        return new TeamServerView(
            me is not null && mate is not null,
            Player?.GetStateName().ToString() ?? "",
            me?.Health ?? 0,
            me?.bSpyBulletOn ?? false,
            me?.Location ?? default,
            Mate?.Controller.GetStateName().ToString() ?? "",
            mate?.Health ?? 0,
            mate is null ? 0 : SHealMateInteraction.DefaultHealth(mate),
            mate?.bSpyBulletOn ?? false,
            mate?.Location ?? default,
            mate?.Physics ?? EPhysics.PHYS_None,
            Mate?.Status ?? "",
            mate?.bReadyForShortLadder ?? false,
            mate is null ? 0 : mate.Rotation.Yaw & 0xFFFF,
            BoostSpot?.Loc,
            BoostSpot?.Dir ?? default,
            BoostSpotsFound);
    }

    /// <summary>
    /// Keeps the mate standing <paramref name="distance"/> in front of the player, facing him (setup only: the bot is
    /// held, and it is moved back only when it has drifted, so in between it walks on the floor as usual).
    /// </summary>
    public void Pin(ref FVector? spot, float distance)
    {
        if (PlayerPawn is not { } me || MatePawn is not { } mate || Mate is not { } bot || mate.Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        if (spot is null)
        {
            FVector dir = new FRotator(0, me.Rotation.Yaw, 0).Vector();
            spot = me.Location + (dir * distance) + new FVector(0f, 0f, mate.CollisionHeight - me.CollisionHeight);
        }

        FVector at = spot.Value;
        float dx = mate.Location.X - at.X;
        float dy = mate.Location.Y - at.Y;
        if ((dx * dx) + (dy * dy) > 20f * 20f)
        {
            mate.SetLocation(at);
            mate.Velocity = default;
        }

        mate.Acceleration = default;
        FVector to = me.Location - mate.Location;
        var face = new FRotator(0, new FVector(to.X, to.Y, 0f).Rotation().Yaw, 0);
        mate.Rotation = face;
        bot.Controller.Rotation = face;
        // A merc's body follows his aim.
        bot.Controller.MyDesiredRotation = face;
        bot.Controller.LocalMyDesiredRotation = face;
    }
}

/// <summary>One side's session and its steps.</summary>
internal sealed class TeamLeg : IDisposable
{
    private readonly List<TeamStep> _steps = new();
    private readonly List<string> _results = new();
    private readonly PlaySide _side;
    private readonly TeamServerOps _ops = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private TeamServer? _server;
    private bool _hooked;
    private int _index;
    private double _stepStart;
    private bool _stepBegun;
    private double _clock;
    private double _nextShot;

    private TeamLeg(SmokeContext context, PlaySide side)
    {
        _side = side;
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = "SteeE",
                Side = side,
                Mode = "Story",
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                // The bots fill the smaller side first: a spy player gets a merc then a spy mate; a merc player two
                // spies then a merc mate.
                Bots = side == PlaySide.Merc ? 3 : 2,
                Difficulty = "normal",
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
            Pilot = null,
        };
        _root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(_root);
    }

    public TeamCtx? Ctx { get; private set; }

    public static TeamLeg Spy(SmokeContext context)
    {
        var leg = new TeamLeg(context, PlaySide.Spy);
        leg.Common();
        if (Want("menu"))
        {
            leg.MenuSteps(spy: true);
        }

        if (Want("revive"))
        {
            leg.ReviveSteps();
        }

        if (Want("coop"))
        {
            leg.CoopSteps();
        }

        if (Want("bot"))
        {
            leg.BotHelpSteps(spy: true);
        }

        return leg;
    }

    public static TeamLeg Merc(SmokeContext context)
    {
        var leg = new TeamLeg(context, PlaySide.Merc);
        leg.Common();
        if (Want("menu"))
        {
            leg.MenuSteps(spy: false);
        }

        if (Want("revive"))
        {
            leg.ReviveSteps();
        }

        if (Want("bot"))
        {
            leg.BotHelpSteps(spy: false);
        }

        return leg;
    }

    /// <summary>SVM_TEAM_SKIP lists step groups to leave out (menu, revive, bot, coop), for quicker runs.</summary>
    private static bool Want(string group) =>
        !(System.Environment.GetEnvironmentVariable("SVM_TEAM_SKIP") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries).Contains(group);

    private void Add(string name, double timeout, Func<TeamCtx, string?> done, Action<TeamCtx>? begin = null, Action<TeamCtx>? tick = null) =>
        _steps.Add(new TeamStep(name, timeout, begin, tick, done));

    // ---- the steps -------------------------------------------------------------------------------------------

    private void Common()
    {
        Add("settle (both bots held)", 90, c =>
        {
            TeamServerView v = c.Server;
            return v.Valid && c.Walking && c.ClientMate is not null && c.StepSeconds > 1.5 ? c.Detail($"player {v.PlayerState}, mate {v.MateState}") : null;
        }, begin: c => c.Ops.Run(s =>
        {
            if (s.Mate is null)
            {
                return false;
            }

            s.Mate.Held = true;
            foreach (AiController e in s.Enemies)
            {
                e.Held = true;
            }

            return true;
        }));
        if (_side == PlaySide.Spy)
        {
            // SteeE is dark: night vision (F1) so the screenshots show the moves.
            Add("night vision (F1)", 10, c =>
                c.PC is { } pc && pc.RendMap != 0 && pc.RendMap != 5 && c.StepSeconds > 1.0 ? c.Detail($"vision {pc.RendMap}") : null,
                begin: c => c.Keys.Tap("SwitchHeadset 1"));

            // The spy spawns crouched: stand up with the crouch key.
            Add("stand up (crouch key)", 10, c => c.Pawn is { bIsCrouched: false } && c.Walking && c.StepSeconds > 0.8 ? c.Detail("standing") : null,
                begin: c =>
                {
                    if (c.Pawn is { bIsCrouched: true })
                    {
                        c.Keys.Tap("bCrouch");
                    }
                });
        }

        Add("bring the mate in front (setup)", 20, c =>
            c.ClientMate is { } m && c.Pawn is { } p && FVector.Dist(m.Location, p.Location) < 170f && c.StepSeconds > 1.0
                ? c.Detail($"mate {FVector.Dist(m.Location, p.Location):F0} uu away")
                : null,
            begin: c =>
            {
                FVector? spot = null;
                c.Ops.Every(s => s.Pin(ref spot, 120f));
            });
        Add("face the mate (mouse, a short walk)", 20, c =>
            c.Pawn is { } p && c.ClientMate is { } m && SMateInteraction.MateInReach(p, m) && c.StepSeconds > 0.5
                ? c.Detail($"facing dots ok, {FVector.Dist(m.Location, p.Location):F0} uu")
                : null,
            tick: c => c.FaceMate(walk: true));
    }

    private void MenuSteps(bool spy)
    {
        string heal = "Heal Mate";
        string unspy = "Unspy Mate";
        Add("heal: hurt the mate (setup)", 10, c => c.ClientMate is { Health: 40 } ? c.Detail("mate at 40 on the client") : null,
            begin: c => c.Ops.Run(s =>
            {
                if (s.MatePawn is { } m)
                {
                    m.Health = 40;
                }

                return true;
            }));
        Add("heal: hold Use (the menu opens with Heal Mate)", 10, c => c.MenuOpenWith(spy, heal), begin: c => c.Keys.Press("bUsing"),
            tick: c => c.FaceMate(walk: false));
        Add("heal: release Use (the mate is healed)", 10, c =>
            c.Server.MateHealth == c.Server.MateDefaultHealth && c.ClientMate is { } m && m.Health == c.Server.MateDefaultHealth && c.Walking
                ? c.Detail($"mate health {m.Health} on the client, player {c.State}")
                : null,
            begin: c => c.Keys.Release("bUsing"));
        int markTeam = spy ? 1 : 0;
        Add("unspy: mark the mate (setup)", 10, c => c.ClientMate is { bSpyBulletOn: true } ? c.Detail("mate marked on the client") : null,
            begin: c => c.Ops.Run(s =>
            {
                s.MatePawn?.HitBySpyBullet(markTeam);
                return true;
            }));
        Add("unspy: hold Use (the menu opens with Unspy Mate)", 10, c => c.MenuOpenWith(spy, unspy), begin: c => c.Keys.Press("bUsing"),
            tick: c => c.FaceMate(walk: false));
        Add("unspy: release Use (the mark is off)", 10, c =>
            !c.Server.MateMarked && c.ClientMate is { bSpyBulletOn: false } && c.Walking ? c.Detail($"mark off, player {c.State}") : null,
            begin: c => c.Keys.Release("bUsing"));
        Add("menu: hurt and mark the mate (setup)", 10, c => c.ClientMate is { Health: 30, bSpyBulletOn: true } ? c.Detail("hurt and marked") : null,
            begin: c => c.Ops.Run(s =>
            {
                if (s.MatePawn is { } m)
                {
                    m.Health = 30;
                    m.HitBySpyBullet(markTeam);
                }

                return true;
            }));
        Add("menu: hold Use (Unspy Mate selected first), S selects Heal Mate", 10, c =>
        {
            if (!c.InMenu || c.Rows.Count < 3)
            {
                return null;
            }

            if (c.Remember is null)
            {
                c.Remember = c.SelectedRow;
                c.Keys.Tap("BT_Down");
                c.TapFrame = c.Frame;
                return null;
            }

            // Done once S is up again (a key still held when the next menu opens would move that menu too).
            return c.SelectedRow == heal && c.Frame - c.TapFrame > 10
                ? c.Detail($"rows [{string.Join(", ", c.Rows)}], first {c.Remember}, after S {c.SelectedRow}")
                : null;
        }, begin: c => c.Keys.Press("bUsing"), tick: c => c.FaceMate(walk: false));
        Add("menu: release Use (healed, still marked)", 10, c =>
            c.Server.MateHealth == c.Server.MateDefaultHealth && c.Server.MateMarked && c.Walking ? c.Detail("heal picked from the menu") : null,
            begin: c => c.Keys.Release("bUsing"));
        Add("menu: take the mark off", 12, c =>
        {
            if (c.Server.MateMarked)
            {
                if (c.InMenu && c.SelectedRow == unspy && c.Frame > 3 && c.Remember is null)
                {
                    c.Remember = true;
                    c.Keys.Release("bUsing");
                }

                return null;
            }

            return c.Walking ? c.Detail("mark off") : null;
        }, begin: c => c.Keys.Press("bUsing"), tick: c => c.FaceMate(walk: false));
    }

    private void ReviveSteps()
    {
        Add("revive: knock the mate down (setup)", 20, c =>
            c.Server.MateState == "s_LyingOnGround" && c.ClientMate is { bLyingOnGround: true } ? c.Detail("mate lying on the ground") : null,
            begin: c => c.Ops.Run(s =>
            {
                s.Mate?.Controller.Handle_DropAttackedDmg(s.Enemies.FirstOrDefault()?.Controller.Pawn);
                return true;
            }));
        Add("revive: face the mate and press Use", 10, c =>
        {
            // What the revive has played by the time the server has the mate reanimating (in the detail).
            c.RecordClips();
            if (c.State.StartsWith("s_ReanimateFriend", StringComparison.Ordinal))
            {
                c.Remember = c.State;
            }

            return c.Remember is string s && c.Server.MateState is "s_Reanimating"
                ? c.Detail($"player {s}, mate {c.Server.MateState}, clips so far [{string.Join(",", c.PlayerClips)}] [{string.Join(",", c.MateClips)}], sounds so far [{string.Join(",", c.Sounds)}]")
                : null;
        }, tick: c =>
        {
            c.FaceMate(walk: false);
            if (c.Frame % 30 == 10)
            {
                c.Keys.Tap("bUsing");
            }
        });
        Add("revive: both clips play, the mate gets up", 15, c =>
        {
            c.RecordClips();
            bool up = c.Server.MateState.StartsWith("s_PlayerWalking", StringComparison.Ordinal);
            // What the player sees (both clips) and hears (their sound notifies, on the client).
            bool heard = c.Sounds.Any(x => x.StartsWith("player:", StringComparison.Ordinal)) && c.Sounds.Any(x => x.StartsWith("mate:", StringComparison.Ordinal));
            return up && c.Walking && heard && c.PlayerClips.Any(n => n.StartsWith("Rean", StringComparison.Ordinal))
                ? c.Detail($"player clips [{string.Join(",", c.PlayerClips)}], mate clips [{string.Join(",", c.MateClips)}], sounds [{string.Join(",", c.Sounds)}], mate {c.Server.MateState}")
                : null;
        });
    }

    /// <summary>The short ladder both ways: the player boosts the bot, then the bot boosts the player (spies only).</summary>
    private void CoopSteps()
    {
        Add("coop: find a boost spot under a high ledge (setup)", 15, c =>
            c.Server.BoostLoc is { } loc ? c.Detail($"{c.Server.BoostSpots} boost spots, nearest at {loc} facing {c.Server.BoostDir}") : null,
            begin: c =>
            {
                c.BeforeCoop = c.Pawn is { } me ? (me.Location, me.Rotation.Yaw & 0xFFFF) : null;
                c.Ops.Every(null);
                c.Ops.Run(s =>
                {
                    if (s.Mate is { } m)
                    {
                        m.Held = true;
                    }

                    s.FindBoostSpot();
                    return true;
                });
            });
        Add("coop: stand at the foot of the ledge, the mate behind (setup)", 15, c =>
            c.Server.BoostLoc is { } loc && c.Pawn is { } p && NavFollower.Horizontal(p.Location - c.CoopStand) < 30f && c.Walking && c.StepSeconds > 2.0 &&
            c.Coop == "OfferLadder" && c.CoopDrawn == "Coop1"
                ? c.Detail($"player at {p.Location}, spot {loc}, coop icon {c.Coop} drawn {c.CoopDrawn} (offer a ladder)")
                : null,
            begin: c =>
            {
                FVector loc = c.Server.BoostLoc ?? default;
                FVector dir = c.Server.BoostDir;
                c.CoopStand = loc + (dir * 40f);
                FVector stand = c.CoopStand;
                FVector mateAt = loc + (dir * 260f);
                int faceWall = (-dir).Rotation().Yaw & 0xFFFF;
                c.Ops.Run(s =>
                {
                    s.PlacePlayer(stand + new FVector(0f, 0f, 2f), faceWall);
                    s.PlaceMate(mateAt + new FVector(0f, 0f, 2f), faceWall);
                    return true;
                });
            });
        Add("coop: offer a boost (co-op key T)", 10, c =>
        {
            c.RecordClips();
            return c.Pawn is { bReadyForShortLadder: true } && c.State == "s_ShortLadder_P_Waiting"
                ? c.Detail($"helper ready, clips [{string.Join(",", c.PlayerClips)}], coop icon {c.Coop}")
                : null;
        }, begin: c => c.Keys.Tap("PerformCoopMvt"));
        Add("coop: the bot climbs on the player's boost", 40, c =>
        {
            c.RecordClips();
            c.NoteServerMate();
            bool jumped = c.MateStates.Contains("s_ShortLadder_S_Jump");
            // Boosted into the ledge grab and hanging (OnLedge), or already pulled up over the top (380 above the spot,
            // walking). PHYS_Ledge alone is not enough: a boost that misses the ledge grabs and drops out through NoGE.
            bool up = c.MateStates.Contains("OnLedge") ||
                (c.Server.BoostLoc is { } loc && c.Server.MateLocation.Z > loc.Z + 380f && c.Server.MatePhysics == EPhysics.PHYS_Walking);
            return jumped && up
                ? c.Detail($"mate states [{string.Join(",", c.MateStates)}], player clips [{string.Join(",", c.PlayerClips)}], sounds [{string.Join(",", c.Sounds)}], mate at {c.Server.MateLocation}")
                : null;
        }, begin: c =>
        {
            c.MateStates.Clear();
            c.Ops.Run(s =>
            {
                if (s.Mate is { } m)
                {
                    m.Held = false;
                }

                return true;
            });
        });
        Add("coop: the bot offers a boost to the player waiting under the ledge", 45, c =>
        {
            c.LookUp();
            c.NoteServerMate();
            return c.Server.MateReadyBoost && c.Server.MateState == "s_ShortLadder_P_Waiting"
                ? c.Detail($"mate ready ({c.Server.MateState}), coop icon {c.Coop}, mate states [{string.Join(",", c.MateStates)}]")
                : null;
        }, begin: c =>
        {
            c.MateStates.Clear();
            FVector loc = c.Server.BoostLoc ?? default;
            FVector dir = c.Server.BoostDir;
            FVector stand = loc + (dir * 70f);
            FVector mateAt = loc + (dir * 300f);
            int faceWall = (-dir).Rotation().Yaw & 0xFFFF;
            c.Ops.Run(s =>
            {
                s.PlacePlayer(stand + new FVector(0f, 0f, 2f), faceWall);
                s.PlaceMate(mateAt + new FVector(0f, 0f, 2f), faceWall);
                return true;
            });
        });
        Add("coop: step in front of the helper and climb (T)", 40, c =>
        {
            if (c.Frame <= 1)
            {
                c.MateStates.Clear();
            }

            if (c.ClientMate is not { } helper || c.Pawn is not { } me)
            {
                return null;
            }

            c.RecordClips();
            if (c.State.StartsWith("s_ShortLadder_S", StringComparison.Ordinal))
            {
                c.Remember ??= c.State;
                c.Keys.ReleaseAll();
            }
            else if (c.Remember is null)
            {
                FVector front = helper.Location + (new FRotator(0, helper.Rotation.Yaw, 0).Vector() * 150f);
                if (c.WalkTo(front, 20f) && c.FaceMateStill() && c.Coop == "ClimbMate" && c.CoopDrawn == "Coop2" && c.Frame - c.TapFrame > 30)
                {
                    // The HUD shows "climb on him" (Move_coop2): the co-op key.
                    c.MateStates.Add("icon " + c.CoopDrawn);
                    c.TapFrame = c.Frame;
                    c.Keys.Tap("PerformCoopMvt");
                }
            }

            if (c.MateStates.Count == 0 || c.MateStates[^1] != c.State)
            {
                // Here the player's own states (the mate's list is not used in this step).
                c.MateStates.Add(c.State);
            }

            // Boosted into the ledge grab and hanging (OnLedge), or already over the top. Entering GrabLedge is not
            // enough: a boost that misses the ledge enters it and drops out through NoGE a tick later.
            bool up = c.MateStates.Contains("OnLedge") ||
                (c.Server.BoostLoc is { } loc && me.Location.Z > loc.Z + 450f && me.Physics == EPhysics.PHYS_Walking);
            return c.Remember is not null && up
                ? c.Detail($"player states [{string.Join(",", c.MateStates)}] at {me.Location}, clips [{string.Join(",", c.PlayerClips)}], sounds [{string.Join(",", c.Sounds)}]")
                : null;
        });
        HoistChainSteps();
    }

    /// <summary>
    /// The hoist chain after the boost (coop.md rules 14 to 21): the boosted player hangs and offers his legs (T), the
    /// bot that boosted him, still below, climbs them and is pulled over the top, and the player is back on the hang.
    /// </summary>
    private void HoistChainSteps()
    {
        Add("coop: hanging after the boost, offer a hoist (co-op key T)", 10, c =>
        {
            c.RecordClips();
            if (c.Remember is null && c.State == "OnLedge" && c.StepSeconds > 0.5)
            {
                c.Remember = true;
                c.Keys.Tap("PerformCoopMvt");
            }

            return c.State == "s_Hoist_P_Waiting" && c.Pawn is { bReadyForHoistClimb: true }
                ? c.Detail($"hoist offered, clips [{string.Join(",", c.PlayerClips)}], at {c.Pawn.Location}")
                : null;
        });
        Add("coop: the bot below climbs the player's legs onto the top", 40, c =>
        {
            c.RecordClips();
            c.NoteServerMate();
            bool climbed = c.MateStates.Contains("s_Hoist_S_Jump");
            bool top = c.Server.BoostLoc is { } loc && c.Server.MateLocation.Z > loc.Z + 380f && c.Server.MatePhysics == EPhysics.PHYS_Walking;
            bool pulled = c.PlayerClips.Contains("CoopDynHstBP") || c.PlayerClips.Contains("CoopDynHstBPf");
            return climbed && top && pulled && c.State == "OnLedge"
                ? c.Detail($"mate states [{string.Join(",", c.MateStates)}] at {c.Server.MateLocation}, player {c.State} clips [{string.Join(",", c.PlayerClips)}], sounds [{string.Join(",", c.Sounds)}]")
                : null;
        });
    }

    private void BotHelpSteps(bool spy)
    {
        int markTeam = spy ? 1 : 0;
        Add("bot: let the mate go, step back", 10, c => c.StepSeconds > 2.0 ? c.Detail($"mate {c.Server.MateStatus}") : null,
            begin: c =>
            {
                c.Ops.Every(null);
                (FVector Location, int Yaw)? back = c.BeforeCoop;
                c.Ops.Run(s =>
                {
                    if (s.Mate is { } m)
                    {
                        m.Held = false;
                    }

                    // After the boost the spy hangs from its ledge; let go there, he stands facing the wall a body's
                    // width away, where nobody fits in front of him (the menu wants both facing within 0.75), so no
                    // mate could heal him, beside SteeE's pit. Back where he stood before the co-op steps (setup).
                    if (back is { } home)
                    {
                        s.PlacePlayer(home.Location + new FVector(0f, 0f, 2f), home.Yaw);
                    }

                    return true;
                });
            },
            tick: c =>
            {
                if (c.StepSeconds < 0.6)
                {
                    c.Keys.Press("BT_Down");
                }
                else
                {
                    c.Keys.Release("BT_Down");
                }
            });
        Add("bot: the mate heals the hurt player", 40, c =>
            c.Server.PlayerHealth >= 100 && c.Pawn is { Health: >= 100 } && c.StepSeconds > 0.5 ? c.Detail($"player health {c.Pawn?.Health} on the client, mate {c.Server.MateStatus}") : null,
            tick: c => c.NoteServerMate(),
            begin: c => c.Ops.Run(s =>
            {
                bool placed = s.PlaceMateNear(350f);
                if (s.PlayerPawn is { } p)
                {
                    p.Health = 35;
                }

                s.TraceRoute(placed);
                return true;
            }));
        Add("bot: the mate takes the player's mark off", 40, c =>
            !c.Server.PlayerMarked && c.StepSeconds > 0.5 ? c.Detail($"player mark off, mate {c.Server.MateStatus}") : null,
            tick: c => c.NoteServerMate(),
            begin: c => c.Ops.Run(s =>
            {
                bool placed = s.PlaceMateNear(350f);
                s.PlayerPawn?.HitBySpyBullet(markTeam);
                s.TraceRoute(placed);
                return true;
            }));
        Add("bot: the mate revives the downed player", 45, c =>
        {
            if (c.State == "s_Reanimating")
            {
                c.Remember = true;
            }

            return c.Remember is true && c.Walking ? c.Detail($"revived, mate {c.Server.MateStatus}") : null;
        }, tick: c => c.NoteServerMate(), begin: c => c.Ops.Run(s =>
        {
            // The mate a short walk away (it may have wandered off on its own goals), then the player knocked down.
            bool placed = s.PlaceMateNear(350f);
            s.TraceRoute(placed);
            s.Player?.Handle_DropAttackedDmg(s.Enemies.FirstOrDefault()?.Controller.Pawn);
            return true;
        }));
    }

    // ---- running ---------------------------------------------------------------------------------------------

    public SmokeResult? Step(SmokeContext context)
    {
        if (_root is null)
        {
            return SmokeResult.Fail("no play scene");
        }

        if (_root.LoadError is { } error)
        {
            return SmokeResult.Fail("play scene did not load: " + error);
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return null;
        }

        if (session.ServerFailure is { } failure)
        {
            return SmokeResult.Fail("server failed: " + failure.Message);
        }

        if (session.LoginError is { } login)
        {
            return SmokeResult.Fail("login refused: " + login);
        }

        if (_ops.Error is { } opError)
        {
            return SmokeResult.Fail("setup on the server failed: " + opError);
        }

        _clock = context.ElapsedSeconds;
        if (!_hooked)
        {
            _server = new TeamServer(session, session.Host);
            TeamServer server = _server;
            session.Host.BeforeWorldTick += _ => _ops.OnTick(server);
            _keys = KeyPilot.Attach(_root);
            Ctx = new TeamCtx(session, _root, _keys, _ops);
            TeamCtx ctx = Ctx;
            Action<PlaySession, long>? after = session.AfterClientTick;
            session.AfterClientTick = (s, t) =>
            {
                after?.Invoke(s, t);
                ctx.NoteSounds();
            };
            _hooked = true;
            _stepStart = _clock;
        }

        _keys!.Update();
        if (!_keys.Ready || session.LocalPawn is null)
        {
            _stepStart = _clock;
            return null;
        }

        TeamCtx c = Ctx!;
        TeamStep step = _steps[_index];
        c.StepSeconds = _clock - _stepStart;
        if (!_stepBegun)
        {
            _stepBegun = true;
            c.Frame = 0;
            c.Remember = null;
            c.ResetClips();
            step.Begin?.Invoke(c);
            GD.Print($"SVM_SMOKE {PlayTeamSmoke.TestName} {_side} step {step.Name}");
            return null;
        }

        c.Frame++;
        step.Tick?.Invoke(c);
        if (_clock >= _nextShot)
        {
            _nextShot = _clock + 1.0;
            Shot(step.Name);
            if (System.Environment.GetEnvironmentVariable("SVM_TEAM_TRACE") == "1")
            {
                GD.Print($"SVM_TEAM client {step.Name}: {c.Describe()}");
            }
        }

        string? detail = step.Done(c);
        if (detail is not null)
        {
            _results.Add($"{step.Name}: {detail}");
            GD.Print($"SVM_SMOKE {PlayTeamSmoke.TestName} {_side} ok {step.Name}: {detail}");
            Shot(step.Name + " ok");
            _keys.ReleaseAll();
            _index++;
            _stepBegun = false;
            _stepStart = _clock;
            return _index >= _steps.Count ? SmokeResult.Pass($"{_side}: " + string.Join("; ", _results)) : null;
        }

        if (c.StepSeconds > step.Timeout)
        {
            Shot(step.Name + " FAILED");
            return SmokeResult.Fail($"step '{step.Name}' had no result after {step.Timeout:F0} s ({c.Describe()}) | done: {string.Join("; ", _results)}");
        }

        return null;
    }

    private void Shot(string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        string safe = new string(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{PlayTeamSmoke.TestName}-{_side}-{_clock:000.0}-{safe}.png")));
    }

    public void Dispose()
    {
        _keys?.ReleaseAll();
        _root?.QueueFree();
        _root = null;
    }
}

internal sealed record TeamStep(string Name, double Timeout, Action<TeamCtx>? Begin, Action<TeamCtx>? Tick, Func<TeamCtx, string?> Done);

/// <summary>What a step sees and does: the keys, the client's world and HUD, the server's published view.</summary>
internal sealed class TeamCtx
{
    private readonly PlayRoot _root;

    public TeamCtx(PlaySession session, PlayRoot root, KeyPilot keys, TeamServerOps ops)
    {
        Session = session;
        _root = root;
        Keys = keys;
        Ops = ops;
    }

    public PlaySession Session { get; }

    public KeyPilot Keys { get; }

    public TeamServerOps Ops { get; }

    public int Frame { get; set; }

    public double StepSeconds { get; set; }

    public object? Remember { get; set; }

    /// <summary>The frame a step tapped a key on.</summary>
    public int TapFrame { get; set; }

    public TeamServerView Server => Ops.View;

    public SPlayerController? PC => Session.LocalController;

    public SBasePawn? Pawn => Session.LocalPawn;

    public string State => PC?.GetStateName().ToString() ?? "";

    public bool Walking => State.StartsWith("s_PlayerWalking", StringComparison.Ordinal);

    public bool InMenu => State == "s_MenuInteraction";

    /// <summary>The client's copy of the mate's pawn (the other pawn of the player's side).</summary>
    public SBasePawn? ClientMate
    {
        get
        {
            if (Pawn is not { } me)
            {
                return null;
            }

            foreach (SBasePawn p in Session.Client.World.Level.AllActors<SBasePawn>())
            {
                if (!p.bDeleteMe && !ReferenceEquals(p, me) && p.SPawnType == me.SPawnType)
                {
                    return p;
                }
            }

            return null;
        }
    }

    /// <summary>The Use menu rows the HUD shows (spies), or the usable entries (mercs have no list page).</summary>
    public List<string> Rows
    {
        get
        {
            var rows = new List<string>();
            if (_root.Hud is { } hud && hud.Snapshot.ShowInteractions)
            {
                rows.AddRange(hud.Snapshot.Interactions);
                return rows;
            }

            if (PC?.IManager is { } m && Pawn is SPAWNDEFENSE)
            {
                for (int i = 0; i < m.NbObjects; i++)
                {
                    if (m.Interactions[i] is { bDeleteMe: false } e && e.CanUse(PC))
                    {
                        rows.Add(e.Description);
                    }
                }
            }

            return rows;
        }
    }

    public string? SelectedRow
    {
        get
        {
            if (_root.Hud is { } hud && hud.Snapshot.ShowInteractions)
            {
                int i = hud.Snapshot.SelectedInteraction;
                return i >= 0 && i < hud.Snapshot.Interactions.Count ? hud.Snapshot.Interactions[i] : null;
            }

            return PC?.IManager?.GetCurrentInteraction()?.Description;
        }
    }

    /// <summary>In the menu with <paramref name="row"/> shown (and, for a spy, the list page drawn).</summary>
    public string? MenuOpenWith(bool spy, string row)
    {
        if (!InMenu || !Rows.Contains(row))
        {
            return null;
        }

        bool drawn = _root.Hud?.Interaction?.IsOpen == true;
        if (spy && !drawn)
        {
            return null;
        }

        return Detail($"rows [{string.Join(", ", Rows)}], selected {SelectedRow}, list page {(drawn ? "drawn" : "none (merc)")}");
    }

    /// <summary>Turns the view to the mate with mouse motion; with <paramref name="walk"/> a short walk turns the pawn too.</summary>
    public void FaceMate(bool walk)
    {
        if (PC is not { } pc || Pawn is not { } me || ClientMate is not { } mate)
        {
            return;
        }

        FVector to = mate.Location - me.Location;
        int want = new FVector(to.X, to.Y, 0f).Rotation().Yaw;
        FRotator view = ViewOf(pc, me);
        int err = (short)(want - view.Yaw);
        int pitchErr = (short)(-1200 - (short)view.Pitch);
        if (Math.Abs(err) > 200 || Math.Abs(pitchErr) > 900)
        {
            Look(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -15f, 15f));
        }

        if (!walk)
        {
            return;
        }

        int pawnErr = (short)(want - me.Rotation.Yaw);
        bool close = to.Size() < 100f;
        if (Math.Abs(err) < 600 && Math.Abs(pawnErr) > 1500 && !close)
        {
            Keys.Press("BT_Up");
        }
        else
        {
            Keys.Release("BT_Up");
        }
    }

    /// <summary>The view the mouse turns: a spy's camera, a merc's aim (his desired rotation).</summary>
    private static FRotator ViewOf(SPlayerController pc, SBasePawn me) => me is SPAWNDEFENSE ? pc.MyDesiredRotation : pc.Rotation;

    private void Look(float dx, float dy)
    {
        float width = _root.GetViewport().GetVisibleRect().Size.X;
        if (width > 1f && width < 640f)
        {
            dx *= 1280f / width;
            dy *= 1280f / width;
        }

        Keys.Look(dx, dy);
    }

    /// <summary>Where a co-op step put the player.</summary>
    public FVector CoopStand { get; set; }

    /// <summary>Where the player stood (location, yaw) before the co-op steps took him to the ledge.</summary>
    public (FVector Location, int Yaw)? BeforeCoop { get; set; }

    /// <summary>The coop icon the spy page shows.</summary>
    public string Coop => _root.Hud?.Snapshot.Coop.ToString() ?? "-";

    /// <summary>The coop icon area the HUD drew this frame (Coop1 to Coop4), or "none".</summary>
    public string CoopDrawn
    {
        get
        {
            if (_root.Hud is not { } hud)
            {
                return "none";
            }

            foreach (OpenScctGame.Hud.Magma.MagmaDrawItem item in hud.Items)
            {
                if (item.Path.StartsWith("Coop/Coop", StringComparison.Ordinal))
                {
                    int slash = item.Path.IndexOf('/', 5);
                    return slash > 0 ? item.Path[5..slash] : item.Path[5..];
                }
            }

            return "none";
        }
    }

    /// <summary>The server states the mate went through during a step.</summary>
    public List<string> MateStates { get; } = new();

    public void NoteServerMate()
    {
        string s = Server.MateState;
        if (s.Length > 0 && (MateStates.Count == 0 || MateStates[^1] != s))
        {
            MateStates.Add(s);
            FVector at = Server.MateLocation;
            MateTrail.Add(string.Create(CultureInfo.InvariantCulture, $"{s} ({at.X:F0},{at.Y:F0},{at.Z:F0})"));
        }

        // The bot's team goal as it starts and ends ("team Heal ...", or the side's own play).
        string status = Server.MateStatus;
        string goal = status.StartsWith("team ", StringComparison.Ordinal) ? "goal " + status.Split(' ')[1] : "own play";
        if (goal != _lastGoal)
        {
            _lastGoal = goal;
            FVector at = Server.MateLocation;
            MateTrail.Add(string.Create(CultureInfo.InvariantCulture, $"{goal} ({at.X:F0},{at.Y:F0},{at.Z:F0})"));
        }
    }

    private string _lastGoal = "";

    /// <summary>The server mate's states as they changed this step, with where each began (failure reports).</summary>
    public List<string> MateTrail { get; } = new();

    /// <summary>Tilts the view up at the ledge with mouse motion.</summary>
    public void LookUp()
    {
        if (PC is { } pc && (short)pc.Rotation.Pitch < 5000)
        {
            Look(0f, -6f);
        }
    }

    /// <summary>
    /// Walks to <paramref name="point"/> with the keys (the view turned to it, forward held), true once within
    /// <paramref name="tolerance"/>.
    /// </summary>
    private long _walkLookTick = -1;

    public bool WalkTo(FVector point, float tolerance)
    {
        if (PC is not { } pc || Pawn is not { } me)
        {
            return false;
        }

        FVector to = point - me.Location;
        if (NavFollower.Horizontal(to) < tolerance)
        {
            Keys.Release("BT_Up");
            return true;
        }

        int want = new FVector(to.X, to.Y, 0f).Rotation().Yaw;
        int err = (short)(want - ViewOf(pc, me).Yaw);
        // One turn a simulation tick: the error is read from the last tick's rotation, and a window drawing several
        // frames a tick turned the view several times over and swung it around without walking.
        if (Math.Abs(err) > 200 && Session.TicksRun != _walkLookTick)
        {
            _walkLookTick = Session.TicksRun;
            Look(Math.Clamp(err / KeyPilot.UnitsPerCount, -120f, 120f), 0f);
        }

        if (Math.Abs(err) < 2500)
        {
            Keys.Press("BT_Up");
        }
        else
        {
            Keys.Release("BT_Up");
        }

        return false;
    }

    /// <summary>Turns the view to the mate (mouse only); true once the body faces him too.</summary>
    public bool FaceMateStill()
    {
        FaceMate(walk: false);
        if (Pawn is not { } me || ClientMate is not { } mate)
        {
            return false;
        }

        FVector to = mate.Location - me.Location;
        int want = new FVector(to.X, to.Y, 0f).Rotation().Yaw;
        if (Math.Abs((short)(want - me.Rotation.Yaw)) > 3000)
        {
            // A short step at him turns the body.
            Keys.Tap("BT_Up");
            return false;
        }

        return true;
    }

    /// <summary>The sounds the client's player and mate raised during the step (PlaySound events: clip notifies, gadgets).</summary>
    public HashSet<string> Sounds { get; } = new(StringComparer.Ordinal);

    /// <summary>After each client tick: the sound events of the two pawns.</summary>
    public void NoteSounds()
    {
        SBasePawn? me = Pawn;
        SBasePawn? mate = ClientMate;
        foreach (SimEvent e in Session.Client.World.Events.Current)
        {
            bool mine = me is not null && e.ActorId == me.ActorId;
            bool mates = mate is not null && e.ActorId == mate.ActorId;
            if (e.Kind == SimEventKind.PlaySound && (mine || mates))
            {
                string name = e.What.ToString();
                int dot = name.LastIndexOf('.');
                Sounds.Add((mine ? "player:" : "mate:") + (dot >= 0 ? name[(dot + 1)..] : name));
            }
        }
    }

    public HashSet<string> PlayerClips { get; } = new(StringComparer.Ordinal);

    public HashSet<string> MateClips { get; } = new(StringComparer.Ordinal);

    public void ResetClips()
    {
        PlayerClips.Clear();
        MateClips.Clear();
        Sounds.Clear();
        MateStates.Clear();
        MateTrail.Clear();
        _lastGoal = "";
    }

    /// <summary>The clips the drawn pawns' channels play (channel 0 and the revive channel 16).</summary>
    public void RecordClips()
    {
        Record(Pawn, PlayerClips);
        Record(ClientMate, MateClips);
    }

    private void Record(SBasePawn? pawn, HashSet<string> into)
    {
        if (pawn is null || _root.Pawns?.ViewOf(pawn) is null)
        {
            return;
        }

        foreach (int ch in new[] { 0, 16 })
        {
            // A blend channel counts while it blends in (as the pose applier draws it).
            Name seq = pawn.PawnAnim[ch].Sequence;
            if (!seq.IsNone && (ch == 0 || pawn.PawnAnim[ch].BlendAlpha > 0f))
            {
                into.Add(seq.ToString());
            }
        }
    }

    public string Detail(string text) => string.Create(CultureInfo.InvariantCulture, $"{text} ({StepSeconds:F1} s)");

    public string Describe()
    {
        TeamServerView v = Server;
        SBasePawn? m = ClientMate;
        return string.Create(CultureInfo.InvariantCulture,
            $"client: state {State}, health {Pawn?.Health}, at {Pawn?.Location}, yaw {Pawn?.Rotation.Yaw & 0xFFFF} view {PC?.Rotation.Yaw & 0xFFFF}; mate at {m?.Location} health {m?.Health} marked {m?.bSpyBulletOn} lying {m?.bLyingOnGround} ready {m?.bReadyForShortLadder} c0 {m?.PawnAnim[0].Sequence} phys {m?.Physics} yaw {m?.Rotation.Yaw & 0xFFFF}; menu rows [{string.Join(", ", Rows)}] selected {SelectedRow} coop {(_root.Hud?.Snapshot.Coop.ToString() ?? "-")}; server: player {v.PlayerState} health {v.PlayerHealth} marked {v.PlayerMarked}, mate {v.MateState} health {v.MateHealth}/{v.MateDefaultHealth} marked {v.MateMarked} phys {v.MatePhysics} bot [{v.MateStatus}]; player clips [{string.Join(",", PlayerClips)}] mate clips [{string.Join(",", MateClips)}] sounds [{string.Join(",", Sounds)}] mate trail [{string.Join(", ", MateTrail.TakeLast(30))}]; missing keys [{string.Join(",", Keys.Missing)}]; log [{string.Join(" / ", Session.Log.TakeLast(3))}]");
    }
}
