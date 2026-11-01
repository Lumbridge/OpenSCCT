using System;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Moves;

/// <summary>Bounded input recovery independent of reliable RPC delivery. Commands are applied once in sequence.</summary>
public sealed class SequencedMoveTransport : IMoveTransport
{
    public const int HistoryTicks = 256;
    public const int MaxCommandsPerTick = 4;
    public const int Capacity = HistoryTicks * MaxCommandsPerTick;
    private const int Batch = 6;
    private const string Commands = "SvmMoveCommands", Ack = "SvmMoveAck", Resync = "SvmMoveResync", Reset = "SvmMoveReset";
    private readonly NetDriver _driver;
    private readonly Actor _actor;
    private readonly IMovePawn _pawn;
    private readonly IMoveController _controller;
    private readonly IMoveTransport _legacy;
    private readonly Command[] _ring = new Command[Capacity];
    private uint _epoch, _sent, _sealed, _acknowledged, _applied;
    private long _appliedInputTick;
    private float _appliedInputDelta;
    private bool _hasEpoch, _resetRequested;
    private long _ackTick = -1;
    private float _appliedStamp;
    private FVector _reportedLocation;
    private byte _reportedDivers;
    private Actor? _reportedBase;
    private uint _highestReceived;
    private long _lastPumpTick = -1;
    private float _simulationCredit = 12f;
    private long _lastTransmitTick = -1;
    private int _lastTransmitView;
    private ushort _lastTransmitReliable;
    private uint _lastAckSent;
    private long _receiptRevision, _lastReceiptSent;
    private uint _pawnGuid;
    private bool _resetPending;
    private uint _resendCursor;
    private Command _lastRejected;
    private uint _lastSentFirst, _lastIgnoredEpoch, _lastIgnoredPawn;
    private long _ignoredBatches, _readErrors;
    private long _commandTick = -1;
    private int _commandsThisTick;
    private bool _hasInputClock;
    private long _inputTickOffset;
    private const int InputBufferTicks = 2;
    public string? FirstResynchronization { get; private set; }
    public long CommandsRejected { get; private set; }

    private struct Command
    {
        public uint Sequence;
        public long InputTick;
        public byte Kind;
        public ushort RequiredReliable, CompletedReliable;
        public float Delta;
        public BigServerMoveData Move;
        public FVector ReportedLocation;
        public byte ReportedDivers;
        public Actor? ReportedBase;
        public bool Received;
    }

    public SequencedMoveTransport(NetDriver driver, Actor actor, IMovePawn pawn, IMoveController controller, IMoveTransport legacy)
    {
        _driver = driver; _actor = actor; _pawn = pawn; _controller = controller; _legacy = legacy;
        Epoch(0);
    }

    public uint LastAcknowledged => _acknowledged;
    public uint CommandEpoch => _epoch;
    public uint NextSequence => _sent + 1;
    public uint LastApplied => _applied;
    public string DescribeInputClock() => $"world {_driver.World?.TickCount}, pump {_lastPumpTick}, offset {_inputTickOffset}, appliedInput {_appliedInputTick}, nextInput {_ring[Slot(_applied + 1)].InputTick}, reliable {_ring[Slot(_applied + 1)].CompletedReliable}/{Channel?.Connection.InReliable[Channel.ChIndex]}";
    public string DescribePending() => $"applied {_applied}, received {_highestReceived}, next {_ring[Slot(_applied + 1)].Sequence}/reliable {_ring[Slot(_applied + 1)].RequiredReliable}, epoch {_epoch}, credit {_simulationCredit:F1}, sent {_sent}, ack {_acknowledged}, resend {_resendCursor}, oldest {_ring[Slot(_acknowledged + 1)].Sequence} kind {_ring[Slot(_acknowledged + 1)].Kind} dt {_ring[Slot(_acknowledged + 1)].Delta} msa {_ring[Slot(_acknowledged + 1)].Move.MSATime} vel {_ring[Slot(_acknowledged + 1)].Move.Vel}, resets {ResynchronizationRequests}, rejected {CommandsRejected}: {_lastRejected.Sequence}/{_lastRejected.Kind}/{_lastRejected.Delta}/{_lastRejected.Move.MSATime}/{_lastRejected.Move.Vel}, sentFirst {_lastSentFirst} at {_lastTransmitTick}, ignored {_ignoredBatches}/{_lastIgnoredEpoch}/{_lastIgnoredPawn} pawn {_pawnGuid} reads {_readErrors}";
    public long RecoveredCommands { get; private set; }
    public long DuplicateCommands { get; private set; }
    public long HistoryResets { get; private set; }
    public long ResynchronizationRequests { get; private set; }
    public int OutstandingReliable => 0;
    public float DeltaTime { get; set; } = Sim.DeltaTime;
    public bool Heartbeat { get; set; }

