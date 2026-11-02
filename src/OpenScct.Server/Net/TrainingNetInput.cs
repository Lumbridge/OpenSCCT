using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Game.Training;
using OpenScct.Net.Moves;

namespace OpenScct.Server;

/// <summary>Authority-only taps after movement admission, before the accepted input is consumed.</summary>
internal static class TrainingNetInput
{
    private static long ServerTick(SPlayerController pc)
    {
        // CommandTime temporarily changes Level.TickCount to the client's input clock.
        long tick = pc.XLevel.GetService<PawnStateReplication>()?.SourceTick ?? -1;
        return tick >= 0 ? tick : pc.XLevel.TickCount + (pc.XLevel.IsTicking ? 0 : 1);
    }

    private static SequencedMoveTransport.AppliedCommandContext? Command(SPlayerController pc) =>
        pc.XLevel.GetService<NetMovementBridge>()?.PeekEndpoint(pc)?.Sequenced?.ApplyingCommand;

    public static void Movement(SPlayerController pc, float delta, FVector acceleration, bool jump,
        TrainingInputSource source = TrainingInputSource.Remote)
    {
        if (pc.Role != ENetRole.ROLE_Authority || pc.XLevel.World.IsReplaying ||
            pc.XLevel.GetService<ITrainingInputSink>() is not { } sink) return;
        var command = Command(pc);
        var input = new TrainingInput
        {
            Source = source, Kind = TrainingInputKind.Movement, ServerTick = ServerTick(pc),
            InputTick = command?.InputTick, Epoch = command?.Epoch, Sequence = command?.Sequence,
            ClientTimeSeconds = command?.TimeSeconds, DeltaSeconds = delta, MovementFlags = command?.Flags,
            AccelerationX = acceleration.X, AccelerationY = acceleration.Y, AccelerationZ = acceleration.Z,
            ViewPitch = pc.Rotation.Pitch, ViewYaw = pc.Rotation.Yaw, ViewRoll = pc.Rotation.Roll,
            AimPitch = pc.MyDesiredRotation.Pitch, AimYaw = pc.MyDesiredRotation.Yaw, AimRoll = pc.MyDesiredRotation.Roll,
            Jump = jump,
        };
        sink.RecordInput(pc, input);
    }

    public static Action<int>? Actions(SBasePawn pawn)
    {
        if (pawn.Role != ENetRole.ROLE_Authority || pawn.XLevel.World.IsReplaying ||
            pawn.Controller is not SPlayerController pc || pawn.XLevel.GetService<ITrainingInputSink>() is not { } sink) return null;
        var command = Command(pc);
        long tick = ServerTick(pc);
        return code => sink.RecordAction(pc, new TrainingAction
        {
            Source = TrainingInputSource.Remote, Kind = TrainingActionKind.Native, ServerTick = tick,
            InputTick = command?.InputTick, Epoch = command?.Epoch, Sequence = command?.Sequence,
            ClientTimeSeconds = command?.TimeSeconds, ActionCode = code,
        });
    }

    public static TrainingInputSource LocalSource(SPlayerController pc) =>
        pc.Player is AiViewport ? TrainingInputSource.Bot : TrainingInputSource.Local;
}
