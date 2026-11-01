using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MatchClockNetTests
{
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
