using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Physics.Collision;

namespace OpenScct.Bots;

/// <summary>
/// Reads the lights and zone ambients of a map's <c>render/lighting.json</c> (SVMap 1.1) for the bots' light field: the
/// same lights the renderer lights pawns with (bApplyToInGameLighting, on, not the ESpotShadow projectors). A server
/// without the render part gets <see cref="AiLightField.Unknown"/>.
/// </summary>
public static class AiMapLight
{
    /// <summary>The light field of the map in <paramref name="mapFolder"/>, zone lookups through <paramref name="collision"/>.</summary>
    public static AiLightField Load(string? mapFolder, ICollisionWorld? collision)
    {
        string? path = mapFolder is null ? null : Path.Combine(mapFolder, "render", "lighting.json");
        if (path is null || !File.Exists(path))
        {
            return AiLightField.Unknown;
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
        return Parse(json.RootElement, collision);
    }

    /// <summary>The field of a parsed lighting document.</summary>
    public static AiLightField Parse(JsonElement root, ICollisionWorld? collision)
    {
        var zoneByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var ambient = new Dictionary<int, AiRgb>();
        if (root.TryGetProperty("zones", out JsonElement zones) && zones.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement z in zones.EnumerateArray())
            {
                int index = (int)Num(z, "zoneIndex", -1f);
                if (index < 0)
                {
                    continue;
                }

                if (Str(z, "name") is { } name)
                {
                    zoneByName[name] = index;
                }

                if (z.TryGetProperty("ambient", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 3)
                {
                    ambient[index] = new AiRgb(a[0].GetSingle() / 255f, a[1].GetSingle() / 255f, a[2].GetSingle() / 255f);
                }
            }
        }

        var lights = new List<AiLight>();
        if (root.TryGetProperty("lights", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement l in list.EnumerateArray())
            {
                int effect = (int)Num(l, "effect");
                int type = (int)Num(l, "ltype", 1f);
                bool on = !(l.TryGetProperty("initially_on", out JsonElement io) && io.ValueKind == JsonValueKind.False);
                bool inGame = l.TryGetProperty("apply_ingame", out JsonElement ag) && ag.ValueKind == JsonValueKind.True;
                if (!inGame || type == 0 || !on || effect == AiK.LE_ESpotShadow)
                {
                    continue;
                }

                int zone = -1;
                if (Str(l, "zone") is { } zoneName && zoneByName.TryGetValue(zoneName, out int zi))
                {
                    zone = zi;
                }
                else if (l.TryGetProperty("props", out JsonElement props) && props.TryGetProperty("Region", out JsonElement region) &&
                         region.TryGetProperty("ZoneNumber", out JsonElement zn) && zn.ValueKind == JsonValueKind.Number)
                {
                    zone = zn.GetInt32();
                }

                lights.Add(new AiLight(
                    Vec(l, "location"),
                    Rot(l, "rotation"),
                    Num(l, "brightness", 64f),
                    (byte)Num(l, "hue"),
                    (byte)Num(l, "saturation", 255f),
                    Num(l, "radius", 64f),
                    (byte)Num(l, "cone", 128f),
                    effect,
                    zone,
                    l.TryGetProperty("own_zone_only", out JsonElement oz) && oz.ValueKind == JsonValueKind.True));
            }
        }

        Func<FVector, int>? zoneOf = collision is null ? null : collision.GetZoneIndex;
        return new AiLightField(lights, ambient, zoneOf);
    }

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static FVector Vec(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3
            ? new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())
            : default;

    private static FRotator Rot(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3
            ? new FRotator(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32())
            : default;
}
