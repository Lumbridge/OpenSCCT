using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>
/// Allocation-free iteration over level actors of type <typeparamref name="T"/> in list order (UnrealScript
/// AllActors / DynamicActors). Destroyed actors are skipped. Use it in a foreach.
/// </summary>
public struct ActorIterator<T>
    where T : Actor
{
    private readonly List<Actor>? _list;
    private readonly Name _tag;
    private readonly bool _dynamicOnly;
    private int _index;
    private T? _current;

    internal ActorIterator(List<Actor>? list, Name tag, bool dynamicOnly)
    {
        _list = list;
        _tag = tag;
        _dynamicOnly = dynamicOnly;
        _index = -1;
        _current = null;
    }

    public readonly ActorIterator<T> GetEnumerator() => this;

    public readonly T Current => _current!;

    public bool MoveNext()
    {
        List<Actor>? list = _list;
        if (list is null)
        {
            return false;
        }

        while (++_index < list.Count)
        {
            Actor a = list[_index];
            if (a.bDeleteMe || (_dynamicOnly && a.bStatic) || (!_tag.IsNone && a.Tag != _tag))
            {
                continue;
            }

            if (a is T t)
            {
                _current = t;
                return true;
            }
        }

        _current = null;
        return false;
    }

    /// <summary>Counts the remaining matches (consumes a copy, not this iterator).</summary>
    public readonly int Count()
    {
        ActorIterator<T> copy = this;
        int n = 0;
        while (copy.MoveNext())
        {
            n++;
        }

        return n;
    }
}
