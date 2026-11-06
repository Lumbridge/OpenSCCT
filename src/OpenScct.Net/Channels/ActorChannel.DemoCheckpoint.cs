using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Net;

public sealed partial class ActorChannel
{
    private FVector _demoSpawnLocation;
    private FRotator _demoSpawnRotation;
    private Dictionary<int, (byte[] Payload, int Bits)>? _unmappedDemoProperties;
    private FBitReader? _demoPropertyReader;

    /// <summary>When the recorder first opened this actor on the wire.</summary>
    public uint DemoSpawnTick { get; private set; }

    // Recent is only updated when all object references are mapped. Preserve the actual last payload
    // for other properties, including any None references the original playback client received.
    private void RememberUnmappedDemoProperty(int index, FBitWriter writer, int start)
    {
        _unmappedDemoProperties ??= new();
        _unmappedDemoProperties.TryGetValue(index, out var previous);
        int bits = writer.NumBits - start;
        int bytes = (bits + 7) >> 3;
        byte[] payload = previous.Payload is { } buffer && buffer.Length >= bytes ? buffer : new byte[bytes];
        FBitReader reader = _demoPropertyReader ??= new();
        reader.SetData(writer.Buffer, start, bits);
        for (int i = 0; i < bytes; i++) payload[i] = (byte)reader.ReadBits(Math.Min(8, reader.BitsLeft));
        _unmappedDemoProperties[index] = (payload, bits);
    }

    internal bool CanWriteDemoCheckpoint => _sentInitial && OpenAcked && !Closing && Actor is { bDeleteMe: false, bTearOff: false };

    internal ActorChannel CreateDemoCheckpointChannel(NetConnection checkpoint)
    {
        var channel = (ActorChannel)checkpoint.CreateChannel(ChType, openedLocally: true, ChIndex);
        // Bind only the shadow connection's lookup, with no registration in the live replication state.
        channel.Actor = Actor;
        channel.ClassInfo = ClassInfo;
        channel.NetGuid = NetGuid;
        channel.IsMapActor = IsMapActor;
        checkpoint.ActorChannelByActorId.Add(Actor!.ActorId, channel);
        return channel;
    }

    internal bool WriteDemoCheckpoint(ActorChannel target, bool references)
    {
        NetConnection checkpoint = target.Connection;
        NetClassInfo info = ClassInfo!;
        RepLayout layout = info.Layout;
        FOutBunch bunch = checkpoint.BeginBunch(target, reliable: true);
        if (!references)
        {
            bunch.bOpen = true;
            target.WriteSpawnInfo(bunch.Writer, Actor!, info, checkpoint.Driver, _demoSpawnLocation, _demoSpawnRotation);
        }

        for (int i = 0; i < layout.PropertyCount; i++)
        {
            if (_retirement[i].PacketSeq < 0) continue;
            RepProperty property = layout.Properties[i];
            if (Array.Exists(property.Leaves, static leaf => leaf.Kind == RepLeafKind.Object) != references) continue;

            int mark = bunch.Writer.NumBits;
            WriteDemoCheckpointProperty(bunch.Writer, checkpoint, layout, property, i);
            if (!bunch.Writer.IsError) continue;
            bunch.Writer.Rewind(mark);
            if (mark == 0 || checkpoint.SendBunch(bunch) < 0) return false;
            bunch = checkpoint.BeginBunch(target, reliable: true);
            WriteDemoCheckpointProperty(bunch.Writer, checkpoint, layout, property, i);
            if (bunch.Writer.IsError) return false;
        }

        return bunch.Writer.NumBits == 0 || checkpoint.SendBunch(bunch) >= 0;
    }

    private void WriteDemoCheckpointProperty(FBitWriter writer, NetConnection checkpoint, RepLayout layout, RepProperty property, int index)
    {
        if (_unmappedDemoProperties?.TryGetValue(index, out var sent) == true)
        {
            writer.AppendBits(sent.Payload, sent.Bits);
            return;
        }

        writer.WriteInt((uint)property.WriteIndex, (uint)layout.MaxRepIndex);
        foreach (RepLeaf leaf in property.Leaves)
        {
            NetWire.WriteWords(writer, leaf.Kind, leaf.EnumCount, _recent.AsSpan(property.WordOffset + leaf.WordOffset),
                _recentObjects.AsSpan(property.ObjectOffset + leaf.ObjectOffset), checkpoint);
        }
    }
}
