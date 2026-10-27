using System;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>
/// A property value naming an actor of the same map (<c>{"$actor": id}</c>). The target is looked up on first use,
/// so references may point forward in the actor list. Ids match ignoring ASCII case, as UE2 names do.
/// </summary>
public sealed class ActorRef
{
    private static readonly object s_missing = new();

    private readonly ActorIndex? _index;
    private object? _target;

    /// <summary>A reference that is not attached to a document (for authoring); <see cref="Target"/> is null.</summary>
    public ActorRef(string id)
        : this(id, null)
    {
    }

    internal ActorRef(string id, ActorIndex? index)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        _index = index;
    }

    /// <summary>The referenced actor's id as written.</summary>
    public string Id { get; }

    /// <summary>The referenced actor, or null when no actor of the document has the id.</summary>
    public ActorRecord? Target
    {
        get
        {
            object? cached = _target;
            if (cached is null)
            {
                cached = (object?)_index?.Find(Id) ?? s_missing;
                _target = cached;
            }

            return cached as ActorRecord;
        }
    }

    /// <summary>True when the id names no actor.</summary>
    public bool IsDangling => Target is null;

    public override string ToString() => Id;
}

/// <summary>Id lookup shared by a document's actor references; filled once every record exists.</summary>
internal sealed class ActorIndex
{
    private readonly Dictionary<string, ActorRecord> _byId = new(AsciiCaseComparer.Instance);
    private readonly Dictionary<string, ActorRecord> _byExactId = new(StringComparer.Ordinal);

    public int Count => _byId.Count;

    public bool TryAdd(ActorRecord record)
    {
        if (!_byId.TryAdd(record.Id, record))
        {
            return false;
        }

        _byExactId[record.Id] = record;
        return true;
    }

    public ActorRecord? Find(string id) => _byId.GetValueOrDefault(id);

    public ActorRecord? FindExact(string id) => _byExactId.GetValueOrDefault(id);
}