    public static RepExtension CreateExtension()
    {
        var ext = new RepExtension();
        ext.Functions.Add(new(Commands, false, true, "move-commands-13", ReceiveCommands) { AcceptReordered = true });
        ext.Functions.Add(new(Ack, false, false, "move-ack-5", ReceiveAck));
        ext.Functions.Add(new(Resync, true, true, "move-resync-1", ReceiveResync));
        ext.Functions.Add(new(Reset, true, false, "move-reset-1", ReceiveReset));
        return ext;
    }

    private void Epoch(uint epoch)
    {
        if (_hasEpoch && _epoch == epoch) return;
        _hasEpoch = true; _epoch = epoch; _sent = _sealed = _acknowledged = _applied = 0;
        _resetRequested = false; Array.Clear(_ring); HistoryResets++;
        _highestReceived = 0;
        _appliedInputTick = 0; _appliedInputDelta = 0f;
        _lastPumpTick = -1; _simulationCredit = 12f;
        _lastTransmitTick = -1; _lastAckSent = 0;
        _resendCursor = 1;
        _ackTick = -1;
        _commandTick = -1; _commandsThisTick = 0;
        _hasInputClock = false; _inputTickOffset = 0;
        PredictionReset?.Invoke();
    }

    public Action? PredictionReset { get; set; }

    /// <summary>A possession or explicit placement starts a movement lifetime; routine MirrorState calls do not.</summary>
    public void ResetLifetime()
    {
        if (!_driver.IsServer) return;
        Epoch(_epoch + 1);
        _pawnGuid = _actor is Controller { Pawn: { } pawn } ? (uint)pawn.ActorId : 0;
        _resetPending = true;
    }

    private void SendPendingReset()
    {
        if (!_resetPending || Channel is not { } channel || !channel.Connection.IsNetReady || channel.NumOutRec >= 96 ||
            _actor is not Controller { Pawn: { } pawn } ||
            channel.Connection.FindActorChannel(pawn) is not { OpenAcked: true }) return;
        FOutBunch b = channel.BeginFunction(channel.Layout!.FindFunction(Reset)!, true);
        b.Writer.WriteUInt32(_epoch); b.Writer.WriteUInt32(_pawnGuid);
        if (channel.SendFunction(b) >= 0) _resetPending = false;
    }

    private static void ReceiveReset(Actor actor, FBitReader r, NetConnection connection)
    {
        uint epoch = r.ReadUInt32(), pawnGuid = r.ReadUInt32();
        if (r.IsError || Endpoint(actor, connection)?.Sequenced is not { } t || epoch <= t._epoch) return;
        t.Epoch(epoch); t._pawnGuid = pawnGuid;
    }

    private bool HasCurrentPawn => _actor is Controller { Pawn: { } pawn } && _pawnGuid != 0 &&
        (_driver.IsServer ? (uint)pawn.ActorId == _pawnGuid : ReferenceEquals(_driver.FindActorByNetGuid(_pawnGuid), pawn));

    private ActorChannel? Channel => (_driver.IsServer ? _driver.OwningConnection(_actor) : _driver.ServerConnection)?.FindActorChannel(_actor);

    private void Enqueue(byte kind, float stamp, FVector accel, FVector location, int view, byte divers, in BigServerMoveData big = default)
    {
        if (_driver.World!.IsReplaying || !HasCurrentPawn) return;
        long tick = _driver.World.TickCount;
        if (_commandTick != tick) { _commandTick = tick; _commandsThisTick = 0; }
        if (_resetRequested || _commandsThisTick >= MaxCommandsPerTick || _sent - _acknowledged >= Capacity - 1 ||
            (_sent > _acknowledged && tick - _ring[Slot(_acknowledged + 1)].InputTick >= HistoryTicks))
        {
            RequestResynchronization();
            return;
        }
        uint sequence = ++_sent;
        _commandsThisTick++;
        BigServerMoveData move = big;
        move.TimeStamp = stamp; move.InAccel = accel; move.ClientLoc = location; move.View = view; move.iDiversInfos = divers;
        ActorChannel? channel = Channel;
        _ring[Slot(sequence)] = new Command
        {
            Sequence = sequence, Kind = Heartbeat && kind != 3 ? (byte)4 : kind,
            InputTick = tick,
            RequiredReliable = channel is null ? (ushort)0 : (ushort)channel.Connection.OutReliable[channel.ChIndex],
            Delta = Math.Clamp(DeltaTime, 0f, Sim.DeltaTime), Move = move,
            ReportedLocation = location, ReportedDivers = divers,
            ReportedBase = _pawn.Base,
        };
    }

