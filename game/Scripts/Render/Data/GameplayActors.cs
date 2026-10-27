using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render.Data;

/// <summary>One actor of sim/gameplay.json with its class defaults (sim/classes.json), as the renderer reads it.</summary>
public sealed class GameplayActor
{
    internal GameplayActor(string id, string cls, JsonElement props, JsonElement? defaults)
    {
        Id = id;
        Class = cls;
        Props = props;
        Defaults = defaults;
    }

    public string Id { get; }

    /// <summary>Package.Class, for example SBase.SInfirmary.</summary>
    public string Class { get; }

    /// <summary>The class name without the package.</summary>
    public string ShortClass => Class[(Class.LastIndexOf('.') + 1)..];

    public JsonElement Props { get; }

    public JsonElement? Defaults { get; }

    /// <summary>A property as stored on the actor, else its class default.</summary>
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

    public float Float(string name, float fallback = 0f) =>
        TryGet(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    public int Int(string name, int fallback = 0) =>
        TryGet(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    public bool Bool(string name) => TryGet(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    /// <summary>An object reference's asset path ({"$asset": path}), or null.</summary>
    public string? Asset(string name) =>
        TryGet(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$asset", out JsonElement a)
            ? a.GetString()
            : null;

    public FVector Vector(string name, FVector fallback = default)
    {
        if (!TryGet(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        float F(string k) => v.TryGetProperty(k, out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : 0f;
        return new FVector(F("X"), F("Y"), F("Z"));
    }

    public FRotator Rotator(string name)
    {
        if (!TryGet(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        int I(string k) => v.TryGetProperty(k, out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0;
        return new FRotator(I("Pitch"), I("Yaw"), I("Roll"));
    }

    /// <summary>
    /// The actor's vision values (Actor.HeatIntensity, HeatOpacity, EMFIntensity while bEMFIsOn, EMFOpacity) in the
    /// template's svm_vision_actor layout.
    /// </summary>
    public Vector4 Vision() => new(
        Float("HeatIntensity"),
        Float("HeatOpacity", RenderK.DefaultHeatOpacity),
        Bool("bEMFIsOn") || !TryGet("bEMFIsOn", out _) ? Float("EMFIntensity") : 0f,
        Float("EMFOpacity", 1f));
}

/// <summary>The actors of a map's sim/gameplay.json, by id, with their class defaults.</summary>
public sealed class GameplayActors
{
    private readonly Dictionary<string, GameplayActor> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameplayActor> _actors = [];
    private JsonDocument? _gameplay;
    private JsonDocument? _classes;

    public IReadOnlyList<GameplayActor> Actors => _actors;

    /// <summary>LevelInfo's stored properties (sim/gameplay.json levelInfo), over its class defaults.</summary>
    public GameplayActor? LevelInfo { get; private set; }

    public GameplayActor? Find(string id) => _byId.GetValueOrDefault(id);

    public static GameplayActors Load(string mapFolder)
    {
        var actors = new GameplayActors();
        string gameplay = Path.Combine(mapFolder, "sim", "gameplay.json");
        if (!File.Exists(gameplay))
        {
            return actors;
        }

        actors._gameplay = JsonDocument.Parse(File.ReadAllBytes(gameplay));
        string classes = Path.Combine(mapFolder, "sim", "classes.json");
        JsonElement? classTable = null;
        if (File.Exists(classes))
        {
            actors._classes = JsonDocument.Parse(File.ReadAllBytes(classes));
            if (actors._classes.RootElement.TryGetProperty("classes", out JsonElement c))
            {
                classTable = c;
            }
        }

        JsonElement? DefaultsOf(string cls) =>
            classTable is { } t && t.TryGetProperty(cls, out JsonElement entry) && entry.TryGetProperty("defaults", out JsonElement d) ? d : null;

        JsonElement root = actors._gameplay.RootElement;
        if (root.TryGetProperty("actors", out JsonElement list))
        {
            foreach (JsonElement a in list.EnumerateArray())
            {
                string id = a.GetProperty("id").GetString() ?? "";
                string cls = a.GetProperty("class").GetString() ?? "";
                var actor = new GameplayActor(id, cls, a.GetProperty("props"), DefaultsOf(cls));
                actors._actors.Add(actor);
                actors._byId.TryAdd(id, actor);
            }
        }

        if (root.TryGetProperty("levelInfo", out JsonElement li) && li.TryGetProperty("props", out JsonElement lp))
        {
            actors.LevelInfo = new GameplayActor("LevelInfo0", "Engine.LevelInfo", lp, DefaultsOf("Engine.LevelInfo"));
        }

        return actors;
    }
}
