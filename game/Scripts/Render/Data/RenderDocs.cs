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
}

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
    string LocationName);

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
                    MaterialsDoc.Bool(l, "own_zone_only")));
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
                    MaterialsDoc.Str(z, "location_name") ?? ""));
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
