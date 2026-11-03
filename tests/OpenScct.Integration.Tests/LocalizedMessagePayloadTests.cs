using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class LocalizedMessagePayloadTests
{
    [Fact]
    public void ExplicitMessagePayloadTakesPrecedenceOverBroadcastSender()
    {
        RepFunction function = WalkerRig.Classes.Find(typeof(SPlayerController))!.Layout.FindFunction("ReceiveLocalizedMessage")!;
        var sender = new SGameInfo();
        var attacker = new PlayerReplicationInfo { PlayerName = "Attacker" };
        var victim = new PlayerReplicationInfo { PlayerName = "Victim" };
        // Message-specific data must survive for existing alarms/objectives/scores as well as the
        // event feed's stable detonator PRI. The sender is not a ReceiveLocalizedMessage argument.
        foreach (UObject payload in new UObject[] { new SAlarm(), new SMission(), new LevelInfo(), new PlayerReplicationInfo() })
        {
            var sent = new GameCall("ReceiveLocalizedMessage")
            {
                Class = new ClassRef("SBase.SGameMessages"), Int = 7,
                Pri1 = attacker, Pri2 = victim, Actor = sender, Object = payload,
            };
            var args = new NetCallArgs();
            NetGameHost.Fill(function, sent, args);
            GameCall received = NetGameHost.ToGameCall(function, args);
            Assert.Same(payload, received.Object);
            Assert.Same(attacker, received.Pri1);
            Assert.Same(victim, received.Pri2);
            Assert.Equal(sent.Class, received.Class);
            Assert.Equal(sent.Int, received.Int);
        }
    }

    [Fact]
    public void ActorOnlyMessagePayloadKeepsItsFallback()
    {
        RepFunction function = WalkerRig.Classes.Find(typeof(SPlayerController))!.Layout.FindFunction("ReceiveLocalizedMessage")!;
        var alarm = new SAlarm();
        var args = new NetCallArgs();
        NetGameHost.Fill(function, new GameCall("ReceiveLocalizedMessage") { Actor = alarm }, args);
        Assert.Same(alarm, NetGameHost.ToGameCall(function, args).Object);
    }
}
