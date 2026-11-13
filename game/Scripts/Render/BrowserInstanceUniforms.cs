using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using Godot;

namespace OpenScctGame.Render;

/// <summary>WebGPU compatibility path: keep an actor's uniforms on its own materials when instance uploads are unavailable.</summary>
internal static class BrowserInstanceUniforms
{
    private sealed class Copies
    {
        public readonly Dictionary<ShaderMaterial, ShaderMaterial> Materials = new();
        public readonly HashSet<ShaderMaterial> Owned = new();
    }
    private static readonly ConditionalWeakTable<GeometryInstance3D, Copies> Nodes = new();
    private static readonly ConditionalWeakTable<Shader, Shader> Shaders = new();
    private static readonly ConditionalWeakTable<Material, Material> Sources = new();
    private static readonly ConditionalWeakTable<ShaderMaterial, List<WeakReference<ShaderMaterial>>> Dependents = new();

    internal static Material Source(Material material)
    {
        while (Sources.TryGetValue(material, out Material? source)) material = source;
        return material;
    }

    internal static void SetMaterialParameter(ShaderMaterial material, StringName name, Variant value)
    {
        material.SetShaderParameter(name, value);
        if (!Dependents.TryGetValue(material, out var copies)) return;
        for (int i = copies.Count - 1; i >= 0; i--)
        {
            if (copies[i].TryGetTarget(out var copy) && GodotObject.IsInstanceValid(copy)) SetMaterialParameter(copy, name, value);
            else copies.RemoveAt(i);
        }
    }

    internal static void Apply(GeometryInstance3D node, Dictionary<StringName, Variant> values, StringName? changed = null)
    {
        var copies = Nodes.GetOrCreateValue(node);
        Material? ConvertMaterial(Material? source)
        {
            if (source is not ShaderMaterial material || material.Shader is not { } shader) return source;
            bool fresh = false;
            if (!copies.Owned.Contains(material))
            {
                if (!copies.Materials.TryGetValue(material, out var copy))
                {
                    copy = (ShaderMaterial)material.Duplicate();
                    copy.Shader = Shaders.GetValue(shader, original =>
                    {
                        // Godot preprocesses includes before parsing shader declarations. An empty macro turns
                        // each `instance uniform` into a material uniform, retaining the same GLSL calculations.
                        var variant = (Shader)original.Duplicate();
                        variant.Code = "#define instance\n" + original.Code;
                        variant.ResourceName = original.ResourceName + " browser material uniforms";
                        return variant;
                    });
                    Sources.Add(copy, material);
                    Dependents.GetOrCreateValue(material).Add(new(copy));
                    copies.Materials[material] = copy;
                    copies.Owned.Add(copy);
                    fresh = true;
                }
                material = copy;
            }
            foreach (var pair in values)
                if (fresh || changed is null || changed == pair.Key) material.SetShaderParameter(pair.Key, pair.Value);
            if (material.NextPass is { } next) material.NextPass = ConvertMaterial(next);
            return material;
        }
        if (node.MaterialOverride is { } over) node.MaterialOverride = ConvertMaterial(over);
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                instance.SetSurfaceOverrideMaterial(surface, ConvertMaterial(instance.GetSurfaceOverrideMaterial(surface) ?? mesh.SurfaceGetMaterial(surface)));
    }
}
