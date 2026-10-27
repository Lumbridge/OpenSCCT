using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Story 2v2 matches between bot clients on a dedicated server (docs/NETCODE.md, "Bot matches"). The scripts decide
/// the winner: spies that hack their share of the terminals while the mercs patrol without firing (END_STORY), spies
/// that walk into mercs who shoot on sight (END_TEAMDEATH), and both sides staying home (END_TIMEOUT). Every client
/// must see the same end and hold the same results as the server, no client may need a reinit, resting pawns must
/// agree, and each client must stay under 12 KB/s.
/// <para>
/// The TestBox fixture (two terminals) runs the whole matrix by default. The real SteeE, converted locally into
/// <c>.content/svmap</c>, runs its spy win on profile C by default and the rest with <c>SVM_BOT_MATCHES=1</c> (several
/// minutes), as does the real-time UDP match on SteeE.
/// </para>
/// </summary>
[Collection(BotMatchCollection.Name)]
public sealed class BotMatchTests
{
    private const double BandwidthLimit = 12 * 1024;

    private static readonly NavCache Nav = new();

    private readonly ITestOutputHelper _output;

    public BotMatchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool LongRuns => Environment.GetEnvironmentVariable("SVM_BOT_MATCHES") == "1";

    /// <summary>
    /// Walk graphs for the stalled matches, built as the UDP match builds its own (terminals not blocking players), so
    /// their routes do not depend on which match of the run built <see cref="Nav"/> first.
    /// </summary>
    private static readonly NavCache StallNav = new();

    /// <summary>The settings of a Story script on <paramref name="map"/>.</summary>
    private static BotMatchSettings Settings(string map, string script, ulong seed, NavCache? nav = null) => new()
    {
        Map = map,
        Seed = seed,
        Bots = StoryScripts.Bots(script),
        GameOptions = BotMatchSettings.StoryOptions,
        Classes = WalkerRig.Classes,
        Maps = WalkerRig.Maps,
        Nav = nav ?? Nav,

        // The converted terminal collision keeps a spy's walk to the use spot 4 uu short of it, so the hack never
        // starts (docs/NETCODE.md, "Bot matches"); the spies' script plays with terminals that do not block players.
        TerminalsBlockPlayers = script != "spies",
    };

    [Theory]
    [InlineData("A", "spies", 71UL)]
    [InlineData("B", "spies", 72UL)]
    [InlineData("C", "spies", 73UL)]
    [InlineData("A", "mercs", 74UL)]
    [InlineData("B", "mercs", 75UL)]
    [InlineData("C", "mercs", 76UL)]
    [InlineData("A", "timeout", 77UL)]
    [InlineData("B", "timeout", 78UL)]
    [InlineData("C", "timeout", 79UL)]
    public void StoryMatchOnTestBox(string profile, string script, ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        RunOverSimLink(MapCatalog.TestBox, profile, script, seed);
    }

    [Theory]
    [InlineData("A", "spies", 61UL)]
    [InlineData("B", "spies", 62UL)]
    [InlineData("C", "spies", 63UL)]
    [InlineData("B", "mercs", 64UL)]
    [InlineData("C", "timeout", 65UL)]
    public void StoryMatchOnSteeE(string profile, string script, ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        Assert.SkipUnless((profile == "C" && script == "spies") || LongRuns,
            "Set SVM_BOT_MATCHES=1 for the whole SteeE matrix (several minutes); C spies runs by default.");
        RunOverSimLink("SteeE", profile, script, seed);
    }

