using System;
using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// Replication of <see cref="CubeWalker"/>, a runtime-only class: the generated Engine.Pawn layout plus hand-written
/// members. The walker sends Location, Rotation, Velocity and Physics to every client on every change in
/// 1/16 uu and full rotator precision (clients interpolate them), and accepts one server function, ServerMove,
/// carrying the newest moves (up to <see cref="K.MoveResendCount"/>) so each move survives the loss of a few
/// packets.
/// <para>Wire form of ServerMove: count-1 (2 bits), the newest TimeStamp (32 bits), then per move from the
/// oldest: Forward and Strafe (8 bits each), ViewYaw (16 bits), Buttons (8 bits). TimeStamps are consecutive.</para>
/// </summary>
public static class CubeWalkerNet
{
    public const string ServerMove = "ServerMove";

    /// <summary>The hand-written members appended to the Pawn layout.</summary>
    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension { InterpolateMovement = true };
        extension.Properties.Add(new RepExtensionProperty("M0Location", nameof(Actor.Location), RepLeafKind.VectorFine));
        extension.Properties.Add(new RepExtensionProperty("M0Rotation", nameof(Actor.Rotation), RepLeafKind.RotatorFine));
        extension.Properties.Add(new RepExtensionProperty("M0Velocity", nameof(Actor.Velocity), RepLeafKind.VectorFine));
        extension.Properties.Add(new RepExtensionProperty("M0Physics", nameof(Actor.Physics), RepLeafKind.Enum));
        extension.Functions.Add(new RepExtensionFunction(ServerMove, Reliable: false, ClientToServer: true,
            Signature: "moves" + K.MoveResendCount.ToString(System.Globalization.CultureInfo.InvariantCulture), ReceiveServerMove));
        return extension;
    }

    private static void ReceiveServerMove(Actor actor, FBitReader reader, NetConnection connection)
    {
        Span<CubeMove> moves = stackalloc CubeMove[K.MoveResendCount];
        int count = ReadMoves(reader, moves);
        if (!reader.IsError && actor is CubeWalker walker)
        {
            walker.ServerMove(moves[..count]);
        }
    }

    /// <summary>Writes 1 to <see cref="K.MoveResendCount"/> consecutive moves, oldest first.</summary>
    public static void WriteMoves(FBitWriter writer, ReadOnlySpan<CubeMove> moves)
    {
        if (moves.Length is < 1 or > K.MoveResendCount)
        {
            throw new ArgumentOutOfRangeException(nameof(moves));
        }

        writer.WriteInt((uint)(moves.Length - 1), K.MoveResendCount);
        writer.WriteUInt32(moves[^1].TimeStamp);
        for (int i = 0; i < moves.Length; i++)
        {
            CubeMove move = moves[i];
            writer.WriteByte((byte)move.Forward);
            writer.WriteByte((byte)move.Strafe);
            writer.WriteUInt16(move.ViewYaw);
            writer.WriteByte(move.Buttons);
        }
    }

    /// <summary>Reads moves written by <see cref="WriteMoves"/>; returns how many.</summary>
    public static int ReadMoves(FBitReader reader, Span<CubeMove> moves)
    {
        int count = (int)reader.ReadInt(K.MoveResendCount) + 1;
        uint newest = reader.ReadUInt32();
        if (reader.IsError || count > moves.Length || newest < (uint)count)
        {
            reader.SetError();
            return 0;
        }

        for (int i = 0; i < count; i++)
        {
            var forward = (sbyte)reader.ReadByte();
            var strafe = (sbyte)reader.ReadByte();
            ushort yaw = reader.ReadUInt16();
            byte buttons = reader.ReadByte();
            moves[i] = new CubeMove(newest - (uint)(count - 1 - i), forward, strafe, yaw, buttons);
        }

        return reader.IsError ? 0 : count;
    }
}
