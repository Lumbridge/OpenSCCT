using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Materials;

/// <summary>How a compiler finds the vision modifiers (heat and EMF textures) of its materials.</summary>
public enum VisionLookup
{
    /// <summary>Map materials: their source path when it is a modifier.</summary>
    MapSource,

    /// <summary>Gameplay pack meshes: the material name (Package.Name) when it is a modifier.</summary>
    ModifierName,

    /// <summary>Character skins: the base material their modifiers wrap.</summary>
    SkinBase,
}

/// <summary>Where a surface's light comes from (docs/RENDERING.md#baked-light).</summary>
public enum LightSource
{
    /// <summary>Fullbright: PF_Unlit BSP, bUnlit actors, unlit materials.</summary>
    None,

    /// <summary>BSP lightmap on UV2, tex x lightmap x 2.</summary>
    Lightmap,

    /// <summary>Baked static mesh colours, tex x colour x 2.</summary>
    Vertex,

    /// <summary>Actor lighting: zone ambient plus the four strongest lights, per instance.</summary>
    Actor,
}

/// <summary>
/// Turns a material of the map (render/materials.json, by glTF name) and the light source of the surface into a
/// <see cref="ShaderMaterial"/> of the Faithful template (game/Shaders/svm_world.gdshaderinc). Shaders are shared per
/// variant (light source, blend, sidedness, extras); materials per (name, light source).
/// </summary>
public sealed class MaterialCompiler
{
    public const string TemplatePath = "res://Shaders/svm_world.gdshaderinc";
    public const string TimeParameter = "svm_time";
    public const string VisionParameter = "svm_vision";
    public const string MipBiasParameter = "svm_mip_bias";
    public const string VisionAmbientParameter = "svm_vision_ambient";

    /// <summary>The scale of the night boost, for calibration runs (1 is the measured value).</summary>
    public static float NightAmbientScale { get; set; } = 1f;
    public const string EmfEnvironmentParameter = "svm_emf_env";

    // The RenderingServer's global parameters live as long as the process; this only remembers the one add.
    private static bool s_globalsAdded;

