using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class LocalExplosiveNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimedBotExplosionReachesLocalVictimsActivityFeed(bool mine)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Side = PlaySide.Spy, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Bots = 1, MatchSeed = 905,
        }, clock);
        // The listen host can have been running well before the local client finishes loading.
        session.Host.World.Step(12 * Sim.Hz);
        session.Host.BeforeWorldTick += _ =>
        {
            foreach (var bot in session.Bots!.Bots) bot.Held = true;
        };
        long frame = 0;
        var feed = new HudActivityFeed();
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
                if (session.LocalController is { } pc) feed.Bind(pc, pc.Level.TimeSeconds);
            }
        }
        Step(900);
        SPlayerController owner = Assert.IsType<SPlayerController>(session.LocalController);
        SPlayerController victim = Assert.IsType<SPlayerController>(session.ServerController);
        SPlayerController killer = Assert.Single(session.Bots!.Bots).Controller;
        SBasePawn body = Assert.IsAssignableFrom<SBasePawn>(victim.Pawn);
        SBasePawn merc = Assert.IsAssignableFrom<SBasePawn>(killer.Pawn);
        Assert.IsType<SHud>(owner.myHUD);
        Assert.True(victim.Level.TimeSeconds - owner.Level.TimeSeconds > HudActivityFeed.Lifetime);
        Assert.Equal(1, killer.PlayerReplicationInfo!.Team!.TeamIndex);
        body.SetLocation(new FVector(700f, 300f, body.CollisionHeight + 2f));
        owner.Pawn!.SetLocation(body.Location);
        merc.SetLocation(new FVector(200f, 900f, 100f));
        body.Velocity = owner.Pawn.Velocity = merc.Velocity = FVector.Zero;
        session.Host.Movement.PeekEndpoint(victim)?.Sequenced?.ResetLifetime();
        victim.fSpawnProtectionEnd = 0f;
        var messages = new List<GameCall>();
        NetFunctionHandler? previous = session.Client.Driver.Functions.Fallback;
        session.Client.Driver.Functions.Fallback = (actor, function, args, from) =>
        {
            if (function.Name == "ReceiveLocalizedMessage" && actor is PlayerController receiver)
            {
                GameCall call = NetGameHost.ToGameCall(function, args);
                messages.Add(call);
                HudMessages.ReceiveLocalizedMessage(receiver, call.Class.Path!, call.Int, call.Pri1, call.Pri2,
                    call.Object ?? call.Actor, feed);
            }
            previous?.Invoke(actor, function, args, from);
        };

        FVector at = body.Location - new FVector(100f, 0f, 0f);
        if (mine)
        {
            SMineProx explosive = session.Host.World.Level.SpawnActor(new SMineProx(), at, default, merc, merc)!;
            explosive.Explode = true;
            explosive.fTRef = victim.Level.TimeSeconds;
            explosive.TempsAvantExplosion = .2f;
        }
        else
        {
            SProjFrag explosive = session.Host.World.Level.SpawnActor(new SProjFrag(), at, default, merc, merc)!;
            explosive.SetPhysics(EPhysics.PHYS_None);
            explosive.SetTimer(.2f, false);
        }
        Step(90);

        Assert.Same(owner, session.LocalController);
        Assert.True(body.Health <= 0, "The timed explosion did not kill the local player's pawn.");
        GameCall death = Assert.Single(messages, c => c.Class.Path == HudMessages.DeathMessageClass);
        Assert.Equal((int)SDamageType.SDamage_Explosion, death.Int);
        Assert.Equal(killer.PlayerReplicationInfo.PlayerName, death.Pri1?.PlayerName);
        Assert.Same(owner.PlayerReplicationInfo, death.Pri2);
        HudActivityEntry entry = Assert.Single(feed.Entries, e => e.IsCombat);
        Assert.Equal(HudActivityKind.Kill, entry.Kind);
        Assert.Equal(HudActivityCause.Explosion, entry.Cause);
        Assert.True(entry.InvolvesLocalPlayer);
        Assert.Equal(killer.PlayerReplicationInfo.PlayerName, entry.Actor?.Name);
        Assert.Equal(owner.PlayerReplicationInfo!.PlayerName, entry.Victim?.Name);
    }
}
