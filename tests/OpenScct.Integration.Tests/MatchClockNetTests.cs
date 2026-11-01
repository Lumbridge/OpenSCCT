using System;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MatchClockNetTests
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void Bomb_deadline_tracks_the_fuse_for_existing_and_late_clients(NetProfile profile)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("CineS") is null, "CineS content is required.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Ping200, 1986, 1,
            new ServerSettings { Map = "CineS", GameOptions = "?GameMode=5?TimeLimit=10", PlayersToStart = 4, Profile = profile },
            new ClientSettings { Gameplay = true });
        void Step(int count) { for (int n = 0; n < count; n++) rig.Step(); }
        Step(35 * Sim.Hz + 15);

        SObjective objective = rig.Server.Map.MapActors.OfType<SObjective>().First(o =>
            o.Triggers.OfType<SBombTargetObjectiveTrigger>().Any());
        SBombTargetObjectiveTrigger bomb = objective.Triggers.OfType<SBombTargetObjectiveTrigger>().First();
        objective.ActivationTime = 0;
        objective.RandomTriggers = 0;
        bomb.CaptureTime = 30;
        objective.Reset();
        bomb.ControllerWhoTakesIt = rig.Server.Players[0].Controller;
        bomb.eventTriggering(true);
        float deadline = objective.MinTriggerTime;
        Step(5 * Sim.Hz);
        ClientSession late = rig.AddClient();
        Step(5 * Sim.Hz);

        int samples = 0;
        foreach (ClientSession client in new[] { rig.Clients[0], late })
        {
            SBombTargetObjectiveTrigger copy = Assert.Single(client.ActorChannels.Select(c => c.Actor)
                .OfType<SBombTargetObjectiveTrigger>(), b => !b.bDeleteMe && FVector.DistSquared(b.Location, bomb.Location) < 1f);
            Assert.Equal(bomb.TriggerTime, copy.TriggerTime);
            Assert.Equal(bomb.CaptureTime, copy.CaptureTime);
            Assert.Equal(deadline, copy.GetTriggerTime());
        }
        int objectiveIndex = rig.Server.Driver.MapBinding.IndexOf(objective);
        int[] previousSeconds = [int.MaxValue, int.MaxValue];
        for (int n = 0; n < 15 * Sim.Hz; n++)
        {
            Step(1);
            float fuseLeft = bomb.TimerRate - bomb.TimerCounter;
            foreach (ClientSession client in new[] { rig.Clients[0], late })
            {
                var copy = Assert.IsType<SObjective>(client.Map!.MapActors[objectiveIndex]);
                GameReplicationInfo clock = Assert.IsAssignableFrom<GameReplicationInfo>(client.LocalPlayer?.GameReplicationInfo);
                Assert.Equal(deadline, copy.MinTriggerTime);
                float displayedLeft = copy.MinTriggerTime - clock.PreciseElapsedTime();
                Assert.InRange(displayedLeft - fuseLeft, -0.15f, 0.4f);
                int clientIndex = ReferenceEquals(client, late) ? 1 : 0;
                int shown = (int)MathF.Ceiling(displayedLeft);
                Assert.True(shown <= previousSeconds[clientIndex], "A delayed clock packet must not increase a running bomb countdown.");
                previousSeconds[clientIndex] = shown;
                Assert.InRange(MathF.Ceiling(displayedLeft) - MathF.Ceiling(fuseLeft), -1f, 1f);
                samples++;
            }
        }
        Assert.Equal(1800, samples);

        bomb.eventTriggering(false);
        Step(Sim.Hz);
        foreach (ClientSession client in new[] { rig.Clients[0], late })
            Assert.Equal(0f, Assert.IsType<SObjective>(client.Map!.MapActors[objectiveIndex]).MinTriggerTime);

        bomb.CaptureTime = 5;
        bomb.eventTriggering(true);
        Step(Sim.Hz);
        Assert.InRange(objective.MinTriggerTime - rig.Server.World.Level.Info.GRI!.PreciseElapsedTime(), 3.95f, 4.05f);
        foreach (ClientSession client in new[] { rig.Clients[0], late })
            Assert.Equal(objective.MinTriggerTime, Assert.IsType<SObjective>(client.Map!.MapActors[objectiveIndex]).MinTriggerTime);
        Step(5 * Sim.Hz);
        Assert.False(bomb.bTriggering);
        Assert.True(objective.ObjectiveTaken);
        foreach (ClientSession client in new[] { rig.Clients[0], late })
            Assert.Equal(0f, Assert.IsType<SObjective>(client.Map!.MapActors[objectiveIndex]).MinTriggerTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Late_joined_match_clock_tracks_updates_and_reset_without_counting_client_uptime_twice(bool delayed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        using WalkerRig rig = WalkerRig.Create(delayed ? SimLinkProfile.B : null, 1985, 0,
            new ServerSettings { GameOptions = "?GameMode=5?TimeLimit=10", PlayersToStart = 4, Profile = NetProfile.Enhanced },
            new ClientSettings { Gameplay = true });
        GameReplicationInfo authority = Assert.IsAssignableFrom<GameReplicationInfo>(rig.Server.Game?.GameReplicationInfo);
        for (int n = 0; n < 35 * Sim.Hz; n++) rig.Step();
        Assert.True(authority.ElapsedTime >= 34);
        ClientSession client = rig.AddClient();
        GameReplicationInfo? copy = null;
        for (int n = 0; n < 5 * Sim.Hz && copy is null; n++)
        {
            rig.Step();
            foreach (GameReplicationInfo candidate in client.World.Level.AllActors<GameReplicationInfo>())
                if (candidate.ElapsedTime > 0) copy = candidate;
        }
        Assert.True(copy is not null, $"No replicated clock: client {client.State}, reject '{client.RejectReason}', actors {client.World.Level.Actors.Count}, server players {rig.Server.Players.Count}.");
        int unchangedPackets = 0;
        int samples = 0;
        var pre = client.Driver.PreNetReceive;
        var post = client.Driver.PostNetReceive;
        int beforeElapsed = 0;
        float beforeAnchor = 0;
        bool beforeBegun = false;
        client.Driver.PreNetReceive = actor =>
        {
            if (ReferenceEquals(actor, copy)) { beforeElapsed = copy.ElapsedTime; beforeAnchor = copy.SecondCount; beforeBegun = copy.bMatchHasBegun; }
            pre?.Invoke(actor);
        };
        client.Driver.PostNetReceive = actor =>
        {
            post?.Invoke(actor);
            if (ReferenceEquals(actor, copy) && beforeElapsed == copy.ElapsedTime && beforeBegun == copy.bMatchHasBegun)
            {
                unchangedPackets++;
                Assert.Equal(beforeAnchor, copy.SecondCount);
            }
        };
        for (int n = 0; n < 45 * Sim.Hz; n++)
        {
            // Unrelated replicated properties must not restart the fractional clock on every packet.
            authority.ServerName = "Clock " + n;
            rig.Step();
            if (n < Sim.Hz) continue;
            Assert.InRange(authority.PreciseElapsedTime() - copy.PreciseElapsedTime(), -.15f, 1.25f);
            samples++;
        }
        Assert.True(samples > 2500);
        Assert.True(unchangedPackets > 20, $"Only {unchangedPackets} unchanged clock packets observed.");
        authority.Reset();
        for (int n = 0; n < 2 * Sim.Hz; n++) rig.Step();
        Assert.InRange(copy.ElapsedTime, 0, 2);
        Assert.InRange(authority.PreciseElapsedTime() - copy.PreciseElapsedTime(), -.15f, 1.25f);

        // A match starting within the first second resets 0 to 0: the start flag must rebase its phase too.
        authority.bMatchHasBegun = false;
        authority.Reset();
        for (int n = 0; n < 30; n++) rig.Step();
        Assert.Equal(0, copy.ElapsedTime);
        float previousAnchor = copy.SecondCount;
        authority.Reset();
        authority.bMatchHasBegun = true;
        for (int n = 0; n < 30; n++) rig.Step();
        Assert.True(copy.bMatchHasBegun);
        Assert.Equal(0, copy.ElapsedTime);
        Assert.True(copy.SecondCount > previousAnchor + .2f);
        Assert.InRange(authority.PreciseElapsedTime() - copy.PreciseElapsedTime(), -.15f, .35f);
    }
}
