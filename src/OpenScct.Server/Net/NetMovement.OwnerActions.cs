using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class NetMovementBridge : INetworkPawnActions
{
    private const string OwnerActionFunction = "SvmOwnerAction";
    private const int OwnerActionCapacity = 256;
    private readonly OwnerAction[] _ownerActions = new OwnerAction[OwnerActionCapacity];
    private readonly Dictionary<SPlayerController, uint> _ownerActionSequences = new();
    private int _ownerActionStart, _ownerActionCount;
    public SBasePawn? InputOwner { get; set; }
    public long OwnerActionsSent { get; private set; }
    public long OwnerActionsApplied { get; private set; }

    private struct OwnerAction
    {
        public SPlayerController Controller;
        public SBasePawn? Pawn;
        public uint PawnGuid, Epoch, Sequence, Tick;
        public int Action;
    }

    public static RepExtension OwnerActionsExtension()
    {
        var extension = new RepExtension();
        extension.Functions.Add(new(OwnerActionFunction, true, false, "owner-action-1", ReceiveOwnerAction));
        return extension;
    }

    public void Authored(SBasePawn pawn, int action)
    {
        if (!_driver.IsServer || _driver.Settings.PawnProxies != PawnProxyMode.Snapshots ||
            ReferenceEquals(InputOwner, pawn) || pawn.Controller is not SPlayerController pc ||
            _driver.OwningConnection(pc) is not { } connection || EndpointOf(pc)?.Sequenced is not { } moves) return;
        if (_ownerActionCount == OwnerActionCapacity) { connection.Close("Owner action queue exhausted"); return; }
        _ownerActionSequences.TryGetValue(pc, out uint sequence);
        _ownerActionSequences[pc] = ++sequence;
        long hostTick = pawn.XLevel.GetService<PawnStateReplication>()?.SourceTick ?? -1;
        if (hostTick < 0) hostTick = pawn.XLevel.TickCount + (pawn.XLevel.IsTicking ? 0 : 1);
        _ownerActions[(_ownerActionStart + _ownerActionCount++) & (OwnerActionCapacity - 1)] = new OwnerAction
        {
            Controller = pc, Pawn = pawn, PawnGuid = (uint)pawn.ActorId, Epoch = moves.CommandEpoch,
            Sequence = sequence, Tick = (uint)hostTick, Action = action,
        };
    }

    private void FlushOwnerActions()
    {
        int pending = _ownerActionCount;
        for (int i = 0; i < pending; i++)
        {
            OwnerAction action = _ownerActions[_ownerActionStart];
            _ownerActions[_ownerActionStart] = default;
            _ownerActionStart = (_ownerActionStart + 1) & (OwnerActionCapacity - 1); _ownerActionCount--;
            if (action.Controller.bDeleteMe || action.Pawn is not { bDeleteMe: false } pawn ||
                !ReferenceEquals(action.Controller.Pawn, pawn) || PeekEndpoint(action.Controller)?.Sequenced?.CommandEpoch != action.Epoch ||
                _driver.OwningConnection(action.Controller) is not { State: EConnectionState.USOCK_Open } connection) continue;
            ActorChannel? channel = connection.FindActorChannel(action.Controller);
            if (!connection.IsNetReady || channel is not { Closing: false, NumOutRec: < 96 } ||
                connection.FindActorChannel(pawn) is not { OpenAcked: true, Closing: false } ||
                channel.Layout?.FindFunction(OwnerActionFunction) is not { } function)
            {
                _ownerActions[(_ownerActionStart + _ownerActionCount++) & (OwnerActionCapacity - 1)] = action;
                continue;
            }
            FOutBunch bunch = channel.BeginFunction(function, true);
            bunch.Writer.WriteUInt32(action.PawnGuid); bunch.Writer.WriteUInt32(action.Epoch);
            bunch.Writer.WriteUInt32(action.Sequence); bunch.Writer.WriteUInt32(action.Tick);
            bunch.Writer.WriteUInt16((ushort)action.Action);
            if (channel.SendFunction(bunch) >= 0) OwnerActionsSent++;
            else _ownerActions[(_ownerActionStart + _ownerActionCount++) & (OwnerActionCapacity - 1)] = action;
        }
    }

    private static void ReceiveOwnerAction(Actor actor, FBitReader reader, NetConnection connection)
    {
        var action = new OwnerAction
        {
            PawnGuid = reader.ReadUInt32(), Epoch = reader.ReadUInt32(), Sequence = reader.ReadUInt32(),
            Tick = reader.ReadUInt32(), Action = reader.ReadUInt16(),
        };
        if (reader.IsError || actor is not SPlayerController pc ||
            connection.Driver.World!.Level.GetService<NetMovementBridge>() is not { } bridge) return;
        bridge._ownerActionSequences.TryGetValue(pc, out uint received);
        if (action.Sequence <= received) return;
        bridge._ownerActionSequences[pc] = action.Sequence;
        if (bridge._ownerActionCount == OwnerActionCapacity) { connection.Close("Owner action receive queue exhausted"); return; }
        action.Controller = pc;
        bridge._ownerActions[(bridge._ownerActionStart + bridge._ownerActionCount++) & (OwnerActionCapacity - 1)] = action;
    }

    private void ApplyOwnerActions()
    {
        if (_driver.IsServer) return;
        while (_ownerActionCount > 0)
        {
            OwnerAction action = _ownerActions[_ownerActionStart];
            if (action.Controller.Pawn is not SBasePawn pawn ||
                !ReferenceEquals(_driver.FindActorByNetGuid(action.PawnGuid), pawn))
            {
                // Possession can follow the reliable action while its actor channel is opening.
                if (_driver.NewestServerTick - action.Tick < 30) break;
            }
            else if (EndpointOf(action.Controller) is { } endpoint && endpoint.Sequenced?.CommandEpoch == action.Epoch)
            {
                bool replaying = pawn.XLevel.World.IsReplaying;
                pawn.XLevel.World.IsReplaying = true;
                try { pawn.ExecuteAction(action.Action); }
                finally { pawn.XLevel.World.IsReplaying = replaying; }
                (endpoint.Client?.Pawn as SBasePawnMoves)?.NoteAuthoritativeOwnerAction(action.Sequence);
                OwnerActionsApplied++;
            }
            _ownerActions[_ownerActionStart] = default;
            _ownerActionStart = (_ownerActionStart + 1) & (OwnerActionCapacity - 1); _ownerActionCount--;
        }
    }
}
