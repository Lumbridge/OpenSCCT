using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class ReorderedPacketTests
{
    public sealed class Probe : Actor
    {
        public int Calls;
        public Probe() => RemoteRole = ENetRole.ROLE_SimulatedProxy;
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public void OnlyExplicitlyReorderSafeOwnedFunctionsCanUseLateUnreliableBunches(bool owned, bool safe, int expected)
    {
        var extension = new RepExtension();
        extension.Functions.Add(new("Sequenced", false, true, "test-sequence", (actor, reader, _) =>
        {
            uint sequence = reader.ReadUInt32();
            if (sequence > ((Probe)actor).Calls) ((Probe)actor).Calls = (int)sequence;
        }) { AcceptReordered = safe });
        extension.Functions.Add(new("Ordinary", false, true, "test-ordinary", (actor, _, _) => ((Probe)actor).Calls += 100));
        NetClassRegistry registry = NetRig.CreateRegistry();
        registry.Register<Probe>(extension);
        using var rig = new NetRig(SimLinkProfile.A, 1, 1, registry: registry);
        rig.Step(60);
        NetConnection receiver = rig.ServerSide(0);
        Probe actor = rig.ServerWorld.Level.Spawn<Probe>()!;
        if (owned) actor.SetOwner(receiver.Actor);
        rig.Step(120);
        ActorChannel channel = receiver.FindActorChannel(actor)!;
        Assert.NotNull(channel);
        var newer = new FBitWriter(8192);
        new PacketHeader { Salt = receiver.Salt, Seq = 3001, Tick = 500 }.Write(newer);
        newer.WriteBit(true);
        receiver.ReceivedRawPacket(newer.Buffer, newer.NumBytes, 500);
        var payload = new FBitWriter(2048);
        RepLayout layout = channel.Layout!;
        payload.WriteInt((uint)(layout.PropertyCount + layout.FindFunction("Sequenced")!.Index), (uint)layout.MaxRepIndex);
        payload.WriteUInt32(1);
        // A trailing ordinary RPC must not execute merely because the first RPC opted in.
        payload.WriteInt((uint)(layout.PropertyCount + layout.FindFunction("Ordinary")!.Index), (uint)layout.MaxRepIndex);
        var late = new FBitWriter(8192);
        new PacketHeader { Salt = receiver.Salt, Seq = 3000, Tick = 499 }.Write(late);
        late.WriteBit(false);
        late.WriteBit(false);
        late.WriteInt((uint)channel.ChIndex, K.MAX_CHANNELS);
        late.WriteInt((uint)payload.NumBits, NetConnection.MaxPacketBytesLimit * 8);
        late.AppendBits(payload);
        late.WriteBit(true);
        receiver.ReceivedRawPacket(late.Buffer, late.NumBytes, 501);
        receiver.ReceivedRawPacket(late.Buffer, late.NumBytes, 502);
        Assert.Equal(expected, actor.Calls);
        Assert.Equal(EConnectionState.USOCK_Open, receiver.State);
    }

    [Theory]
    [InlineData(100, 99)]
    [InlineData(100, 68)]
    [InlineData(0, 65535)]
    public void RecentReorderedPacketUnblocksReliableMessagesWithoutRewindingUnreliableState(int newest, int older)
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 0);
        var receiver = new NetConnection(rig.Server, new EndpointId(1), 1, 0);
        receiver.SetOpen();
        receiver.CreateChannel(EChannelType.CHTYPE_Control, false, 0);

        Deliver(receiver, newest, 200, 2, "second", null);
        Assert.Empty(rig.ServerNotify.Texts); // The newer reliable message waits for its predecessor.
        Deliver(receiver, older, 100, 1, "first", "stale unreliable");
        Assert.Equal(new[] { "first", "second" }, rig.ServerNotify.Texts);
        Assert.Equal(200u, receiver.RemoteTick);
        Assert.Equal(200L, receiver.LastReceiveTime);
        Assert.Equal(2, receiver.InReliable[0]);

        Deliver(receiver, older, 100, 1, "first", "stale unreliable");
        Assert.Equal(new[] { "first", "second" }, rig.ServerNotify.Texts);
        Deliver(receiver, (newest + 1) & 65535, 201, 3, "third", "fresh unreliable");
        Assert.Equal(new[] { "first", "second", "third", "fresh unreliable" }, rig.ServerNotify.Texts);
        Assert.Equal(EConnectionState.USOCK_Open, receiver.State);
    }

    [Theory]
    [InlineData(67, 42u)] // Beyond the bounded packet reorder window.
    [InlineData(99, 43u)] // A previous/different connection's packet.
    public void StaleOrWrongSessionPacketCannotDeliverReliableMessages(int older, uint salt)
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 0);
        var receiver = new NetConnection(rig.Server, new EndpointId(1), 1, 0);
        receiver.SetOpen();
        receiver.CreateChannel(EChannelType.CHTYPE_Control, false, 0);
        Deliver(receiver, 100, 200, 2, "second", null);
        Deliver(receiver, older, 100, 1, "first", null, salt);
        Assert.Empty(rig.ServerNotify.Texts);
        Assert.Equal(200u, receiver.RemoteTick);
    }

    [Fact]
    public void PartialLateDeliveryDoesNotAcknowledgeSkippedUnreliableData()
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 1);
        NetConnection receiver = rig.Clients[0].ToServer;
        receiver.SetSalt(42);
        Deliver(receiver, 100, 200, 2, "second", null);
        Deliver(receiver, 99, 100, 1, "first", "stale unreliable");
        Assert.Equal(new[] { "first", "second" }, rig.Clients[0].Notify.Texts);

        receiver.FlushNet(201, force: true);
        var buffer = new byte[2048];
        Assert.True(rig.Server.Transport.TryReceive(buffer, out _, out int length));
        var reader = new FBitReader();
        reader.SetData(buffer, 0, FBitReader.FindTerminatedBitLength(buffer.AsSpan(0, length)));
        Assert.True(PacketHeader.Read(reader, out PacketHeader header));
        Assert.True(header.HasAck);
        Assert.Equal((ushort)100, header.Ack);
        Assert.Equal(0u, header.AckBits & 1u);
    }

    private static void Deliver(NetConnection receiver, int seq, uint tick, int reliableSequence,
        string text, string? unreliable, uint salt = 42)
    {
        var packet = new FBitWriter(8192);
        new PacketHeader { Salt = salt, Seq = (ushort)seq, Tick = tick }.Write(packet);
        WriteText(packet, reliableSequence, text);
        if (unreliable is not null) WriteText(packet, 0, unreliable);
        packet.WriteBit(true);
        receiver.ReceivedRawPacket(packet.Buffer, packet.NumBytes, tick);
    }

    private static void WriteText(FBitWriter packet, int sequence, string text)
    {
        var payload = new FBitWriter(2048);
        ControlMessage.TextLine(text).Write(payload);
        packet.WriteBit(false); // No channel open/close.
        packet.WriteBit(sequence != 0);
        packet.WriteInt(0, K.MAX_CHANNELS);
        if (sequence != 0)
        {
            packet.WriteInt((uint)sequence, K.MAX_CHSEQUENCE);
            packet.WriteInt((uint)EChannelType.CHTYPE_Control, (uint)EChannelType.CHTYPE_MAX);
        }
        packet.WriteInt((uint)payload.NumBits, NetConnection.MaxPacketBytesLimit * 8);
        packet.AppendBits(payload);
    }
}
