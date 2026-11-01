using System;
using System.Collections.Generic;
using System.Globalization;
using CsCheck;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class ConnectionTests
{
    private static readonly SimLinkProfile Harsh = new("harsh", 80, 30, 20f, DuplicatePercent: 5f, ReorderPercent: 10f, ReorderDelayMs: 40);

    [Fact]
    public void ReliableMessagesArriveOnceAndInOrderOverAHarshLink()
    {
        Gen.ULong.Sample(seed =>
        {
            using var rig = new NetRig(Harsh, seed, 1);
            const int count = 120;
            for (int wait = 0; wait < 600 && (rig.Server.ClientConnections.Count == 0 || rig.ServerSide(0).State != EConnectionState.USOCK_Open); wait++)
            {
                rig.Step();
            }

            NetConnection serverSide = rig.ServerSide(0);
            NetRig.Client client = rig.Clients[0];
            Assert.Equal(EConnectionState.USOCK_Open, serverSide.State);
            for (int i = 0; i < count; i++)
            {
                client.ToServer.ControlChannel!.Send(ControlMessage.TextLine("up " + i.ToString(CultureInfo.InvariantCulture)));
                serverSide.ControlChannel!.Send(ControlMessage.TextLine("down " + i.ToString(CultureInfo.InvariantCulture)));
                rig.Step();
            }

            rig.Step(60 * 20);
            Assert.Equal(count, rig.ServerNotify.Texts.Count);
            Assert.Equal(count, client.Notify.Texts.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal("up " + i.ToString(CultureInfo.InvariantCulture), rig.ServerNotify.Texts[i]);
                Assert.Equal("down " + i.ToString(CultureInfo.InvariantCulture), client.Notify.Texts[i]);
            }

            Assert.True(serverSide.Stats.OutReliableResends > 0 || client.ToServer.Stats.OutReliableResends > 0);
            Assert.Equal(0, serverSide.ReliableBunchesInFlight);
            Assert.Equal(0, client.ToServer.ReliableBunchesInFlight);
        }, iter: 12, threads: 1);
    }

    [Fact]
    public void RoundTripTimeFollowsThePing()
    {
        using var rig = new NetRig(new SimLinkProfile("ping", 100, 0, 0f), 3, 1, flushEvery: 1);
        rig.Step(600);
        double rttMs = rig.Clients[0].ToServer.Stats.RttMicros / 1000.0;
        Assert.InRange(rttMs, 99.0, 118.0);
        Assert.InRange(rig.ServerSide(0).Stats.RttMicros / 1000.0, 99.0, 118.0);

        // The acknowledgement lag of the statistics period (spec/native/net-driver.md rule 7) follows it too.
        Assert.InRange(rig.ServerSide(0).AvgLag, 0.085f, 0.125f);
    }

    [Fact]
    public void LostPacketsAreDetected()
    {
        using var rig = new NetRig(new SimLinkProfile("lossy", 40, 0, 10f), 8, 1);
        rig.Step(60 * 30);
        NetStats c = rig.Clients[0].ToServer.Stats;
        double lossPercent = 100.0 * c.OutPacketsLost / c.OutPackets;
        Assert.InRange(lossPercent, 7.0, 13.0);
        NetStats s = rig.ServerSide(0).Stats;
        Assert.InRange(100.0 * s.OutPacketsLost / s.OutPackets, 6.0, 14.0);
        Assert.InRange(100.0 * c.InPacketsLost / s.OutPackets, 6.0, 14.0);
        Assert.InRange(rig.ServerSide(0).InLoss, 2f, 25f);
    }

    [Fact]
    public void ClosingTheControlChannelClosesBothEnds()
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 1);
        rig.Step(30);
        NetConnection serverSide = rig.ServerSide(0);
        rig.Clients[0].ToServer.Close("bye");
        Assert.Equal(EConnectionState.USOCK_Closed, rig.Clients[0].ToServer.State);
        rig.Step(5);
        Assert.Equal(EConnectionState.USOCK_Closed, serverSide.State);
        Assert.Contains(serverSide, rig.ServerNotify.Closed);
        Assert.Empty(rig.Server.ClientConnections);
    }

    [Fact]
    public void SilentConnectionsTimeOut()
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 1);
        rig.Step(30);
        NetConnection serverSide = rig.ServerSide(0);
        rig.Network.Profile = new SimLinkProfile("dead", 0, 0, 100f);
        rig.Step(60 * 16);
        Assert.Equal(EConnectionState.USOCK_Closed, serverSide.State);
        Assert.Equal("Connection timed out", serverSide.CloseReason);
        Assert.Equal(EConnectionState.USOCK_Closed, rig.Clients[0].ToServer.State);
    }

    [Fact]
    public void ActorsReplicateAndLostChangesAreResent()
    {
        using var rig = new NetRig(new SimLinkProfile("lossy", 60, 0, 30f), 21, 1);
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(10f, 20f, 30f), new FRotator(0, 1000, 0))!;
        mover.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        rig.Step(240);
        NetRig.Client client = rig.Clients[0];
        ActorChannel channel = Assert.Single(client.Notify.Opened, c => c.Actor is TestMover);
        Actor copy = channel.Actor!;
        Assert.IsType<TestMover>(copy);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.Role);
        Assert.Equal(ENetRole.ROLE_Authority, copy.RemoteRole);
        Assert.Equal((uint)mover.ActorId, channel.NetGuid);

        // One change each, then nothing: every value must still arrive although 30% of packets are lost.
        for (int round = 0; round < 20; round++)
        {
            mover.Location = new FVector(100f + round, -50f, 7.5f);
            mover.Physics = (EPhysics)(round % 17);
            mover.Rotation = new FRotator(round * 100, round * 200, 0);
            rig.Step(300);
            Assert.Equal(mover.Physics, copy.Physics);
            Assert.Equal(mover.Rotation, copy.Rotation);
            Assert.Equal(mover.Location, channel.Motion!.LatestLocation);
        }
    }

    [Fact]
    public void DestroyedActorsLeaveTheClientAndChannelsAreReused()
    {
        using var rig = new NetRig(SimLinkProfile.B, 4, 1);
        var movers = new List<TestMover>();
        for (int i = 0; i < 5; i++)
        {
            movers.Add(rig.ServerWorld.Level.Spawn<TestMover>(new FVector(i * 100f, 0f, 0f))!);
        }

        rig.Step(120);
        NetRig.Client client = rig.Clients[0];
        Assert.Equal(5, client.Notify.Opened.Count);
        foreach (TestMover mover in movers)
        {
            mover.Destroy();
        }

        rig.Step(180);
        foreach (ActorChannel channel in client.Notify.Opened)
        {
            Assert.True(channel.Actor!.bDeleteMe);
        }

        Assert.Empty(client.ToServer.ActorChannels);
        Assert.Empty(rig.ServerSide(0).ActorChannels);
        Assert.Equal(5, CountDestroyed(client.World));

        TestMover again = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(1f, 2f, 3f))!;
        rig.Step(120);
        ActorChannel reopened = Assert.Single(client.ToServer.ActorChannels);
        Assert.Equal((uint)again.ActorId, reopened.NetGuid);

        // The spawn information carries the exact location.
        Assert.Equal(new FVector(1f, 2f, 3f), reopened.Actor!.Location);

        // Indexes are handed out round robin, so a just-closed index is not reused at once.
        Assert.Equal(6, reopened.ChIndex);
    }

    private static int CountDestroyed(World world)
    {
        int alive = 0;
        foreach (TestMover _ in world.Level.AllActors<TestMover>())
        {
            alive++;
        }

        return 5 - alive;
    }

    [Fact]
    public void GarbageDatagramsDoNotHurtTheServer()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1, SimLinkProfile.A);
        using SimLinkTransport serverTransport = network.Bind("server");
        var rigNotify = new GarbageNotify();
        using var server = new NetDriver(serverTransport, NetRig.CreateRegistry(), rigNotify, isServer: true) { World = new World(1) };
        var attackers = new List<SimLinkTransport>();
        for (int i = 0; i < 8; i++)
        {
            attackers.Add(network.Bind("attacker" + i.ToString(CultureInfo.InvariantCulture)));
        }

        var rng = new SimRandom(5, 0, 0);
        var junk = new byte[600];
        for (int frame = 1; frame <= 60 * 20; frame++)
        {
            foreach (SimLinkTransport attacker in attackers)
            {
                int length = 1 + rng.Rand(junk.Length);
                for (int b = 0; b < length; b++)
                {
                    junk[b] = (byte)rng.Rand(256);
                }

                // Sometimes a valid-looking header so deeper parsing runs too.
                if (rng.Rand(3) == 0 && length > 1)
                {
                    junk[0] = 0xA7;
                }

                attacker.Send(attacker.Connect("server"), junk.AsSpan(0, length));
            }

            clock.AdvanceTo(VirtualClock.TickToMicroseconds(frame, Sim.Hz));
            server.TickDispatch(clock.NowMicroseconds);
            server.TickFlush(clock.NowMicroseconds);
        }

        Assert.Equal(0, rigNotify.Messages);
        foreach (NetConnection connection in server.ClientConnections)
        {
            Assert.NotEqual(EConnectionState.USOCK_Open, connection.State);
        }

        foreach (SimLinkTransport attacker in attackers)
        {
            attacker.Dispose();
        }
    }

    private sealed class GarbageNotify : INetworkNotify
    {
        public int Messages;

        public void NotifyControlMessage(NetConnection connection, in ControlMessage message) => Messages++;
    }

    [Fact]
    public void ControlMessagesRoundTripAndClipLongText()
    {
        string longName = string.Concat(System.Linq.Enumerable.Repeat("Sam😀", 80));
        ControlMessage[] messages =
        [
            ControlMessage.Hello(0x1122334455667788UL, 42UL, longName, 20000, 0xDEADABCDEFUL),
            ControlMessage.Welcome(7, "TestBox", 123456u, 35, 0xABCDEF0123456789UL, "sim/gameplay.json=00ff"),
            ControlMessage.Reject(ControlMessage.VersionMismatchText, "TestBox"),
            ControlMessage.Join(),
            ControlMessage.TextLine(new string('x', 5000)),
        ];

        var w = new FBitWriter(64 * 1024);
        foreach (ControlMessage m in messages)
        {
            m.Write(w);
        }

        var r = new FBitReader(w.Buffer, w.NumBits);
        ControlMessage hello = ControlMessage.Read(r);
        Assert.Equal(0x1122334455667788UL, hello.ProtocolHash);
        Assert.Equal(42UL, hello.ContentHash);
        Assert.Equal(0xDEADABCDEFUL, hello.CharacterHash);
        Assert.Equal(20000, hello.NetSpeed);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(hello.Text!) <= ControlMessage.MaxLoginBytes);
        Assert.StartsWith(hello.Text!, longName, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(hello.Text![^1]));
        Assert.Equal(messages[1], ControlMessage.Read(r));
        Assert.Equal(messages[2], ControlMessage.Read(r));
        Assert.Equal(messages[3], ControlMessage.Read(r));
        Assert.Equal(ControlMessage.MaxTextBytes, ControlMessage.Read(r).Text!.Length);
        Assert.False(r.IsError);
        Assert.True(r.AtEnd);
    }

    [Fact]
    public void ProtocolHashCoversClassesAndLayouts()
    {
        ulong a = NetRig.CreateRegistry().ComputeProtocolHash();
        Assert.Equal(a, NetRig.CreateRegistry().ComputeProtocolHash());
        Assert.NotEqual(a, new NetClassRegistry().ComputeProtocolHash());
        Assert.NotEqual(a, new NetClassRegistry(new OpenScct.Core.Uc.UcClassIndex(OpenScct.Core.CorePackage.Info, OpenScct.Core.Engine.EnginePackage.Info),
            RepConditionSource.Script).ComputeProtocolHash());
    }
}
