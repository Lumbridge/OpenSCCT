using System;
using System.Text;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Bit stream reader (UE2 FBitReader) over a range of bits in a byte array. Reading past the end sets
/// <see cref="IsError"/> and returns zeros (UE2 ArIsError); callers check the flag after a group of reads.
/// The reader is reusable: <see cref="SetData(byte[], int, int)"/> points it at new data without allocating.
/// </summary>
public sealed class FBitReader
{
    private byte[] _buffer = [];
    private int _start;
    private int _pos;
    private int _end;

    public FBitReader()
    {
    }

    public FBitReader(byte[] buffer, int numBits)
    {
        SetData(buffer, 0, numBits);
    }

    /// <summary>Reads bits [startBit, startBit + numBits) of <paramref name="buffer"/>.</summary>
    public void SetData(byte[] buffer, int startBit, int numBits)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(startBit);
        ArgumentOutOfRangeException.ThrowIfNegative(numBits);
        if ((long)startBit + numBits > (long)buffer.Length * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(numBits), "Range runs past the end of the buffer.");
        }

        _buffer = buffer;
        _start = startBit;
        _pos = startBit;
        _end = startBit + numBits;
        IsError = false;
    }

    public bool IsError { get; private set; }

    /// <summary>Bits read so far.</summary>
    public int Position => _pos - _start;

    public int NumBits => _end - _start;

    public int BitsLeft => _end - _pos;

    public bool AtEnd => _pos >= _end;

    /// <summary>Position in the underlying buffer, in bits from its start.</summary>
    public int AbsolutePosition => _pos;

    public byte[] Buffer => _buffer;

    /// <summary>Marks the stream as bad, for example after a value out of range.</summary>
    public void SetError() => IsError = true;

    private bool Take(int bits)
    {
        if (IsError || bits > _end - _pos)
        {
            IsError = true;
            _pos = _end;
            return false;
        }

        return true;
    }

    public void Skip(int bits)
    {
        if (Take(bits))
        {
            _pos += bits;
        }
    }

    public bool ReadBit()
    {
        if (!Take(1))
        {
            return false;
        }

        bool bit = (_buffer[_pos >> 3] & (1 << (_pos & 7))) != 0;
        _pos++;
        return bit;
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 64), low bit first.</summary>
    public ulong ReadBits(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 64);
        if (!Take(count))
        {
            return 0;
        }

        ulong value = 0;
        int shift = 0;
        int pos = _pos;
        while (count > 0)
        {
            int bitOffset = pos & 7;
            int take = 8 - bitOffset;
            if (take > count)
            {
                take = count;
            }

            ulong chunk = (ulong)((_buffer[pos >> 3] >> bitOffset) & ((1 << take) - 1));
            value |= chunk << shift;
            shift += take;
            count -= take;
            pos += take;
        }

        _pos = pos;
        return value;
    }

    /// <summary>Reads a value written with <see cref="FBitWriter.WriteInt"/> or WriteIntWrapped (UE2 SerializeInt).</summary>
    [UcNative("FBitReader::SerializeInt")]
    public uint ReadInt(uint valueMax)
    {
        ArgumentOutOfRangeException.ThrowIfZero(valueMax);
        ulong value = 0;
        for (ulong mask = 1; value + mask < valueMax && mask <= uint.MaxValue; mask <<= 1)
        {
            if (!Take(1))
            {
                return 0;
            }

            if ((_buffer[_pos >> 3] & (1 << (_pos & 7))) != 0)
            {
                value |= mask;
            }

            _pos++;
        }

        return (uint)value;
    }

    public byte ReadByte() => (byte)ReadBits(8);

    public ushort ReadUInt16() => (ushort)ReadBits(16);

    public uint ReadUInt32() => (uint)ReadBits(32);

    public int ReadInt32() => (int)(uint)ReadBits(32);

    public ulong ReadUInt64() => ReadBits(64);

    public float ReadFloat() => BitConverter.UInt32BitsToSingle((uint)ReadBits(32));

    /// <summary>Reads a value written with <see cref="FBitWriter.WriteIntPacked"/>. More than 5 groups is an error.</summary>
    public uint ReadIntPacked()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            bool more = ReadBit();
            uint group = (uint)ReadBits(7);
            if (IsError)
            {
                return 0;
            }

            value |= group << shift;
            if (!more)
            {
                return value;
            }
        }

        IsError = true;
        return 0;
    }

    public int ReadSignedPacked()
    {
        uint z = ReadIntPacked();
        return (int)(z >> 1) ^ -(int)(z & 1);
    }

    public void ReadBytes(Span<byte> destination)
    {
        if (!Take(destination.Length * 8))
        {
            destination.Clear();
            return;
        }

        if ((_pos & 7) == 0)
        {
            _buffer.AsSpan(_pos >> 3, destination.Length).CopyTo(destination);
            _pos += destination.Length * 8;
            return;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)ReadBits(8);
        }
    }

    /// <summary>Copies the next <paramref name="bitCount"/> bits into <paramref name="destination"/> from bit 0.</summary>
    public void ReadBitsInto(byte[] destination, int bitCount)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!Take(bitCount))
        {
            return;
        }

        int whole = bitCount >> 3;
        Array.Clear(destination, 0, (bitCount + 7) >> 3);
        for (int i = 0; i < whole; i++)
        {
            destination[i] = (byte)ReadBits(8);
        }

        int rest = bitCount & 7;
        if (rest != 0)
        {
            destination[whole] = (byte)ReadBits(rest);
        }
    }

    /// <summary>Reads text written with <see cref="FBitWriter.WriteString"/>. Longer than maxBytes is an error.</summary>
    public string ReadString(int maxBytes = 1024)
    {
        uint count = ReadIntPacked();
        if (IsError || count > maxBytes || count * 8 > BitsLeft)
        {
            IsError = true;
            return string.Empty;
        }

        if (count == 0)
        {
            return string.Empty;
        }

        Span<byte> bytes = count <= 256 ? stackalloc byte[(int)count] : new byte[count];
        ReadBytes(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public float ReadQuantizedFloat(float min, float max, int bits) =>
        NetQuantize.DequantizeFloat((uint)ReadBits(bits), min, max, bits);

    /// <summary>Reads a vector written with <see cref="FBitWriter.WritePackedVector(int, int, int, int)"/>, still quantised.</summary>
    public void ReadPackedVector(out int qx, out int qy, out int qz, int maxBitsPerComponent = NetQuantize.DefaultVectorBits)
    {
        int bits = (int)ReadInt((uint)maxBitsPerComponent) + 1;
        long bias = 1L << (bits - 1);
        qx = (int)((long)ReadBits(bits) - bias);
        qy = (int)((long)ReadBits(bits) - bias);
        qz = (int)((long)ReadBits(bits) - bias);
        if (IsError)
        {
            qx = qy = qz = 0;
        }
    }

    public FVector ReadPackedVector(int scale = NetQuantize.DefaultVectorScale, int maxBitsPerComponent = NetQuantize.DefaultVectorBits)
    {
        ReadPackedVector(out int qx, out int qy, out int qz, maxBitsPerComponent);
        return NetQuantize.DequantizeVector(qx, qy, qz, scale);
    }

    /// <summary>Reads a vector written with <see cref="FBitWriter.WriteRoundedVector"/> (whole units).</summary>
    public void ReadRoundedVector(out int x, out int y, out int z)
    {
        int bits = (int)ReadInt(FBitWriter.MaxRoundedVectorBits) + 1;
        uint limit = 1u << (bits + 1);
        int bias = 1 << bits;
        x = (int)ReadInt(limit) - bias;
        y = (int)ReadInt(limit) - bias;
        z = (int)ReadInt(limit) - bias;
        if (IsError)
        {
            x = y = z = 0;
        }
    }

    /// <summary>Reads a rotator written with <see cref="FBitWriter.WriteCompressedRotator"/>. Axes come back in 0..65535.</summary>
    public FRotator ReadCompressedRotator()
    {
        int pitch = ReadBit() ? (int)ReadBits(16) : 0;
        int yaw = ReadBit() ? (int)ReadBits(16) : 0;
        int roll = ReadBit() ? (int)ReadBits(16) : 0;
        return new FRotator(pitch, yaw, roll);
    }

    /// <summary>Reads a UE2 byte-compressed rotator. Axes come back as multiples of 256.</summary>
    public FRotator ReadCompressedRotatorByte()
    {
        int pitch = ReadBit() ? (int)ReadBits(8) << 8 : 0;
        int yaw = ReadBit() ? (int)ReadBits(8) << 8 : 0;
        int roll = ReadBit() ? (int)ReadBits(8) << 8 : 0;
        return new FRotator(pitch, yaw, roll);
    }

    /// <summary>
    /// Bit length of a packet that ends with a 1 terminator bit followed by zero padding (the UE2 packet trailer):
    /// the index of the highest set bit. -1 when there is no terminator.
    /// </summary>
    public static int FindTerminatedBitLength(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return -1;
        }

        byte last = data[^1];
        if (last == 0)
        {
            return -1;
        }

        int highBit = 31 - System.Numerics.BitOperations.LeadingZeroCount(last);
        return (data.Length - 1) * 8 + highBit;
    }
}
