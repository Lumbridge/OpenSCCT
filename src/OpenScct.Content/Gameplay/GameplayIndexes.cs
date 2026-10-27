using System;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>What an event edge stands for (SVMap 1.1 <c>kind</c>; 1.0 documents have fire edges only).</summary>
public enum EventEdgeKind : byte
{
    /// <summary>The owner fires <see cref="EventEdge.Event"/> at the actors whose Tag matches (no <c>kind</c> stored).</summary>
    Fire,

    /// <summary>A sender filter: the owner reacts to its own Tag only when the sender is one of <see cref="EventEdge.To"/>.</summary>
    Accept,

    /// <summary>An animation notify of a sequence the owner plays fires the event.</summary>
    Notify,

    /// <summary>A kind this reader does not know (a later minor version).</summary>
    Other,
}

/// <summary>
/// One event edge: <see cref="From"/> fires <see cref="Event"/> through property <see cref="Via"/> at the actors in
/// <see cref="To"/>. SVMap 1.1 adds <see cref="Kind"/> (absent for fire edges) and, for notify edges, the sequence,
/// the time into it and the notify object.
/// </summary>
public sealed record EventEdge(string From, string Via, string Event, IReadOnlyList<string> To)
{
    public const string AcceptKind = "accept";
    public const string NotifyKind = "notify";

    /// <summary>The stored <c>kind</c>: null for a fire edge, <c>accept</c> or <c>notify</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Notify edges: the sequence name.</summary>
    public string? Seq { get; init; }

    /// <summary>Notify edges: seconds into the sequence at play rate 1 (float32).</summary>
    public float? Time { get; init; }

    /// <summary>Notify edges: the notify object's asset path.</summary>
    public string? Notify { get; init; }

    public EventEdgeKind EdgeKind => Kind switch
    {
        null => EventEdgeKind.Fire,
        AcceptKind => EventEdgeKind.Accept,
        NotifyKind => EventEdgeKind.Notify,
        _ => EventEdgeKind.Other,
    };

    /// <summary>Same members and values, exact spelling; times compare by their float bits.</summary>
    public bool ContentEquals(EventEdge other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return From == other.From && Via == other.Via && Event == other.Event && GameplayDoc.SameIds(To, other.To) &&
            Kind == other.Kind && Seq == other.Seq && Notify == other.Notify && SameTime(Time, other.Time);
    }

    public override string ToString()
    {
        string extra = Kind is null ? string.Empty : $" {Kind}";
        if (Seq is not null || Time is not null || Notify is not null)
        {
            extra += $" seq={Seq} time={(Time is float t ? Json.Float32Text.Format(t) : "-")} notify={Notify}";
        }

        return $"{From}.{Via} -> {Event} [{string.Join(", ", To)}]{extra}";
    }

    private static bool SameTime(float? a, float? b) =>
        a is null ? b is null : b is not null && BitConverter.SingleToInt32Bits(a.Value) == BitConverter.SingleToInt32Bits(b.Value);
}

/// <summary>An SObjective of a mission and the actors that complete it (its Triggers).</summary>
public sealed record ObjectiveNode(string Objective, IReadOnlyList<string> Triggers)
{
    public bool ContentEquals(ObjectiveNode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Objective == other.Objective && GameplayDoc.SameIds(Triggers, other.Triggers);
    }
}

/// <summary>
/// An SMission with its game mode, the objectives needed to win and its objectives. SVMap 1.1 adds the mission's
/// own <see cref="Triggers"/> (Disk Hunt missions list their disks there), stored only when there are some.
/// </summary>
public sealed record MissionNode(string Mission, int? GameMode, int? MinimumObjectives, IReadOnlyList<ObjectiveNode> Objectives)
{
    /// <summary>The mission's own Triggers (1.1), or null when none are stored.</summary>
    public IReadOnlyList<string>? Triggers { get; init; }

    public bool ContentEquals(MissionNode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Mission != other.Mission || GameMode != other.GameMode || MinimumObjectives != other.MinimumObjectives ||
            Objectives.Count != other.Objectives.Count)
        {
            return false;
        }

        if (Triggers is null ? other.Triggers is not null : other.Triggers is null || !GameplayDoc.SameIds(Triggers, other.Triggers))
        {
            return false;
        }

        for (int i = 0; i < Objectives.Count; i++)
        {
            if (!Objectives[i].ContentEquals(other.Objectives[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A tag of the tag index and the ids of the actors carrying it, in level order.</summary>
public sealed record TagEntry(string Tag, IReadOnlyList<string> Ids);