    public void SealPredictionTick()
    {
        // Keep controls emitted by animation completion after the movement step. Moving them before physics
        // can restore root motion against the next idle clip at a fence or ledge handoff.
        ushort reliable = Channel is { } channel ? (ushort)channel.Connection.OutReliable[channel.ChIndex] : (ushort)0;
        for (uint seq = _sealed + 1; seq <= _sent; seq++)
            _ring[Slot(seq)].CompletedReliable = seq == _sent ? reliable : _ring[Slot(seq)].RequiredReliable;
        if (_sent > _sealed)
        {
            // Animation/state entries can change the cylinder, physics and placement after PlayerMove queued its
            // command. Acknowledge against the same completed state stored by prediction, while retaining the
            // original big-move fields for its action/root handoff semantics.
            ref Command last = ref _ring[Slot(_sent)];
            last.ReportedLocation = _pawn.Location;
            last.ReportedDivers = (byte)(((int)_pawn.Physics << 4) | (_pawn.ZoneConfig & 3));
            last.ReportedBase = _pawn.Base;
        }
        _sealed = _sent;
        PumpClient();
    }

    public void PumpClient()
    {
        if (_driver.IsServer || _sent <= _acknowledged || Channel is not { } channel || !channel.Connection.IsNetReady) return;
        Command latest = _ring[Slot(_sent)];
        long tick = _driver.World!.TickCount;
        long normalAckTicks = Math.Max(12, (channel.Connection.Stats.RttMicros * Sim.Hz + 999_999) / 1_000_000 + InputBufferTicks + 3);
        bool recovering = tick - _ring[Slot(_acknowledged + 1)].InputTick > normalAckTicks;
        bool active = latest.Kind == 3 || !_pawn.Velocity.IsZero() || !latest.Move.InAccel.IsZero() ||
            MovePacking.DiversJump((byte)latest.Move.iDiversInfos) || latest.Move.View != _lastTransmitView ||
            latest.CompletedReliable != _lastTransmitReliable || recovering;
        // Idle simulation still records every tick. Bundle three unchanged ticks instead of exchanging empty
        // 60 Hz packets, while movement, aim and action changes are sent immediately.
        if (!active && _lastTransmitTick >= 0 && tick - _lastTransmitTick < 3) return;
        // Always repeat the oldest gap, but rotate the other recovery slots through the pending window.
        // Repeating only its first three entries makes recovery advance by three commands per RTT.
        Span<uint> selected = stackalloc uint[Batch];
        uint first = _acknowledged + 1;
        while (first < _sent && _ring[Slot(first)].Received) first++;
        int count = 1;
        selected[0] = first;
        uint end = _sent > Batch / 2 ? _sent - Batch / 2 : _sent;
        if (end < first) end = _sent;
        // Recover the region blocking simulation before cycling through an entire post-outage window. The
        // newest commands have their own slots below; distant receipt gaps cannot delay an old stop or action.
        end = Math.Min(end, first + Batch * 2u - 1);
        uint cursor = Math.Clamp(_resendCursor, first, end);
        for (uint scanned = 0; scanned <= end - first && count < Batch / 2; scanned++)
        {
            uint seq = cursor;
            cursor = cursor == end ? first : cursor + 1;
            if (seq != first && !_ring[Slot(seq)].Received) selected[count++] = seq;
        }
        uint recent = Math.Max(_acknowledged + 1, _sent > Batch / 2 ? _sent - Batch / 2 + 1 : 1);
        for (uint seq = recent; seq <= _sent; seq++)
        {
            bool seen = false;
            for (int i = 0; i < count; i++) seen |= selected[i] == seq;
            if (!seen && !_ring[Slot(seq)].Received) selected[count++] = seq;
        }
        // Acknowledged receipt is independent of simulation progress: a reliable action may temporarily
        // hold execution while all of its inputs are already buffered. Use any remaining space for gaps.
        for (uint seq = first + 1; seq <= _sent && count < Batch; seq++)
        {
            bool seen = false;
            for (int i = 0; i < count; i++) seen |= selected[i] == seq;
            if (!seen && !_ring[Slot(seq)].Received) selected[count++] = seq;
        }
        FOutBunch b = channel.BeginFunction(channel.Layout!.FindFunction(Commands)!, false);
        b.Writer.WriteIntPacked(_epoch); b.Writer.WriteIntPacked(_pawnGuid); b.Writer.WriteByte((byte)count);
        Command previous = default;
        for (int i = 0; i < count; i++)
        {
            Command command = _ring[Slot(selected[i])];
            Write(b.Writer, command, previous, channel.Connection);
            previous = command;
        }
        if (channel.SendFunction(b) >= 0)
        {
            _lastTransmitTick = tick; _lastTransmitView = latest.Move.View; _lastTransmitReliable = latest.CompletedReliable;
            _resendCursor = cursor;
            _lastSentFirst = selected[0];
        }
    }

