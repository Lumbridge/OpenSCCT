using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class DemoRecorder
{
    private const int CheckpointIntervalTicks = 60 * Sim.Hz;
    private const int CheckpointRetryTicks = Sim.Hz / 4;
    private long _nextCheckpoint;
    private long _retryCheckpoint;

    internal int PlaybackPlayerId { get; init; }

    /// <summary>Capture after a network flush so the bootstrap and the continuation describe the same tick.</summary>
    internal void CaptureCheckpoint()
    {
        long now = _host.World.TickCount;
        if (IsFinished || Writer.Index.Packets == 0 || _host.Driver.Settings.PawnProxies != PawnProxyMode.Snapshots)
            return;

        // Editor playtests can spawn players without a round-start marker. Use their recording's first tick.
        if (_nextCheckpoint == 0) _nextCheckpoint = (long)Writer.Index.FirstTick + CheckpointIntervalTicks;
        if (now < _nextCheckpoint || now < _retryCheckpoint) return;
        if (now - _nextCheckpoint >= Sim.Hz || (!_matchBegun && !HasPlayingPawn()))
        {
            _nextCheckpoint += CheckpointIntervalTicks;
            return;
        }

        bool captured = Writer.TryWriteCheckpoint((uint)now, sink =>
            _host.Driver.TryWriteDemoCheckpoint(Connection, sink,
                bootstrap => _host.SendDemoWelcome(bootstrap, PlaybackPlayerId),
                bootstrap => _host.PawnStates.WriteDemoCheckpoint(Connection, bootstrap) &&
                    _host.WriteDemoCheckpointActors(bootstrap),
                out byte[] continuation) ? continuation : null);
        // A newly opened or closing channel can make this flush unsuitable. Retry briefly without stalling play.
        if (captured) _nextCheckpoint += CheckpointIntervalTicks;
        else _retryCheckpoint = now + CheckpointRetryTicks;
    }

    private bool HasPlayingPawn()
    {
        foreach (SBasePawn pawn in _host.World.Level.DynamicActors<SBasePawn>())
            if (pawn is { bDeleteMe: false, PlayerReplicationInfo.bOnlySpectator: false })
                return true;
        return false;
    }
}

public sealed partial class ServerHost
{
    internal void SendDemoWelcome(NetConnection connection, int playerId) =>
        connection.ControlChannel?.Send(ControlMessage.Welcome(playerId, Map.Name, (uint)World.TickCount, NetRate,
            Map.ContentHash, Map.FileListing, Driver.Settings.PawnProxies));
}