    private readonly MaterialsDoc _doc;
    private readonly TextureCache _textures;
    private readonly Dictionary<string, Shader> _shaders = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, LightSource), Material?> _materials = new();

    public MaterialCompiler(MaterialsDoc doc, TextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(textures);
        _doc = doc;
        _textures = textures;
        EnsureGlobals();
        WorldLights.EnsureGlobals();
        SpotShadowLight.EnsureGlobals();
    }

    /// <summary>Lightmap scale of BSP surfaces; <see cref="RenderK.LightmapScale"/> unless a calibration run sets it.</summary>
    public float LightmapScale { get; set; } = RenderK.LightmapScale;

    /// <summary>The shared render pack: heat and EMF textures of the materials' vision modifiers. Null draws every surface at its actor's flat heat.</summary>
    public RenderPack? Pack { get; set; }

    /// <summary>How materials find their vision modifiers in the pack.</summary>
    public VisionLookup VisionLookup { get; set; } = VisionLookup.MapSource;

    /// <summary>Materials given a heat or EMF texture from the pack.</summary>
    public int VisionMaterials { get; private set; }

    public int ShaderVariants => _shaders.Count;

    public int MaterialCount => _materials.Count;

    /// <summary>Materials named by the geometry but missing from materials.json, or without a texture.</summary>
    public SortedSet<string> Unknown { get; } = new(StringComparer.Ordinal);

    /// <summary>Registers the global shader parameters the template reads (once per process).</summary>
    public static void EnsureGlobals()
    {
        if (s_globalsAdded)
        {
            return;
        }

        RenderingServer.GlobalShaderParameterAdd(TimeParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        RenderingServer.GlobalShaderParameterAdd(VisionParameter, RenderingServer.GlobalShaderParameterType.Int, RenderK.RendMapNormal);
        RenderingServer.GlobalShaderParameterAdd(MipBiasParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        RenderingServer.GlobalShaderParameterAdd(VisionAmbientParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        white.Fill(new Color(1f, 1f, 1f, 1f));
        RenderingServer.GlobalShaderParameterAdd(EmfEnvironmentParameter, RenderingServer.GlobalShaderParameterType.Sampler2D,
            ImageTexture.CreateFromImage(white));
        s_globalsAdded = true;
    }

    /// <summary>
    /// The vision mode every Faithful material draws (PlayerController.RendMap). Process wide, like the global
    /// parameter it sets: thermal and EMF change what the scene writes, the other modes draw the normal scene.
    /// </summary>
    public static void SetVision(int rendMap)
    {
        EnsureGlobals();
        RenderingServer.GlobalShaderParameterSet(VisionParameter, rendMap);
        float boost = rendMap == RenderK.RendMapNight ? RenderK.NightSceneBoost * NightAmbientScale : 0f;
        RenderingServer.GlobalShaderParameterSet(VisionAmbientParameter, boost);
    }

    /// <summary>The LOD bias of colour textures (D3DDrv DefaultTexMipBias is 0 in the shipped ini); for calibration runs.</summary>
    public static void SetMipBias(float bias)
    {
        EnsureGlobals();
        RenderingServer.GlobalShaderParameterSet(MipBiasParameter, bias);
    }

    /// <summary>The EMF environment map (LevelInfo.pEMFTexture, Echelon.EMFInvDarkBlue) the EMF scene samples.</summary>
    public static void SetEmfEnvironment(Texture2D? texture)
    {
        EnsureGlobals();
        if (texture is not null)
        {
            RenderingServer.GlobalShaderParameterSet(EmfEnvironmentParameter, texture);
        }
    }

    /// <summary>The scene time animated texture modifiers run on (Level.TimeSeconds in the original).</summary>
    public static void SetTime(float seconds) => RenderingServer.GlobalShaderParameterSet(s_timeName, seconds);

    // Made once: SetTime runs every frame, and a name passed as a string makes a StringName per call.
    private static readonly StringName s_timeName = TimeParameter;

    /// <summary>The texture URIs <see cref="Get"/> will want, for <see cref="TextureCache.Prefetch"/>.</summary>
    public IEnumerable<(string Uri, bool Mipmaps)> TexturesOf(MaterialEntry m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.Texture is { } t)
        {
            yield return (t, Mipmapped(m, "Diffuse"));
        }

        if (m.Lightmap is { } lm)
        {
            yield return (lm, false);
        }

        if (m.SelfIllum is { } si)
        {
            yield return (si, Mipmapped(m, "SelfIllumination"));
        }

        if (m.SelfIllumMask is { } sim)
        {
            yield return (sim, Mipmapped(m, "SelfIlluminationMask"));
        }

        if (m.EnvColor is not null && m.SpecMask is { } sm)
        {
            yield return (sm, Mipmapped(m, "SpecularityMask"));
        }
    }

    /// <summary>The material of <paramref name="name"/> lit by <paramref name="light"/>, or null when it is not drawn.</summary>
    public Material? Get(string name, LightSource light)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_materials.TryGetValue((name, light), out Material? cached))
        {
            return cached;
        }

        Material? material = Build(name, light);
        _materials[(name, light)] = material;
        return material;
    }

    /// <summary>A material built from loose flags, for meshes outside the map (characters, animated props).</summary>
    public Material Build(MaterialEntry entry, LightSource light)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Compose(entry, light);
    }

    private Material? Build(string name, LightSource light)
    {
        MaterialEntry? entry = _doc.Find(name);
        if (entry is null || entry.Skip || entry.Texture is null)
        {
            Unknown.Add(name);
            return null;
        }

        return Compose(entry, light);
    }

    private ShaderMaterial Compose(MaterialEntry entry, LightSource light)
    {
        // Unlit materials draw fullbright; self-illumination keeps the lit diffuse under its mask (UE2 lerp).
        if (entry.Unlit && entry.SelfIllum is null)
        {
            light = LightSource.None;
        }

        if (light == LightSource.Lightmap && entry.Lightmap is null)
        {
            light = LightSource.None;
        }

        MaterialBlend blend = entry.Blend;
        bool selfIllum = entry.SelfIllum is not null;
        bool selfIllumMask = selfIllum && entry.SelfIllumMask is not null;
        bool env = entry.EnvColor is not null && entry.SpecMask is not null;
        bool uvAnim = entry.UvAnim is { Code: > 0 };
        Shader shader = ShaderFor(light, blend, entry.TwoSided, selfIllum, selfIllumMask, env, uvAnim);

        var material = new ShaderMaterial { Shader = shader, ResourceName = entry.Name };
        Texture2D? diffuse = _textures.Get(entry.Texture, Mipmapped(entry, "Diffuse"));
        SetTexture(material, "tex_diffuse", diffuse);
        if (light == LightSource.Lightmap)
        {
            SetTexture(material, "tex_lightmap", _textures.Get(entry.Lightmap, mipmaps: false));
            material.SetShaderParameter("lightmap_scale", LightmapScale);
        }

        if (selfIllum)
        {
            SetTexture(material, "tex_self_illum", _textures.Get(entry.SelfIllum, Mipmapped(entry, "SelfIllumination")));
            if (selfIllumMask)
            {
                SetTexture(material, "tex_self_illum_mask", _textures.Get(entry.SelfIllumMask, Mipmapped(entry, "SelfIlluminationMask")));
            }
        }

        if (env)
        {
            SetTexture(material, "tex_spec_mask", _textures.Get(entry.SpecMask, Mipmapped(entry, "SpecularityMask")));

            // EnvColor is the env map's mean colour in linear light; the frame works in display values.
            Vector3 c = entry.EnvColor!.Value;
            material.SetShaderParameter("env_color", new Vector3(ToDisplay(c.X), ToDisplay(c.Y), ToDisplay(c.Z)));
        }

        ApplyVision(material, VisionLookup switch
        {
            VisionLookup.SkinBase => Pack?.VisionOfSkin(entry.Name),
            VisionLookup.ModifierName => Pack?.VisionOfModifierName(entry.Name),
            _ => Pack?.VisionOf(entry.Source),
        });

        if (uvAnim)
        {
            UvAnim a = entry.UvAnim!;
            material.SetShaderParameter("uv_anim_type", a.Code);
            material.SetShaderParameter("uv_anim_a", a.A);
            material.SetShaderParameter("uv_anim_b", a.B);
            if (diffuse is not null)
            {
                material.SetShaderParameter("tex_size", new Vector2(diffuse.GetWidth(), diffuse.GetHeight()));
            }
        }

        return material;
    }

    /// <summary>
    /// Whether a material input samples with mipmaps: only when the game stores more than one level for the texture
    /// behind it (most world textures store one, and the original draws them unfiltered across levels, which is why
    /// distant walls read sharp in the captures). Unknown textures (combined, derived) keep generated mipmaps.
    /// docs/RENDERING.md#materials.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "stored mip counts of the texture packages (render pack mips.json); SteeE golden SSIM 0.97 to 0.99")]
    private bool Mipmapped(MaterialEntry entry, string input)
    {
        if (Pack is null)
        {
            return true;
        }

        string? texture = _doc.TextureOf(entry.Source, input) ?? (entry.Source is null ? entry.Name : null);
        return Pack.MipLevels(texture) is not int levels || levels > 1;
    }

    /// <summary>
    /// The heat and EMF textures of the material's vision modifiers (docs/RENDERING.md#vision-modes): heat_mode 1 for
    /// HEATTEX_Cool, 2 for HEATTEX_Heat; emf_mode 1, or 2 for bInvertEMFTexture.
    /// </summary>
    private void ApplyVision(ShaderMaterial material, VisionTextures? vision)
    {
        if (vision is null || Pack is null)
        {
            return;
        }

        bool any = false;
        if (vision.HeatTexture is { } heat && Pack.Textures.Get(heat) is { } heatTexture)
        {
            material.SetShaderParameter("tex_heat", heatTexture);
            material.SetShaderParameter("heat_mode", vision.HeatType == 0 ? 1 : 2);
            any = true;
        }

        if (vision.EmfTexture is { } emf && Pack.Textures.Get(emf) is { } emfTexture)
        {
            material.SetShaderParameter("tex_emf", emfTexture);
            material.SetShaderParameter("emf_mode", vision.EmfInvert ? 2 : 1);
            any = true;
        }

        if (any)
        {
            VisionMaterials++;
        }
    }

    private Shader ShaderFor(LightSource light, MaterialBlend blend, bool twoSided, bool selfIllum, bool selfIllumMask,
        bool env, bool uvAnim)
    {
        string key = $"{light}|{blend}|{twoSided}|{selfIllum}|{selfIllumMask}|{env}|{uvAnim}";
        if (_shaders.TryGetValue(key, out Shader? shader))
        {
            return shader;
        }

        var modes = new List<string> { "unshaded", "fog_disabled", twoSided ? "cull_disabled" : "cull_back" };
        switch (blend)
        {
            case MaterialBlend.Translucent:
                modes.Add("blend_mix");
                modes.Add("depth_draw_opaque");
                break;
            case MaterialBlend.Additive:
                modes.Add("blend_add");
                modes.Add("depth_draw_never");
                break;
            case MaterialBlend.Modulate:
            case MaterialBlend.Darken:
                modes.Add("blend_mul");
                modes.Add("depth_draw_never");
                break;
            default:
                modes.Add("depth_draw_opaque");
                break;
        }

        var code = new StringBuilder();
        code.Append("shader_type spatial;\nrender_mode ").Append(string.Join(", ", modes)).Append(";\n");
        Define(code, light switch
        {
            LightSource.Lightmap => "LIGHT_LIGHTMAP",
            LightSource.Vertex => "LIGHT_VERTEX",
            LightSource.Actor => "LIGHT_ACTOR",
            _ => null,
        });
        Define(code, blend switch
        {
            MaterialBlend.Masked => "BLEND_MASKED",
            MaterialBlend.Translucent => "BLEND_TRANSLUCENT",
            MaterialBlend.Additive => "BLEND_ADDITIVE",
            MaterialBlend.Modulate => "BLEND_MODULATE",
            MaterialBlend.Darken => "BLEND_DARKEN",
            _ => null,
        });
        Define(code, selfIllum ? "SELF_ILLUM" : null);
        Define(code, selfIllumMask ? "SELF_ILLUM_MASK" : null);
        Define(code, env ? "ENV_COLOR" : null);
        Define(code, uvAnim ? "UV_ANIM" : null);
        code.Append("#include \"").Append(TemplatePath).Append("\"\n");
        shader = new Shader { Code = code.ToString(), ResourceName = "svm_world " + key };
        _shaders[key] = shader;
        return shader;
    }

    private static void SetTexture(ShaderMaterial material, string parameter, Texture2D? texture)
    {
        if (texture is not null)
        {
            material.SetShaderParameter(parameter, texture);
        }
    }

    private static void Define(StringBuilder code, string? name)
    {
        if (name is not null)
        {
            code.Append("#define ").Append(name).Append('\n');
        }
    }

    /// <summary>Linear light to the sRGB display value.</summary>
    public static float ToDisplay(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
}
