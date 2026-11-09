using System;
using System.Reflection;
using System.IO;
using Godot;
using OpenScct.Content.Materials;

namespace OpenScctGame.Render.Materials;

/// <summary>The shared shader and parameter bindings used by the game and the editor's material preview.</summary>
public static class AuthoredMaterialBinding
{
    public static string ShaderCode { get; } = LoadShader();
    private static string LoadShader()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AuthoredMaterialShader")
            ?? throw new InvalidOperationException("The authored material shader was not packaged.");
        using var reader = new StreamReader(stream);
        string declarations = "";
        foreach (string slot in AuthoredMaterial.Slots)
            declarations += $"uniform sampler2D am_tex_{slot} : filter_linear_mipmap_anisotropic, repeat_enable;\nuniform bool am_has_{slot} = false;\nuniform bool am_srgb_{slot} = false;\nuniform int am_channel_{slot} = 0;\nuniform bool am_invert_{slot} = false;\n";
        return reader.ReadToEnd().Replace("// BINDINGS", declarations, StringComparison.Ordinal);
    }

    public static void Apply(ShaderMaterial target, AuthoredMaterial definition, Func<string, Texture2D?> texture, Func<string, MaterialTexture, Texture2D?>? authoredTexture = null)
    {
        target.SetMeta("authored_displacement", definition.Displacement != 0);
        foreach (PropertyInfo p in typeof(AuthoredMaterial).GetProperties())
        {
            string uniform = "am_" + char.ToLowerInvariant(p.Name[0]) + p.Name[1..];
            if (p.PropertyType == typeof(float)) target.SetShaderParameter(uniform, (float)p.GetValue(definition)!);
            else if (p.PropertyType == typeof(float[]) && p.GetValue(definition) is float[] { Length: 3 } c)
                target.SetShaderParameter(uniform, new Vector3(c[0], c[1], c[2]));
        }
        target.SetShaderParameter("am_normalDirectX", definition.NormalDirectX);
        target.SetShaderParameter("am_bumpFromHeight", definition.BumpFromHeight);
        target.SetShaderParameter("am_triplanar", definition.Triplanar);
        target.SetShaderParameter("am_parallaxSteps", definition.ParallaxSteps);
        MaterialAnimation a = definition.Animation;
        target.SetShaderParameter("am_atlas", new Vector4(a.Columns, a.Rows, a.Frames, a.Fps));
        target.SetShaderParameter("am_motion", new Vector4(a.ScrollU, a.ScrollV, a.Rotate, a.Phase));
        target.SetShaderParameter("am_loop", a.Loop);
        target.SetShaderParameter("am_pingPong", a.PingPong);
        foreach (string slot in AuthoredMaterial.Slots)
        {
            bool has = definition.Textures.TryGetValue(slot, out MaterialTexture? binding);
            target.SetShaderParameter("am_has_" + slot, has);
            if (!has || binding is null) continue;
            Texture2D? image = authoredTexture is null ? texture(binding.File) : authoredTexture(slot, binding);
            if (image is not null) target.SetShaderParameter("am_tex_" + slot, image);
            target.SetShaderParameter("am_srgb_" + slot, binding.Srgb);
            target.SetShaderParameter("am_invert_" + slot, binding.Invert);
            target.SetShaderParameter("am_channel_" + slot, binding.Channel switch { "g" => 1, "b" => 2, "a" => 3, "luminance" => 4, _ => 0 });
        }
    }
}
