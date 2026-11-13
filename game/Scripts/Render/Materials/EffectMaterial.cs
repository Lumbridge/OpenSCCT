using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>The unlit gameplay-effect material subset, with fog evaluated before blending at the effect's depth.</summary>
public partial class EffectMaterial : ShaderMaterial
{
    private static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);
    private BaseMaterial3D.BlendModeEnum _blend;
    private BaseMaterial3D.TransparencyEnum _transparency;
    private BaseMaterial3D.CullModeEnum _cull;
    private BaseMaterial3D.BillboardModeEnum _billboard;
    private bool _fixedSize, _noDepth, _vertexColor, _hiddenInVision, _displayTexture;
    private BaseMaterial3D.StencilModeEnum _stencil;
    private BaseMaterial3D.StencilCompareEnum _compare;
    private int _stencilFlags, _reference;
    private Color _color = Colors.White;
    private Texture2D? _texture;
    private Vector3 _scale = Vector3.One, _offset;

    public EffectMaterial()
    {
        MaterialCompiler.EnsureGlobals();
        Rebuild();
    }

    public BaseMaterial3D.ShadingModeEnum ShadingMode
    {
        get => BaseMaterial3D.ShadingModeEnum.Unshaded;
        set { if (value != BaseMaterial3D.ShadingModeEnum.Unshaded) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
    public BaseMaterial3D.BlendModeEnum BlendMode { get => _blend; set { _blend = value; Rebuild(); } }
    public BaseMaterial3D.TransparencyEnum Transparency { get => _transparency; set { _transparency = value; Rebuild(); } }
    public BaseMaterial3D.CullModeEnum CullMode { get => _cull; set { _cull = value; Rebuild(); } }
    public BaseMaterial3D.BillboardModeEnum BillboardMode { get => _billboard; set { _billboard = value; Rebuild(); } }
    public bool FixedSize { get => _fixedSize; set { _fixedSize = value; Rebuild(); } }
    public bool NoDepthTest { get => _noDepth; set { _noDepth = value; Rebuild(); } }
    public bool VertexColorUseAsAlbedo { get => _vertexColor; set { _vertexColor = value; Rebuild(); } }

    /// <summary>
    /// Not drawn in thermal and EMF vision (a mark left on a surface: a bullet hole, a burn, a footprint). Those modes
    /// read the frame's colours as heat and field strength, so a mark's own colours would show as a glow.
    /// </summary>
    public bool HiddenInVision { get => _hiddenInVision; set { _hiddenInVision = value; Rebuild(); } }
    /// <summary>
    /// The texture holds display values and is added as they are, as the world template samples its textures and
    /// as the Fast renderer draws every effect; off, Godot's renderer decodes it as sRGB first, which darkens it.
    /// </summary>
    public bool DisplayTexture { get => _displayTexture; set { _displayTexture = value; Rebuild(); } }
    public BaseMaterial3D.StencilModeEnum StencilMode { get => _stencil; set { _stencil = value; Rebuild(); } }
    public int StencilFlags { get => _stencilFlags; set { _stencilFlags = value; Rebuild(); } }
    public BaseMaterial3D.StencilCompareEnum StencilCompare { get => _compare; set { _compare = value; Rebuild(); } }
    public int StencilReference { get => _reference; set { _reference = value; Rebuild(); } }
    public Color AlbedoColor { get => _color; set { _color = value; SetShaderParameter("effect_color", value); } }
    public Texture2D? AlbedoTexture
    {
        get => _texture;
        set { _texture = value; SetShaderParameter("effect_texture", value is null ? default(Variant) : Variant.From(value)); SetShaderParameter("effect_has_texture", value is not null); }
    }
    public Vector3 Uv1Scale { get => _scale; set { _scale = value; SetShaderParameter("effect_uv_scale", value); } }
    public Vector3 Uv1Offset { get => _offset; set { _offset = value; SetShaderParameter("effect_uv_offset", value); } }

    private void Rebuild()
    {
        if (_blend is not (BaseMaterial3D.BlendModeEnum.Mix or BaseMaterial3D.BlendModeEnum.Add or BaseMaterial3D.BlendModeEnum.Sub or BaseMaterial3D.BlendModeEnum.Mul))
            throw new InvalidOperationException("EffectMaterial supports mix, additive, subtractive and multiplicative gameplay effects.");
        if (_transparency is not (BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.Alpha))
            throw new InvalidOperationException("EffectMaterial supports opaque and alpha gameplay effects.");
        if (_billboard is not (BaseMaterial3D.BillboardModeEnum.Disabled or BaseMaterial3D.BillboardModeEnum.Enabled))
            throw new InvalidOperationException("EffectMaterial supports ordinary and camera-facing gameplay effects.");
        string key = $"{_blend}|{_transparency}|{_cull}|{_billboard}|{_fixedSize}|{_noDepth}|{_vertexColor}|{_hiddenInVision}|{_displayTexture}|{_stencil}|{_stencilFlags}|{_compare}|{_reference}";
        if (!Shaders.TryGetValue(key, out Shader? shader))
        {
            string blend = _blend switch
            {
                BaseMaterial3D.BlendModeEnum.Add => "add",
                BaseMaterial3D.BlendModeEnum.Sub => "sub",
                BaseMaterial3D.BlendModeEnum.Mul => "mul",
                _ => "mix",
            };
            string cull = _cull switch { BaseMaterial3D.CullModeEnum.Front => "front", BaseMaterial3D.CullModeEnum.Disabled => "disabled", _ => "back" };
            string code = $"shader_type spatial;\nrender_mode unshaded, fog_disabled, blend_{blend}, cull_{cull}, depth_draw_opaque" + (_noDepth ? ", depth_test_disabled" : "") + ";\n";
            if (_stencil == BaseMaterial3D.StencilModeEnum.Custom && _stencilFlags != 0)
            {
                // The merc beam reads the weapon stencil. Preserve the complete compare set for property mutations.
                string compare = _compare switch
                {
                    BaseMaterial3D.StencilCompareEnum.Less => "less", BaseMaterial3D.StencilCompareEnum.Equal => "equal",
                    BaseMaterial3D.StencilCompareEnum.LessOrEqual => "less_or_equal", BaseMaterial3D.StencilCompareEnum.Greater => "greater",
                    BaseMaterial3D.StencilCompareEnum.NotEqual => "not_equal", BaseMaterial3D.StencilCompareEnum.GreaterOrEqual => "greater_or_equal", _ => "always",
                };
                var flags = new List<string>();
                if ((_stencilFlags & (int)BaseMaterial3D.StencilFlagsEnum.Read) != 0) flags.Add("read");
                if ((_stencilFlags & (int)BaseMaterial3D.StencilFlagsEnum.Write) != 0) flags.Add("write");
                if ((_stencilFlags & (int)BaseMaterial3D.StencilFlagsEnum.WriteDepthFail) != 0) flags.Add("write_depth_fail");
                code += $"stencil_mode {string.Join(", ", flags)}, compare_{compare}, {_reference};\n";
            }
            if (_blend is BaseMaterial3D.BlendModeEnum.Add or BaseMaterial3D.BlendModeEnum.Sub) code += "#define EFFECT_ADDITIVE\n";
            if (_blend == BaseMaterial3D.BlendModeEnum.Mul) code += "#define EFFECT_MULTIPLY\n";
            if (_transparency == BaseMaterial3D.TransparencyEnum.Alpha || _blend != BaseMaterial3D.BlendModeEnum.Mix) code += "#define EFFECT_ALPHA\n";
            if (_billboard != BaseMaterial3D.BillboardModeEnum.Disabled) code += "#define EFFECT_BILLBOARD\n";
            if (_fixedSize) code += "#define EFFECT_FIXED_SIZE\n";
            if (_vertexColor) code += "#define EFFECT_VERTEX_COLOR\n";
            if (_hiddenInVision) code += "#define EFFECT_HIDDEN_IN_VISION\n";
            if (_displayTexture) code += "#define EFFECT_DISPLAY_TEXTURE\n";
            shader = ResponsiveSmokeShaders.Shared(code + "#include \"res://Shaders/svm_effect.gdshaderinc\"\n", "svm_effect " + key);
            Shaders[key] = shader;
        }
        Shader = ResponsiveSmokeShaders.Current(shader);
    }
}
