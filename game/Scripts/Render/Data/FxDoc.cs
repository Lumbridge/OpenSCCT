using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScctGame.Render.Data;

/// <summary>One sub-emitter of an Emitter actor (render/fx.json emitters[].emitters[]): its class and its properties over the class defaults.</summary>
public sealed class FxSubEmitter
{
    internal FxSubEmitter(string objectPath, string cls, JsonElement props, JsonElement? defaults, string? texture)
    {
        ObjectPath = objectPath;
        Class = cls;
        Props = props;
        Defaults = defaults;
        Texture = texture;
    }

    public string ObjectPath { get; }

    /// <summary>Engine.SpriteEmitter, Engine.MeshEmitter, ...</summary>
    public string Class { get; }

    public JsonElement Props { get; }

    public JsonElement? Defaults { get; }

    /// <summary>The PNG of its Texture in the map's render folder, or null.</summary>
    public string? Texture { get; }

    public bool TryGet(string name, out JsonElement value)
    {
        if (Props.TryGetProperty(name, out value))
        {
            return true;
        }

        if (Defaults is { } d && d.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

/// <summary>An Emitter actor of render/fx.json.</summary>
public sealed class FxEmitter
{
    internal FxEmitter(string id, FVector location, FRotator rotation, string tag, JsonElement props, List<FxSubEmitter> emitters)
    {
        Id = id;
        Location = location;
        Rotation = rotation;
        Tag = tag;
        Props = props;
        Emitters = emitters;
    }

    public string Id { get; }

    public FVector Location { get; }

    public FRotator Rotation { get; }

    /// <summary>The actor's Tag: triggering an event of that name triggers the emitter.</summary>
    public string Tag { get; }

    public JsonElement Props { get; }

    public IReadOnlyList<FxSubEmitter> Emitters { get; }

    public float Float(string name, float fallback = 0f) =>
        Props.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}

/// <summary>
/// An ERainVolume of render/fx.json rain[] (UE units): its location (the floor of the box), DrawScale x DrawScale3D, and
/// the volume's m_RainSize, m_RainFalloff, m_RainFadeScale, m_DistanceToCamera, m_maxDistFromCamera, m_RainThickness,
/// m_RainMatScale, with the rain material's texture, blend and panner.
/// </summary>
public sealed record RainEntry(
    string Name,
    FVector Location,
    FVector Scale,
    FVector Size,
    float Falloff,
    float FadeScale,
    float DistanceToCamera,
    float MaxDistance,
    float Thickness,
    float MatScale,
    string? Texture,
    bool Additive,
    UvAnimRaw? Panner);

/// <summary>A TexFriendlyPanner's stored values (PanU, PanV, PanRate, scaleU, scaleV).</summary>
public sealed record UvAnimRaw(float PanU, float PanV, float Rate, float ScaleU, float ScaleV);

/// <summary>render/fx.json: the map's Emitter actors with their sub-emitters (spec/svmap/1.1).</summary>
public sealed class FxDoc
{
    private JsonDocument? _doc;

    public List<FxEmitter> Emitters { get; } = [];

    public List<RainEntry> Rain { get; } = [];

    /// <summary>Only the rain volumes of render/fx.json (empty when the file is missing).</summary>
    public static List<RainEntry> LoadRain(string path)
    {
        var rain = new List<RainEntry>();
        if (!File.Exists(path))
        {
            return rain;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        ReadRain(doc.RootElement, rain);
        return rain;
    }

    private static void ReadRain(JsonElement root, List<RainEntry> into)
    {
        if (!root.TryGetProperty("rain", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement a in list.EnumerateArray())
        {
            if (!a.TryGetProperty("rain", out JsonElement r) || r.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            UvAnimRaw? panner = null;
            if (r.TryGetProperty("uv_anim", out JsonElement anim) && anim.ValueKind == JsonValueKind.Object
                && MaterialsDoc.Str(anim, "type") == "friendly_panner")
            {
                panner = new UvAnimRaw(Num(anim, "pan_u"), Num(anim, "pan_v"), Num(anim, "rate", 0.1f), Num(anim, "scale_u", 1f), Num(anim, "scale_v", 1f));
            }

            into.Add(new RainEntry(
                MaterialsDoc.Str(a, "name") ?? "",
                Vec(a, "location", default),
                Vec(a, "scale", new FVector(1f, 1f, 1f)),
                Vec(r, "size", new FVector(250f, 250f, 450f)),
                Num(r, "falloff", 100f),
                Num(r, "fade_scale", -1f),
                Num(r, "distance_to_camera", 80f),
                Num(r, "max_dist", 1000f),
                Num(r, "thickness", 80f),
                Num(r, "mat_scale", 10f),
                MaterialsDoc.Str(r, "texture_uri"),
                !r.TryGetProperty("additive", out JsonElement add) || add.ValueKind != JsonValueKind.False,
                panner));
        }
    }

    private static FVector Vec(JsonElement e, string name, FVector fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3
            ? new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())
            : fallback;

    public static FxDoc Load(string path, string sharedRoot)
    {
        var fx = new FxDoc();
        if (!File.Exists(path))
        {
            return fx;
        }

        fx._doc = JsonDocument.Parse(File.ReadAllBytes(path));
        ReadRain(fx._doc.RootElement, fx.Rain);
        SharedClasses classes = SharedClasses.Load(sharedRoot);
        if (!fx._doc.RootElement.TryGetProperty("emitters", out JsonElement emitters))
        {
            return fx;
        }

        foreach (JsonProperty e in emitters.EnumerateObject())
        {
            JsonElement props = e.Value.TryGetProperty("props", out JsonElement p) ? p : default;
            var subs = new List<FxSubEmitter>();
            if (e.Value.TryGetProperty("emitters", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in list.EnumerateArray())
                {
                    string cls = MaterialsDoc.Str(s, "class") ?? "";
                    subs.Add(new FxSubEmitter(MaterialsDoc.Str(s, "object") ?? "", cls,
                        s.TryGetProperty("props", out JsonElement sp) ? sp : default, classes.DefaultsOf(cls), MaterialsDoc.Str(s, "texture")));
                }
            }

            FVector location = default;
            FRotator rotation = default;
            string tag = "Emitter";
            if (props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty("Location", out JsonElement l))
                {
                    location = new FVector(Num(l, "X"), Num(l, "Y"), Num(l, "Z"));
                }

                if (props.TryGetProperty("Rotation", out JsonElement r))
                {
                    rotation = new FRotator((int)Num(r, "Pitch"), (int)Num(r, "Yaw"), (int)Num(r, "Roll"));
                }

                tag = MaterialsDoc.Str(props, "Tag") ?? tag;
            }

            fx.Emitters.Add(new FxEmitter(e.Name, location, rotation, tag, props, subs));
        }

        return fx;
    }

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}

/// <summary>Effective class defaults of the shared class pack (.content/shared/classes.json), loaded once.</summary>
public sealed class SharedClasses
{
    private static readonly ConcurrentDictionary<string, SharedClasses> s_loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonDocument? _doc;

    private SharedClasses(JsonDocument? doc) => _doc = doc;

    public static SharedClasses Load(string sharedRoot) => s_loaded.GetOrAdd(Path.GetFullPath(sharedRoot), root =>
    {
        string path = Path.Combine(root, "classes.json");
        return new SharedClasses(File.Exists(path) ? JsonDocument.Parse(File.ReadAllBytes(path)) : null);
    });

    /// <summary>The effective defaults of a class (Package.Class), or null.</summary>
    public JsonElement? DefaultsOf(string cls) =>
        _doc is not null && _doc.RootElement.TryGetProperty("classes", out JsonElement c) && c.TryGetProperty(cls, out JsonElement entry)
            && entry.TryGetProperty("defaults", out JsonElement d)
            ? d
            : null;
}
