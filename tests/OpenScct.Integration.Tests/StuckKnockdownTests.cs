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
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// A spy knocked down by a drop attack in a networked match gets up on both copies: the server's controller follows its
/// owner out of the knockdown (s_FallDropAttacked, s_FallDown, s_LyingOnGround, s_StandUp) within 2 s. The drop is
/// dealt through the attacking merc's server controller (performDropAttack, as the falling physics' trigger calls it)
/// while the spy is in a state that can be drop attacked. Before the reinit stand-in followed state-sync.md rules 12
/// and 15, a reinit raised while the server's copy was still in s_StandUp (its owner already walking) opened a new
/// epoch without mirroring a state, so every later state change of the owner was refused and the server's copy stayed
/// in s_StandUp until the next state the server mirrored itself (seeds 6, 13, 16, 22, 27 of the scan: 14 of 100).
/// <c>SVM_KO_SEEDS=first-last</c> runs the scan over the spy win on SimLink C with 5% stalled frames.
/// </summary>
[Collection(BotMatchCollection.Name)]
public sealed class StuckKnockdownTests
{
    private static readonly NavCache Nav = new();

    /// <summary>The victim's states from the drop to the end of the stand-up.</summary>
    private static readonly HashSet<string> KnockedDown =
        ["s_FallDropAttacked", "s_FallFallDropAttacked", "s_FallDown", "s_LyingOnGround", "s_StandUp", "s_Reanimating"];

    [Theory]
    [InlineData("A", 0f, "timeout", 5UL)]
    [InlineData("C", 0f, "timeout", 5UL)]
    [InlineData("C", 0.05f, "timeout", 5UL)]
    public void ADropAttackedSpyGetsUpOnBothCopies(string profile, float stallChance, string script, ulong seed) =>
        Run(profile, stallChance, script, seed);

    public static TheoryData<ulong> ScanSeeds()
    {
        var data = new TheoryData<ulong>();
        string? range = Environment.GetEnvironmentVariable("SVM_KO_SEEDS");
        if (range is { Length: > 0 })
        {
            string[] parts = range.Split('-');
            for (ulong x = ulong.Parse(parts[0], CultureInfo.InvariantCulture); x <= ulong.Parse(parts[1], CultureInfo.InvariantCulture); x++)
            {
                data.Add(x);
            }
        }
        else
        {
            foreach (ulong x in new ulong[] { 6UL, 13UL, 16UL, 22UL, 27UL })
            {
                data.Add(x);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ScanSeeds))]
    public void ADropAttackedWalkingSpyGetsUp(ulong seed) => Run("C", 0.05f, "spies", seed);

    private static void Run(string profile, float stallChance, string script, ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        Nav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, terminalsBlockPlayers: false);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, seed, SimLinkProfile.Get(profile));
        var settings = new BotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Seed = seed,
            Bots = StoryScripts.Bots(script),
            GameOptions = BotMatchSettings.StoryOptions,
            Classes = WalkerRig.Classes,
            Maps = WalkerRig.Maps,
            Nav = Nav,
            TerminalsBlockPlayers = false,
        };
        using var match = new BotMatch(settings, network.Bind, "server", clock.NowMicroseconds);
        var stalls = new SimRandom(seed, 0, 1);
        var pick = new SimRandom(seed, 7, 1);
        BotClient spy = match.Bots[pick.Rand(2)];
        BotClient merc = match.Bots[2];
        Assert.Equal(BotTeam.Spy, spy.Team);
        Assert.Equal(BotTeam.Merc, merc.Team);
        var log = new StringBuilder();
        string lastServer = "";
        string lastClient = "";
        long dropFrame = script == "timeout" ? 8 * Sim.Hz : (4 + pick.Rand(20)) * Sim.Hz;
        long frame = 0;
        long walkingAgainAt = -1;
        long clientUpAt = -1;
        long serverUpAt = -1;
        bool dropped = false;
        bool clientWasDown = false;
        bool serverWasDown = false;
        while (frame < 50 * Sim.Hz)
        {
            frame++;
            long now = Math.Max(VirtualClock.TickToMicroseconds(frame, Sim.Hz), clock.NowMicroseconds + 200);
            if (stallChance > 0f && stalls.FRand() < stallChance)
            {
                now += 20_000 + stalls.Rand(230_000);
            }

            clock.AdvanceTo(now);
            match.Step(clock.NowMicroseconds);
            SPlayerController? ss = match.ServerController(spy);
            SPlayerController? cs = spy.Controller;
            if (frame >= dropFrame && frame < dropFrame + 5 * Sim.Hz && !dropped && ss is { } sv && sv.Pawn is SBasePawn { Health: > 0 } &&
                Game.Combat.CombatStateEvents.CanBeDropAttacked(sv))
            {
                dropped = true;
                dropFrame = frame;
                Assert.NotNull(ss);
                Assert.True(match.ServerController(merc) is { } mpc && ss!.Pawn is SBasePawn victim, "no merc or spy on the server");
                match.ServerController(merc)!.performDropAttack((SBasePawn)ss!.Pawn!);
            }

            string s = (ss?.GetStateName().ToString() ?? "-") + string.Create(CultureInfo.InvariantCulture, $" msat {ss?.MirrorStateAcknowledgementTime:F3}");
            string c = (cs?.GetStateName().ToString() ?? "-") + string.Create(CultureInfo.InvariantCulture, $" msat {cs?.MirrorStateAcknowledgementTime:F3}");
            if (s != lastServer || c != lastClient)
            {
                log.Append(CultureInfo.InvariantCulture, $"{frame} {match.Server.World.TimeSeconds:F2} server {s} client {c}\n");
                lastServer = s;
                lastClient = c;
            }

            if (match.Ended)
            {
                break;
            }

            bool clientDown = cs is not null && KnockedDown.Contains(cs.GetStateName().ToString());
            bool serverDown = ss is not null && KnockedDown.Contains(ss.GetStateName().ToString());
            if (dropped && clientUpAt < 0 && clientWasDown && !clientDown)
            {
                clientUpAt = frame;
            }

            if (dropped && serverUpAt < 0 && serverWasDown && !serverDown)
            {
                serverUpAt = frame;
            }

            clientWasDown = clientDown;
            serverWasDown = serverDown;
            if (dropped && frame > dropFrame && walkingAgainAt < 0 && s.StartsWith("s_PlayerWalking", StringComparison.Ordinal) &&
                c.StartsWith("s_PlayerWalking", StringComparison.Ordinal))
            {
                walkingAgainAt = frame;
            }
        }

        Assert.SkipUnless(dropped, "The spy was never in a state that can be drop attacked.");
        if (walkingAgainAt < 0 && match.Ended && frame - dropFrame < 22 * Sim.Hz)
        {
            return;
        }

        Assert.True(walkingAgainAt > 0, "The spy never walked again on both copies:\n" + log);
        Assert.True(clientUpAt > 0 && serverUpAt > 0, "The knockdown did not end on both copies:\n" + log);
        Assert.True(serverUpAt - clientUpAt < 2 * Sim.Hz,
            $"The server's copy got up {(serverUpAt - clientUpAt) / (float)Sim.Hz:F1} s after its owner:\n" + log);
        Assert.True(walkingAgainAt - dropFrame < 22 * Sim.Hz, $"The spy walked again {(walkingAgainAt - dropFrame) / (float)Sim.Hz:F1} s after the drop:\n" + log);
    }
}
