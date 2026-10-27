using System;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>
/// The default animation set of one skeletal mesh that actors use (SVMap 1.1 <c>animations</c>): the MeshAnimation
/// and its sequences with frame counts, rates and notifies. The server needs the notifies: AnimNotify_Trigger fires
/// events (the SteeE crane).
/// </summary>
public sealed class AnimationSet
{
    private readonly Dictionary<string, int> _byName;
    private readonly Dictionary<string, int> _byNameCaseless;

    public AnimationSet(string mesh, string animation, IReadOnlyList<AnimSequenceInfo> sequences)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(sequences);
        Mesh = mesh;
        Animation = animation;
        Sequences = sequences;
        _byName = new Dictionary<string, int>(sequences.Count, StringComparer.Ordinal);
        _byNameCaseless = new Dictionary<string, int>(sequences.Count, AsciiCaseComparer.Instance);
        for (int i = 0; i < sequences.Count; i++)
        {
            _byName.TryAdd(sequences[i].Name, i);
            _byNameCaseless.TryAdd(sequences[i].Name, i);
        }
    }

    /// <summary>Asset path of the skeletal mesh (the key in <c>animations</c>).</summary>
    public string Mesh { get; }

    /// <summary>Asset path of the MeshAnimation.</summary>
    public string Animation { get; }

    /// <summary>Sequences in document order.</summary>
    public IReadOnlyList<AnimSequenceInfo> Sequences { get; }

    /// <summary>A sequence by name, ignoring ASCII case as UE2 names do.</summary>
    public AnimSequenceInfo? FindSequence(string name) => _byNameCaseless.TryGetValue(name, out int i) ? Sequences[i] : null;

    /// <summary>A sequence by its exact spelling, as the derived event edges look it up.</summary>
    internal AnimSequenceInfo? FindSequenceExact(string name) => _byName.TryGetValue(name, out int i) ? Sequences[i] : null;
}

/// <summary>One animation sequence: <see cref="Frames"/> frames at <see cref="Rate"/> frames per second at play rate 1.</summary>
public sealed record AnimSequenceInfo(string Name, int Frames, float Rate, IReadOnlyList<AnimNotifyInfo> Notifies)
{
    /// <summary>The first frame in the animation's track data, when stored.</summary>
    public int? StartFrame { get; init; }

    /// <summary>The sequence's groups, when stored.</summary>
    public IReadOnlyList<string>? Groups { get; init; }

    /// <summary>Length of a one-shot play at rate 1: <c>(frames - 1) / rate</c> seconds (float32), 0 when the rate is not positive.</summary>
    public float OneShotSeconds => Rate > 0f && Frames > 0 ? (Frames - 1) / Rate : 0f;
}

/// <summary>
/// One notify of a sequence: its normalized time (0 to 1), and the notify object (path, class and typed props such
/// as <c>AnimNotify_Trigger.EventName</c>). Members the document does not store are null.
/// </summary>
public sealed record AnimNotifyInfo(float Time, PropBag Props)
{
    /// <summary>The script function a notify calls (<c>None</c> for notify objects).</summary>
    public string? Function { get; init; }

    /// <summary>Asset path of the notify object.</summary>
    public string? Object { get; init; }

    /// <summary>Class of the notify object (<c>Engine.AnimNotify_Trigger</c>, <c>Engine.AnimNotify_Sound</c>...).</summary>
    public string? Class { get; init; }

    /// <summary>False when the document stores no <c>props</c> member (then <see cref="Props"/> is empty).</summary>
    public bool HasProps { get; init; } = true;

    /// <summary>
    /// Seconds into a sequence of <paramref name="frames"/> frames at <paramref name="rate"/> at which this notify
    /// fires at play rate 1, in float32: <c>(time * frames) / rate</c>, 0 when the rate is not positive
    /// (spec/svmap/1.1/README.md, notify edges).
    /// </summary>
    public float SecondsInto(int frames, float rate) => NotifySeconds(Time, frames, rate);

    /// <inheritdoc cref="SecondsInto"/>
    public static float NotifySeconds(float time, int frames, float rate)
    {
        if (!(rate > 0f))
        {
            return 0f;
        }

        float scaled = time * (float)frames;
        return scaled / rate;
    }
}
