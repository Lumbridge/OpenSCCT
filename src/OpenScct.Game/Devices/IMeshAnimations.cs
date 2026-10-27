using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Game.Devices;

/// <summary>
/// The animation sequences a mesh can play: frame count, rate and notifies. Animated props (SAnimatedMesh) read it
/// as the level service <c>Level.GetService&lt;IMeshAnimations&gt;()</c>; the content side fills it from the
/// converted animation packages. Without the service, or for an unknown sequence, PlayAnim does nothing.
/// </summary>
public interface IMeshAnimations
{
    /// <summary>The sequence <paramref name="Sequence"/> of the animations <paramref name="Mesh"/> uses, or null.</summary>
    MeshAnimSequence? FindSequence(AssetRef Mesh, Name Sequence);
}

/// <summary>One notify of a sequence: its normalized time in [0, 1] over the frame count, and the notify object.</summary>
public readonly record struct MeshAnimNotify(float Time, UAnimNotify Notify);

/// <summary>An animation sequence: <see cref="NumFrames"/> frames played at <see cref="Rate"/> frames per second.</summary>
public sealed class MeshAnimSequence
{
    public MeshAnimSequence(Name name, int numFrames, float rate, IReadOnlyList<MeshAnimNotify>? notifies = null)
    {
        if (numFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(numFrames), "A sequence has at least one frame.");
        }

        if (!(rate > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "A sequence rate must be positive.");
        }

        Name = name;
        NumFrames = numFrames;
        Rate = rate;
        Notifies = notifies ?? [];
    }

    public Name Name { get; }

    public int NumFrames { get; }

    /// <summary>Frames per second at play rate 1.</summary>
    public float Rate { get; }

    /// <summary>Notifies in package order (firing follows time; ties keep this order).</summary>
    public IReadOnlyList<MeshAnimNotify> Notifies { get; }
}

/// <summary>An in-memory <see cref="IMeshAnimations"/>: sequences registered per mesh.</summary>
public sealed class MeshAnimations : IMeshAnimations
{
    private readonly Dictionary<(AssetRef Mesh, Name Sequence), MeshAnimSequence> _sequences = new();

    /// <summary>Adds (or replaces) a sequence for <paramref name="mesh"/>.</summary>
    public MeshAnimations Add(AssetRef mesh, MeshAnimSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        _sequences[(mesh, sequence.Name)] = sequence;
        return this;
    }

    public MeshAnimSequence? FindSequence(AssetRef Mesh, Name Sequence) =>
        _sequences.GetValueOrDefault((Mesh, Sequence));
}
