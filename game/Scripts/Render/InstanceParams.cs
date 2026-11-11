using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace OpenScctGame.Render;

/// <summary>
/// The per-instance shader parameters the game sets on its meshes (lights, tint, glow hue, vision values), with a
/// record of each kept on this side. The engine keeps them in its rendering server, and reading one back is a call
/// that waits for the render thread: several of those a frame held the main thread for as long as the frame took to
/// draw whenever rendering had a thread of its own. Every write goes through <see cref="Set"/>, so <see cref="Get"/>
/// answers from the record.
/// </summary>
public static class InstanceParams
{
    private static readonly ConditionalWeakTable<GeometryInstance3D, Dictionary<StringName, Variant>> s_values = new();

    /// <summary>Sets an instance shader parameter on the node and records it.</summary>
    public static void Set(GeometryInstance3D node, StringName name, Variant value)
    {
        s_values.GetValue(node, static _ => [])[name] = value;
        node.SetInstanceShaderParameter(name, value);
    }

    /// <summary>The value last given to the node through <see cref="Set"/>, or nil (as the engine answers for one never set).</summary>
    public static Variant Get(GeometryInstance3D node, StringName name) =>
        s_values.TryGetValue(node, out Dictionary<StringName, Variant>? values) && values.TryGetValue(name, out Variant value) ? value : default;
}
