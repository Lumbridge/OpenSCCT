using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace OpenScctGame.Render.Data;

/// <summary>How a material blends with the frame (UE2 Shader OutputBlending / FinalBlend, docs/RENDERING.md#materials).</summary>
public enum MaterialBlend
{
    Opaque,

    /// <summary>Alpha test at <see cref="RenderK.MaskedAlphaRef"/>.</summary>
    Masked,

    /// <summary>SRCALPHA, INVSRCALPHA through the texture alpha.</summary>
    Translucent,

    /// <summary>ONE, ONE (also stands in for the screen blend ONE, INVSRCCOLOR).</summary>
    Additive,

    /// <summary>DESTCOLOR, SRCCOLOR: dest x src x 2.</summary>
    Modulate,

    /// <summary>ZERO, INVSRCCOLOR: dest x (1 - src).</summary>
    Darken,
}

/// <summary>An animated texture coordinate modifier (TexPanner, TexFriendlyPanner, TexOscillator, TexRotator).</summary>
public sealed record UvAnim(string Type, Vector4 A, Vector4 B)
{
    /// <summary>Shader code of the type: 1 panner (A.xy direction, A.z rate), 2 oscillator, 3 rotator.</summary>
    public int Code => Type switch
    {
        "panner" or "friendly_panner" => 1,
        "oscillator" => 2,
        "rotator" => 3,
        _ => 0,
    };

    public static UvAnim? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("type", out JsonElement typeEl))
        {
            return null;
        }

        string type = typeEl.GetString() ?? "";
        switch (type)
        {
            case "panner":
            {
                // PanDirection is a rotator; the pan is its unit vector's X and Y times PanRate per second.
                JsonElement d = e.GetProperty("direction");
                float pitch = RotatorRadians(d[0].GetInt32());
                float yaw = RotatorRadians(d[1].GetInt32());
                float rate = Num(e, "rate");
                float dx = MathF.Cos(pitch) * MathF.Cos(yaw);
                float dy = MathF.Cos(pitch) * MathF.Sin(yaw);
                return new UvAnim(type, new Vector4(dx, dy, rate, 0f), Vector4.Zero);
            }

            case "friendly_panner":
            {
                float rate = Num(e, "rate");
                float u = Num(e, "pan_u") / MathF.Max(1e-6f, Num(e, "scale_u", 1f));
                float v = Num(e, "pan_v") / MathF.Max(1e-6f, Num(e, "scale_v", 1f));
                return new UvAnim(type, new Vector4(u, v, rate, 0f), Vector4.Zero);
            }

            case "oscillator":
                return new UvAnim(type,
                    new Vector4(Num(e, "UOscillationAmplitude"), Num(e, "UOscillationRate"), Num(e, "UOscillationType"), 0f),
                    new Vector4(Num(e, "VOscillationAmplitude"), Num(e, "VOscillationRate"), Num(e, "VOscillationType"), 0f));

            case "rotator":
            {
                JsonElement r = e.GetProperty("rotation");
                float yawRate = RotatorRadians(r[1].GetInt32());
                return new UvAnim(type, new Vector4(yawRate, Num(e, "rotation_type"), Num(e, "u_offset"), Num(e, "v_offset")), Vector4.Zero);
            }

            default:
                return null;
        }
    }

    private static float RotatorRadians(int units) => units * (MathF.PI * 2f / 65536f);

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}