    /// <summary>
    /// The spy win on TestBox over SimLink C, with the frames stepped as a loaded machine steps the real-time UDP match:
    /// 2% of the frames stall 20 to 250 ms and the frames behind are then run back to back on a clock that has moved
    /// on, so the packets of the stall arrive together, with the 3 minute limit of the UDP match. Seeds 1001, 1010,
    /// 1018 and 1034 kept the spies from Terminal B while the merc bots walked their spawn heading whatever way they
    /// looked (a merc's view follows his aim every frame, and the bots turned only the view), parking a merc in a
    /// doorway; seed 1022 then held two spies and two mercs nose to nose in the east doorway until the walkers gave way
    /// to each other.
    /// </summary>
    [Theory]
    [InlineData(1001UL)]
    [InlineData(1010UL)]
    [InlineData(1018UL)]
    [InlineData(1022UL)]
    [InlineData(1034UL)]
    public void StoryMatchOnTestBoxWithStalls(ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        StallNav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, terminalsBlockPlayers: false);
        RunOverSimLink(MapCatalog.TestBox, "C", "spies", seed, maxFrames: 3L * 60 * Sim.Hz, stalls: new SimRandom(seed, 0, 1), nav: StallNav);
    }

    /// <summary>The spy win over real UDP sockets on localhost (LiteNetLib), in real time: TestBox, or SteeE with SVM_BOT_MATCHES=1.</summary>
    [Fact]
    public void StoryMatchOverUdpOnLocalhost()
    {
        string map = LongRuns && WalkerRig.Maps.FindFolder("SteeE") is not null ? "SteeE" : MapCatalog.TestBox;
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(map) is null, "The TestBox fixture is not in this checkout.");
        Nav.Prepare(WalkerRig.Maps, map, terminalsBlockPlayers: false);
        using LiteNetLibTransport server = LiteNetLibTransport.Listen(0, 8, loopbackOnly: true);
        string address = "127.0.0.1:" + server.LocalPort.ToString(CultureInfo.InvariantCulture);
        ITransport Bind(string name) => name == "server" ? server : LiteNetLibTransport.CreateClient(loopbackOnly: true);
        var watch = Stopwatch.StartNew();
        long Now() => (long)watch.Elapsed.TotalMicroseconds;
        using var match = new BotMatch(Settings(map, "spies", 70UL), Bind, address, Now());
        long frame = 0;
        long endFrame = -1;

        // TestBox's spies win in under a minute; SteeE's in a few.
        long maxFrames = (map == MapCatalog.TestBox ? 3L : 12L) * 60 * Sim.Hz;
        while (frame < maxFrames)
        {
            long due = VirtualClock.TickToMicroseconds(frame + 1, Sim.Hz);
            if (Now() < due)
            {
                Thread.Sleep(1);
                continue;
            }

            frame++;
            match.Step(Now());
            if (match.Ended && endFrame < 0)
            {
                endFrame = frame;
            }

            if (endFrame >= 0 && frame - endFrame > 3 * Sim.Hz)
            {
                break;
            }
        }

        BotMatchReport report = match.Report(watch.Elapsed.TotalSeconds);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"UDP {map} spies: {frame} frames in {watch.Elapsed.TotalSeconds:F1} s of real time"));
        _output.WriteLine(report.ToString());
        foreach (BotClient bot in match.Bots)
        {
            _output.WriteLine($"  {bot.Name}: {bot.Brain.Status}");
        }

        AssertMatch(report, StoryScripts.Expected("spies"), checkObjectives: true);
    }

    /// <summary>Chance per frame of a stall in a stalled match, and its shortest and longest length (microseconds).</summary>
    private const float StallChance = 0.02f;

    private const int StallMin = 20_000;

    private const int StallMax = 250_000;

    /// <summary>Clock step of a frame run back to back behind a stall (microseconds).</summary>
    private const int CatchUpStep = 200;

    private void RunOverSimLink(string map, string profile, string script, ulong seed, long maxFrames = 60L * 60 * 12,
        SimRandom? stalls = null, NavCache? nav = null)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, seed, SimLinkProfile.Get(profile));
        using var match = new BotMatch(Settings(map, script, seed, nav), network.Bind, "server", clock.NowMicroseconds);
        var watch = Stopwatch.StartNew();
        long frame = 0;
        long endFrame = -1;
        while (frame < maxFrames)
        {
            frame++;
            long now = Math.Max(VirtualClock.TickToMicroseconds(frame, Sim.Hz), clock.NowMicroseconds + (stalls is null ? 0 : CatchUpStep));
            if (stalls is not null && stalls.FRand() < StallChance)
            {
                now += StallMin + stalls.Rand(StallMax - StallMin);
            }

            clock.AdvanceTo(now);
            match.Step(clock.NowMicroseconds);
            if (match.Ended && endFrame < 0)
            {
                endFrame = frame;
            }

            // Keep running for 3 s after the end so the results reach every client.
            if (endFrame >= 0 && frame - endFrame > 3 * Sim.Hz)
            {
                break;
            }
        }

        BotMatchReport report = match.Report(frame / (double)Sim.Hz);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{map} {profile} {script}: {frame} frames ({frame / (double)Sim.Hz:F0} s simulated) in {watch.Elapsed.TotalSeconds:F1} s"));
        _output.WriteLine(report.ToString());
        foreach (BotClient bot in match.Bots)
        {
            _output.WriteLine($"  {bot.Name}: {bot.Brain.Status}");
        }

        AssertMatch(report, StoryScripts.Expected(script), checkObjectives: script == "spies", maxReinits: script == "mercs" ? MercsReinits : 0);
    }

    /// <summary>
    /// Reinits a client may need in the eliminations script: a spy hit near a roof edge can leave its server copy
    /// held at the lip while the client falls, and the divergence check then reinitialises the pawn, as designed
    /// (docs/NETCODE.md, open items). Every other script must need none.
    /// </summary>
    private const int MercsReinits = 2;

    private static void AssertMatch(BotMatchReport report, (int Team, EEndReason Reason) expected, bool checkObjectives, int maxReinits = 0)
    {
        Assert.True(report.Started, "The match did not start.");
        Assert.True(report.Ended, "The round did not end.");
        Assert.Equal(expected.Team, report.WinningTeam);
        Assert.Equal((int)expected.Reason, report.WinningReason);
        if (checkObjectives)
        {
            Assert.True(report.MissionObjectivesTaken >= report.MissionObjectivesNeeded,
                $"{report.MissionObjectivesTaken} of {report.MissionObjectivesNeeded} objectives taken.");
        }

        foreach (BotConnectionReport c in report.Connections)
        {
            Assert.Equal(expected.Team, c.ClientWinningTeam);
            Assert.Equal((int)expected.Reason, c.ClientWinningReason);
            Assert.True(c.ClientSawGameEnded, $"{c.Name}'s controller did not reach GameEnded.");
            Assert.True(c.ResultsMatch, $"{c.Name}'s results differ from the server's.");
            Assert.True(c.Reinits <= maxReinits, $"{c.Name} needed {c.Reinits} reinits.");
            Assert.True(c.RestErrorP95 < 2f, string.Create(CultureInfo.InvariantCulture, $"{c.Name} rests {c.RestErrorP95:F2} uu away from the server."));
            Assert.True(c.ServerToClientBytesPerSecond < BandwidthLimit,
                string.Create(CultureInfo.InvariantCulture, $"{c.ServerToClientBytesPerSecond:F0} B/s to {c.Name} is over 12 KB/s."));
        }
    }
}

/// <summary>Bot matches run one at a time (each takes a whole core and shares the walk graphs).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BotMatchCollection
{
    public const string Name = "Bot matches";
}
