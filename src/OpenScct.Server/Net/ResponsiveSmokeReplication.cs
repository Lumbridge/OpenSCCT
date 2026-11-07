using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// Bounded, atomic cloud baselines and sparse updates on persistent actor channels. Short-lived disturbances
/// use an independent, repeated unreliable stream, so a delayed geometry chunk cannot hold a bullet hole shut.
/// The normal actor-channel recorder captures both streams without a separate demo format.
/// </summary>
public sealed partial class ResponsiveSmokeReplication
{
    private const string StateFunction = "SvmResponsiveSmokeState";
    private const string CarvesFunction = "SvmResponsiveSmokeCarves";
    private const string RequestFunction = "SvmResponsiveSmokeRequest";
    private const int MaxPayload = 1024 * 1024;
    private const int ChunksPerPass = 4;
    private const int RepeatTicks = 3;
    private readonly NetDriver _driver;
    private readonly Dictionary<SSmokeEmitter, Source> _sources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ActorChannel, Viewer> _viewers = new();
    private readonly Dictionary<SSmokeEmitter, Receiver> _received = new(ReferenceEqualityComparer.Instance);
    private readonly List<SSmokeEmitter> _live = new();
    private readonly List<SSmokeEmitter> _gone = new();
    private readonly List<ActorChannel> _closed = new();
    private long _clockTick = -1;
    private float _clockSeconds, _clockStep = Sim.DeltaTime;
    private uint _sequence;
    private int _start;

    private sealed class Source
    {
        public ResponsiveSmokeSnapshot Snapshot = null!;
        public byte[] Baseline = [];
        public byte[] Carves = [];
        public ulong CarveKey = ulong.MaxValue;
        public uint GeometryRevision;
    }

    private sealed class Viewer
    {
        public ResponsiveSmokeSnapshot? Baseline;
        public Transfer? State, Fast;
        public long LastFastTick = -RepeatTicks;
        public ulong CarveKey = ulong.MaxValue;
        public uint GeometryRevision;
    }

    private sealed class Transfer
    {
        public required byte[] Bytes;
        public required uint Sequence;
        public required byte Kind;
        public required uint Revision;
        public uint BaseRevision;
        public required long Tick;
        public required float Seconds, Step;
        public ResponsiveSmokeSnapshot? Snapshot;
        public uint GeometryRevision;
        public int Offset;
    }

    private sealed class Receiver
    {
        public ResponsiveSmokeSnapshot? Baseline;
        public Assembly? State, Fast;
        public uint StateSequence, FastSequence;
        public bool NeedsBaseline;
        public long LastRequestTick = -60;
    }

    private sealed class Assembly
    {
        public required uint Sequence, Revision, BaseRevision;
        public required byte Kind;
        public required byte[] Bytes;
        public required bool[] Seen;
        public int Received;
    }

    private ResponsiveSmokeReplication(NetDriver driver)
    {
        _driver = driver;
        driver.World!.Level.SetService(this);
    }

    public static ResponsiveSmokeReplication For(NetDriver driver) =>
        driver.World!.Level.GetService<ResponsiveSmokeReplication>() ?? new(driver);