/// <summary>
/// A static texture-coordinate transform (TexScaler, fixed TexRotator): u' = a u + c v + e, v' = b u + d v + f, kept as
/// the rows (a, c, e) and (b, d, f) the shader dots with (u, v, 1).
/// </summary>
public readonly record struct UvAffine(Vector3 RowU, Vector3 RowV)
{
    public static UvAffine Identity => new(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));

    /// <summary>The exporter's [a, b, c, d, e, f], or null.</summary>
    public static UvAffine? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 6)
        {
            return null;
        }

        float a = e[0].GetSingle(), b = e[1].GetSingle(), c = e[2].GetSingle();
        float d = e[3].GetSingle(), x = e[4].GetSingle(), y = e[5].GetSingle();
        return new UvAffine(new Vector3(a, c, x), new Vector3(b, d, y));
    }

    /// <summary>The transform that undoes this one (identity when it cannot be undone).</summary>
    public UvAffine Inverse()
    {
        float a = RowU.X, c = RowU.Y, e = RowU.Z, b = RowV.X, d = RowV.Y, f = RowV.Z;
        float det = (a * d) - (b * c);
        if (MathF.Abs(det) < 1e-8f)
        {
            return Identity;
        }

        float ia = d / det, ic = -c / det, ib = -b / det, id = a / det;
        return new UvAffine(new Vector3(ia, ic, -((ia * e) + (ic * f))), new Vector3(ib, id, -((ib * e) + (id * f))));
    }

    /// <summary>This transform applied after <paramref name="first"/>.</summary>
    public UvAffine After(UvAffine first)
    {
        Vector3 u = first.RowU, v = first.RowV;
        return new UvAffine(
            new Vector3((RowU.X * u.X) + (RowU.Y * v.X), (RowU.X * u.Y) + (RowU.Y * v.Y), (RowU.X * u.Z) + (RowU.Y * v.Z) + RowU.Z),
            new Vector3((RowV.X * u.X) + (RowV.Y * v.X), (RowV.X * u.Y) + (RowV.Y * v.Y), (RowV.X * u.Z) + (RowV.Y * v.Z) + RowV.Z));
    }
}

/// <summary>One input of a material evaluated on its own: its texture, animated modifier and static transform.</summary>
public sealed record MaterialLayer(string Texture, UvAnim? Anim, UvAffine? Matrix);

