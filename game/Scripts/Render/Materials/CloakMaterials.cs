using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>Single-pass cloak materials, retaining each surface's textures and shading during the fade.</summary>
public sealed class CloakMaterials
{
    public const string ShaderPath = "res://Shaders/svm_cloak.gdshader";
    public static readonly StringName GlassParameter = "svm_glass";
    private static readonly Dictionary<Shader, Shader> s_shaders = [];
    private readonly Dictionary<Material, ShaderMaterial> _cloaked = [];
    private readonly Dictionary<Material, Material> _original = [];
    private float _amount;

    public void SetAmount(float amount)
    {
        _amount = amount;
        foreach (ShaderMaterial material in _cloaked.Values) material.SetShaderParameter(GlassParameter, amount);
    }

    public Material Original(Material material) => _original.GetValueOrDefault(material, material);

    public Material Get(Material material)
    {
        material = Original(material);
        if (_cloaked.TryGetValue(material, out ShaderMaterial? cached)) return cached;

        ShaderMaterial cloak;
        if (material is ShaderMaterial faithful && faithful.Shader is { } shader &&
            shader.Code.Contains(MaterialCompiler.TemplatePath, StringComparison.Ordinal))
        {
            if (!s_shaders.TryGetValue(shader, out Shader? variant))
            {
                string code = shader.Code;
                int start = code.IndexOf("render_mode ", StringComparison.Ordinal);
                int end = code.IndexOf(';', start);
                string cull = code[start..end].Contains("cull_disabled", StringComparison.Ordinal) ? "cull_disabled" : "cull_back";
                // All surfaces write their own nearest depth, including split cosmetics and attachments. Sampling
                // the screen puts this in the transparent pass; ALPHA=1 prevents a second blend with the body.
                code = code[..start] + $"render_mode unshaded, fog_disabled, {cull}, blend_mix, depth_draw_always" + code[end..];
                code = code.Replace("#include", "#define SVM_CLOAK\n#include", StringComparison.Ordinal);
                variant = MaterialCompiler.SharedShader(code, shader.ResourceName + " cloak");
                s_shaders[shader] = variant;
            }

            cloak = (ShaderMaterial)faithful.Duplicate();
            cloak.Shader = variant;
        }
        else
        {
            // Imported materials without Faithful metadata still cloak as a whole, rather than keeping an
            // opaque base pass. Converted game characters use the full actor-lit variant above.
            cloak = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
            if (material is BaseMaterial3D imported)
            {
                cloak.SetShaderParameter("base_color", imported.AlbedoColor);
                if (imported.AlbedoTexture is { } texture) cloak.SetShaderParameter("tex_diffuse", texture);
                cloak.SetShaderParameter("masked", imported.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor);
            }
        }

        cloak.NextPass = null;
        cloak.SetShaderParameter(GlassParameter, _amount);
        _cloaked[material] = cloak;
        _original[cloak] = material;
        return cloak;
    }
}
