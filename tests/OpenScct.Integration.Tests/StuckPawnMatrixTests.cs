using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Stuck pawns in stalled-frame bot matches (the TestBox spy win on SimLink C, as
/// <see cref="BotMatchTests.StoryMatchOnTestBoxWithStalls"/>, at 5% stalls): no server or client copy of a pawn may stay in
/// PHYS_Falling for more than <see cref="MaxFallSeconds"/> (a fall out of the world ends in s_Dead and is listed apart),
/// no controller may stay in a knockdown state (s_FallDown, s_LyingOnGround, s_StandUp, s_Reanimating) for more than
/// <see cref="MaxKnockdownSeconds"/>, and the server's copy may not stay in a knockdown state its owner has left for more
/// than <see cref="MaxStateLagSeconds"/>, nor any controller in the use menu for more than <see cref="MaxMenuSeconds"/>.
/// Rest errors, reinits, falls out of the world, closed connections (with the server's reason) and rounds that do not
/// end are written to the output, not asserted (docs/NETCODE.md, "Stalled-frame bot matches"). The default run plays the
/// seeds that showed the stuck states; <c>SVM_STALL_SEEDS=first-last</c> plays a range (the 150 seed matrix is
/// <c>2001-2150</c>), <c>SVM_STALL_CHANCE</c> sets the stall chance, <c>SVM_STALL_LOG=folder</c> writes each seed's state
/// changes and rests there, and <c>SVM_STALL_TRACE=Name:first-last</c> adds that bot's two copies frame by frame.
/// </summary>
[Collection(BotMatchCollection.Name)]
public sealed class StuckPawnMatrixTests
{
    private const float StallChance = 0.05f;

    private const int StallMin = 20_000;

    private const int StallMax = 250_000;

    private const int CatchUpStep = 200;

    /// <summary>The longest fall a TestBox match can need (seconds); the deepest drop there takes under 1 s.</summary>
    private const float MaxFallSeconds = 2.5f;

    /// <summary>A knockdown: 7 s lying, under 3 s for the fall and the stand-up clips.</summary>
    private const float MaxKnockdownSeconds = 12f;

    /// <summary>The use menu: the spy bots release Use as soon as it opens and the hack starts within a second.</summary>
    private const float MaxMenuSeconds = 15f;

    /// <summary>How long the server's copy may stay in a knockdown state after its owner left it (seconds).</summary>
    private const float MaxStateLagSeconds = 3f;

    private static readonly NavCache StallNav = new();

    private static readonly HashSet<string> KnockdownStates = ["s_FallDown", "s_LyingOnGround", "s_StandUp", "s_Reanimating"];

    private readonly ITestOutputHelper _output;

    public StuckPawnMatrixTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<ulong> Seeds()
    {
        var data = new TheoryData<ulong>();
        string? range = Environment.GetEnvironmentVariable("SVM_STALL_SEEDS");
        if (range is { Length: > 0 })
        {
            string[] parts = range.Split('-');
            ulong first = ulong.Parse(parts[0], CultureInfo.InvariantCulture);
            ulong last = parts.Length > 1 ? ulong.Parse(parts[1], CultureInfo.InvariantCulture) : first;
            for (ulong s = first; s <= last; s++)
            {
                data.Add(s);
            }

            return data;
        }

        foreach (ulong s in DefaultSeeds)
        {
            data.Add(s);
        }

        return data;
    }

    /// <summary>
    /// 2016: a merc's client hung in PHYS_Falling in the wall by the door to room C for 4 s (a correction's unchecked
    /// teleport), then fell through the floor; 2103: a spy's client the same at the door to the corridor to room B.
    /// </summary>
    private static readonly ulong[] DefaultSeeds = [2016UL, 2103UL];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoStuckPawnsInStalledMatch(ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        StallNav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, terminalsBlockPlayers: false);
        string? chance = Environment.GetEnvironmentVariable("SVM_STALL_CHANCE");
        StallRun run = Run(seed, chance is { Length: > 0 } ? float.Parse(chance, CultureInfo.InvariantCulture) : StallChance);
        _output.WriteLine(run.Summary());
        string? logDir = Environment.GetEnvironmentVariable("SVM_STALL_LOG");
        if (logDir is { Length: > 0 })
        {
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, $"seed{seed}.txt"), run.Summary() + "\n" + run.Log);
        }

        Assert.True(run.Problems.Count == 0, string.Join("; ", run.Problems));
    }

    /// <summary>A stalled match and what its monitor found.</summary>
    internal sealed class StallRun
    {
        /// <summary>Stuck pawns.</summary>
        public List<string> Problems { get; } = [];

        /// <summary>What the run showed that is not a stuck pawn: rest errors, reinits, falls out of the world, no round end.</summary>
        public List<string> Notes { get; } = [];

        public StringBuilder Log { get; } = new();

        public BotMatchReport? Report { get; set; }

        public long Frames { get; set; }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"{Frames} frames, problems {Problems.Count}, notes {Notes.Count}");
            foreach (string p in Problems)
            {
                sb.Append("\n  ").Append(p);
            }

            foreach (string n in Notes)
            {
                sb.Append("\n  note: ").Append(n);
            }

            if (Report is not null)
            {
                sb.Append('\n').Append(Report);
            }

            return sb.ToString();
        }
    }

    /// <summary>Tracks one copy (server or client) of a bot's pawn and controller.</summary>
    private sealed class CopyWatch(string label)
    {
        private string _state = "";
        private EPhysics _physics = EPhysics.PHYS_None;
        private float _stateSince;
        private float _fallSince = -1f;
        private bool _fallReported;
        private bool _stateReported;

        public string State => _state;

        public float StateSince => _stateSince;

        public void Observe(SPlayerController? pc, float now, long frame, StallRun run)
        {
            string state = pc?.GetStateName().ToString() ?? "-";
            EPhysics physics = pc?.Pawn?.Physics ?? EPhysics.PHYS_None;
            FVector at = pc?.Pawn?.Location ?? default;
            if (state != _state)
            {
                run.Log.Append(CultureInfo.InvariantCulture, $"{frame} {now:F2} {label} state {_state} -> {state} at {at}\n");
                _state = state;
                _stateSince = now;
                _stateReported = false;
            }

            if (physics != _physics)
            {
                run.Log.Append(CultureInfo.InvariantCulture, $"{frame} {now:F2} {label} physics {_physics} -> {physics} at {at}\n");
                _physics = physics;
                _fallSince = physics == EPhysics.PHYS_Falling ? now : -1f;
                _fallReported = false;
            }

            if (_fallSince >= 0f && !_fallReported && now - _fallSince > MaxFallSeconds)
            {
                _fallReported = true;
                string fall = string.Create(CultureInfo.InvariantCulture, $"{label} falling since {_fallSince:F2} s (frame {frame}) at {at} in {state}");
                if (state is "s_PlayerFalling_OutOfWorld" or "s_Dead")
                {
                    run.Notes.Add(fall + " (out of the world)");
                }
                else
                {
                    run.Problems.Add(fall);
                }
            }

            if (KnockdownStates.Contains(state) && !_stateReported && now - _stateSince > MaxKnockdownSeconds)
            {
                _stateReported = true;
                run.Problems.Add(string.Create(CultureInfo.InvariantCulture, $"{label} in {state} since {_stateSince:F2} s (frame {frame}) at {at}"));
            }

            if (state == "s_MenuInteraction" && !_stateReported && now - _stateSince > MaxMenuSeconds)
            {
                _stateReported = true;
                run.Problems.Add(string.Create(CultureInfo.InvariantCulture, $"{label} in the use menu since {_stateSince:F2} s (frame {frame}) at {at}"));
            }
        }
    }

    /// <summary>Logs the start of each rest where the two copies are still and apart by more than 2 uu.</summary>
    private static void LogRest(string name, SPlayerController? server, SPlayerController? client, float now, long frame, StallRun run, ref int logged)
    {
        float error = server?.Pawn is { } sp0 && client?.Pawn is { } cp0 ? (sp0.Location - cp0.Location).Size() : 0f;
        if (server?.Pawn is not { } sp || client?.Pawn is not { } cp || !sp.Velocity.IsZero() || !cp.Velocity.IsZero() || error <= 2f)
        {
            if (logged > 30)
            {
                run.Log.Append(CultureInfo.InvariantCulture, $"{frame} {now:F2} {name} REST END after {logged} frames, error now {error:F2}{'\n'}");
            }

            logged = 0;
            return;
        }

        if (logged++ == 30)
        {
            run.Log.Append(CultureInfo.InvariantCulture,
                $"{frame} {now:F2} {name} REST {error:F2} server {sp.Location} {sp.Physics} {server.GetStateName()} client {cp.Location} {cp.Physics} {client.GetStateName()}{'\n'}");
        }
    }

    /// <summary>The gap between <paramref name="pawn"/>'s cylinder and the nearest other pawn's (uu), for traces.</summary>
    private static float Nearest(Pawn? pawn, World world)
    {
        float best = float.MaxValue;
        if (pawn is null)
        {
            return best;
        }

        foreach (Pawn other in world.Level.AllActors<Pawn>())
        {
            if (!ReferenceEquals(other, pawn) && !other.bDeleteMe)
            {
                best = Math.Min(best, (other.Location - pawn.Location).Size2D() - other.CollisionRadius - pawn.CollisionRadius);
            }
        }

        return best;
    }

    /// <summary>Corrections the server sent to <paramref name="bot"/> and the client applied, and moves applied, for traces.</summary>
    private static string Corrections(BotMatch match, BotClient bot)
    {
        ServerMoves? server = match.ServerController(bot) is { } spc ? match.Server.Movement.EndpointOf(spc)?.Server : null;
        ClientMoves? client = bot.Controller is { } cpc ? bot.Session.Movement.EndpointOf(cpc)?.Client : null;
        return string.Create(CultureInfo.InvariantCulture,
            $"sent {(server?.Sent.Corrections ?? 0) + (server?.Sent.ShortCorrections ?? 0)} applied {client?.CorrectionsApplied ?? 0} moves {server?.MovesApplied ?? 0}");
    }

    internal static StallRun Run(ulong seed, float stallChance = StallChance)
    {
        var run = new StallRun();
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, seed, SimLinkProfile.Get("C"));
        var settings = new BotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Seed = seed,
            Bots = StoryScripts.Bots("spies"),
            GameOptions = BotMatchSettings.StoryOptions,
            Classes = WalkerRig.Classes,
            Maps = WalkerRig.Maps,
            Nav = StallNav,
            TerminalsBlockPlayers = false,
        };
        using var match = new BotMatch(settings, network.Bind, "server", clock.NowMicroseconds);
        var stalls = new SimRandom(seed, 0, 1);
        var watches = new List<(CopyWatch Server, CopyWatch Client)>();
        foreach (BotClient bot in match.Bots)
        {
            watches.Add((new CopyWatch(bot.Name + " server"), new CopyWatch(bot.Name + " client")));
        }

        var restLogged = new int[match.Bots.Count];
        var lagReported = new bool[match.Bots.Count];
        var serverConnections = new NetConnection?[match.Bots.Count];

        // SVM_STALL_TRACE=Name:first-last logs that bot's two copies every frame of the range.
        (string Name, long From, long To)? trace = null;
        if (Environment.GetEnvironmentVariable("SVM_STALL_TRACE") is { Length: > 0 } spec)
        {
            string[] nameRange = spec.Split(':');
            string[] fromTo = nameRange[1].Split('-');
            trace = (nameRange[0], long.Parse(fromTo[0], CultureInfo.InvariantCulture), long.Parse(fromTo[1], CultureInfo.InvariantCulture));
        }

        long maxFrames = 3L * 60 * Sim.Hz;
        long frame = 0;
        long endFrame = -1;
        while (frame < maxFrames)
        {
            frame++;
            long now = Math.Max(VirtualClock.TickToMicroseconds(frame, Sim.Hz), clock.NowMicroseconds + CatchUpStep);
            if (stalls.FRand() < stallChance)
            {
                now += StallMin + stalls.Rand(StallMax - StallMin);
            }

            clock.AdvanceTo(now);
            match.Step(clock.NowMicroseconds);
            float t = match.Server.World.TimeSeconds;
            if (endFrame < 0)
            {
                for (int i = 0; i < match.Bots.Count; i++)
                {
                    BotClient bot = match.Bots[i];
                    watches[i].Server.Observe(match.ServerController(bot), t, frame, run);
                    NetConnection? conn = null;
                    foreach (PlayerSlot slot in match.Server.Players)
                    {
                        if (slot.Name == bot.Name)
                        {
                            conn = slot.Connection;
                        }
                    }

                    if (serverConnections[i] is { } was && !ReferenceEquals(was, conn))
                    {
                        run.Notes.Add(string.Create(CultureInfo.InvariantCulture,
                            $"{bot.Name}'s connection closed on the server at {t:F2} s (frame {frame}): {was.CloseReason ?? "no reason"}"));
                    }

                    serverConnections[i] = conn;
                    watches[i].Client.Observe(bot.Controller, t, frame, run);
                    LogRest(bot.Name, match.ServerController(bot), bot.Controller, t, frame, run, ref restLogged[i]);
                    (CopyWatch sw, CopyWatch cw) = watches[i];
                    if (KnockdownStates.Contains(sw.State) && sw.State != cw.State && !KnockdownStates.Contains(cw.State) &&
                        t - cw.StateSince > MaxStateLagSeconds && !lagReported[i])
                    {
                        lagReported[i] = true;
                        run.Problems.Add(string.Create(CultureInfo.InvariantCulture,
                            $"{bot.Name} server in {sw.State} {t - cw.StateSince:F1} s after its owner went to {cw.State} (frame {frame})"));
                    }
                    if (trace is { } tr && tr.Name == bot.Name && frame >= tr.From && frame <= tr.To)
                    {
                        SPlayerController? s = match.ServerController(bot);
                        SPlayerController? c = bot.Controller;
                        run.Log.Append(CultureInfo.InvariantCulture,
                            $"{frame} {t:F3} clk {clock.NowMicroseconds} T S {s?.Pawn?.Location} v {s?.Pawn?.Velocity} {s?.Pawn?.Physics} {s?.GetStateName()} | C {c?.Pawn?.Location} v {c?.Pawn?.Velocity} {c?.Pawn?.Physics} {c?.GetStateName()} a {c?.Pawn?.Acceleration} near S {Nearest(s?.Pawn, match.Server.World):F1} C {Nearest(c?.Pawn, bot.Session.World):F1} corr {Corrections(match, bot)} menu S {s?.IManager?.GetNbInteractions()}/{s?.IManager?.GetCurrentInteraction()} C {c?.IManager?.GetNbInteractions()}/{c?.IManager?.GetCurrentInteraction()} use {c?.bUsing}{'\n'}");
                    }
                }
            }

            if (match.Ended && endFrame < 0)
            {
                endFrame = frame;
            }

            if (endFrame >= 0 && frame - endFrame > 3 * Sim.Hz)
            {
                break;
            }
        }

        run.Frames = frame;
        run.Report = match.Report(frame / (double)Sim.Hz);
        if (!run.Report.Ended)
        {
            run.Notes.Add("the round did not end");
        }

        foreach (BotConnectionReport c in run.Report.Connections)
        {
            if (c.RestErrorP95 >= 2f)
            {
                run.Notes.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Name} rests {c.RestErrorP95:F2} uu away (max {c.MaxRestError:F2})"));
            }

            if (c.Reinits > 0)
            {
                run.Notes.Add($"{c.Name} needed {c.Reinits} reinits");
            }
        }

        return run;
    }
}
