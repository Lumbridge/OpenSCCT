using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Net;

public sealed partial class NetConnection
{
    internal const string DemoBootstrapReady = "#demo-checkpoint-ready";

    private const int DemoContinuationHeaderBytes = 8;
    private const int DemoContinuationChannelBytes = 4;
    private const ushort DemoChannelOpenFlag = 0x8000;
    private List<Actor>? _demoInitialActors;
    private bool _demoBootstrapReady;

    internal bool TryDeferDemoPostNetBeginPlay(Actor actor)
    {
        if (_driver.Transport is not DemoPlaybackTransport { IsBootstrapping: true }) return false;
        (_demoInitialActors ??= new()).Add(actor);
        return true;
    }

    internal bool TryFinishDemoActorBootstrap(in ControlMessage message)
    {
        if (message.Type != EControlMessage.Text || message.Text != DemoBootstrapReady ||
            _driver.Transport is not DemoPlaybackTransport { IsBootstrapping: true })
            return false;

        if (_demoBootstrapReady)
        {
            Close("Duplicate demo checkpoint marker");
            return true;
        }

        _demoBootstrapReady = true;
        if (_demoInitialActors is { } actors)
        {
            _demoInitialActors = null;
            foreach (Actor actor in actors)
                if (!actor.bDeleteMe) actor.eventPostNetBeginPlay();
        }
        return true;
    }

    // Include closed channel indices with nonzero reliable sequences. If a later actor reuses that index,
    // its opening bunch continues the old sequence rather than starting again at one.
    internal byte[] WriteDemoContinuation()
    {
        int count = 0;
        for (int i = 0; i < Channels.Length; i++)
        {
            if (Channels[i] is not null || OutReliable[i] != 0) count++;
        }

        var result = new byte[DemoContinuationHeaderBytes + count * DemoContinuationChannelBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(result, Salt);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), unchecked((ushort)(_outSeq - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)count);

        int offset = DemoContinuationHeaderBytes;
        for (int i = 0; i < Channels.Length; i++)
        {
            bool open = Channels[i] is not null;
            int sequence = OutReliable[i];
            if (!open && sequence == 0) continue;
            ushort index = open ? (ushort)(i | DemoChannelOpenFlag) : (ushort)i;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), index);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 2), (ushort)sequence);
            offset += DemoContinuationChannelBytes;
        }

        return result;
    }

    internal bool TryRestoreDemoContinuation(ReadOnlySpan<byte> data)
    {
        if (_driver.IsServer || !_driver.Transport.IsPlayback || State != EConnectionState.USOCK_Open ||
            !_demoBootstrapReady || _demoInitialActors is not null ||
            !_hasReceived || !_saltKnown || data.Length < DemoContinuationHeaderBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != Salt)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        if (count > Channels.Length || data.Length != DemoContinuationHeaderBytes + count * DemoContinuationChannelBytes)
        {
            return false;
        }

        Span<int> sequences = stackalloc int[K.MAX_CHANNELS];
        sequences.Clear();
        int lastIndex = -1;
        int openChannels = 0;
        for (int offset = DemoContinuationHeaderBytes; offset < data.Length; offset += DemoContinuationChannelBytes)
        {
            ushort encodedIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            int index = encodedIndex & ~DemoChannelOpenFlag;
            int sequence = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
            bool open = (encodedIndex & DemoChannelOpenFlag) != 0;
            if (index <= lastIndex || index >= Channels.Length || sequence >= K.MAX_CHSEQUENCE ||
                open != (Channels[index] is not null) ||
                Channels[index] is { NumInRec: not 0 } or ActorChannel { Actor: null })
            {
                return false;
            }

            sequences[index] = sequence;
            lastIndex = index;
            if (open) openChannels++;
        }

        // Control and actor channels are the only supported types. A missing bootstrap actor or a partial open
        // would make the succeeding original delta stream unsafe to apply.
        if (ControlChannel is null || openChannels != ActorChannelList.Count + 1) return false;
        sequences.CopyTo(InReliable);
        _inSeq = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        _inAckBits = 0;
        _demoBootstrapReady = false;
        return true;
    }
}
