using System;
using OpenScct.Core;

namespace OpenScct.Net.Moves;

/// <summary>Within-packet vector deltas at exactly the normal packed-vector precision and bounds.</summary>
internal static class MoveBatchVector
{
    private const int Scale = NetQuantize.DefaultVectorScale, Bits = NetQuantize.DefaultVectorBits;
    private const int Limit = (1 << (Bits - 1)) - 1;

    public static bool Equal(FVector a, FVector b)
    {
        NetQuantize.QuantizeVector(a, Scale, Bits, out int ax, out int ay, out int az);
        NetQuantize.QuantizeVector(b, Scale, Bits, out int bx, out int by, out int bz);
        return ax == bx && ay == by && az == bz;
    }

    public static void Write(FBitWriter writer, FVector value, FVector previous)
    {
        NetQuantize.QuantizeVector(value, Scale, Bits, out int x, out int y, out int z);
        NetQuantize.QuantizeVector(previous, Scale, Bits, out int px, out int py, out int pz);
        bool changed = x != px || y != py || z != pz;
        writer.WriteBit(changed);
        if (!changed) return;
        int dx = x - px, dy = y - py, dz = z - pz;
        int largestDelta = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
        bool relative = largestDelta <= Limit && largestDelta < Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        writer.WriteBit(relative);
        if (relative)
        {
            WriteComponent(writer, dx); WriteComponent(writer, dy); WriteComponent(writer, dz);
        }
        else writer.WritePackedVector(x, y, z);
    }

    public static FVector Read(FBitReader reader, FVector previous)
    {
        if (!reader.ReadBit()) return previous;
        bool relative = reader.ReadBit();
        long x, y, z;
        if (relative)
        {
            x = ReadComponent(reader); y = ReadComponent(reader); z = ReadComponent(reader);
            NetQuantize.QuantizeVector(previous, Scale, Bits, out int px, out int py, out int pz);
            x += px; y += py; z += pz;
        }
        else
        {
            reader.ReadPackedVector(out int ax, out int ay, out int az);
            x = ax; y = ay; z = az;
        }
        if (x < -Limit || x > Limit || y < -Limit || y > Limit || z < -Limit || z > Limit)
        {
            reader.SetError();
            return default;
        }
        return NetQuantize.DequantizeVector((int)x, (int)y, (int)z, Scale);
    }

    private static void WriteComponent(FBitWriter writer, int value)
    {
        writer.WriteBit(value != 0);
        if (value != 0) writer.WriteSignedPacked(value);
    }
    private static int ReadComponent(FBitReader reader) => reader.ReadBit() ? reader.ReadSignedPacked() : 0;
}
