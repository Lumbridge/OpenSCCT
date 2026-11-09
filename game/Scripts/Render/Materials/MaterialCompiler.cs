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

/// <summary>
/// Heat and EMF masks made for a material the render pack has no vision modifiers for (a map outfit's own model, from
/// the stock suit it stands in for): the heat in HEATTEX_Cool form, the EMF not inverted.
/// </summary>
public sealed record VisionMasks(Texture2D Heat, Texture2D Emf);

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
public sealed partial class MaterialCompiler
{
    public const string TemplatePath = "res://Shaders/svm_world.gdshaderinc";
    public const string TimeParameter = "svm_time";
    public const string VisionParameter = "svm_vision";
    public const string MipBiasParameter = "svm_mip_bias";
    public const string VisionAmbientParameter = "svm_vision_ambient";

    /// <summary>
    /// Masked surfaces use alpha to coverage (rl:gfx.aa: the settings' msaa is on), so their cut edges are smoothed by
    /// the MSAA samples. Read when a shader is made: a change applies from the next map.
    /// </summary>
    public static bool AlphaToCoverage { get; set; }

    /// <summary>The scale of the night boost, for calibration runs (1 is the measured value).</summary>
    public static float NightAmbientScale { get; set; } = 1f;
    public const string EmfEnvironmentParameter = "svm_emf_env";

    /// <summary>The sky camera's eye (Godot space) that sky zone surfaces are drawn from (SVM_SKY_ZONE).</summary>
    public const string SkyOriginParameter = "svm_sky_origin";

    /// <summary>The sky camera's visibility layers (its fog slot) that sky zone surfaces shade with.</summary>
    public const string SkyLayersParameter = "svm_sky_layers";

    // The RenderingServer's global parameters live as long as the process; this only remembers the one add.
    private static bool s_globalsAdded;

