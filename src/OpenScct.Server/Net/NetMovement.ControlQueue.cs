using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class NetMovementBridge
{
    private readonly Dictionary<SPlayerController, ControlQueue> _controls = new(ReferenceEqualityComparer.Instance);

    private sealed class ControlCall
    {
        public int Sequence;
        public RepFunction Function = null!;
        public NetFunctionHandler Handler = null!;
        public NetConnection Connection = null!;
        public readonly NetCallArgs Args = new();
    }

    private sealed class ControlQueue
    {
        public readonly ControlCall[] Calls = new ControlCall[256];
        public int Start, Count;
        public ControlQueue() { for (int i = 0; i < Calls.Length; i++) Calls[i] = new ControlCall(); }
    }

    private bool DeferMovementCall(Actor actor, RepFunction f, NetCallArgs args, NetConnection connection, NetFunctionHandler handler)
    {
        if (actor is not SPlayerController { Pawn: SBasePawn { Health: > 0 }, bWaitingForReinit: false } pc || pc.StateNode is SPlayerController.s_Inactive || f.Name is not
            ("PCChangeServerState" or "ReplicateActionToServer" or "ReplicateActionToServerWithVelocity" or
             "ServerGetZipP" or "ServerGetNewZipP" or "ServerGetZipPAndNormal" or "ServerSetWallJumpDir" or "ServerSetInfoForVandamme" or NetCombatBridge.FunctionName) ||
            EndpointOf(pc)?.Sequenced is null) return false;
        if (!_controls.TryGetValue(pc, out ControlQueue? queue)) _controls.Add(pc, queue = new ControlQueue());
        if (queue.Count == queue.Calls.Length)
        {
            connection.Close($"Movement control queue exhausted: {EndpointOf(pc)?.Sequenced?.DescribePending()}, state {pc.GetStateName()}, controls {queue.Calls[queue.Start].Sequence}..{connection.ReceivedReliableSequence(actor)}");
            return true;
        }
        ControlCall call = queue.Calls[(queue.Start + queue.Count++) & 255];
        call.Sequence = connection.ReceivedReliableSequence(actor); call.Function = f; call.Handler = handler; call.Connection = connection;
        args.Values.AsSpan(0, f.ValueCount).CopyTo(call.Args.Values);
        return true;
    }

    private void ApplyMovementCalls(SPlayerController pc, int throughSequence)
    {
        if (!_controls.TryGetValue(pc, out ControlQueue? queue)) return;
        while (queue.Count > 0)
        {
            ControlCall call = queue.Calls[queue.Start];
            if (NetConnection.ReliableSeqDiff(call.Sequence, throughSequence) > 0) break;
            queue.Start = (queue.Start + 1) & 255; queue.Count--;
            NetFunctionHandler handler = call.Handler;
            call.Handler = null!;
            try { handler(pc, call.Function, call.Args, call.Connection); }
            finally { call.Args.Clear(); }
        }
    }

    private void ResetMovementCalls(SPlayerController pc)
    {
        if (!_controls.TryGetValue(pc, out ControlQueue? queue)) return;
        while (queue.Count > 0)
        {
            queue.Calls[queue.Start].Args.Clear();
            queue.Calls[queue.Start].Handler = null!;
            queue.Start = (queue.Start + 1) & 255; queue.Count--;
        }
    }
}
