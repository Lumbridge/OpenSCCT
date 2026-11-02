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
