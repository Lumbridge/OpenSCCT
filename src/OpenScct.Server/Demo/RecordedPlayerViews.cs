using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class ServerHost
{
    internal const string DemoViewPrefix = "#demo-view ";
    internal const string DemoViewRequest = DemoViewPrefix + "1";
    private readonly Dictionary<int, long> _lastRecordedView = new();

    internal void RequestDemoViews(bool enabled)
    {
        _lastRecordedView.Clear();
        foreach (PlayerSlot player in _players)
            player.Connection.ControlChannel?.Send(ControlMessage.TextLine(enabled ? DemoViewRequest : DemoViewPrefix + "0"));
    }

    private bool ReceiveDemoView(NetConnection connection, string? text)
    {
        if (text?.StartsWith(DemoViewPrefix, StringComparison.Ordinal) != true) return false;
        // Offline play logs in through the map loader and attaches its controller directly to the connection.
        if (Recorder is not { IsFinished: false } recorder || !connection.bJoined ||
            connection.UserData is not PlayerSlot slot ||
            connection.Actor is not PlayerController { Pawn: { Health: > 0, bDeleteMe: false } pawn,
                PlayerReplicationInfo: { bOnlySpectator: false } pri }) return true;
        long now = World.TickCount;
        if (_lastRecordedView.TryGetValue(slot.PlayerId, out long last) && now - last < Sim.Hz / 20) return true;
        if (!DemoViewFrame.TryDecode(text[DemoViewPrefix.Length..], out var view)) return true;
        _lastRecordedView[slot.PlayerId] = now;
        // All identity/timing is authoritative. The reported camera is stored only in the replay; it cannot move
        // a pawn, alter aim, reveal actors to another connection, or affect collision/weapon validation.
        recorder.Writer.WriteView(view with { Tick = (uint)now, PlayerId = pri.PlayerID, PawnId = pawn.ActorId });
        return true;
    }
}

public sealed partial class ClientSession
{
    private bool _recordPlayerViews;
    private long _lastDemoViewSent = -Sim.Hz;

    public bool RecordingPlayerViews => _recordPlayerViews;

    /// <summary>A sample belongs to the current replicated pawn, even across respawns and replay seeks.</summary>
    public bool TryRecordedDemoView(DemoRecording recording, Pawn pawn, uint tick, out DemoViewFrame view)
    {
        view = default;
        return !pawn.bDeleteMe && pawn.PlayerReplicationInfo is { } pri &&
            recording.TryView(pri.PlayerID, tick, out view) &&
            ReferenceEquals(Driver.FindActorByNetGuid((uint)view.PawnId), pawn);
    }

    private bool ReceiveDemoViewRequest(string? text)
    {
        if (text is not (ServerHost.DemoViewRequest or ServerHost.DemoViewPrefix + "0")) return false;
        _recordPlayerViews = text == ServerHost.DemoViewRequest;
        return true;
    }

    /// <summary>At most ten actual rendered views per second while this server is recording.</summary>
    public void SubmitDemoView(DemoViewFrame view)
    {
        if (!_recordPlayerViews || _transport.IsPlayback || !view.IsValid ||
            _connection is not { State: EConnectionState.USOCK_Open, ReliableBunchesInFlight: < 64 } ||
            LocalTick - _lastDemoViewSent < Sim.Hz / 10) return;
        _lastDemoViewSent = LocalTick;
        SendText(ServerHost.DemoViewPrefix + view.Encode());
    }
}
