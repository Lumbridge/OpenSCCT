using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Projectors;

/// <summary>
/// Adds projection to the existing opaque/masked actor draw. Material objects retain their live uniforms and
/// sequence clocks. Materials also used by fixed cells get an instance-local copy, keeping static decals single.
/// </summary>
internal sealed class MovingProjectorReceivers(HashSet<Material> fixedMaterials) : IDisposable
{
    internal const string Define = "SVM_G16_RECEIVER";
    internal const string LiveParameterMeta = "svm_g16_live_parameters";
    private sealed record Binding(Shader Original, Shader Projected);
    private sealed class Slot(Material original, Material? overridden, ShaderMaterial copy)
    {
        public Material Original = original;
        public Material? Override = overridden;
        public ShaderMaterial Copy = copy;
        public Shader? SourceShader;
        public readonly List<StringName> Uniforms = [];
        public readonly Dictionary<StringName, Variant> Values = [];
    }
    private readonly Dictionary<ShaderMaterial, Binding> _bindings = [];
    private readonly Dictionary<(MeshInstance3D Node, int Surface), Slot> _copies = [];
    private readonly HashSet<ShaderMaterial> _used = [];
    private readonly List<ShaderMaterial> _unused = [];
    private readonly List<(MeshInstance3D Node, int Surface)> _retired = [];

