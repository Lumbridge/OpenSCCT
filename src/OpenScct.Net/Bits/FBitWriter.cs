using System;
using System.Text;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Bit stream writer (UE2 FBitWriter). Bits are stored least significant first within each byte, like UE2.
/// The capacity is fixed; a write that does not fit sets <see cref="IsError"/> and writes nothing, after which
/// every further write is ignored (UE2 ArIsError).
/// </summary>
public sealed class FBitWriter
{
    private readonly byte[] _buffer;
    private int _num;
    private int _max;

    public FBitWriter(int maxBits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBits);
        _max = maxBits;
        _buffer = new byte[(maxBits + 7) >> 3];
    }

    /// <summary>Bits written so far.</summary>
    public int NumBits => _num;

    /// <summary>Bytes covered by the bits written so far.</summary>
    public int NumBytes => (_num + 7) >> 3;

    /// <summary>Current bit limit (at most the capacity given to the constructor).</summary>
    public int MaxBits => _max;

    /// <summary>Bit capacity of the underlying buffer.</summary>
    public int CapacityBits => _buffer.Length << 3;

    public int BitsLeft => _max - _num;

    /// <summary>Set when a write did not fit. Cleared by <see cref="Reset"/>.</summary>
    public bool IsError { get; private set; }

    /// <summary>The underlying buffer. Only the first <see cref="NumBytes"/> bytes are meaningful.</summary>
    public byte[] Buffer => _buffer;

    public ReadOnlySpan<byte> Data => _buffer.AsSpan(0, NumBytes);

    /// <summary>Empties the writer and clears the error flag. The bit limit is kept.</summary>
    public void Reset()
    {
        Array.Clear(_buffer, 0, NumBytes);
        _num = 0;
        IsError = false;
    }

    /// <summary>
    /// Takes back everything written after bit <paramref name="numBits"/> and clears the error flag, so a value that
    /// did not fit can be withdrawn (UE2 ReplicateActor takes the last property back when a bunch overflows).
    /// </summary>
    public void Rewind(int numBits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numBits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numBits, _num);
        int firstByte = numBits >> 3;
        int keep = numBits & 7;
        if (keep != 0)
        {
            _buffer[firstByte] &= (byte)((1 << keep) - 1);
            firstByte++;
        }

        int endByte = NumBytes;
        if (endByte > firstByte)
        {
            Array.Clear(_buffer, firstByte, endByte - firstByte);
        }

        _num = numBits;
        IsError = false;
    }

    /// <summary>
    /// The UE2 compressed vector (FVector::SerializeCompressed as used by UStructProperty::NetSerializeItem): the
    /// components are already rounded to whole units; one shared bit count (1 to 20, sent as 0..19) then each
    /// component biased by 2^bits. Components beyond 20 bits are clamped.
    /// </summary>
    [UcNative("UStructProperty::NetSerializeItem", Exe = 0x109567e0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 17: vectors rounded, up to 20 bits per component")]
    public void WriteRoundedVector(int x, int y, int z)
    {
        x = ClampVectorComponent(x);
        y = ClampVectorComponent(y);
        z = ClampVectorComponent(z);
        int max = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        int bits = Math.Clamp(CeilLogTwo((uint)max + 1u), 1, MaxRoundedVectorBits);
        WriteInt((uint)(bits - 1), MaxRoundedVectorBits);
        uint bias = 1u << bits;
        uint limit = 1u << (bits + 1);
        WriteInt((uint)(x + (int)bias), limit);
        WriteInt((uint)(y + (int)bias), limit);
        WriteInt((uint)(z + (int)bias), limit);
    }

    /// <summary>Largest bit count per component of <see cref="WriteRoundedVector"/>.</summary>
    public const int MaxRoundedVectorBits = 20;

    /// <summary>Clamps a rounded vector component into what <see cref="WriteRoundedVector"/> can carry.</summary>
    public static int ClampVectorComponent(int value)
    {
        const int limit = (1 << MaxRoundedVectorBits) - 1;
        return value > limit ? limit : value < -limit ? -limit : value;
    }

    /// <summary>Lowers or restores the bit limit, up to the buffer capacity.</summary>
    public void SetMaxBits(int maxBits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxBits, CapacityBits);
        _max = maxBits;
    }

    private bool Reserve(int bits)
    {
        if (IsError || bits > _max - _num)
        {
            IsError = true;
            return false;
        }

        return true;
    }

    public void WriteBit(bool value)
    {
        if (!Reserve(1))
        {
            return;
        }

        if (value)
        {
            _buffer[_num >> 3] |= (byte)(1 << (_num & 7));
        }

        _num++;
    }

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/> (0 to 64), low bit first.</summary>
    public void WriteBits(ulong value, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 64);
        if (!Reserve(count))
        {
            return;
        }

        int num = _num;
        while (count > 0)
        {
            int bitOffset = num & 7;
            int take = 8 - bitOffset;
            if (take > count)
            {
                take = count;
            }

            uint chunk = (uint)(value & ((1UL << take) - 1));
            _buffer[num >> 3] |= (byte)(chunk << bitOffset);
            value >>= take;
            count -= take;
            num += take;
        }

        _num = num;
    }

    /// <summary>
    /// Writes <paramref name="value"/> in the range [0, <paramref name="valueMax"/>) with the UE2 SerializeInt
    /// rule: bits go out low first while the value read so far plus the next bit could still be below the
    /// maximum, so a maximum that is not a power of two can save the top bit.
    /// </summary>
    [UcNative("FBitWriter::WriteInt")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 FBitWriter::WriteInt bit loop")]
    public void WriteInt(uint value, uint valueMax)
    {
        ArgumentOutOfRangeException.ThrowIfZero(valueMax);
        if (value >= valueMax)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be below valueMax.");
        }

        int bits = CountIntBits(value, valueMax);
        if (!Reserve(bits))
        {
            return;
        }

        ulong newValue = 0;
        for (ulong mask = 1; newValue + mask < valueMax && mask <= uint.MaxValue; mask <<= 1)
        {
            if ((value & mask) != 0)
            {
                _buffer[_num >> 3] |= (byte)(1 << (_num & 7));
                newValue += mask;
            }

            _num++;
        }
    }

    /// <summary>Bits <see cref="WriteInt"/> uses for <paramref name="value"/>.</summary>
    public static int CountIntBits(uint value, uint valueMax)
    {
        int bits = 0;
        ulong newValue = 0;
        for (ulong mask = 1; newValue + mask < valueMax && mask <= uint.MaxValue; mask <<= 1)
        {
            if ((value & mask) != 0)
            {
                newValue += mask;
            }

            bits++;
        }

        return bits;
    }

    /// <summary>
    /// Writes a wrapping sequence number (UE2 WriteIntWrapped). <paramref name="valueMax"/> must be a power of
    /// two; only the low log2(valueMax) bits of <paramref name="value"/> are sent. Read it back with ReadInt.
    /// </summary>
    [UcNative("FBitWriter::WriteIntWrapped")]
    public void WriteIntWrapped(uint value, uint valueMax)
    {
        if (valueMax < 2 || (valueMax & (valueMax - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(valueMax), valueMax, "valueMax must be a power of two.");
        }

        WriteBits(value & (valueMax - 1), CeilLogTwo(valueMax));
    }

    public void WriteByte(byte value) => WriteBits(value, 8);

    public void WriteUInt16(ushort value) => WriteBits(value, 16);

    public void WriteUInt32(uint value) => WriteBits(value, 32);

    public void WriteInt32(int value) => WriteBits((uint)value, 32);

    public void WriteUInt64(ulong value) => WriteBits(value, 64);

    /// <summary>Raw IEEE bits, no quantisation.</summary>
    public void WriteFloat(float value) => WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    /// <summary>Variable length unsigned int: groups of 7 bits, each preceded by a "more follows" bit.</summary>
    public void WriteIntPacked(uint value)
    {
        int groups = 1;
        for (uint v = value >> 7; v != 0; v >>= 7)
        {
            groups++;
        }

        if (!Reserve(groups * 8))
        {
            return;
        }

        do
        {
            uint group = value & 0x7F;
            value >>= 7;
            WriteBit(value != 0);
            WriteBits(group, 7);
        }
        while (value != 0);
    }

    /// <summary>Variable length signed int (zigzag, then <see cref="WriteIntPacked"/>).</summary>
    public void WriteSignedPacked(int value) => WriteIntPacked((uint)((value << 1) ^ (value >> 31)));

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (!Reserve(bytes.Length * 8))
        {
            return;
        }

        if ((_num & 7) == 0)
        {
            bytes.CopyTo(_buffer.AsSpan(_num >> 3));
            _num += bytes.Length * 8;
            return;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            WriteBits(bytes[i], 8);
        }
    }

    /// <summary>Appends the first <paramref name="bitCount"/> bits of <paramref name="source"/>.</summary>
    public void AppendBits(ReadOnlySpan<byte> source, int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        if (!Reserve(bitCount))
        {
            return;
        }

        int whole = bitCount >> 3;
        if ((_num & 7) == 0)
        {
            source[..whole].CopyTo(_buffer.AsSpan(_num >> 3));
            _num += whole * 8;
        }
        else
        {
            for (int i = 0; i < whole; i++)
            {
                WriteBits(source[i], 8);
            }
        }

        int rest = bitCount & 7;
        if (rest != 0)
        {
            WriteBits(source[whole], rest);
        }
    }

    /// <summary>Appends everything another writer holds.</summary>
    public void AppendBits(FBitWriter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        AppendBits(other._buffer, other._num);
    }

    /// <summary>UTF-8 text with a packed byte count. Null is sent as the empty string.</summary>
    public void WriteString(string? text, int maxBytes = 1024)
    {
        text ??= string.Empty;
        int count = Encoding.UTF8.GetByteCount(text);
        if (count > maxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(text), count, "Text is longer than maxBytes.");
        }

        Span<byte> bytes = count <= 256 ? stackalloc byte[count] : new byte[count];
        Encoding.UTF8.GetBytes(text, bytes);
        WriteIntPacked((uint)count);
        WriteBytes(bytes);
    }

    /// <summary>Writes a float mapped linearly onto <paramref name="bits"/> bits over [min, max], clamped.</summary>
    public void WriteQuantizedFloat(float value, float min, float max, int bits) =>
        WriteBits(NetQuantize.QuantizeFloat(value, min, max, bits), bits);

    /// <summary>
    /// Writes a vector already quantised to ints (see <see cref="NetQuantize.QuantizeVector"/>): a shared bit
    /// count (enough for the largest component plus a sign) followed by each component biased to unsigned.
    /// </summary>
    public void WritePackedVector(int qx, int qy, int qz, int maxBitsPerComponent = NetQuantize.DefaultVectorBits)
    {
        if (maxBitsPerComponent is < 2 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBitsPerComponent));
        }

        int bits = 1 + Math.Max(BitLength(qx), Math.Max(BitLength(qy), BitLength(qz)));
        if (bits > maxBitsPerComponent)
        {
            throw new ArgumentOutOfRangeException(nameof(qx), "Quantised component does not fit maxBitsPerComponent.");
        }

        if (!Reserve(CountIntBits((uint)(bits - 1), (uint)maxBitsPerComponent) + 3 * bits))
        {
            return;
        }

        WriteInt((uint)(bits - 1), (uint)maxBitsPerComponent);
        long bias = 1L << (bits - 1);
        WriteBits((ulong)(qx + bias), bits);
        WriteBits((ulong)(qy + bias), bits);
        WriteBits((ulong)(qz + bias), bits);
    }

    /// <summary>Quantises <paramref name="value"/> to 1/<paramref name="scale"/> uu and packs it.</summary>
    public void WritePackedVector(FVector value, int scale = NetQuantize.DefaultVectorScale,
        int maxBitsPerComponent = NetQuantize.DefaultVectorBits)
    {
        NetQuantize.QuantizeVector(value, scale, maxBitsPerComponent, out int qx, out int qy, out int qz);
        WritePackedVector(qx, qy, qz, maxBitsPerComponent);
    }

    /// <summary>
    /// Compressed rotator with 16 bits per axis: each axis is sent as a "non-zero" bit and, when set, its low 16
    /// bits. Same layout as the UE2 compressed rotator, which sends the high byte instead (see
    /// <see cref="WriteCompressedRotatorByte"/>).
    /// </summary>
    public void WriteCompressedRotator(FRotator value)
    {
        WriteCompressedAxis16(value.Pitch);
        WriteCompressedAxis16(value.Yaw);
        WriteCompressedAxis16(value.Roll);
    }

    /// <summary>The UE2 compressed rotator: per axis a "non-zero" bit and the axis high byte.</summary>
    [UcNative("FRotator::SerializeCompressed")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 FRotator::SerializeCompressed (high byte per axis)")]
    public void WriteCompressedRotatorByte(FRotator value)
    {
        WriteCompressedAxis8(value.Pitch);
        WriteCompressedAxis8(value.Yaw);
        WriteCompressedAxis8(value.Roll);
    }

    private void WriteCompressedAxis16(int axis)
    {
        uint s = (uint)axis & 0xFFFF;
        WriteBit(s != 0);
        if (s != 0)
        {
            WriteBits(s, 16);
        }
    }

    private void WriteCompressedAxis8(int axis)
    {
        uint b = ((uint)axis >> 8) & 0xFF;
        WriteBit(b != 0);
        if (b != 0)
        {
            WriteBits(b, 8);
        }
    }

    /// <summary>Bits needed for the magnitude of <paramref name="value"/> (0 for 0).</summary>
    internal static int BitLength(int value)
    {
        uint magnitude = value < 0 ? (uint)-(long)value : (uint)value;
        return 32 - System.Numerics.BitOperations.LeadingZeroCount(magnitude);
    }

    /// <summary>Smallest n with 2^n &gt;= value (UE2 appCeilLogTwo).</summary>
    public static int CeilLogTwo(uint value) =>
        value <= 1 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount(value - 1);
}
