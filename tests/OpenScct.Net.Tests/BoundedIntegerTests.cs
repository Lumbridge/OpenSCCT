using System;
using System.Collections.Generic;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>Independent UE2 per-bit oracles keep the chunked integer codec wire-compatible.</summary>
public sealed class BoundedIntegerTests
{
    [Fact]
    public void AllSmallValuesAndFullWidthBoundariesMatchTheOriginalWireBits()
    {
        var actual = new FBitWriter(128);
        var reference = new FBitWriter(128);
        var reader = new FBitReader();
        var legacy = new FBitReader();
        foreach ((uint value, uint maximum) in Cases())
        for (int offset = 0; offset < 8; offset++)
        {
            actual.Reset();
            reference.Reset();
            actual.WriteBits(0x55, offset);
            reference.WriteBits(0x55, offset);
            actual.WriteInt(value, maximum);
            WriteOriginal(reference, value, maximum);
            Assert.Equal(reference.NumBits - offset, FBitWriter.CountIntBits(value, maximum));
            Assert.Equal(reference.NumBits, actual.NumBits);
            // The trailer detects a saved final bit incorrectly emitted, read or overwritten.
            actual.WriteBits(0x16B, 9);
            reference.WriteBits(0x16B, 9);
            Assert.True(reference.Data.SequenceEqual(actual.Data));
            reader.SetData(reference.Buffer, offset, reference.NumBits - offset);
            legacy.SetData(actual.Buffer, offset, actual.NumBits - offset);
            Assert.Equal(value, reader.ReadInt(maximum));
            Assert.Equal(value, ReadOriginal(legacy, maximum));
            Assert.Equal(legacy.Position, reader.Position);
            Assert.Equal(0x16BUL, reader.ReadBits(9));
            Assert.Equal(0x16BUL, legacy.ReadBits(9));
            Assert.False(reader.IsError);
            Assert.True(reader.AtEnd);
        }
    }

    [Fact]
    public void TruncatedReadsMatchTheOriginalErrorAndCursorAtEveryBit()
    {
        var encoded = new FBitWriter(64);
        var reader = new FBitReader();
        var legacy = new FBitReader();
        foreach ((uint value, uint maximum) in BoundaryCases())
        for (int offset = 0; offset < 8; offset++)
        {
            encoded.Reset();
            encoded.WriteBits(0x55, offset);
            WriteOriginal(encoded, value, maximum);
            int bits = encoded.NumBits - offset;
            for (int available = 0; available <= bits; available++)
            {
                reader.SetData(encoded.Buffer, offset, available);
                legacy.SetData(encoded.Buffer, offset, available);
                Assert.Equal(ReadOriginal(legacy, maximum), reader.ReadInt(maximum));
                Assert.Equal(legacy.IsError, reader.IsError);
                Assert.Equal(legacy.Position, reader.Position);
                Assert.Equal(available < bits, reader.IsError);
            }

            reader.SetData(encoded.Buffer, offset, bits);
            legacy.SetData(encoded.Buffer, offset, bits);
            reader.SetError();
            legacy.SetError();
            Assert.Equal(ReadOriginal(legacy, maximum), reader.ReadInt(maximum));
            Assert.Equal(legacy.Position, reader.Position);
            Assert.True(reader.IsError);
        }
    }

    [Fact]
    public void OverflowIsAtomicAndRewindAndResetClearPreviousBits()
    {
        var writer = new FBitWriter(64);
        var reference = new FBitWriter(64);
        foreach ((uint value, uint maximum) in BoundaryCases())
        for (int offset = 0; offset < 8; offset++)
        {
            reference.Reset();
            reference.WriteBits(0x55, offset);
            WriteOriginal(reference, value, maximum);
            int bits = reference.NumBits - offset;
            if (bits == 0) continue;
            writer.Reset();
            writer.SetMaxBits(offset + bits - 1);
            writer.WriteBits(0x55, offset);
            writer.WriteInt(value, maximum);
            Assert.True(writer.IsError);
            Assert.Equal(offset, writer.NumBits);
            Assert.Equal((byte)(0x55 & ((1 << offset) - 1)), writer.Buffer[0]);
            writer.WriteInt(0, 1);
            Assert.True(writer.IsError);
            Assert.Equal(offset, writer.NumBits);

            writer.Rewind(offset);
            writer.SetMaxBits(64);
            writer.WriteBits(uint.MaxValue, 32);
            writer.Rewind(offset);
            writer.WriteInt(value, maximum);
            Assert.False(writer.IsError);
            Assert.Equal(reference.NumBits, writer.NumBits);
            Assert.True(reference.Data.SequenceEqual(writer.Data));
        }
    }

    [Fact]
    public void InvalidBoundsRemainRejectedWithoutWriting()
    {
        var writer = new FBitWriter(64);
        var reader = new FBitReader(new byte[8], 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteInt(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteInt(7, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadInt(0));
        Assert.Equal(0, writer.NumBits);
        Assert.Equal(0, reader.Position);
        Assert.Equal(0, FBitWriter.CountIntBits(0, 0));
        Assert.False(writer.IsError);
        Assert.False(reader.IsError);
    }

    private static IEnumerable<(uint Value, uint Maximum)> Cases()
    {
        for (uint maximum = 1; maximum <= 128; maximum++)
        for (uint value = 0; value < maximum; value++) yield return (value, maximum);
        foreach (var pair in BoundaryCases()) yield return pair;
        var random = new Random(42137);
        for (int i = 0; i < 10000; i++)
        {
            uint maximum = (uint)random.NextInt64(1, 1L << 32);
            yield return ((uint)random.NextInt64(maximum), maximum);
        }
    }

    private static IEnumerable<(uint Value, uint Maximum)> BoundaryCases()
    {
        var maxima = new HashSet<uint> { 1, uint.MaxValue };
        for (int bit = 1; bit < 32; bit++)
        {
            uint power = 1u << bit;
            maxima.Add(power - 1);
            maxima.Add(power);
            maxima.Add(power + 1);
        }
        foreach (uint maximum in maxima)
        foreach (uint value in new uint[] { 0, 1, maximum - 1, maximum / 2, (maximum - 1) / 2, 0x55555555, 0xAAAAAAAA })
            yield return (value % maximum, maximum);
    }

    private static void WriteOriginal(FBitWriter writer, uint value, uint maximum)
    {
        ulong accumulated = 0;
        for (ulong mask = 1; accumulated + mask < maximum && mask <= uint.MaxValue; mask <<= 1)
        {
            bool set = (value & mask) != 0;
            writer.WriteBit(set);
            if (set) accumulated += mask;
        }
    }

    private static uint ReadOriginal(FBitReader reader, uint maximum)
    {
        ulong value = 0;
        for (ulong mask = 1; value + mask < maximum && mask <= uint.MaxValue; mask <<= 1)
        {
            bool set = reader.ReadBit();
            if (reader.IsError) return 0;
            if (set) value |= mask;
        }
        return (uint)value;
    }
}