    public void RequestResynchronization()
    {
        if (_driver.IsServer || _resetRequested || Channel is not { } channel) return;
        FOutBunch reset = channel.BeginFunction(channel.Layout!.FindFunction(Resync)!, true);
        reset.Writer.WriteUInt32(_epoch);
        if (channel.SendFunction(reset) >= 0)
        {
            FirstResynchronization ??= DescribePending();
            _resetRequested = true; ResynchronizationRequests++;
        }
    }

    private static MoveEndpoint? Endpoint(Actor actor, NetConnection connection) =>
        connection.Driver.World?.Level.GetService<IMoveEndpointSource>()?.EndpointOf(actor);

    private static void ReceiveCommands(Actor actor, FBitReader r, NetConnection connection)
    {
        uint epoch = r.ReadIntPacked(), pawnGuid = r.ReadIntPacked(); int count = r.ReadByte();
        MoveEndpoint? endpoint = Endpoint(actor, connection);
        SequencedMoveTransport? transport = endpoint?.Sequenced;
        if (count > Batch) { connection.Close("Malformed movement batch"); return; }
        bool accept = transport is not null && epoch == transport._epoch && pawnGuid == transport._pawnGuid && transport.HasCurrentPawn;
        if (!accept && transport is not null)
        {
            transport._ignoredBatches++; transport._lastIgnoredEpoch = epoch; transport._lastIgnoredPawn = pawnGuid;
        }
        Command previous = default;
        for (int i = 0; i < count; i++)
        {
            Command command = Read(r, previous, connection);
            previous = command;
            if (r.IsError) { if (transport is not null) transport._readErrors++; continue; }
            if (!accept) continue;
            if (command.Sequence <= transport!._applied) { transport.DuplicateCommands++; continue; }
            if (command.Sequence - transport._applied >= Capacity || !float.IsFinite(command.Move.TimeStamp) ||
                command.Kind > 4 || !float.IsFinite(command.Delta) || command.Delta < 0f || command.Delta > Sim.DeltaTime + 0.00001f ||
                !float.IsFinite(command.Move.MSATime) || !float.IsFinite(command.Move.Vel.X) || !float.IsFinite(command.Move.Vel.Y) || !float.IsFinite(command.Move.Vel.Z))
            { transport.CommandsRejected++; transport._lastRejected = command; continue; }
            ref Command slot = ref transport._ring[Slot(command.Sequence)];
            if (slot.Sequence == command.Sequence) { transport.DuplicateCommands++; continue; }
            slot = command;
            transport._receiptRevision++;
            transport._highestReceived = Math.Max(transport._highestReceived, command.Sequence);
        }
    }

    public Action<int>? ApplyControls { get; set; }