    private readonly MaterialsDoc _doc;
    private readonly TextureCache _textures;
    private readonly Dictionary<string, Shader> _shaders = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, LightSource), Material?> _materials = new();
    private readonly Dictionary<(string, LightSource), Material?> _seeThrough = new();
    private readonly Dictionary<(string, LightSource), Material?> _plain = new();
    private readonly Dictionary<(string, LightSource), Material?> _sky = new();
    private readonly Dictionary<(string, LightSource, bool, bool), Material?> _unreached = new();
    private readonly Dictionary<(string, LightSource, bool, bool, bool), Material?> _normalVision = new();

    public MaterialCompiler(MaterialsDoc doc, TextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(textures);
        _doc = doc;
        _textures = textures;
        s_sequenceCompilers.Add(new(this));
        EnsureGlobals();
        WorldLights.EnsureGlobals();
        SpotShadowLight.EnsureGlobals();
        FlareLights.EnsureGlobals();
    }

    /// <summary>Lightmap scale of BSP surfaces; <see cref="RenderK.LightmapScale"/> unless a calibration run sets it.</summary>
    public float LightmapScale { get; set; } = RenderK.LightmapScale;

    /// <summary>The shared render pack: heat and EMF textures of the materials' vision modifiers. Null draws every surface at its actor's flat heat.</summary>
    public RenderPack? Pack { get; set; }

    /// <summary>How materials find their vision modifiers in the pack.</summary>
    public VisionLookup VisionLookup { get; set; } = VisionLookup.MapSource;

    /// <summary>Masks for materials (by name) that have no vision modifiers in <see cref="Pack"/>; null for none.</summary>
    public Func<string, VisionMasks?>? OwnVision { get; set; }

    /// <summary>
    /// Whether the materials compile OpenSCCT's cosmetics (SVM_COSMETIC: svm_tint, svm_glow_hue, the camo finish and
    /// sheen). Only character models set them (SkeletalModel, and the skins put over its surfaces); without it the
    /// template draws as they hold their defaults, with less code.
    /// </summary>
    public bool Cosmetics { get; init; }

    /// <summary>
    /// Whether solid surfaces mark what they are in the stencil (<see cref="StencilBsp"/> and the others). Only projectors
    /// with bProjectStaticMesh off read the marks (14 of the 85 maps). A map without them leaves the marks out, so its
    /// plain surfaces (<see cref="GetPlain"/>) can take Godot's shared depth-only shader.
    /// </summary>
    public bool StencilMarks { get; init; } = true;

    /// <summary>Materials given a heat or EMF texture from the pack.</summary>
    public int VisionMaterials { get; private set; }

    public int ShaderVariants => _shaders.Count;

    public int MaterialCount => _materials.Count;

    /// <summary>Materials named by the geometry but missing from materials.json, or without a texture.</summary>
    public SortedSet<string> Unknown { get; } = new(StringComparer.Ordinal);

    /// <summary>Registers the global shader parameters the template reads (once per process).</summary>
    public static void EnsureGlobals()
    {
        OpenScctGame.Render.Post.SurfaceFogRegistry.EnsureGlobals();
        if (s_globalsAdded)
        {
            return;
        }

        ShaderGlobals.Add(TimeParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        ShaderGlobals.Add(VisionParameter, RenderingServer.GlobalShaderParameterType.Int, RenderK.RendMapNormal);
        ShaderGlobals.Add(MipBiasParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        ShaderGlobals.Add(VisionAmbientParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        white.Fill(new Color(1f, 1f, 1f, 1f));
        ShaderGlobals.Add(EmfEnvironmentParameter, RenderingServer.GlobalShaderParameterType.Sampler2D,
            ImageTexture.CreateFromImage(white));
        ShaderGlobals.Add(SkyOriginParameter, RenderingServer.GlobalShaderParameterType.Vec3, Vector3.Zero);
        ShaderGlobals.Add(SkyLayersParameter, RenderingServer.GlobalShaderParameterType.Int, (int)RenderLayers.Sky);
        s_globalsAdded = true;
    }

    /// <summary>The vision mode last set by <see cref="SetVision"/>.</summary>
    public static int Vision { get; private set; } = RenderK.RendMapNormal;

    /// <summary>
    /// The vision mode every Faithful material draws (PlayerController.RendMap). Process wide, like the global
    /// parameter it sets: thermal and EMF change what the scene writes, the other modes draw the normal scene.
    /// </summary>
    public static void SetVision(int rendMap, float squintingFactor = 0f)
    {
        Vision = rendMap;
        EnsureGlobals();
        ShaderGlobals.Set(VisionParameter, rendMap);
        float boost = rendMap == RenderK.RendMapNight ? RenderK.NightSceneBoost * NightAmbientScale
            : rendMap == RenderK.RendMapNormal ? RenderK.SquintSceneBoost(squintingFactor) : 0f;
        ShaderGlobals.Set(VisionAmbientParameter, boost);
    }

    /// <summary>The LOD bias of colour textures (D3DDrv DefaultTexMipBias is 0 in the shipped ini); for calibration runs.</summary>
    public static void SetMipBias(float bias)
    {
        EnsureGlobals();
        ShaderGlobals.Set(MipBiasParameter, bias);
    }

    /// <summary>The EMF environment map (LevelInfo.pEMFTexture, Echelon.EMFInvDarkBlue) the EMF scene samples.</summary>
    public static void SetEmfEnvironment(Texture2D? texture)
    {
        EnsureGlobals();
        if (texture is not null)
        {
            ShaderGlobals.Set(EmfEnvironmentParameter, texture);
        }
    }

    /// <summary>The scene time animated texture modifiers run on (Level.TimeSeconds in the original).</summary>
    public static void SetTime(float seconds)
    {
        ShaderGlobals.Set(s_timeName, seconds);
        for (int i = s_sequenceCompilers.Count - 1; i >= 0; i--)
        {
            if (s_sequenceCompilers[i].TryGetTarget(out MaterialCompiler? compiler)) compiler.UpdateSequences(seconds);
            else s_sequenceCompilers.RemoveAt(i);
        }
    }

    // Made once: SetTime runs every frame, and a name passed as a string makes a StringName per call.
    private static readonly StringName s_timeName = TimeParameter;

    /// <summary>The texture URIs <see cref="Get"/> will want, for <see cref="TextureCache.Prefetch"/>.</summary>
    public IEnumerable<(string Uri, bool Mipmaps)> TexturesOf(MaterialEntry m)
    {
        ArgumentNullException.ThrowIfNull(m);
        foreach (MaterialSequenceEntry sequence in m.Sequences.Values)
            foreach (MaterialSequenceFrame frame in sequence.Items)
            {
                yield return (frame.Flipbook?.Texture ?? frame.Layer.Texture, true);
                if (frame.Material is { } state)
                    foreach (var texture in TexturesOf(state)) yield return texture;
            }
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

        if (m.EnvColor is not null)
        {
            if (m.SpecMask is { } sm)
            {
                yield return (sm, Mipmapped(m, "SpecularityMask"));
            }

            if (m.EnvTexture is { } env)
            {
                yield return (env, false);
            }
        }

        if (m.Opacity is { } op)
        {
            yield return (op.Texture, true);
        }

        if (m.Combine is { } c)
        {
            yield return (c.First.Texture, true);
            yield return (c.Second.Texture, true);
            if (c.Mask is { } mask)
            {
                yield return (mask.Texture, true);
            }
        }
        else if (m.Flipbook is { } fb)
        {
            yield return (fb.Texture, true);
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

    /// <summary>
    /// The material of <paramref name="name"/> for an actor whose HeatOpacity or EMFOpacity lies between 0 and 1 (glass,
    /// crates, fake ceilings, partitions: about 5,000 placed actors over the maps). A solid or masked material gets a
    /// second, blended pass: in thermal and EMF the solid pass leaves such a surface out and the blended pass draws its
    /// vision value over what lies behind, by the opacity (docs/RENDERING.md#vision-modes). Every other mode draws the
    /// solid pass alone. Translucent materials already blend by the opacity and are returned as they are.
    /// </summary>
    public Material? GetSeeThrough(string name, LightSource light)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_seeThrough.TryGetValue((name, light), out Material? cached))
        {
            return cached;
        }

        Material? material = Get(name, light);
        MaterialEntry? entry = _doc.Find(name);
        if (material is not null && entry is { Blend: MaterialBlend.Opaque or MaterialBlend.Masked })
        {
            int counted = VisionMaterials;
            ShaderMaterial solid = Compose(entry, light);
            solid.SetShaderParameter(SeeThroughParameter, true);
            solid.NextPass = Compose(entry, light, seeThroughPass: true);
            VisionMaterials = counted;
            material = solid;
        }

        _seeThrough[(name, light)] = material;
        return material;
    }

    /// <summary>
    /// The material of <paramref name="name"/> for merged map geometry whose actors are all fully visible in thermal and
    /// EMF (HeatOpacity and EMFOpacity above 0, none partly see-through), so the template's vision cut-outs never fire on
    /// it. A one-sided opaque material then compiles without them and without its own vertex position (SVM_PLAIN_DEPTH,
    /// same pixels). Godot draws the depth pre-pass of such a material with its shared depth-only shader when the stencil
    /// marks are off too, instead of running the whole world shader twice per pixel. Every other material is the one
    /// <see cref="Get"/> returns.
    /// </summary>
    public Material? GetPlain(string name, LightSource light)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_plain.TryGetValue((name, light), out Material? cached))
        {
            return cached;
        }

        Material? material = Get(name, light);
        MaterialEntry? entry = _doc.Find(name);
        if (material is not null && entry is { Blend: MaterialBlend.Opaque, TwoSided: false }
            && !(entry.Sequences.TryGetValue("diffuse", out MaterialSequenceEntry? sequence) && sequence.SwitchMaterial))
        {
            int counted = VisionMaterials;
            material = Compose(entry, light, plainDepth: true);
            VisionMaterials = counted;
        }

        _plain[(name, light)] = material;
        return material;
    }

    /// <summary>
    /// The material of <paramref name="name"/> for a surface of the sky zone (SVM_SKY_ZONE): drawn as the sky camera
    /// sees it (from <see cref="SkyOriginParameter"/>, shaded with <see cref="SkyLayersParameter"/>) by whichever camera
    /// draws it, behind every surface of the main world, before every other transparent surface (lowest render
    /// priority), without stencil marks. Thermal and EMF draw no sky zone, so it needs no see-through pass. Null when
    /// <see cref="Get"/> draws nothing.
    /// </summary>
    public Material? GetSky(string name, LightSource light)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_sky.TryGetValue((name, light), out Material? cached))
        {
            return cached;
        }

        Material? material = Get(name, light);
        if (material is not null && _doc.Find(name) is { } entry)
        {
            int counted = VisionMaterials;
            material = Compose(entry, light, sky: true);
            VisionMaterials = counted;
        }

        _sky[(name, light)] = material;
        return material;
    }

    /// <summary>
    /// A twin of the material <see cref="Get"/> (with <paramref name="plain"/>, <see cref="GetPlain"/>) hands out, for
    /// merged map geometry that no spot shadow light can reach this frame (<see cref="LightReach"/>): the template leaves
    /// the spot term out (SVM_NO_SPOT). Without <paramref name="keepDynamic"/> the geometry is out of reach of every
    /// dynamic world light too, and a baked surface (lightmap, vertex light) also leaves their term out (SVM_NO_DYN); an
    /// actor-lit surface has no such term, so both of its twins are one material. The terms left out are exactly zero
    /// there, so the twin draws the same pixels with a smaller shader; twins with the same code share one
    /// <see cref="Shader"/> (<see cref="ShaderFor"/>). Null when the material has no twin: fullbright materials have no
    /// such terms, authored ones light their highlights from every light, and MaterialSequence state switches change
    /// their shader at run time.
    /// </summary>
    public Material? GetUnreached(string name, LightSource light, bool plain, bool keepDynamic)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_unreached.TryGetValue((name, light, plain, keepDynamic), out Material? cached))
        {
            return cached;
        }

        Material? full = plain ? GetPlain(name, light) : Get(name, light);
        MaterialEntry? entry = _doc.Find(name);
        Material? twin = null;
        if (full is not null && entry is not null && HasUnreached(entry, light))
        {
            if (keepDynamic && !Baked(entry, light))
            {
                // No dynamic term to keep: the same defines as the twin without it.
                twin = GetUnreached(name, light, plain, keepDynamic: false);
            }
            else
            {
                // The same flags as the full material (GetPlain's conditions are Compose's own), plus the terms left out.
                int counted = VisionMaterials;
                twin = Compose(entry, light, plainDepth: plain, noSpot: true, noDynamic: !keepDynamic);
                VisionMaterials = counted;
            }
        }

        _unreached[(name, light, plain, keepDynamic)] = twin;
        return twin;
    }

    /// <summary>
    /// The twin of the material <see cref="Get"/> hands out that merged map geometry draws while the vision is neither
    /// thermal nor EMF (<see cref="LightReach"/>): the template leaves the thermal and EMF outputs and their cut-outs out
    /// (SVM_NORMAL_VISION). The other modes never run that code, so the twin draws the pixels of <see cref="Get"/>,
    /// <see cref="GetPlain"/> and <see cref="GetSeeThrough"/> there alike, without the see-through pass (which draws
    /// nothing outside thermal and EMF). With <paramref name="plain"/> it keeps <see cref="GetPlain"/>'s depth
    /// (SVM_PLAIN_DEPTH); other buckets keep their own vertex transform, so depth stays bit-identical. Without
    /// <paramref name="keepSpot"/> it also leaves out the terms <see cref="GetUnreached"/> leaves out, and is null where
    /// that has no twin. Null for MaterialSequence state switches, which change their shader at run time.
    /// </summary>
    public Material? GetNormalVision(string name, LightSource light, bool plain, bool keepSpot = true, bool keepDynamic = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        keepDynamic |= keepSpot;
        if (_normalVision.TryGetValue((name, light, plain, keepSpot, keepDynamic), out Material? cached))
        {
            return cached;
        }

        Material? full = Get(name, light);
        MaterialEntry? entry = _doc.Find(name);
        Material? twin = null;
        if (full is not null && entry is not null && !SwitchesShader(entry))
        {
            if (keepSpot)
            {
                twin = Compose(entry, light, plainDepth: plain, normalVision: true);
            }
            else if (HasUnreached(entry, light))
            {
                twin = keepDynamic && !Baked(entry, light)
                    ? GetNormalVision(name, light, plain, keepSpot: false, keepDynamic: false)
                    : Compose(entry, light, plainDepth: plain, normalVision: true, noSpot: true, noDynamic: !keepDynamic);
            }
        }

        _normalVision[(name, light, plain, keepSpot, keepDynamic)] = twin;
        return twin;
    }

    /// <summary>A MaterialSequence that switches whole material states: its shader changes at run time.</summary>
    private static bool SwitchesShader(MaterialEntry entry) =>
        entry.Sequences.TryGetValue("diffuse", out MaterialSequenceEntry? sequence) && sequence.SwitchMaterial;

    /// <summary>
    /// Whether the material has twins without the spot (and dynamic) light terms: fullbright materials have no such
    /// terms, authored ones light their highlights from every light, and state switches change their shader.
    /// </summary>
    private static bool HasUnreached(MaterialEntry entry, LightSource light) =>
        entry.Authored is null && !SwitchesShader(entry) && LitBy(entry, light) != LightSource.None;

    /// <summary>A baked surface (lightmap, vertex light): the only kind with a dynamic world light term.</summary>
    private static bool Baked(MaterialEntry entry, LightSource light) =>
        LitBy(entry, light) is LightSource.Lightmap or LightSource.Vertex;

    /// <summary>The template's flag that hands partly see-through surfaces to the blended pass in thermal and EMF.</summary>
    public const string SeeThroughParameter = "vision_see_through";

    /// <summary>
    /// A material built from loose flags, for meshes outside the map (characters, animated props, light sprites);
    /// <paramref name="sky"/> for one in the sky zone, as <see cref="GetSky"/>.
    /// </summary>
    public Material Build(MaterialEntry entry, LightSource light, bool billboard = false, bool sky = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Compose(entry, light, billboard: billboard, sky: sky);
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

    /// <summary>
    /// The light a material draws with: unlit materials draw fullbright (self-illumination keeps the lit diffuse under
    /// its mask, UE2 lerp), and so does a lightmapped surface without a lightmap.
    /// </summary>
    private static LightSource LitBy(MaterialEntry entry, LightSource light) =>
        (entry.Unlit && entry.SelfIllum is null) || (light == LightSource.Lightmap && entry.Lightmap is null) ? LightSource.None : light;

    private ShaderMaterial Compose(MaterialEntry entry, LightSource light, bool seeThroughPass = false, bool billboard = false,
        bool plainDepth = false, bool sky = false, bool noSpot = false, bool noDynamic = false, bool normalVision = false)
    {
        if (entry.Sequences.TryGetValue("diffuse", out MaterialSequenceEntry? sequence) && sequence.SwitchMaterial)
            return ComposeSequence(entry, sequence, light, seeThroughPass, billboard, sky);
        light = LitBy(entry, light);
        MaterialBlend blend = entry.Blend;
        bool selfIllum = entry.SelfIllum is not null;
        bool selfIllumMask = selfIllum && entry.SelfIllumMask is not null;
        // The glow and its mask move by their own modifier chains (TSP's door light lines, TME's wall-trim ray).
        bool selfIllumLayer = selfIllumMask && entry.SelfIllumLayer is not null && entry.SelfIllumMaskLayer is not null;
        // A Shader's Specular is added through its SpecularityMask's alpha, or whole when it has none and its env map is a
        // 2D texture looked up by the reflection (146 map materials: StroD's chrome vases crypt_txt.sphear, AquaE's kiosk
        // screen, glass, signs); those used to lose the env map. Without a mask and without the env texture (the gameplay
        // pack's materials.json carries neither the texture nor the OB_Brighten blend: PolaE's first-aid cabinet glass
        // fist_aid_glass, Brighten over a dark env map) the mean colour over the whole surface is not drawn: it turned the
        // glass opaque white (PolaE spy-180 SSIM 0.989 to 0.975).
        bool env = entry.EnvColor is not null && (entry.SpecMask is not null || entry.EnvTexture is not null);
        bool uvAnim = entry.UvAnim is { Code: > 0 };
        CombineEntry? combine = entry.Combine;
        FlipbookEntry? flipbook = combine is null ? entry.Flipbook : null;
        var extras = new List<string>();
        AddIf(extras, entry.Authored is not null, "AUTHORED_MATERIAL");
        AddIf(extras, entry.Authored is { Refraction: > 0, Surface: "translucent" }, "AM_REFRACTION");
        AddIf(extras, billboard, "BILLBOARD");
        AddIf(extras, combine is not null && entry.LightBeforeCombine && light != LightSource.None, "COMBINE_LIGHT_FIRST");
        AddIf(extras, selfIllum, "SELF_ILLUM");
        AddIf(extras, selfIllumMask, "SELF_ILLUM_MASK");
        AddIf(extras, selfIllumLayer, "SI_LAYER");
        AddIf(extras, env, "ENV_COLOR");
        // Native SetShaderMaterial draws translucent Specular in a separate ONE/ONE pass
        // (SRCALPHA/ONE with SpecularityMask), independent of the diffuse opacity.
        AddIf(extras, env && blend == MaterialBlend.Translucent && !seeThroughPass, "ENV_SEPARATE");
        AddIf(extras, env && entry.EnvTexture is not null, "ENV_TEX");
        AddIf(extras, env && entry.SpecMaskLayer is not null, "SPEC_LAYER");
        AddIf(extras, uvAnim, "UV_ANIM");
        AddIf(extras, entry.Opacity is not null, "OPACITY_LAYER");
        AddIf(extras, combine is not null, "COMBINE");
        AddIf(extras, flipbook is not null, "FLIPBOOK");
        foreach ((string slot, string prefix) in SequenceSlots)
            AddIf(extras, entry.Sequences.ContainsKey(slot), "SEQUENCE_" + prefix.ToUpperInvariant());
        AddIf(extras, entry.NormalTexture is not null && light == LightSource.Actor, "NORMAL_MAP");
        AddIf(extras, plainDepth && blend == MaterialBlend.Opaque && !entry.TwoSided && !billboard && !seeThroughPass && !sky, "SVM_PLAIN_DEPTH");
        AddIf(extras, sky && !seeThroughPass, "SVM_SKY_ZONE");
        // LightReach's twins (GetUnreached): the spot term of every lit surface, the dynamic term of baked ones.
        AddIf(extras, noSpot && light != LightSource.None, "SVM_NO_SPOT");
        AddIf(extras, noDynamic && light is LightSource.Lightmap or LightSource.Vertex, "SVM_NO_DYN");
        // LightReach's twins for the modes other than thermal and EMF (GetNormalVision).
        normalVision &= !seeThroughPass;
        AddIf(extras, normalVision, "SVM_NORMAL_VISION");
        AddIf(extras, Cosmetics, "SVM_COSMETIC");
        Shader shader = seeThroughPass ? SeeThroughShaderFor(light, blend, entry.TwoSided, extras) : ShaderFor(light, blend, entry.TwoSided, extras);

        var material = new ShaderMaterial { Shader = shader, ResourceName = entry.Name };
        if (sky && blend is not (MaterialBlend.Opaque or MaterialBlend.Masked))
        {
            // Before every other transparent surface, as the sky's picture lay under them all. Solid sky keeps the
            // default: its squeezed depth already puts it behind the world, and Godot sorts opaque draws by priority
            // too, so a lowest priority would draw it first in the depth pre-pass, before anything could hide it.
            material.RenderPriority = (int)Material.RenderPriorityMin;
        }

        Texture2D? diffuse = combine is not null ? _textures.Get(combine.First.Texture, mipmaps: true)
            : flipbook is not null ? _textures.Get(flipbook.Texture, mipmaps: true)
            : _textures.Get(entry.Texture, Mipmapped(entry, "Diffuse"));
        SetTexture(material, "tex_diffuse", diffuse);
        if (blend == MaterialBlend.Additive &&
            string.Equals(entry.Source, "MHOsfx_xbox.FX.rayonlum", StringComparison.OrdinalIgnoreCase))
        {
            // The stacked beam04 surfaces in the hospital shafts and ClarD overexpose the wall.
            // Calibrated against the original MounE shaft, with its sign and blue cast visible.
            material.SetShaderParameter("additive_gain", 0.1f);
        }

        if (entry.NormalTexture is { } normal) SetTexture(material, "tex_normal", _textures.Get(normal));
        ApplyLayers(material, entry, combine, flipbook);
        BindSequences(material, entry);
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

            if (selfIllumLayer)
            {
                SetLayer(material, "si", entry.SelfIllumLayer!);
                SetLayer(material, "sim", entry.SelfIllumMaskLayer!);
            }
        }

        if (env)
        {
            if (entry.SpecMask is not null)
            {
                SetTexture(material, "tex_spec_mask", _textures.Get(entry.SpecMask, Mipmapped(entry, "SpecularityMask")));
            }

            if (entry.EnvTexture is { } envTexture)
            {
                SetTexture(material, "tex_env", _textures.Get(envTexture, mipmaps: false));
                // A world-space TexEnvMap (ClubD's terrace glass, CLU_envmap01): the reflection is turned to world space first.
                if (entry.EnvWorld)
                {
                    material.SetShaderParameter("env_world", 1);
                }

                // Normal-view TexEnvMap uses R.xy for skeletal meshes too (game 0x1096d6f0, source 11).
                // The EMF scene uses its own texture matrix (RenderK.EmfReflectionUv).
            }

            if (entry.SpecMaskLayer is { } sm)
            {
                SetLayer(material, "sm", sm);
            }

            // EnvColor is the env map's mean colour in linear light; the frame works in display values.
            Vector3 c = entry.EnvColor!.Value;
            material.SetShaderParameter("env_color", new Vector3(ToDisplay(c.X), ToDisplay(c.Y), ToDisplay(c.Z)));
        }

        // A normal vision twin draws no heat or EMF.
        if (!normalVision)
        {
            ApplyVision(material, entry);
        }

        if (uvAnim)
        {
            UvAnim a = entry.UvAnim!;
            material.SetShaderParameter("uv_anim_type", a.Code);
            material.SetShaderParameter("uv_anim_a", a.A);
            material.SetShaderParameter("uv_anim_b", a.B);
            material.SetShaderParameter("uv_anim_c", a.C);
            if (diffuse is not null)
            {
                material.SetShaderParameter("tex_size", new Vector2(diffuse.GetWidth(), diffuse.GetHeight()));
            }
        }

        if (entry.Authored is { } authored) AuthoredMaterialBinding.Apply(material, authored, file => _textures.Get(file), (slot, binding) => _textures.GetAuthored(slot, binding, authored.Animation));
        if (entry.AuthoredDirection is { } direction)
        { SetTexture(material, "am_bakedDirection", _textures.Get(direction)); material.SetShaderParameter("am_hasBakedDirection", true); }
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

        string? texture = _doc.TextureOf(entry.Source, input)
            ?? (entry.Source is null || VisionLookup == VisionLookup.SkinBase ? entry.Name : null);
        return Pack.MipLevels(texture) is not int levels || levels > 1;
    }

    /// <summary>The heat and EMF textures of <paramref name="entry"/>'s vision modifiers, or its own masks.</summary>
    private void ApplyVision(ShaderMaterial material, MaterialEntry entry)
    {
        VisionTextures? vision = VisionLookup switch
        {
            // A cosmetic camo is named "<skin>+<pattern>": it keeps the heat and EMF textures of the skin it repaints.
            VisionLookup.SkinBase => Pack?.VisionOfModifierName(entry.Source)
                ?? Pack?.VisionOfSkin(entry.Name is { } skin && skin.IndexOf('+') is var plus and > 0 ? skin[..plus] : entry.Name),
            VisionLookup.ModifierName => Pack?.VisionOfModifierName(entry.Name),
            _ => Pack?.VisionOf(entry.Source),
        };
        if (vision is null && entry.Name is { } name && OwnVision?.Invoke(name) is { } masks)
        {
            ApplyVision(material, masks);
        }
        else
        {
            ApplyVision(material, vision);
        }
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

    private void ApplyVision(ShaderMaterial material, VisionMasks masks)
    {
        material.SetShaderParameter("tex_heat", masks.Heat);
        material.SetShaderParameter("heat_mode", 1);
        material.SetShaderParameter("tex_emf", masks.Emf);
        material.SetShaderParameter("emf_mode", 1);
        VisionMaterials++;
    }

    /// <summary>
    /// The inputs evaluated on their own (docs/RENDERING.md#materials): a moving Opacity, the Combiner's inputs, the
    /// flipbook atlas, and the transform that takes the mesh UVs back to their own for the separate layers.
    /// </summary>
    private void ApplyLayers(ShaderMaterial material, MaterialEntry entry, CombineEntry? combine, FlipbookEntry? flipbook)
    {
        if (entry.Opacity is not null || entry.SpecMaskLayer is not null || entry.SelfIllumLayer is not null || entry.Sequences.Count > 0)
        {
            UvAffine undo = entry.UvMatrix?.Inverse() ?? UvAffine.Identity;
            material.SetShaderParameter("base_row_u", undo.RowU);
            material.SetShaderParameter("base_row_v", undo.RowV);
        }

        if (entry.Opacity is { } op)
        {
            SetTexture(material, "tex_opacity", _textures.Get(op.Texture, mipmaps: true));
            SetLayer(material, "op", op);
            material.SetShaderParameter("op_lum", entry.OpacityLum ? 1 : 0);
        }

        if (combine is not null)
        {
            SetTexture(material, "tex_c2", _textures.Get(combine.Second.Texture, mipmaps: true));
            SetLayer(material, "c1", combine.First);
            SetLayer(material, "c2", combine.Second);
            if (combine.Mask is { } mask)
            {
                SetTexture(material, "tex_cmask", _textures.Get(mask.Texture, mipmaps: true));
                SetLayer(material, "cm", mask);
                material.SetShaderParameter("cmask_on", 1);
            }

            material.SetShaderParameter("combine", new Vector4I(combine.Op, combine.AlphaOp, (int)combine.Modulate, combine.InvertMask ? 1 : 0));
            if (combine.Env2)
            {
                // Material2 a TexEnvMap (ClarD / Sche* puddle ripples): by the reflection, playing its frames.
                FlipbookEntry? f = combine.Flip2;
                material.SetShaderParameter("c2_env", 1);
                material.SetShaderParameter("c2_flip", f is null ? new Vector4(1f, 1f, 1f, 0f) : new Vector4(f.Cols, f.Rows, f.Frames, f.Fps));
            }
        }

        if (flipbook is not null)
        {
            material.SetShaderParameter("flipbook", new Vector4(flipbook.Cols, flipbook.Rows, flipbook.Frames, flipbook.Fps));
        }
    }

    /// <summary>
    /// A layer's animation and static transforms, as the shader's &lt;prefix&gt;_anim_*, &lt;prefix&gt;_row_* and (when the
    /// layer has one) &lt;prefix&gt;_post_* uniforms.
    /// </summary>
    private static void SetLayer(ShaderMaterial material, string prefix, MaterialLayer layer)
    {
        UvAffine m = layer.Matrix ?? UvAffine.Identity;
        material.SetShaderParameter(prefix + "_row_u", m.RowU);
        material.SetShaderParameter(prefix + "_row_v", m.RowV);
        if (layer.Anim is { Code: > 0 } a)
        {
            material.SetShaderParameter(prefix + "_anim_type", a.Code);
            material.SetShaderParameter(prefix + "_anim_a", a.A);
            material.SetShaderParameter(prefix + "_anim_b", a.B);
            material.SetShaderParameter(prefix + "_anim_c", a.C);
        }

        if (layer.Post is { } post)
        {
            material.SetShaderParameter(prefix + "_post_u", post.RowU);
            material.SetShaderParameter(prefix + "_post_v", post.RowV);
        }
    }

    private static void AddIf(List<string> list, bool condition, string define)
    {
        if (condition)
        {
            list.Add(define);
        }
    }

    /// <summary>Stencil value solid BSP surfaces write (MapProjectors reads the stencil).</summary>
    public const int StencilBsp = 1;

    /// <summary>Stencil value solid vertex-lit (baked) static mesh surfaces write: bProjectStaticMesh off skips it.</summary>
    public const int StencilStaticMesh = 2;

    /// <summary>Stencil value other solid world surfaces write (actor-lit meshes).</summary>
    public const int StencilOther = 3;

    /// <summary>
    /// Stencil value the merc's first-person arms and rifle write over their own (Shaders/svm_first_person_mask.gdshader):
    /// his laser beam skips those pixels, as the original's weapon overlay covers it.
    /// </summary>
    public const int StencilFirstPerson = 4;

    /// <summary>
    /// The process-wide Shader of <paramref name="code"/>, made (named <paramref name="name"/>, then given to
    /// <paramref name="init"/>) on first use. Callers must not change it: other materials use the same Shader.
    /// Identical code is one Shader (one RID), so every map and model shares its pipelines; its responsive smoke
    /// variant follows the match (<see cref="ResponsiveSmokeShaders"/>).
    /// </summary>
    internal static Shader SharedShader(string code, string name, Action<Shader>? init = null) =>
        ResponsiveSmokeShaders.Shared(code, name, init);

    private Shader ShaderFor(LightSource light, MaterialBlend blend, bool twoSided, List<string> extras)
    {
        bool coverage = blend == MaterialBlend.Masked && AlphaToCoverage;
        string key = $"{light}|{blend}|{twoSided}|{coverage}|{StencilMarks}|{string.Join(',', extras)}";
        if (_shaders.TryGetValue(key, out Shader? shader))
        {
            return ResponsiveSmokeShaders.Current(shader);
        }

        bool sky = extras.Contains("SVM_SKY_ZONE");
        var modes = new List<string> { "unshaded", "fog_disabled", twoSided ? "cull_disabled" : "cull_back" };
        if (sky)
        {
            // The template places sky surfaces itself (SVM_SKY_ZONE).
            modes.Add("skip_vertex_transform");
        }

        switch (blend)
        {
            case MaterialBlend.Translucent:
                modes.Add(extras.Contains("ENV_SEPARATE") ? "blend_premul_alpha" : "blend_mix");
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

        if (coverage)
        {
            modes.Add("alpha_to_coverage");
        }

        var code = new StringBuilder();
        code.Append("shader_type spatial;\nrender_mode ").Append(string.Join(", ", modes)).Append(";\n");
        // The sky zone leaves no marks: the main view's stencil stays as the sky backdrop left it.
        if (blend is MaterialBlend.Opaque or MaterialBlend.Masked && StencilMarks && !sky)
        {
            // Solid surfaces mark what they are in the stencil: projectors with bProjectStaticMesh off skip static meshes.
            code.Append("stencil_mode write, compare_always, ").Append(light switch
            {
                LightSource.Lightmap => StencilBsp,
                LightSource.Vertex => StencilStaticMesh,
                _ => StencilOther,
            }).Append(";\n");
        }

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
        foreach (string extra in extras)
        {
            Define(code, extra);
        }

        Define(code, coverage ? "ALPHA_COVERAGE" : null);

        if (extras.Contains("AUTHORED_MATERIAL")) code.Append(AuthoredMaterialBinding.ShaderCode);

        code.Append("#include \"").Append(TemplatePath).Append("\"\n");
        // The code holds the blend and every extra, so every key that shares a Shader gives it the same meta.
        bool shadowAnimated = blend == MaterialBlend.Masked &&
            (extras.Contains("UV_ANIM") || extras.Contains("FLIPBOOK") || extras.Contains("OPACITY_LAYER") || extras.Contains("COMBINE") || extras.Contains("SEQUENCE_D") || extras.Contains("AUTHORED_MATERIAL"));
        shader = SharedShader(code.ToString(), "svm_world " + key, s => s.SetMeta("svm_shadow_animated", shadowAnimated));
        _shaders[key] = shader;
        return shader;
    }

    /// <summary>
    /// The blended pass of a partly see-through solid or masked surface (<see cref="GetSeeThrough"/>): drawn only in
    /// thermal and EMF, after the solid scene, blended by the actor's opacity and writing no depth. A masked material
    /// keeps its alpha cut.
    /// </summary>
    private Shader SeeThroughShaderFor(LightSource light, MaterialBlend blend, bool twoSided, List<string> extras)
    {
        string key = $"seethrough|{light}|{blend}|{twoSided}|{string.Join(',', extras)}";
        if (_shaders.TryGetValue(key, out Shader? shader))
        {
            return ResponsiveSmokeShaders.Current(shader);
        }

        var code = new StringBuilder();
        code.Append("shader_type spatial;\nrender_mode unshaded, fog_disabled, ").Append(twoSided ? "cull_disabled" : "cull_back")
            .Append(", blend_mix, depth_draw_never;\n");
        Define(code, light switch
        {
            LightSource.Lightmap => "LIGHT_LIGHTMAP",
            LightSource.Vertex => "LIGHT_VERTEX",
            LightSource.Actor => "LIGHT_ACTOR",
            _ => null,
        });
        Define(code, "VISION_SEE_THROUGH");
        Define(code, blend == MaterialBlend.Masked ? "SEE_THROUGH_CUT" : null);
        foreach (string extra in extras)
        {
            Define(code, extra);
        }

        code.Append("#include \"").Append(TemplatePath).Append("\"\n");
        shader = SharedShader(code.ToString(), "svm_world " + key);
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
