using System;
using System.Threading;

namespace OpenScct.Net;

/// <summary>
/// Fixed-size single-producer single-consumer queue of datagrams. Lock-free and allocation-free after
/// construction: every slot owns a buffer of the largest datagram size. A full ring drops new datagrams, as a
/// full socket buffer would.
/// </summary>
internal sealed class DatagramRing
{
    private readonly byte[][] _slots;
    private readonly int[] _lengths;
    private readonly int _mask;
    private long _head;
    private long _tail;

    public DatagramRing(int capacity, int maxDatagramSize)
    {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
        }

        _slots = new byte[capacity][];
        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new byte[maxDatagramSize];
        }

        _lengths = new int[capacity];
        _mask = capacity - 1;
        MaxDatagramSize = maxDatagramSize;
    }

    public int MaxDatagramSize { get; }

    /// <summary>Datagrams dropped because the ring was full.</summary>
    public long Dropped;

    /// <summary>Producer side.</summary>
    public bool TryWrite(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length > MaxDatagramSize)
        {
            return false;
        }

        long head = _head;
        if (head - Volatile.Read(ref _tail) > _mask)
        {
            Dropped++;
            return false;
        }

        int slot = (int)(head & _mask);
        datagram.CopyTo(_slots[slot]);
        _lengths[slot] = datagram.Length;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>Consumer side.</summary>
    public bool TryRead(Span<byte> destination, out int length)
    {
        long tail = _tail;
        if (tail == Volatile.Read(ref _head))
        {
            length = 0;
            return false;
        }

        int slot = (int)(tail & _mask);
        length = _lengths[slot];
        _slots[slot].AsSpan(0, length).CopyTo(destination);
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}
