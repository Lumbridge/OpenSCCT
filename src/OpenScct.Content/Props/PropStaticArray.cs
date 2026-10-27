using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>
/// The stored elements of a static array property (<c>ArrayDim &gt; 1</c>), sorted by element index. Elements that
/// are not stored keep their class default.
/// </summary>
public sealed class PropStaticArray : IReadOnlyCollection<KeyValuePair<int, PropValue>>
{
    private readonly KeyValuePair<int, PropValue>[] _elements;

    public PropStaticArray(IEnumerable<KeyValuePair<int, PropValue>> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var list = new List<KeyValuePair<int, PropValue>>(elements);
        list.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Key < 0)
            {
                throw new ArgumentException($"Element index {list[i].Key} is negative.", nameof(elements));
            }

            if (i > 0 && list[i].Key == list[i - 1].Key)
            {
                throw new ArgumentException($"Element index {list[i].Key} is stored twice.", nameof(elements));
            }
        }

        _elements = list.ToArray();
    }

    /// <summary>Number of stored elements.</summary>
    public int Count => _elements.Length;

    /// <summary>The highest stored element index, or -1 when none is stored.</summary>
    public int MaxIndex => _elements.Length == 0 ? -1 : _elements[^1].Key;

    public bool Contains(int index) => Find(index) >= 0;

    public bool TryGet(int index, out PropValue value)
    {
        int at = Find(index);
        value = at >= 0 ? _elements[at].Value : default;
        return at >= 0;
    }

    /// <summary>The stored element at <paramref name="index"/>; throws when it is not stored.</summary>
    public PropValue Get(int index) =>
        TryGet(index, out PropValue v) ? v : throw new KeyNotFoundException($"Element {index} is not stored.");

    public bool ContentEquals(PropStaticArray other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._elements.Length != _elements.Length)
        {
            return false;
        }

        for (int i = 0; i < _elements.Length; i++)
        {
            if (_elements[i].Key != other._elements[i].Key || !_elements[i].Value.Equals(other._elements[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    public IEnumerator<KeyValuePair<int, PropValue>> GetEnumerator() => ((IEnumerable<KeyValuePair<int, PropValue>>)_elements).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int Find(int index)
    {
        int lo = 0;
        int hi = _elements.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int key = _elements[mid].Key;
            if (key == index)
            {
                return mid;
            }

            if (key < index)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }
}