/// <summary>
/// A UE2 Combiner whose inputs move on their own (materials.json flags "combine"): Material1, Material2 and the Mask with
/// their own modifiers, combined per pixel by CombineOperation / AlphaOperation (docs/RENDERING.md#materials).
/// <see cref="Env2"/>: Material2 is a TexEnvMap, looked up by the reflection; <see cref="Flip2"/> its AnimNext chain as
/// an atlas (Second.Texture is then the atlas).
/// </summary>
public sealed record CombineEntry(int Op, int AlphaOp, float Modulate, bool InvertMask, MaterialLayer First, MaterialLayer Second,
    MaterialLayer? Mask, bool Env2 = false, FlipbookEntry? Flip2 = null)
{
    public static CombineEntry? From(JsonElement c)
    {
        if (c.ValueKind != JsonValueKind.Object || MaterialsDoc.Str(c, "tex1") is not { } t1 || MaterialsDoc.Str(c, "tex2") is not { } t2)
        {
            return null;
        }

        MaterialLayer Layer(string tex, string suffix) => new(tex,
            c.TryGetProperty("anim" + suffix, out JsonElement a) ? UvAnim.From(a) : null,
            c.TryGetProperty("matrix" + suffix, out JsonElement m) ? UvAffine.From(m) : null);

        string? mask = MaterialsDoc.Str(c, "mask");
        return new CombineEntry(
            Int(c, "op"), Int(c, "alpha_op"), Math.Max(1, Int(c, "mod")), MaterialsDoc.Bool(c, "invert_mask"),
            Layer(t1, "1"), Layer(t2, "2"), mask is null ? null : Layer(mask, "Mask"),
            MaterialsDoc.Bool(c, "env2"),
            c.TryGetProperty("flip2", out JsonElement f2) && f2.ValueKind == JsonValueKind.Object
                ? new FlipbookEntry(t2, Int(f2, "cols"), Int(f2, "rows"), Int(f2, "frames"), Num(f2, "fps"))
                : null);
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static float Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;
}

/// <summary>An animated texture (Texture.AnimNext chain) as a row-major atlas of its frames, stepped at Fps.</summary>
public sealed record FlipbookEntry(string Texture, int Cols, int Rows, int Frames, float Fps)
{
    public static FlipbookEntry? From(JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Object || MaterialsDoc.Str(f, "texture") is not { } tex)
        {
            return null;
        }

        int cols = f.TryGetProperty("cols", out JsonElement c) ? c.GetInt32() : 1;
        int rows = f.TryGetProperty("rows", out JsonElement r) ? r.GetInt32() : 1;
        int frames = f.TryGetProperty("frames", out JsonElement n) ? n.GetInt32() : 1;
        float fps = f.TryGetProperty("fps", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetSingle() : 30f;
        return frames > 1 && cols > 0 && rows > 0 ? new FlipbookEntry(tex, cols, rows, frames, fps) : null;
    }
}

/// <summary>One material of render/materials.json, keyed by its glTF material name.</summary>
public sealed class MaterialEntry
{
    public required string Name { get; init; }

    public string? Source { get; init; }

    public string? Texture { get; init; }

    public bool Masked { get; init; }

    public bool Translucent { get; init; }

    public bool TwoSided { get; init; }

    public bool Unlit { get; init; }

    public string? SelfIllum { get; init; }

    public string? SelfIllumMask { get; init; }

    public string? SpecMask { get; init; }

    public Vector3? EnvColor { get; init; }

    public UvAnim? UvAnim { get; init; }

    /// <summary>The static transform the exporter baked into the mesh UVs; separate layers undo it.</summary>
    public UvAffine? UvMatrix { get; init; }

    /// <summary>A Shader.Opacity that moves on its own, sampled apart from the Diffuse (alpha, or luminance when OpacityLum).</summary>
    public MaterialLayer? Opacity { get; init; }

    public bool OpacityLum { get; init; }

    /// <summary>The SpecularityMask's own modifiers when they differ from the Diffuse's (the mask texture is SpecMask).</summary>
    public MaterialLayer? SpecMaskLayer { get; init; }

    /// <summary>The env map's 2D texture, looked up by the camera-space reflection; null keeps EnvColor.</summary>
    public string? EnvTexture { get; init; }

    /// <summary>"env_world": the TexEnvMap is world space (TCS_WorldEnvMapCoords): looked up by the world-space reflection.</summary>
    public bool EnvWorld { get; init; }

    public CombineEntry? Combine { get; init; }

    public FlipbookEntry? Flipbook { get; init; }

    public bool Mirror { get; init; }

    public bool Projector { get; init; }

    /// <summary>Set from the map's additive / modulate lists.</summary>
    public MaterialBlend? ListBlend { get; set; }

    /// <summary>The lightmap the glTF material names (scct_lightmap), for BSP surfaces.</summary>
    public string? Lightmap { get; set; }

    public bool Skip { get; init; }

    public MaterialBlend Blend =>
        ListBlend ?? (Translucent ? MaterialBlend.Translucent : Masked ? MaterialBlend.Masked : MaterialBlend.Opaque);
}

/// <summary>render/materials.json.</summary>
public sealed class MaterialsDoc
{
    private readonly Dictionary<string, MaterialEntry> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Class, JsonElement Props)> _graph = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MaterialEntry> Materials => _byName;

    public MaterialEntry? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// The texture behind a material of the graph for one input (Diffuse, SelfIllumination, SelfIlluminationMask,
    /// SpecularityMask): Shaders and lit materials name it, modifiers and FinalBlends wrap a Material, Combiners take
    /// their first material. Null when the graph does not lead to a Texture.
    /// </summary>
    public string? TextureOf(string? materialPath, string input = "Diffuse")
    {
        string? path = materialPath;
        for (int depth = 0; path is not null && depth < 16; depth++)
        {
            if (!_graph.TryGetValue(path, out (string Class, JsonElement Props) node))
            {
                return null;
            }

            string cls = node.Class[(node.Class.LastIndexOf('.') + 1)..];
            if (cls == "Texture")
            {
                return path;
            }

            string member = cls switch
            {
                "Shader" or "LitSpecularMaterial" => depth == 0 ? input : "Diffuse",
                "Combiner" => "Material1",
                _ => "Material",
            };
            path = node.Props.ValueKind == JsonValueKind.Object && node.Props.TryGetProperty(member, out JsonElement r)
                && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("$asset", out JsonElement a)
                ? a.GetString()
                : null;
        }

        return null;
    }

    public static MaterialsDoc Load(string path)
    {
        var doc = new MaterialsDoc();
        if (!File.Exists(path))
        {
            return doc;
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = json.RootElement;
        if (root.TryGetProperty("materials", out JsonElement materials))
        {
            foreach (JsonProperty m in materials.EnumerateObject())
            {
                JsonElement f = m.Value.TryGetProperty("flags", out JsonElement flags) ? flags : default;
                bool has = f.ValueKind == JsonValueKind.Object;
                doc._byName[m.Name] = new MaterialEntry
                {
                    Name = m.Name,
                    Source = m.Value.TryGetProperty("source", out JsonElement s) ? s.GetString() : null,
                    Texture = has ? Str(f, "texture") : null,
                    Masked = has && Bool(f, "masked"),
                    Translucent = has && Bool(f, "translucent"),
                    TwoSided = has && Bool(f, "two_sided"),
                    Unlit = has && Bool(f, "unlit"),
                    SelfIllum = has ? Str(f, "self_illum") : null,
                    SelfIllumMask = has ? Str(f, "self_illum_mask") : null,
                    SpecMask = has ? Str(f, "spec_mask") : null,
                    EnvColor = has && f.TryGetProperty("env_color", out JsonElement env) && env.ValueKind == JsonValueKind.Array
                        ? new Vector3(env[0].GetSingle(), env[1].GetSingle(), env[2].GetSingle())
                        : null,
                    UvAnim = has && f.TryGetProperty("uv_anim", out JsonElement uv) ? UvAnim.From(uv) : null,
                    UvMatrix = has && f.TryGetProperty("uv_matrix", out JsonElement um) ? UvAffine.From(um) : null,
                    Opacity = has ? LayerOf(f, "opacity", Str(f, "opacity")) : null,
                    OpacityLum = has && Bool(f, "opacity_lum"),
                    SpecMaskLayer = has && (f.TryGetProperty("spec_mask_anim", out _) || f.TryGetProperty("spec_mask_matrix", out _))
                        ? LayerOf(f, "spec_mask", Str(f, "spec_mask"))
                        : null,
                    EnvTexture = has ? Str(f, "env_texture") : null,
                    EnvWorld = has && Bool(f, "env_world"),
                    Combine = has && f.TryGetProperty("combine", out JsonElement comb) ? CombineEntry.From(comb) : null,
                    Flipbook = has && f.TryGetProperty("flipbook", out JsonElement fb) ? FlipbookEntry.From(fb) : null,
                    Mirror = has && Bool(f, "mirror"),
                    Projector = has && Bool(f, "projector"),
                    Skip = has && Bool(f, "skip"),
                };
            }
        }

        if (root.TryGetProperty("graph", out JsonElement graph) && graph.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty g in graph.EnumerateObject())
            {
                string cls = Str(g.Value, "class") ?? "";
                JsonElement props = g.Value.TryGetProperty("props", out JsonElement gp) ? gp.Clone() : default;
                doc._graph[g.Name] = (cls, props);
            }
        }

        ApplyList(doc, root, "additive", _ => MaterialBlend.Additive);
        ApplyList(doc, root, "modulate", e => Str(e, "mode") == "darken" ? MaterialBlend.Darken : MaterialBlend.Modulate);
        return doc;
    }

    /// <summary>A layer of the flags: the texture plus "&lt;prefix&gt;_anim" and "&lt;prefix&gt;_matrix".</summary>
    private static MaterialLayer? LayerOf(JsonElement f, string prefix, string? texture) =>
        texture is null
            ? null
            : new MaterialLayer(texture,
                f.TryGetProperty(prefix + "_anim", out JsonElement a) ? UvAnim.From(a) : null,
                f.TryGetProperty(prefix + "_matrix", out JsonElement m) ? UvAffine.From(m) : null);

    private static void ApplyList(MaterialsDoc doc, JsonElement root, string list, Func<JsonElement, MaterialBlend> blendOf)
    {
        if (!root.TryGetProperty(list, out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var safe = new Dictionary<string, MaterialEntry>(StringComparer.Ordinal);
        foreach (MaterialEntry m in doc._byName.Values)
        {
            safe.TryAdd(Safe(m.Name), m);
        }

        foreach (JsonElement e in entries.EnumerateArray())
        {
            string? key = Str(e, "key");
            string? name = Str(e, "name");
            MaterialEntry? m = (key is not null ? doc.Find(key) : null) ?? (name is not null ? safe.GetValueOrDefault(name) : null);
            if (m is not null)
            {
                m.ListBlend = blendOf(e);
            }
        }
    }

    /// <summary>The exporter's file-safe form of a key: anything but letters, digits, '_' and '-' becomes '_'.</summary>
    public static string Safe(string key)
    {
        char[] chars = key.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    internal static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>A light of render/lighting.json, as the actor lighting needs it (UE units).</summary>
public sealed record LightEntry(
    string Name,
    OpenScct.Core.FVector Location,
    OpenScct.Core.FRotator Rotation,
    float Brightness,
    byte Hue,
    byte Saturation,
    float Radius,
    byte Cone,
    int Effect,
    int LightType,
    bool ApplyInGame,
    bool ApplyStatic,
    bool Dynamic,
    bool InitiallyOn,
    string? Zone,
    float Period,
    float Phase,
    bool OwnZoneOnly)
{
    /// <summary>Zone index of <see cref="Zone"/>, set once the zones are read; -1 when unknown.</summary>
    public int ZoneIndex { get; set; } = -1;

    /// <summary>A number made from the name, for per-light noise (the flicker).</summary>
    public int Seed { get; } = Name.GetHashCode(StringComparison.Ordinal);

    /// <summary>The Echelon frustum of an LE_ESpotShadow light (zero for other lights).</summary>
    public EchelonSpot? Echelon { get; init; }
}

/// <summary>
/// The Echelon light values of an LE_ESpotShadow map light: EchelonRange, SpotWidth / SpotHeight at that range,
/// NearPlaneRatio, EchelonNormalInfluence, and its EchelonLightingMaterial (ProjectedLightingTexture, AttenuationType).
/// </summary>
public sealed record EchelonSpot(float Range, float Width, float Height, float NearPlaneRatio, float NormalInfluence, string? Texture,
    int Attenuation);

/// <summary>A light's corona (bCorona) of render/lighting.json: Skins[0] drawn as a camera-facing additive halo.</summary>
public sealed record CoronaEntry(
    string Light,
    OpenScct.Core.FVector Location,
    OpenScct.Core.FRotator Rotation,
    string? Texture,
    Color Colour,
    float MinSize,
    float MaxSize,
    float DrawScale,
    Vector2 Scale3D,
    float Rotation01,
    float RotationOffset,
    float Radius,
    float Cone,
    bool Orient,
    bool On,
    float ScaleGlow);

/// <summary>A zone of render/lighting.json.</summary>
public sealed record ZoneEntry(
    string Name,
    int ZoneIndex,
    bool Sky,
    Color Ambient,
    bool Fog,
    Color FogColor,
    float FogStart,
    float FogEnd,
    string LocationName)
{
    /// <summary>ZoneInfo.DistanceFogBlendTime: seconds the view's fog takes to move to this zone's (0 at once).</summary>
    public float FogBlendTime { get; init; } = 1f;

    /// <summary>ZoneInfo.bClearToFogColor: with <see cref="Fog"/>, the main view clears to <see cref="FogColor"/>.</summary>
    public bool ClearToFog { get; init; }
}

/// <summary>render/lighting.json.</summary>
public sealed class LightingDoc
{
    public List<LightEntry> Lights { get; } = [];

    public List<ZoneEntry> Zones { get; } = [];

    public List<CoronaEntry> Coronas { get; } = [];

    public List<string> Lightmaps { get; } = [];

    /// <summary>The SkyZoneInfo's location in UE space, or null when the map has no sky zone.</summary>
    public OpenScct.Core.FVector? SkyLocation { get; private set; }

    public OpenScct.Core.FRotator SkyRotation { get; private set; }

    public int SkyZone { get; private set; } = -1;

    /// <summary>LevelInfo.GlowThreshold (224 when the map does not store it).</summary>
    public int GlowThreshold { get; private set; } = 224;

    public ZoneEntry? ZoneByIndex(int index)
    {
        foreach (ZoneEntry z in Zones)
        {
            if (z.ZoneIndex == index)
            {
                return z;
            }
        }

        return null;
    }

    public static LightingDoc Load(string path)
    {
        var doc = new LightingDoc();
        if (!File.Exists(path))
        {
            return doc;
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = json.RootElement;
        if (root.TryGetProperty("lights", out JsonElement lights))
        {
            foreach (JsonElement l in lights.EnumerateArray())
            {
                if (l.TryGetProperty("corona", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
                {
                    JsonElement col = c.GetProperty("color");
                    JsonElement s3 = c.TryGetProperty("scale3d", out JsonElement s3e) ? s3e : default;
                    bool lightOn = (int)Num(l, "ltype", 1f) != 0 && !(l.TryGetProperty("initially_on", out JsonElement io) && io.ValueKind == JsonValueKind.False);
                    doc.Coronas.Add(new CoronaEntry(
                        MaterialsDoc.Str(l, "name") ?? "",
                        Vec(l, "location"),
                        Rot(l, "rotation"),
                        MaterialsDoc.Str(c, "texture_uri"),
                        new Color(col[0].GetSingle(), col[1].GetSingle(), col[2].GetSingle()),
                        Num(c, "min"),
                        Num(c, "max", 1000f),
                        Num(c, "draw_scale", 1f),
                        s3.ValueKind == JsonValueKind.Array ? new Vector2(s3[0].GetSingle(), s3[1].GetSingle()) : Vector2.One,
                        Num(c, "rotation"),
                        Num(c, "rotation_offset"),
                        Num(c, "radius"),
                        Num(c, "cone"),
                        MaterialsDoc.Bool(c, "orient"),
                        MaterialsDoc.Bool(c, "on") && lightOn,
                        Num(c, "scale_glow", 1f)));
                }

                doc.Lights.Add(new LightEntry(
                    MaterialsDoc.Str(l, "name") ?? "",
                    Vec(l, "location"),
                    Rot(l, "rotation"),
                    Num(l, "brightness", 64f),
                    (byte)Num(l, "hue"),
                    (byte)Num(l, "saturation", 255f),
                    Num(l, "radius", 64f),
                    (byte)Num(l, "cone", 128f),
                    (int)Num(l, "effect"),
                    (int)Num(l, "ltype", 1f),
                    MaterialsDoc.Bool(l, "apply_ingame"),
                    MaterialsDoc.Bool(l, "apply_static"),
                    MaterialsDoc.Bool(l, "dynamic"),
                    !l.TryGetProperty("initially_on", out JsonElement on) || on.ValueKind != JsonValueKind.False,
                    MaterialsDoc.Str(l, "zone"),
                    Num(l, "light_period", 32f),
                    Num(l, "light_phase"),
                    MaterialsDoc.Bool(l, "own_zone_only"))
                {
                    Echelon = EchelonOf(l),
                });
            }
        }

        if (root.TryGetProperty("zones", out JsonElement zones))
        {
            foreach (JsonElement z in zones.EnumerateArray())
            {
                doc.Zones.Add(new ZoneEntry(
                    MaterialsDoc.Str(z, "name") ?? "",
                    (int)Num(z, "zoneIndex", -1f),
                    MaterialsDoc.Str(z, "class") == "SkyZoneInfo",
                    Rgba8(z, "ambient"),
                    MaterialsDoc.Bool(z, "fog"),
                    Rgba8(z, "fog_color"),
                    Num(z, "fog_start"),
                    Num(z, "fog_end"),
                    MaterialsDoc.Str(z, "location_name") ?? "")
                {
                    FogBlendTime = Num(z, "fog_blend_time", 1f),
                    ClearToFog = MaterialsDoc.Bool(z, "clear_to_fog"),
                });
            }
        }

        foreach (LightEntry l in doc.Lights)
        {
            foreach (ZoneEntry z in doc.Zones)
            {
                if (string.Equals(z.Name, l.Zone, StringComparison.Ordinal))
                {
                    l.ZoneIndex = z.ZoneIndex;
                }
            }
        }

        if (root.TryGetProperty("lightmaps", out JsonElement lightmaps))
        {
            foreach (JsonElement l in lightmaps.EnumerateArray())
            {
                doc.Lightmaps.Add(l.GetString() ?? "");
            }
        }

        if (root.TryGetProperty("sky", out JsonElement sky) && sky.ValueKind == JsonValueKind.Object)
        {
            doc.SkyLocation = Vec(sky, "location");
            doc.SkyRotation = Rot(sky, "rotation");
            doc.SkyZone = (int)Num(sky, "zone", -1f);
        }

        if (root.TryGetProperty("look", out JsonElement look) && look.ValueKind == JsonValueKind.Object)
        {
            doc.GlowThreshold = (int)Num(look, "glow_threshold", 224f);
        }

        return doc;
    }

    /// <summary>
    /// The Echelon values of an LE_ESpotShadow light, with the Actor defaults (EchelonRange 1000, SpotWidth and
    /// SpotHeight 250, NearPlaneRatio 0.1, EchelonNormalInfluence 1) and EchAtten_Linear (EchelonLightingMaterial
    /// default) where absent.
    /// </summary>
    private static EchelonSpot? EchelonOf(JsonElement l)
    {
        if ((int)Num(l, "effect") != RenderK.LE_ESpotShadow)
        {
            return null;
        }

        float range = Num(l, "echelon_range");
        float width = Num(l, "spot_width");
        float height = Num(l, "spot_height");
        float influence = l.TryGetProperty("props", out JsonElement props) && props.ValueKind == JsonValueKind.Object
            ? Num(props, "EchelonNormalInfluence", 1f)
            : 1f;
        return new EchelonSpot(
            range > 0f ? range : 1000f,
            width > 0f ? width : 250f,
            height > 0f ? height : 250f,
            Num(l, "near_plane_ratio", 0.1f),
            influence,
            MaterialsDoc.Str(l, "echelon_texture_uri"),
            (int)Num(l, "echelon_attenuation", 1f));
    }

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static OpenScct.Core.FVector Vec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3)
        {
            return default;
        }

        return new OpenScct.Core.FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }

    private static OpenScct.Core.FRotator Rot(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v))
        {
            return default;
        }

        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3)
        {
            return new OpenScct.Core.FRotator(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32());
        }

        if (v.ValueKind == JsonValueKind.Object)
        {
            return new OpenScct.Core.FRotator((int)Num(v, "Pitch"), (int)Num(v, "Yaw"), (int)Num(v, "Roll"));
        }

        return default;
    }

    /// <summary>A colour stored as [R, G, B, A] bytes, as display values 0..1.</summary>
    private static Color Rgba8(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3)
        {
            return new Color(0f, 0f, 0f, 0f);
        }

        float a = v.GetArrayLength() > 3 ? v[3].GetSingle() / 255f : 1f;
        return new Color(v[0].GetSingle() / 255f, v[1].GetSingle() / 255f, v[2].GetSingle() / 255f, a);
    }
}
