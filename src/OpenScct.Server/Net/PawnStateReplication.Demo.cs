using System;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class PawnStateReplication
{
    /// <summary>Seed the same animation revisions and wire epoch that subsequent packets refer to.</summary>
    internal bool WriteDemoCheckpoint(NetConnection source, NetConnection bootstrap)
    {
        foreach (ActorChannel channel in bootstrap.ActorChannels)
        {
            if (channel.Actor is not SBasePawn pawn || !_tracks.TryGetValue(pawn, out Track? track) || track.Newest < 0)
                continue;
            if (source.FindActorChannel(pawn) is not { } sourceChannel) return false;
            _viewers.TryGetValue(sourceChannel, out Viewer? viewer);
            bool sameLifetime = viewer is { WireEpoch: > 0 } && viewer.Epoch == track.Epoch;
            uint wireEpoch = sameLifetime ? viewer!.WireEpoch : (viewer?.WireEpoch ?? 0) + 1;
            long newest = sameLifetime && viewer!.SnapshotTick >= 0 ? viewer.SnapshotTick : track.Newest;
            if (track.Newest - newest >= History - PhaseRecoveryTicks) return false;
            if (!WriteDemoPose(channel, bootstrap, pawn, track, wireEpoch, newest))
                return false;
        }
        return true;
    }

    private static bool WriteDemoPose(ActorChannel channel, NetConnection bootstrap, SBasePawn pawn, Track track,
        uint wireEpoch, long newest)
    {
        const int interpolationTicks = 6;
        long first = Math.Max(0, newest - interpolationTicks);
        Span<ushort> revisions = stackalloc ushort[PawnAnimation.ChannelCount];
        revisions.Clear();
        RepFunction animation = channel.Layout!.FindFunction(Animation)!;
        for (long tick = Math.Max(0, first - PhaseRecoveryTicks); tick <= track.Newest; tick++)
        {
            Frame frame = track.Frames[Slot(tick)];
            if (frame.Tick != tick || frame.Epoch != track.Epoch) continue;
            for (int i = 0; i < frame.Pose.Length; i++)
            {
                ushort revision = frame.Revisions[i];
                if (revisions[i] == revision) continue;
                revisions[i] = revision;
                if (frame.Pose[i].Sequence.IsNone) continue;
                FOutBunch bunch = channel.BeginFunction(animation, reliable: true);
                bunch.Writer.WriteIntPacked(wireEpoch);
                bunch.Writer.WriteIntPacked((uint)tick);
                bunch.Writer.WriteByte((byte)i);
                bunch.Writer.WriteIntPacked(revision);
                WriteChannel(bunch.Writer, frame.Pose[i]);
                if (channel.SendFunction(bunch) < 0) return false;
            }
        }

        RepFunction snapshot = channel.Layout.FindFunction(Snapshot)!;
        for (long tick = first; tick <= newest; tick++)
        {
            Frame frame = track.Frames[Slot(tick)];
            if (frame.Tick != tick || frame.Epoch != track.Epoch) continue;
            FOutBunch bunch = channel.BeginFunction(snapshot, reliable: false);
            WriteFrame(bunch.Writer, frame, bootstrap, wireEpoch, track, pawn);
            if (channel.SendFunction(bunch) < 0) return false;
        }

        Frame last = track.Frames[Slot(track.Newest)];
        FOutBunch body = channel.BeginFunction(channel.Layout.FindFunction(Presentation)!, reliable: true);
        body.Writer.WriteIntPacked(wireEpoch);
        body.Writer.WriteIntPacked((uint)last.Tick);
        WriteBody(body.Writer, last, bootstrap, pawn);
        return channel.SendFunction(body) >= 0;
    }
}