    public long StateBytesSent { get; private set; }
    public long CarveBytesSent { get; private set; }
    public long BaselinesReceived { get; private set; }
    public long DeltasReceived { get; private set; }
    public long CarveUpdatesReceived { get; private set; }
    public long ResyncRequests { get; private set; }
    public long InvalidPayloads { get; private set; }

    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension { PersistentActorChannel = true };
        extension.Functions.Add(new(StateFunction, true, false, "responsive-smoke-state-2-shape-6", ReceiveState));
        extension.Functions.Add(new(CarvesFunction, false, false, "responsive-smoke-carves-2", ReceiveCarves)
            { AcceptReordered = true });
        return extension;
    }

    public static RepExtensionFunction RequestExtension() =>
        new(RequestFunction, true, true, "responsive-smoke-request-2", ReceiveRequest);

    public void Flush()
    {
        Level level = _driver.World!.Level;
        if (!_driver.IsServer || !ResponsiveSmokeWorld.IsEnabled(level)) return;
        long start = DiagnosticsStart(out long allocated);
        try { FlushCore(level); }
        finally { DiagnosticsEnd(ref _flushDiagnostics, start, allocated); }
    }

    private void FlushCore(Level level)
    {
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(level);
        _live.Clear();
        foreach (SSmokeEmitter cloud in level.DynamicActors<SSmokeEmitter>())
        {
            if (cloud.bDeleteMe || !world.TryGet(cloud, out ResponsiveSmokeField field)) continue;
            _live.Add(cloud);
            if (!_sources.TryGetValue(cloud, out Source? source)) _sources.Add(cloud, source = new());
            if (source.Snapshot is not null && source.Snapshot.Revision == field.Revision) continue;
            ResponsiveSmokeSnapshot current = world.Capture(cloud);
            if (source.Snapshot is null || source.Snapshot.CreatedAt != current.CreatedAt || source.Snapshot.Origin != current.Origin ||
                source.Snapshot.Center != current.Center || !source.Snapshot.Cells.AsSpan().SequenceEqual(current.Cells) ||
                !source.Snapshot.ActivationTicks.AsSpan().SequenceEqual(current.ActivationTicks))
                source.GeometryRevision = current.Revision;
            source.Snapshot = current;
            source.Baseline = []; // Encode lazily; firing alone must not enqueue reliable field traffic.
            ulong key = CarveKey(source.Snapshot.Carves);
            if (source.CarveKey != key)
            {
                source.CarveKey = key;
                source.Carves = EncodeCarves(source.Snapshot.Carves);
            }
        }
        Prune();
        if (_live.Count == 0) return;
        // Rotate first service to prevent a succession of large baselines starving later clouds.
        _start %= _live.Count;
        foreach (NetConnection connection in _driver.ClientConnections)
        {
            if (!connection.bJoined || connection.State != EConnectionState.USOCK_Open) continue;
            for (int n = 0; n < _live.Count && connection.IsNetReady; n++)
            {
                SSmokeEmitter cloud = _live[(_start + n) % _live.Count];
                if (connection.FindActorChannel(cloud) is not { OpenAcked: true, Closing: false } channel) continue;
                if (!_viewers.TryGetValue(channel, out Viewer? viewer)) _viewers.Add(channel, viewer = new());
                Source source = _sources[cloud];
                // Finish the current chunk set before coalescing to a newer list: sustained fire must not keep
                // restarting a multi-packet clearing forever. Repeating active lists recovers dropped packets.
                bool newCarves = viewer.CarveKey != source.CarveKey;
                if (viewer.Fast is null && (newCarves || source.Snapshot.Carves.Length > 0) &&
                    level.TickCount - viewer.LastFastTick >= RepeatTicks)
                {
                    viewer.Fast = MakeTransfer(source.Carves, 2, source.Snapshot, 0);
                    viewer.CarveKey = source.CarveKey;
                    viewer.LastFastTick = level.TickCount;
                }
                if (viewer.State is null && (viewer.Baseline is null || viewer.GeometryRevision != source.GeometryRevision))
                {
                    if (source.Baseline.Length == 0) source.Baseline = EncodeBaseline(source.Snapshot);
                    byte[] bytes = source.Baseline;
                    byte kind = 0;
                    uint baseline = 0;
                    if (viewer.Baseline is { } previous)
                    {
                        byte[] delta = EncodeDelta(previous, source.Snapshot);
                        if (delta.Length < bytes.Length) { bytes = delta; kind = 1; baseline = previous.Revision; }
                    }
                    viewer.State = MakeTransfer(bytes, kind, source.Snapshot, baseline);
                    viewer.State.GeometryRevision = source.GeometryRevision;
                }
                // Reserve progress for the first baseline, then give temporary openings priority over later
                // geometry. Neither stream consumes an unbounded number of packets in one replication pass.
                if (viewer.Baseline is null) SendState(channel, viewer, 1);
                if (viewer.Fast is { } fast && Send(channel, fast, reliable: false, ChunksPerPass)) viewer.Fast = null;
                SendState(channel, viewer, ChunksPerPass - 1);
            }
        }
        _start = (_start + 1) % _live.Count;
    }

    private void SendState(ActorChannel channel, Viewer viewer, int budget)
    {
        if (viewer.State is { } pending && Send(channel, pending, reliable: true, budget))
        {
            viewer.Baseline = pending.Snapshot;
            viewer.GeometryRevision = pending.GeometryRevision;
            viewer.State = null;
        }
    }

    private Transfer MakeTransfer(byte[] bytes, byte kind, ResponsiveSmokeSnapshot snapshot, uint baseline)
    {
        if (bytes.Length is <= 0 or > MaxPayload) throw new InvalidDataException("Smoke payload exceeds its bounded transfer size.");
        Level level = _driver.World!.Level;
        return new Transfer { Bytes = bytes, Sequence = ++_sequence, Kind = kind, Revision = snapshot.Revision,
            BaseRevision = baseline, Snapshot = snapshot, Tick = level.TickCount, Seconds = level.WorldTimeSeconds, Step = level.DeltaTime };
    }

    private bool Send(ActorChannel channel, Transfer transfer, bool reliable, int budget)
    {
        string function = reliable ? StateFunction : CarvesFunction;
        int chunk = Math.Min(768, Math.Max(16, _driver.MaxPacketBytes - 128));
        for (int i = 0; i < budget && transfer.Offset < transfer.Bytes.Length; i++)
        {
            if (!channel.Connection.IsNetReady || reliable && channel.NumOutRec >= 64) return false;
            int count = Math.Min(chunk, transfer.Bytes.Length - transfer.Offset);
            FOutBunch bunch = channel.BeginFunction(channel.Layout!.FindFunction(function)!, reliable);
            FBitWriter writer = bunch.Writer;
            writer.WriteIntPacked(transfer.Sequence); writer.WriteByte(transfer.Kind);
            writer.WriteIntPacked(transfer.Revision); writer.WriteIntPacked(transfer.BaseRevision);
            writer.WriteIntPacked((uint)transfer.Tick); writer.WriteFloat(transfer.Seconds); writer.WriteFloat(transfer.Step);
            writer.WriteIntPacked((uint)transfer.Bytes.Length); writer.WriteIntPacked((uint)transfer.Offset);
            writer.WriteIntPacked((uint)count); writer.WriteBytes(transfer.Bytes.AsSpan(transfer.Offset, count));
            if (channel.SendFunction(bunch) < 0) return false;
            transfer.Offset += count;
            if (reliable) StateBytesSent += count; else CarveBytesSent += count;
        }
        return transfer.Offset == transfer.Bytes.Length;
    }

    private static ulong CarveKey(IReadOnlyList<ResponsiveSmokeCarve> carves) =>
        ((ulong)(carves.Count == 0 ? 0 : carves[^1].Sequence) << 32) | (uint)carves.Count;

    private void Prune()
    {
        foreach (SSmokeEmitter cloud in _sources.Keys) if (cloud.bDeleteMe) _gone.Add(cloud);
        foreach (SSmokeEmitter cloud in _gone) _sources.Remove(cloud);
        _gone.Clear();
        foreach (ActorChannel channel in _viewers.Keys)
            if (channel.Closing || channel.Connection.State == EConnectionState.USOCK_Closed || channel.Actor?.bDeleteMe != false) _closed.Add(channel);
        foreach (ActorChannel channel in _closed) _viewers.Remove(channel);
        _closed.Clear();
        foreach (SSmokeEmitter cloud in _received.Keys) if (cloud.bDeleteMe) _gone.Add(cloud);
        foreach (SSmokeEmitter cloud in _gone) _received.Remove(cloud);
        _gone.Clear();
    }

    /// <summary>Align analytic cloud age with the established server timeline, including late joins and replay.</summary>
    public void AfterReceive()
    {
        if (_driver.IsServer) return;
        Level level = _driver.World!.Level;
        if (_clockTick >= 0 && _driver.Timeline.IsValid)
        {
            long now = _driver.Timeline.RenderTimeFixed(_driver.LocalTick, 0);
            float seconds = _clockSeconds + (float)((now - _clockTick * NetTimeline.Fraction) * (double)_clockStep / NetTimeline.Fraction);
            ResponsiveSmokeWorld.For(level).SynchronizeTime(seconds);
        }
        Prune();
        if (_driver.ServerConnection is not { Actor: PlayerController controller } connection ||
            connection.FindActorChannel(controller) is not { Closing: false, NumOutRec: < 64 } owner) return;
        foreach (var pair in _received)
        {
            Receiver receiver = pair.Value;
            if (!receiver.NeedsBaseline || _driver.LocalTick - receiver.LastRequestTick < 30 || !connection.IsNetReady ||
                connection.FindActorChannel(pair.Key) is not { Closing: false } cloudChannel) continue;
            FOutBunch bunch = owner.BeginFunction(owner.Layout!.FindFunction(RequestFunction)!, true);
            bunch.Writer.WriteIntPacked(cloudChannel.NetGuid);
            if (owner.SendFunction(bunch) >= 0) { receiver.LastRequestTick = _driver.LocalTick; ResyncRequests++; }
        }
    }

    private static void ReceiveState(Actor actor, FBitReader reader, NetConnection connection) => Receive(actor, reader, connection, false);
    private static void ReceiveCarves(Actor actor, FBitReader reader, NetConnection connection) => Receive(actor, reader, connection, true);

    private static void Receive(Actor actor, FBitReader reader, NetConnection connection, bool fast)
    {
        if (connection.Driver.IsServer || actor is not SSmokeEmitter cloud || cloud.bDeleteMe) return;
        uint sequence = reader.ReadIntPacked(); byte kind = reader.ReadByte();
        uint revision = reader.ReadIntPacked(), baseline = reader.ReadIntPacked();
        uint tick = reader.ReadIntPacked(); float seconds = reader.ReadFloat(), step = reader.ReadFloat();
        uint length = reader.ReadIntPacked(), offset = reader.ReadIntPacked(), count = reader.ReadIntPacked();
        if (reader.IsError || sequence == 0 || revision == 0 || length is 0 or > MaxPayload || count is 0 or > 1024 ||
            offset > length || count > length - offset || count * 8 > reader.BitsLeft ||
            !float.IsFinite(seconds) || !float.IsFinite(step) || step < 0f || step > 1f || (fast ? kind != 2 : kind > 1))
        { reader.SetError(); return; }
        ResponsiveSmokeReplication stream = For(connection.Driver);
        if (!stream._received.TryGetValue(cloud, out Receiver? receiver)) stream._received.Add(cloud, receiver = new());
        uint completed = fast ? receiver.FastSequence : receiver.StateSequence;
        Assembly? assembly = fast ? receiver.Fast : receiver.State;
        if (sequence <= completed || assembly is not null && sequence < assembly.Sequence)
        { reader.Skip((int)count * 8); return; }
        if (assembly is null || assembly.Sequence != sequence)
        {
            assembly = new Assembly { Sequence = sequence, Kind = kind, Revision = revision, BaseRevision = baseline,
                Bytes = new byte[length], Seen = new bool[length] };
            if (fast) receiver.Fast = assembly; else receiver.State = assembly;
        }
        if (assembly.Bytes.Length != length || assembly.Revision != revision || assembly.BaseRevision != baseline || assembly.Kind != kind)
        { reader.SetError(); stream.InvalidPayloads++; return; }
        Span<byte> part = assembly.Bytes.AsSpan((int)offset, (int)count);
        reader.ReadBytes(part);
        if (reader.IsError) return;
        for (int i = (int)offset; i < offset + count; i++)
            if (!assembly.Seen[i]) { assembly.Seen[i] = true; assembly.Received++; }
        if (assembly.Received != assembly.Bytes.Length) return;
        try
        {
            ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(cloud.XLevel);
            if (fast)
            {
                ResponsiveSmokeCarve[] carves = stream.DecodeCarves(assembly.Bytes);
                world.ApplyCarves(cloud, revision, seconds, carves);
                receiver.FastSequence = sequence; receiver.Fast = null; stream.CarveUpdatesReceived++;
            }
            else
            {
                if (kind == 1 && receiver.Baseline?.Revision != baseline)
                {
                    receiver.NeedsBaseline = true; receiver.State = null; receiver.StateSequence = sequence;
                    return;
                }
                ResponsiveSmokeSnapshot snapshot = kind == 0 ? stream.DecodeBaseline(assembly.Bytes)
                    : stream.DecodeDelta(receiver.Baseline!, assembly.Bytes);
                if (snapshot.Revision != revision) throw new InvalidDataException("Smoke transfer revision mismatch.");
                world.ApplySnapshot(cloud, snapshot);
                receiver.Baseline = snapshot; receiver.StateSequence = sequence; receiver.State = null;
                receiver.NeedsBaseline = false;
                if (kind == 0) stream.BaselinesReceived++; else stream.DeltasReceived++;
            }
            if (tick >= stream._clockTick)
            {
                stream._clockTick = tick; stream._clockSeconds = seconds; stream._clockStep = step;
                world.SynchronizeTime(seconds);
            }
        }
        catch (InvalidDataException)
        {
            stream.InvalidPayloads++;
            if (fast) receiver.Fast = null;
            else { receiver.State = null; receiver.NeedsBaseline = true; }
            reader.SetError();
        }
    }

    private static void ReceiveRequest(Actor actor, FBitReader reader, NetConnection connection)
    {
        uint guid = reader.ReadIntPacked();
        if (reader.IsError || !connection.Driver.IsServer || actor is not PlayerController || !ReferenceEquals(connection.Actor, actor)) return;
        foreach (ActorChannel channel in connection.ActorChannels)
        {
            if (channel.NetGuid != guid || channel is not { OpenAcked: true, Closing: false, Actor: SSmokeEmitter }) continue;
            ResponsiveSmokeReplication stream = For(connection.Driver);
            if (stream._viewers.TryGetValue(channel, out Viewer? viewer))
            {
                viewer.Baseline = null;
                // Requests repeat while the client waits. Keep a full snapshot already in flight so a slow
                // connection can finish it instead of discarding partial progress on every retry.
                if (viewer.State?.Kind != 0) viewer.State = null;
            }
            break;
        }
    }
}