    internal static bool Supported(ShaderMaterial material)
    {
        if (material.Shader is not { } shader) return false;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        return code.Contains(MaterialCompiler.TemplatePath, StringComparison.Ordinal) &&
            !code.Contains("#define SVM_PROJECTOR_DECAL", StringComparison.Ordinal) &&
            !code.Contains("#define SVM_CLOAK", StringComparison.Ordinal) &&
            !code.Contains("#define VISION_SEE_THROUGH", StringComparison.Ordinal) &&
            !code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) &&
            !code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) &&
            !code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) &&
            !code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
    }

    internal static Shader ProjectedShader(Shader source)
    {
        string code = ResponsiveSmokeShaders.BaseCode(source);
        if (!code.Contains("#define " + Define, StringComparison.Ordinal))
            code = code.Replace("#include \"" + MaterialCompiler.TemplatePath + "\"",
                "#define " + Define + "\n#include \"" + MaterialCompiler.TemplatePath + "\"", StringComparison.Ordinal);
        return ResponsiveSmokeShaders.Shared(code, source.ResourceName + " G16 receiver");
    }

    public int Count => _used.Count;
    public bool UnsupportedReceiver { get; private set; }

    public void Update(IReadOnlyList<MeshInstance3D> nodes, World3D world, Aabb bounds, bool enabled)
    {
        _used.Clear(); UnsupportedReceiver = false;
        foreach (MeshInstance3D node in nodes)
        {
            if (!enabled || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.IsVisibleInTree() ||
                (node.Layers & (RenderLayers.World | RenderLayers.FirstPerson)) == 0 || node.Mesh is not { } mesh ||
                node.GetWorld3D() != world || node.HasMeta("svm_fixed_cell")) continue;
            // Rest-pose bounds can omit an extended arm. Skinning is still performed by the original GPU draw.
            if (!bounds.Intersects((node.GlobalTransform * mesh.GetAabb()).Grow(node.Skin is null ? .05f : 1.5f))) continue;
            if (node.MaterialOverride is { }) Apply(node, -1, node.MaterialOverride);
            else for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                Apply(node, surface, node.GetSurfaceOverrideMaterial(surface) ?? mesh.SurfaceGetMaterial(surface));
        }
        if (UnsupportedReceiver) _used.Clear();
        _unused.Clear();
        foreach ((ShaderMaterial material, Binding binding) in _bindings)
            if (!_used.Contains(material))
            {
                if (GodotObject.IsInstanceValid(material) && material.Shader == binding.Projected) material.Shader = binding.Original;
                _unused.Add(material);
            }
        foreach (ShaderMaterial material in _unused) _bindings.Remove(material);
        _retired.Clear();
        foreach (var (key, slot) in _copies)
        {
            if (!GodotObject.IsInstanceValid(key.Node)) { _retired.Add(key); continue; }
            if (!_used.Contains(slot.Copy) && Current(key.Node, key.Surface) == slot.Copy)
                Set(key.Node, key.Surface, slot.Override);
        }
        foreach (var key in _retired) _copies.Remove(key);
    }

    private static Material? Current(MeshInstance3D node, int surface) => surface < 0 ? node.MaterialOverride :
        node.GetSurfaceOverrideMaterial(surface) ?? node.Mesh?.SurfaceGetMaterial(surface);
    private static void Set(MeshInstance3D node, int surface, Material? value)
    {
        if (surface < 0) node.MaterialOverride = value;
        else node.SetSurfaceOverrideMaterial(surface, value);
    }

    private void Apply(MeshInstance3D node, int surface, Material? drawing)
    {
        var key = (node, surface);
        _copies.TryGetValue(key, out Slot? slot);
        Material? source = slot is not null && drawing == slot.Copy ? slot.Original : drawing;
        if (source is not ShaderMaterial material)
        {
            // Unknown opaque/PBR materials need their original depth-buffer projection, rather than losing it.
            if (source is not BaseMaterial3D basic || basic.Transparency is BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.AlphaScissor)
                UnsupportedReceiver = true;
            return;
        }
        if (!Supported(material))
        {
            string code = material.Shader is { } shader ? ResponsiveSmokeShaders.BaseCode(shader) : "";
            if (!code.Contains(MaterialCompiler.TemplatePath, StringComparison.Ordinal) &&
                material.Shader?.ResourcePath != CloakMaterials.ShaderPath && !code.Contains("depth_draw_never", StringComparison.Ordinal))
                UnsupportedReceiver = true;
            return;
        }
        if (fixedMaterials.Contains(source))
        {
            if (slot is null || slot.Original != source)
            {
                Material? overridden = surface < 0 ? node.MaterialOverride : node.GetSurfaceOverrideMaterial(surface);
                slot = new Slot(source, overridden, (ShaderMaterial)material.Duplicate());
                slot.Copy.SetMeta(LiveParameterMeta, true);
                _copies[key] = slot;
            }
            // A fixed material may be shared with a moving object whose texture/opacity is animated. Read the
            // original, rather than advancing another sequence clock or freezing its copied parameters.
            if (slot.SourceShader != material.Shader)
            {
                slot.SourceShader = material.Shader; slot.Uniforms.Clear(); slot.Values.Clear();
                slot.Copy.Shader = material.Shader;
                foreach (var uniform in material.Shader!.GetShaderUniformList())
                    slot.Uniforms.Add(uniform.AsGodotDictionary()["name"].AsStringName());
            }
            slot.Copy.NextPass = material.NextPass;
            slot.Copy.RenderPriority = material.RenderPriority;
            foreach (StringName name in slot.Uniforms)
            {
                Variant value = material.GetShaderParameter(name);
                if (!slot.Values.TryGetValue(name, out Variant prior) || !prior.Equals(value))
                {
                    slot.Values[name] = value;
                    slot.Copy.SetShaderParameter(name, value);
                }
            }
            material = slot.Copy;
            Set(node, surface, material);
        }
        _used.Add(material);
        if (_bindings.TryGetValue(material, out Binding? known) && material.Shader == known.Projected) return;
        Shader original = material.Shader!;
        Shader projected = ProjectedShader(original);
        _bindings[material] = new Binding(original, projected);
        material.Shader = projected;
    }

    public void Dispose()
    {
        foreach ((ShaderMaterial material, Binding binding) in _bindings)
            if (GodotObject.IsInstanceValid(material) && material.Shader == binding.Projected) material.Shader = binding.Original;
        foreach (var (key, slot) in _copies)
            if (GodotObject.IsInstanceValid(key.Node) && Current(key.Node, key.Surface) == slot.Copy) Set(key.Node, key.Surface, slot.Override);
        _bindings.Clear(); _copies.Clear(); _used.Clear();
    }
}
