using OpenScct.Core.Engine;

namespace OpenScct.Content.Actors;

/// <summary>
/// Marker for a placed actor whose class has no C# class (generated or ported). It keeps the original class, the map
/// record and the class defaults of the map's class table, so the actor still has its place in the level (id, tag,
/// location, the Actor fields its class sets) and later code can read its full state; it has no behaviour.
/// </summary>
public sealed class UnportedActor : Actor
{
    /// <summary>The original <c>Package.Class</c>.</summary>
    public string ClassPath { get; internal set; } = string.Empty;

    /// <summary>The map record this actor was created from.</summary>
    public ActorRecord? Record { get; internal set; }

    /// <summary>The effective defaults of its class from <c>sim/classes.json</c>, or null when the map has none.</summary>
    public PropBag? ClassDefaults { get; internal set; }

    /// <summary>
    /// The effective value of one original property: the record's value overlaid on the class default
    /// (<see cref="ClassTable.Overlay"/>), else the class default; null when neither has one.
    /// </summary>
    public PropValue? GetEffective(string name)
    {
        PropValue value = default;
        bool stored = Record is not null && Record.Props.TryGetValue(name, out value);
        if (ClassDefaults is not null && ClassDefaults.TryGetValue(name, out PropValue d))
        {
            return stored ? ClassTable.Overlay(d, value) : d;
        }

        return stored ? value : null;
    }

    public override string ToString() => $"{ClassPath}#{ActorId}";
}
