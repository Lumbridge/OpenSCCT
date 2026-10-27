using System;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// A uniform list for <c>UniformSetCacheRD.GetCache</c> kept from frame to frame: the <see cref="RDUniform"/>
/// objects and their array are made once and only their ids change. Making them in every render callback created
/// a dozen finalizable wrappers a frame for the garbage collector.
/// </summary>
public sealed class UniformSlots
{
    private readonly RDUniform[] _uniforms;
    private readonly Rid[] _first;
    private readonly Rid[] _second;
    private readonly int[] _count;

    /// <summary>One slot per binding, in order: binding i has type <paramref name="types"/>[i].</summary>
    public UniformSlots(params RenderingDevice.UniformType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        _uniforms = new RDUniform[types.Length];
        _first = new Rid[types.Length];
        _second = new Rid[types.Length];
        _count = new int[types.Length];
        Array = [];
        for (int i = 0; i < types.Length; i++)
        {
            _uniforms[i] = new RDUniform { UniformType = types[i], Binding = i };
            Array.Add(_uniforms[i]);
        }
    }

    /// <summary>The uniforms, for the uniform set cache.</summary>
    public Godot.Collections.Array<RDUniform> Array { get; }

    /// <summary>Slot <paramref name="binding"/> reads one id (an image or a buffer).</summary>
    public void Set(int binding, Rid id) => Set(binding, id, default, 1);

    /// <summary>Slot <paramref name="binding"/> reads a sampler and a texture.</summary>
    public void Set(int binding, Rid sampler, Rid texture) => Set(binding, sampler, texture, 2);

    private void Set(int binding, Rid first, Rid second, int count)
    {
        if (_first[binding] == first && _second[binding] == second && _count[binding] == count)
        {
            return;
        }

        RDUniform u = _uniforms[binding];
        u.ClearIds();
        u.AddId(first);
        if (count == 2)
        {
            u.AddId(second);
        }

        _first[binding] = first;
        _second[binding] = second;
        _count[binding] = count;
    }
}
