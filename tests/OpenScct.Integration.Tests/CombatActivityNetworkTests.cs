using System.Collections.Generic;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Integration.Tests;

public sealed class CombatActivityNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData("grenade", 100f, ENetMode.NM_DedicatedServer)]
    [InlineData("proximity", 100f, ENetMode.NM_DedicatedServer)]
    [InlineData("laser", 100f, ENetMode.NM_DedicatedServer)]
    [InlineData("grenade", 300f, ENetMode.NM_DedicatedServer)]
    [InlineData("proximity", 300f, ENetMode.NM_DedicatedServer)]
    [InlineData("laser", 300f, ENetMode.NM_DedicatedServer)]
    [InlineData("grenade", 100f, ENetMode.NM_ListenServer)]
    [InlineData("proximity", 100f, ENetMode.NM_ListenServer)]
    [InlineData("laser", 100f, ENetMode.NM_ListenServer)]
    [InlineData("grenade", 300f, ENetMode.NM_ListenServer)]
    [InlineData("proximity", 300f, ENetMode.NM_ListenServer)]
    [InlineData("laser", 300f, ENetMode.NM_ListenServer)]
    public void MercExplosiveKillArrivesAsOneAttributedDeathMessageAcrossLatency(string weapon, float distance, ENetMode netMode)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 504, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = "TestBox", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
            NetMode = netMode,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=4?LivesDEF=4",
        }, network.Bind, "server", 0);
        long frame = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }

        Step(120);
        if (netMode == ENetMode.NM_ListenServer)
            match.ServerController(match.Bots[0])!.StartMatchOnServer();
        Step(780);
        SPlayerController victim = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[0]));
        SPlayerController merc = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[2]));
        SPlayerController owner = Assert.IsType<SPlayerController>(match.Bots[0].Controller);
        SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(victim.Pawn);
        SBasePawn attacker = Assert.IsAssignableFrom<SBasePawn>(merc.Pawn);
        var received = new List<GameCall>[match.Bots.Count];
        for (int i = 0; i < match.Bots.Count; i++)
        {
            List<GameCall> inbox = received[i] = new();
            ClientSession client = match.Bots[i].Session;
            NetFunctionHandler? previous = client.Driver.Functions.Fallback;
            client.Driver.Functions.Fallback = (actor, function, args, from) =>
            {
                if (function.Name == "ReceiveLocalizedMessage")
                {
                    GameCall call = NetGameHost.ToGameCall(function, args);
                    if (call.Class.Path == "SBase.SDeathMessage" && call.Pri2?.PlayerName == "Spy1")
                        inbox.Add(call);
                }
                previous?.Invoke(actor, function, args, from);
            };
        }

        // Exercise both the close blast and lethal falloff damage from the actual weapon actors.
        // Low health makes the falloff lethal without changing the weapon's damage or range.
        Assert.True(pawn.SetLocation(new FVector(700f, 300f, 100f)));
        Assert.True(match.ServerController(match.Bots[1])!.Pawn!.SetLocation(new FVector(450f, 950f, 100f)));
        pawn.Health = distance > 250f ? 25 : 100;
        FVector at = pawn.Location - new FVector(distance, 0f, 0f);
        if (weapon == "grenade")
        {
            SProjFrag grenade = victim.XLevel.SpawnActor(new SProjFrag(), at, default, attacker, attacker)!;
            grenade.Explode(at, FVector.Zero);
        }
        else if (weapon == "proximity")
        {
            SMineProx mine = victim.XLevel.SpawnActor(new SMineProx { PcOwner = merc }, at, default, attacker, attacker)!;
            mine.Explosion();
        }
        else
        {
            SMineLaser mine = victim.XLevel.SpawnActor(new SMineLaser { PcOwner = merc }, at, default, attacker, attacker)!;
            mine.Explosion();
        }

        Assert.True(pawn.Health <= 0, $"{weapon} at {distance} left {pawn.Health} health.");
        Step(120);

        Assert.All(received, inbox =>
        {
            GameCall death = Assert.Single(inbox);
            Assert.Equal("Merc1", death.Pri1?.PlayerName);
            Assert.Equal("Spy1", death.Pri2?.PlayerName);
        });
        Assert.Same(owner.PlayerReplicationInfo, received[0][0].Pri2);
        Assert.Equal(1, victim.PlayerReplicationInfo!.Deaths);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Kills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerGasFaintArrivesAsOneAttributedActivityMessageAcrossLatency(bool sticky)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestBox") is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 503, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = "TestBox", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
            NetMode = ENetMode.NM_DedicatedServer,
        }, network.Bind, "server", 0);
        long frame = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }

        Step(900);
        SPlayerController authority = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[2]));
        SPlayerController spy = Assert.IsType<SPlayerController>(match.ServerController(match.Bots[0]));
        SPlayerController owner = Assert.IsType<SPlayerController>(match.Bots[2].Controller);
        SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        Assert.Equal(ENetMode.NM_DedicatedServer, match.Server.World.Level.Info.NetMode);
        var received = new List<GameCall>[match.Bots.Count];
        for (int i = 0; i < match.Bots.Count; i++)
        {
            List<GameCall> inbox = received[i] = new();
            ClientSession client = match.Bots[i].Session;
            NetFunctionHandler? previous = client.Driver.Functions.Fallback;
            client.Driver.Functions.Fallback = (actor, function, args, from) =>
            {
                if (function.Name == "ReceiveLocalizedMessage")
                {
                    GameCall call = NetGameHost.ToGameCall(function, args);
                    if (call.Int is K.MsgSmokeSleep or K.MsgStickyCameraSleep && call.Pri2?.PlayerName == "Merc1")
                        inbox.Add(call);
                }
                previous?.Invoke(actor, function, args, from);
            };
        }

        Actor gas = sticky
            ? authority.XLevel.SpawnActor(new SStickyCamEmitter(), pawn.Location - new FVector(192f, 0f, 0f), default)!
            : authority.XLevel.SpawnActor(new SSmokeEmitter(), pawn.Location, default)!;
        gas.Instigator = spy.Pawn;
        Step(sticky ? 180 : 480);

        Assert.All(received, inbox =>
        {
            GameCall sleep = Assert.Single(inbox);
            Assert.Equal(sticky ? K.MsgStickyCameraSleep : K.MsgSmokeSleep, sleep.Int);
            Assert.Equal("Spy1", sleep.Pri1?.PlayerName);
            Assert.Equal("Merc1", sleep.Pri2?.PlayerName);
        });
        Assert.Same(owner.PlayerReplicationInfo, received[2][0].Pri2);
        Assert.True(pawn.bSleeping || pawn.bLyingOnGround || authority.IsInState("s_LyingOnGround"));
    }
}
