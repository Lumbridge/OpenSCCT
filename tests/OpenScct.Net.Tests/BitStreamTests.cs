using System;
using CsCheck;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class BitStreamTests
{
    /// <summary>One write of a mixed stream, with everything needed to check the read.</summary>
    private readonly record struct Op(int Kind, ulong Raw, int N, float F);

    private static readonly Gen<Op> GenOp =
        Gen.Select(Gen.Int[0, 11], Gen.ULong, Gen.Int[0, 64], Gen.Float[-300000f, 300000f], (k, raw, n, f) => new Op(k, raw, n, f));

    private static uint MaxFor(Op op) => (uint)(op.Raw >> 40) % 100_000u + 1u;

    private static void Write(FBitWriter w, Op op)
    {
        switch (op.Kind)
        {
            case 0: w.WriteBit((op.Raw & 1) != 0); break;
            case 1: w.WriteBits(op.Raw, op.N); break;
            case 2: w.WriteInt((uint)op.Raw % MaxFor(op), MaxFor(op)); break;
            case 3: w.WriteIntWrapped((uint)op.Raw, 1u << (1 + op.N % 16)); break;
            case 4: w.WriteIntPacked((uint)op.Raw); break;
            case 5: w.WriteSignedPacked((int)op.Raw); break;
            case 6: w.WriteFloat(op.F); break;
            case 7: w.WriteBytes(BitConverter.GetBytes(op.Raw).AsSpan(0, op.N % 9)); break;
            case 8: w.WritePackedVector(new FVector(op.F, op.F * 0.5f, -op.F * 0.25f)); break;
            case 9: w.WriteCompressedRotator(new FRotator((int)op.Raw, (int)(op.Raw >> 20), (int)(op.Raw >> 40))); break;
            case 10: w.WriteCompressedRotatorByte(new FRotator((int)op.Raw, (int)(op.Raw >> 20), (int)(op.Raw >> 40))); break;
            default: w.WriteQuantizedFloat(op.F, -300000f, 300000f, 1 + op.N % 24); break;
        }
    }

    private static void Check(FBitReader r, Op op)
    {
        switch (op.Kind)
        {
            case 0: Assert.Equal((op.Raw & 1) != 0, r.ReadBit()); break;
            case 1: Assert.Equal(op.N == 64 ? op.Raw : op.Raw & ((1UL << op.N) - 1), r.ReadBits(op.N)); break;
            case 2: Assert.Equal((uint)op.Raw % MaxFor(op), r.ReadInt(MaxFor(op))); break;
            case 3:
                uint max = 1u << (1 + op.N % 16);
                Assert.Equal((uint)op.Raw & (max - 1), r.ReadInt(max));
                break;
            case 4: Assert.Equal((uint)op.Raw, r.ReadIntPacked()); break;
            case 5: Assert.Equal((int)op.Raw, r.ReadSignedPacked()); break;
            case 6: Assert.Equal(BitConverter.SingleToUInt32Bits(op.F), BitConverter.SingleToUInt32Bits(r.ReadFloat())); break;
            case 7:
                byte[] expected = BitConverter.GetBytes(op.Raw).AsSpan(0, op.N % 9).ToArray();
                byte[] actual = new byte[expected.Length];
                r.ReadBytes(actual);
                Assert.Equal(expected, actual);
                break;
            case 8:
                var v = new FVector(op.F, op.F * 0.5f, -op.F * 0.25f);
                Assert.Equal(NetQuantize.RoundTripVector(v), r.ReadPackedVector());
                break;
            case 9:
            {
                FRotator rot = r.ReadCompressedRotator();
                Assert.Equal((int)op.Raw & 0xFFFF, rot.Pitch);
                Assert.Equal((int)(op.Raw >> 20) & 0xFFFF, rot.Yaw);
                Assert.Equal((int)(op.Raw >> 40) & 0xFFFF, rot.Roll);
                break;
            }

            case 10:
            {
                FRotator rot = r.ReadCompressedRotatorByte();
                Assert.Equal((int)op.Raw & 0xFF00, rot.Pitch);
                Assert.Equal((int)(op.Raw >> 20) & 0xFF00, rot.Yaw);
                Assert.Equal((int)(op.Raw >> 40) & 0xFF00, rot.Roll);
                break;
            }

            default:
                int bits = 1 + op.N % 24;
                float got = r.ReadQuantizedFloat(-300000f, 300000f, bits);
                float step = 600000f / ((1 << bits) - 1);
                // Half a step, plus a few float ulps at the range end (24 bits is finer than a float there).
                Assert.True(MathF.Abs(got - op.F) <= step * 0.5f + 0.125f, $"{got} vs {op.F} at {bits} bits");
                break;
        }
    }

    [Fact]
    public void MixedStreamsRoundTrip()
    {
        GenOp.Array[0, 60].Sample(ops =>
        {
            var w = new FBitWriter(64 * 1024);
            foreach (Op op in ops)
            {
                Write(w, op);
            }

            Assert.False(w.IsError);
            var r = new FBitReader(w.Buffer, w.NumBits);
            foreach (Op op in ops)
            {
                Check(r, op);
            }

            Assert.False(r.IsError);
            Assert.True(r.AtEnd);
        }, iter: 3000);
    }

    [Fact]
    public void WriteIntUsesAtMostCeilLogTwoBits()
    {
        Gen.Select(Gen.UInt[1, uint.MaxValue], Gen.UInt).Sample((max, raw) =>
        {
            uint value = raw % max;
            var w = new FBitWriter(64);
            w.WriteInt(value, max);
            Assert.True(w.NumBits <= FBitWriter.CeilLogTwo(max));
            Assert.Equal(value, new FBitReader(w.Buffer, w.NumBits).ReadInt(max));
        }, iter: 5000);
    }

    [Fact]
    public void WriteIntSavesTheTopBitWhenItCannotBeSet()
    {
        // UE2 SerializeInt with max 3: value 1 (01b) stops after one bit, because a second set bit would reach 3;
        // values 0 and 2 need both bits.
        Assert.Equal(1, FBitWriter.CountIntBits(1, 3));
        Assert.Equal(2, FBitWriter.CountIntBits(2, 3));
        Assert.Equal(2, FBitWriter.CountIntBits(0, 3));
        Assert.Equal(0, FBitWriter.CountIntBits(0, 1));
        Assert.Equal(10, FBitWriter.CountIntBits(1022, K.MAX_CHANNELS));
    }

    [Fact]
    public void PackedIntsAreShortForSmallValues()
    {
        var w = new FBitWriter(64);
        w.WriteIntPacked(127);
        Assert.Equal(8, w.NumBits);
        w.WriteIntPacked(128);
        Assert.Equal(24, w.NumBits);
        w.WriteSignedPacked(-1);
        Assert.Equal(32, w.NumBits);
    }

    [Fact]
    public void PackedVectorsAreWithinHalfAStep()
    {
        Gen.Select(Gen.Float[-262144f, 262144f], Gen.Float[-262144f, 262144f], Gen.Float[-262144f, 262144f]).Sample((x, y, z) =>
        {
            var v = new FVector(x, y, z);
            var w = new FBitWriter(256);
            w.WritePackedVector(v);
            FVector back = new FBitReader(w.Buffer, w.NumBits).ReadPackedVector();
            float half = 0.5f / NetQuantize.DefaultVectorScale;
            Assert.True(MathF.Abs(back.X - x) <= half + 1e-3f && MathF.Abs(back.Y - y) <= half + 1e-3f && MathF.Abs(back.Z - z) <= half + 1e-3f,
                $"{v} came back as {back}");
            Assert.True(w.NumBits <= 5 + 3 * 24);
        }, iter: 5000);
    }

    [Fact]
    public void ZeroVectorTakesEightBits()
    {
        var w = new FBitWriter(64);
        w.WritePackedVector(FVector.Zero);
        Assert.Equal(8, w.NumBits);
    }

    [Fact]
    public void OverflowSetsTheErrorAndWritesNothing()
    {
        var w = new FBitWriter(10);
        w.WriteBits(0x3FF, 10);
        w.WriteBit(true);
        Assert.True(w.IsError);
        Assert.Equal(10, w.NumBits);
        w.WriteByte(0xFF);
        Assert.Equal(10, w.NumBits);
        w.Reset();
        Assert.False(w.IsError);
        Assert.Equal(0, w.NumBits);
        Assert.Equal(0, w.Buffer[0]);
    }

    [Fact]
    public void ReadingPastTheEndSetsTheErrorAndReturnsZero()
    {
        var w = new FBitWriter(16);
        w.WriteBits(0xABC, 12);
        var r = new FBitReader(w.Buffer, 12);
        Assert.Equal(0xABCUL, r.ReadBits(12));
        Assert.Equal(0UL, r.ReadBits(1));
        Assert.True(r.IsError);
        Assert.Equal(0u, r.ReadIntPacked());
    }

    [Fact]
    public void GarbageNeverThrows()
    {
        Gen.Byte.Array[0, 64].Sample(bytes =>
        {
            var r = new FBitReader(bytes, bytes.Length * 8);
            for (int i = 0; i < 40 && !r.IsError; i++)
            {
                _ = r.ReadIntPacked();
                _ = r.ReadString(32);
                r.ReadPackedVector(out _, out _, out _);
                _ = r.ReadCompressedRotator();
                _ = r.ReadInt(1000);
            }
        }, iter: 3000);
    }

    [Fact]
    public void StringsRoundTrip()
    {
        Gen.String.Sample(text =>
        {
            var w = new FBitWriter(64 * 1024);
            w.WriteBit(true);
            w.WriteString(text, 4096);
            var r = new FBitReader(w.Buffer, w.NumBits);
            Assert.True(r.ReadBit());
            string back = r.ReadString(4096);
            Assert.False(r.IsError);
            Assert.Equal(System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text)), back);
        }, iter: 2000);
    }

    [Fact]
    public void TerminatorGivesTheExactBitLength()
    {
        Gen.Int[0, 200].Sample(bits =>
        {
            var w = new FBitWriter(256);
            w.WriteBits(0, Math.Min(bits, 64));
            for (int i = 64; i < bits; i++)
            {
                w.WriteBit(i % 3 == 0);
            }

            int payload = w.NumBits;
            w.WriteBit(true);
            Assert.Equal(payload, FBitReader.FindTerminatedBitLength(w.Data));
        });

        Assert.Equal(-1, FBitReader.FindTerminatedBitLength([]));
        Assert.Equal(-1, FBitReader.FindTerminatedBitLength([1, 0]));
    }

    [Fact]
    public void AppendBitsCopiesUnalignedRuns()
    {
        Gen.Select(Gen.Int[0, 7], Gen.Byte.Array[1, 40], Gen.Int[0, 7]).Sample((lead, bytes, trim) =>
        {
            int bitCount = Math.Max(0, bytes.Length * 8 - trim);
            var w = new FBitWriter(512);
            w.WriteBits(0b1011011, lead);
            w.AppendBits(bytes, bitCount);
            var r = new FBitReader(w.Buffer, w.NumBits);
            r.Skip(lead);
            for (int i = 0; i < bitCount; i++)
            {
                Assert.Equal((bytes[i >> 3] & (1 << (i & 7))) != 0, r.ReadBit());
            }
        }, iter: 2000);
    }
}