    public void PumpServer(ServerMoves server)
    {
        SendPendingReset();
        if (!_hasEpoch || !HasCurrentPawn || Channel is not { } channel) return;
        long tick = _driver.World!.TickCount;
        // Retain enough elapsed simulation time to recover every input still in prediction history.
        // A shorter credit cap permanently discards time during an outage: new input consumes all
        // subsequently earned credit, so the old commands can never catch up. Work is still bounded
        // to eight commands per pump, the anchored input clock, and one simulation step per input tick.
        if (_lastPumpTick >= 0) _simulationCredit = Math.Min(HistoryTicks, _simulationCredit + Math.Max(0, tick - _lastPumpTick));
        _lastPumpTick = tick;
        SequencedMoveTransport transport = this;
        // Bound catch-up work after an outage. A later packet continues the contiguous run.
        for (int i = 0; i < 8; i++)
        {
            ref Command next = ref transport!._ring[Slot(transport._applied + 1)];
            if (next.Sequence != transport._applied + 1) break;
            Command c = next;
            float tickDelta = c.Delta + (_applied != 0 && c.InputTick == _appliedInputTick ? _appliedInputDelta : 0f);
            if (tickDelta > Sim.DeltaTime + 0.00001f)
            { channel.Connection.Close("Movement input exceeds one simulation step per tick"); break; }
            if (_applied != 0 && (c.InputTick < _appliedInputTick || c.InputTick - _appliedInputTick > HistoryTicks ||
                Math.Abs((c.Move.TimeStamp - (double)_appliedStamp) - (c.InputTick - _appliedInputTick) * (double)Sim.DeltaTime) > 2 * Sim.DeltaTime + 0.005))
            { channel.Connection.Close("Invalid movement input timeline"); break; }
            uint epoch = _epoch;
            float cost = c.Delta / Sim.DeltaTime;
            if (cost > _simulationCredit + 0.0001f) break;
            if (NetConnection.ReliableSeqDiff(c.CompletedReliable, channel.Connection.InReliable[channel.ChIndex]) > 0) break;
            // Anchor input ticks once per lifetime. A small playout cushion absorbs ordinary arrival jitter,
            // rather than making constant input alternate between zero and two simulation steps per host tick.
            // Commands older than this clock still catch up after an outage, retaining their individual steps.
            if (!_hasInputClock)
            {
                // A recovered batch can start with old input. Anchor its newest command to arrival time;
                // anchoring the oldest makes the startup loss/delay a permanent input-lag penalty.
                long newestInputTick = _ring[Slot(_highestReceived)].InputTick;
                int jitterTicks = (int)Math.Clamp((channel.Connection.Stats.JitterMicros * Sim.Hz + 999_999) / 1_000_000, 0, 4);
                _inputTickOffset = tick - Math.Max(c.InputTick, newestInputTick) + InputBufferTicks + jitterTicks;
                _hasInputClock = true;
            }
            if (c.InputTick > tick - _inputTickOffset) break;
            IMoveCommandPawn? commandPawn = transport._pawn as IMoveCommandPawn;
            commandPawn?.BeginMoveCommand();
            try
            {
                // Traversal state entries and their latent deadlines belong to the same input clock as physics.
                // Applying their pre/post controls on receive time makes those deadlines depend on RTT and bursts.
                using var commandClock = _driver.World!.Level.CommandTime(c.InputTick, c.Move.TimeStamp, Sim.DeltaTime);
                ApplyControls?.Invoke(c.RequiredReliable);
                // A deferred state entry can respawn or explicitly reset the pawn. Never apply or acknowledge
                // an old lifetime's command after a callback clears the ring and establishes a new epoch.
                if (_epoch != epoch || !HasCurrentPawn) break;
                if (transport._applied == 0) server.CurrentTimeStamp = c.Move.TimeStamp - c.Delta;
                BigServerMoveData m = c.Move;
                long before = server.MovesApplied;
                commandPawn?.SetCommandTime(m.TimeStamp, c.InputTick);
                server.CommandDeltaTime = c.Delta;
                try
                {
                    if (transport._pawn is IMoveCommandPawn { CommandSimulationEnabled: false })
                    {
                        // Knockdowns and held targets belong to the authoritative world clock. A stopped or stalled
                        // owner must not slow their animation, wake-up timer or paired movement.
                        // Their commands still carry ordered native actions (notably stand-up). Do not discard those
                        // with the disabled movement step, or let the following walking state overtake them.
                        if (c.Kind == 3 && Math.Abs(m.MSATime - transport._controller.MirrorStateAcknowledgementTime) < 0.01f)
                            transport._pawn.ExecuteReceivedActions(m.Val0, m.Val1);
                        server.CurrentTimeStamp = m.TimeStamp;
                    }
                    else if (c.Kind == 4)
                    {
                        server.CurrentTimeStamp = m.TimeStamp; server.ServerTimeStamp = transport._controller.TimeSeconds;
                        transport._pawn.MoveAutonomous(c.Delta, default, false);
                    }
                    else if (c.Kind == 3) server.UnreliableReplicateBigServerMove(m);
                    else server.ProcessServerMove(c.Kind == 2 ? EServerMoveKind.PCRot : EServerMoveKind.Full,
                        m.TimeStamp, m.InAccel, m.ClientLoc, m.View, (byte)m.iDiversInfos, -1, 0, false);
                }
                finally { server.CommandDeltaTime = null; }
                if (_epoch != epoch || !HasCurrentPawn) break;
                ApplyControls?.Invoke(c.CompletedReliable);
                if (_epoch != epoch || !HasCurrentPawn) break;
                if (server.MovesApplied > before && c.Sequence < transport._highestReceived) transport.RecoveredCommands++;
                transport._applied = c.Sequence; transport._appliedStamp = c.Move.TimeStamp; transport._appliedInputTick = c.InputTick; next = default;
                _reportedLocation = c.ReportedLocation; _reportedDivers = c.ReportedDivers;
                _reportedBase = c.ReportedBase;
                _appliedInputDelta = tickDelta;
                _simulationCredit -= cost;
            }
            finally { commandPawn?.EndMoveCommand(); }
        }
    }

    public void SendAcknowledgement(ServerMoves server)
    {
        SendPendingReset();
        long tick = _driver.World!.TickCount;
        if (_highestReceived == 0 || tick - _ackTick < 2 || Channel is not { } channel || !channel.Connection.IsNetReady) return;
        if (_applied == _lastAckSent && _receiptRevision == _lastReceiptSent && tick - _ackTick < 6) return;
        FOutBunch b = channel.BeginFunction(channel.Layout!.FindFunction(Ack)!, false);
        b.Writer.WriteIntPacked(_epoch); b.Writer.WriteIntPacked(_applied); b.Writer.WriteIntPacked((uint)tick);
        // Receipt recovery covers the next 255 entries; the larger history also reserves slots for tick splits.
        int received = (int)Math.Min(255u, _highestReceived - _applied);
        b.Writer.WriteByte((byte)received);
        for (uint offset = 1; offset <= received; offset++)
            b.Writer.WriteBit(_ring[Slot(_applied + offset)].Sequence == _applied + offset);
        b.Writer.WriteFloat(_appliedStamp);
        bool correction = _applied != 0 && ((_pawn.Location - _reportedLocation).SizeSquared() >= MoveK.CorrectionErrorSquared ||
            _pawn.Physics != MovePacking.DiversPhysics(_reportedDivers) || _pawn.ZoneConfig != MovePacking.DiversZone(_reportedDivers) ||
            !ReferenceEquals(_pawn.Base, _reportedBase));
        b.Writer.WriteBit(correction);
        if (correction)
        {
            Vec(b.Writer, _pawn.Location); Vec(b.Writer, _pawn.Velocity);
            b.Writer.WriteByte((byte)_pawn.Physics); b.Writer.WriteByte(_pawn.ZoneConfig);
            channel.Connection.WriteObject(b.Writer, _pawn.Base, out _);
            bool based = _pawn.Base is { bWorldGeometry: false, bStatic: false };
            b.Writer.WriteBit(based);
            if (based)
            {
                Actor basis = _pawn.Base!;
                Vec(b.Writer, basis.Location);
                b.Writer.WriteInt32(basis.Rotation.Pitch); b.Writer.WriteInt32(basis.Rotation.Yaw); b.Writer.WriteInt32(basis.Rotation.Roll);
            }
        }
        if (channel.SendFunction(b) >= 0) { _ackTick = tick; _lastAckSent = _applied; _lastReceiptSent = _receiptRevision; }
    }

    private static void ReceiveAck(Actor actor, FBitReader r, NetConnection connection)
    {
        uint epoch = r.ReadIntPacked(), sequence = r.ReadIntPacked(); _ = r.ReadIntPacked();
        int received = r.ReadByte();
        Span<bool> receipt = stackalloc bool[Capacity];
        for (int i = 0; i < received; i++) receipt[i] = r.ReadBit();
        float stamp = r.ReadFloat(); bool correction = r.ReadBit();
        FVector location = default, velocity = default;
        FVector? basePosition = null; FRotator baseRotation = default;
        EPhysics physics = default; byte zone = 0; Actor? @base = null;
        if (correction)
        {
            location = Vec(r); velocity = Vec(r); physics = (EPhysics)r.ReadByte(); zone = r.ReadByte();
            @base = connection.ReadObject(r) as Actor;
            if (r.ReadBit())
            {
                basePosition = Vec(r);
                baseRotation = new FRotator(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            }
        }
        MoveEndpoint? endpoint = Endpoint(actor, connection);
        if (r.IsError || endpoint?.Sequenced is not { } transport || !transport._hasEpoch || epoch != transport._epoch || sequence < transport._acknowledged || sequence > transport._sent) return;
        transport._acknowledged = sequence;
        for (int i = 0; i < received; i++)
        {
            uint seq = sequence + 1u + (uint)i;
            if (seq <= transport._sent && receipt[i] && transport._ring[Slot(seq)].Sequence == seq)
                transport._ring[Slot(seq)].Received = true;
        }
        if (sequence == 0) return;
        if (epoch != transport.CommandEpoch) return;
        if (correction) endpoint.Client?.ReceiveAuthoritativeState(epoch, sequence, stamp, location, velocity, physics, zone, @base, basePosition, baseRotation);
        else endpoint.Client?.AcknowledgePrediction(epoch, sequence, stamp);
    }

    private static void ReceiveResync(Actor actor, FBitReader r, NetConnection connection)
    {
        uint epoch = r.ReadUInt32();
        if (!r.IsError && Endpoint(actor, connection)?.Sequenced is { } t && epoch == t._epoch)
            t._controller.ReinitPawnBecauseOfDivergences();
    }

    public void MyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel) => Enqueue(0, TimeStamp, InAccel, ClientLoc, View, DiversInfos);
    public void MyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel) => Enqueue(1, TimeStamp, default, ClientLoc, View, DiversInfos);
    public void MyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel) => Enqueue(2, TimeStamp, default, ClientLoc, View, DiversInfos);
    public void BigServerMove(float MSATime, float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos, int Val0, int Val1)
    {
        var big = new BigServerMoveData { MSATime = MSATime, Vel = Vel, Val0 = Val0, Val1 = Val1 };
        switch (MovePacking.DiversPhysics(DiversInfos))
        {
            case EPhysics.PHYS_Repositionnal:
                big.ZipP1 = _pawn.NewZipP1; big.DifZipP2 = _pawn.ZipP2 - _pawn.NewZipP1; big.vect3 = _pawn.vVandammePos; big.vectUnit = _pawn.m_geoNormal * MoveK.UnitWireScale; break;
            case EPhysics.PHYS_Vandamme:
                big.vect3 = _pawn.vVandammePos; big.vectUnit = _pawn.vWallJumpDir * MoveK.UnitWireScale; break;
            default:
                big.ZipP1 = _pawn.NewZipP1; big.DifZipP2 = _pawn.NewZipP2 - _pawn.NewZipP1; break;
        }
        Enqueue(3, TimeStamp, InAccel, ClientLoc, View, DiversInfos, big);
    }
    public void UnreliableReplicateBigServerMove(in BigServerMoveData move) { }
    public void MyShortClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, Actor? NewBase) { }
    public void MyClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY, float NewVelZ, Actor? NewBase) { }
    public void ClientReinitPawnBecauseOfDivergences(byte RT, float MSATime, FVector vPos, FRotator rRot, Actor? aBase)
    {
        ResetLifetime();
        _legacy.ClientReinitPawnBecauseOfDivergences(RT, MSATime, vPos, rRot, aBase);
    }
    public void TellServerIAMReadyToReinit() => _legacy.TellServerIAMReadyToReinit();

    private static int Slot(uint seq) => (int)(seq & (Capacity - 1));
    private static void Vec(FBitWriter w, FVector v) { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }
    private static FVector Vec(FBitReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    // Repeated fields are relative only to the previous command in this same datagram. Every batch is
    // independently decodable, including resends and reordered/lost packets. Keep timestamps and command
    // identities explicit; this removes redundant payload without changing simulation or quantization.
    private static void Write(FBitWriter w, in Command c, in Command previous, NetConnection map)
    {
        BigServerMoveData m = c.Move;
        w.WriteIntPacked(c.Sequence); w.WriteBits(c.Kind, 3);
        w.WriteBit(c.RequiredReliable != previous.RequiredReliable);
        if (c.RequiredReliable != previous.RequiredReliable) w.WriteIntPacked(c.RequiredReliable);
        w.WriteBit(c.CompletedReliable != c.RequiredReliable);
        if (c.CompletedReliable != c.RequiredReliable) w.WriteIntPacked(c.CompletedReliable);
        w.WriteBit(c.Delta != Sim.DeltaTime); if (c.Delta != Sim.DeltaTime) w.WriteFloat(c.Delta);
        w.WriteFloat(m.TimeStamp);
        w.WriteIntPacked((uint)c.InputTick);
        WriteDeltaVector(w, m.InAccel, previous.Move.InAccel);
        WriteDeltaVector(w, m.ClientLoc, previous.Move.ClientLoc);
        w.WriteBit(m.View != previous.Move.View);
        if (m.View != previous.Move.View)
        {
            uint yaw = (uint)m.View & 32767, pitch = (uint)m.View >> 15;
            w.WriteBit(yaw != 0); if (yaw != 0) w.WriteBits(yaw, 15);
            w.WriteBit(pitch != 0); if (pitch != 0) w.WriteBits(pitch, 15);
        }
        w.WriteBit(m.iDiversInfos != previous.Move.iDiversInfos);
        if (m.iDiversInfos != previous.Move.iDiversInfos) w.WriteByte((byte)m.iDiversInfos);
        if (c.Kind == 3)
        {
            WriteDeltaFloat(w, m.MSATime, previous.Move.MSATime);
            WriteDeltaFloat(w, m.Vel.X, previous.Move.Vel.X);
            WriteDeltaFloat(w, m.Vel.Y, previous.Move.Vel.Y);
            WriteDeltaFloat(w, m.Vel.Z, previous.Move.Vel.Z);
            w.WriteBit(m.Val0 != previous.Move.Val0); if (m.Val0 != previous.Move.Val0) w.WriteInt32(m.Val0);
            w.WriteBit(m.Val1 != previous.Move.Val1); if (m.Val1 != previous.Move.Val1) w.WriteInt32(m.Val1);
            WriteDeltaVector(w, m.ZipP1, previous.Move.ZipP1);
            WriteDeltaVector(w, m.DifZipP2, previous.Move.DifZipP2);
            WriteDeltaVector(w, m.vect3, previous.Move.vect3);
            WriteDeltaVector(w, m.vectUnit, previous.Move.vectUnit);
        }
        bool completed = !MoveBatchVector.Equal(c.ReportedLocation, m.ClientLoc) ||
            (c.ReportedDivers & 0xf3) != ((byte)m.iDiversInfos & 0xf3);
        w.WriteBit(completed);
        if (completed) { WriteDeltaVector(w, c.ReportedLocation, m.ClientLoc); w.WriteByte(c.ReportedDivers); }
        w.WriteBit(!ReferenceEquals(c.ReportedBase, previous.ReportedBase));
        if (!ReferenceEquals(c.ReportedBase, previous.ReportedBase)) map.WriteObject(w, c.ReportedBase, out _);
    }
    private static void WriteDeltaVector(FBitWriter w, FVector value, FVector previous) => MoveBatchVector.Write(w, value, previous);
    private static FVector ReadDeltaVector(FBitReader r, FVector previous) => MoveBatchVector.Read(r, previous);

    private static void WriteDeltaFloat(FBitWriter w, float value, float previous)
    {
        w.WriteBit(value != previous);
        if (value != previous) w.WriteFloat(value);
    }
    private static float ReadDeltaFloat(FBitReader r, float previous) => r.ReadBit() ? r.ReadFloat() : previous;

    private static Command Read(FBitReader r, in Command previous, NetConnection map)
    {
        var c = new Command { Sequence = r.ReadIntPacked(), Kind = (byte)r.ReadBits(3) };
        uint required = r.ReadBit() ? r.ReadIntPacked() : previous.RequiredReliable;
        uint completed = r.ReadBit() ? r.ReadIntPacked() : required;
        if (required > ushort.MaxValue || completed > ushort.MaxValue) { map.Close("Invalid movement control fence"); return c; }
        c.RequiredReliable = (ushort)required; c.CompletedReliable = (ushort)completed;
        c.Delta = r.ReadBit() ? r.ReadFloat() : Sim.DeltaTime;
        c.Move.TimeStamp = r.ReadFloat(); c.InputTick = r.ReadIntPacked();
        c.Move.InAccel = ReadDeltaVector(r, previous.Move.InAccel);
        c.Move.ClientLoc = ReadDeltaVector(r, previous.Move.ClientLoc);
        if (r.ReadBit())
        {
            uint yaw = r.ReadBit() ? (uint)r.ReadBits(15) : 0, pitch = r.ReadBit() ? (uint)r.ReadBits(15) : 0;
            c.Move.View = (int)(yaw | (pitch << 15));
        }
        else c.Move.View = previous.Move.View;
        c.Move.iDiversInfos = r.ReadBit() ? r.ReadByte() : previous.Move.iDiversInfos;
        if (c.Kind == 3)
        {
            c.Move.MSATime = ReadDeltaFloat(r, previous.Move.MSATime);
            c.Move.Vel = new FVector(ReadDeltaFloat(r, previous.Move.Vel.X), ReadDeltaFloat(r, previous.Move.Vel.Y), ReadDeltaFloat(r, previous.Move.Vel.Z));
            c.Move.Val0 = r.ReadBit() ? r.ReadInt32() : previous.Move.Val0;
            c.Move.Val1 = r.ReadBit() ? r.ReadInt32() : previous.Move.Val1;
            c.Move.ZipP1 = ReadDeltaVector(r, previous.Move.ZipP1);
            c.Move.DifZipP2 = ReadDeltaVector(r, previous.Move.DifZipP2);
            c.Move.vect3 = ReadDeltaVector(r, previous.Move.vect3);
            c.Move.vectUnit = ReadDeltaVector(r, previous.Move.vectUnit);
        }
        c.ReportedLocation = c.Move.ClientLoc; c.ReportedDivers = (byte)c.Move.iDiversInfos;
        if (r.ReadBit()) { c.ReportedLocation = ReadDeltaVector(r, c.Move.ClientLoc); c.ReportedDivers = r.ReadByte(); }
        c.ReportedBase = r.ReadBit() ? map.ReadObject(r) as Actor : previous.ReportedBase;
        return c;
    }
}
