using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Lighting;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Bots;

/// <summary>
/// Reads the lights and zone ambients of a map's <c>render/lighting.json</c> (SVMap 1.1) for the bots' light field:
/// the lights the renderer lights pawns with. The actor light set takes the lights kept for run time
/// (bApplyToInGameLighting or bDynamicLight) with a light type and not LE_ESpotShadow
/// (<see cref="ActorLightRules.LightsActors"/>, as <c>ActorLighting</c>); the LE_ESpotShadow lights with brightness
/// above 0 are the spot shadow lights (as <c>MapSpotLights</c>), shadowed by a line through the collision world.
/// Lights named by an STriggerLight of the loaded map follow its switch. A server without the render part gets
/// <see cref="AiLightField.Unknown"/>.
/// </summary>
public static class AiMapLight
{
    private const ETraceFlags ShadowFlags = ETraceFlags.TRACE_World | ETraceFlags.TRACE_StopAtFirstHit;

    /// <summary>
    /// The light field of the map in <paramref name="mapFolder"/>, zone lookups and shadow lines through
    /// <paramref name="collision"/>, switches from the trigger lights of <paramref name="loaded"/>.
    /// </summary>
    public static AiLightField Load(string? mapFolder, ICollisionWorld? collision, MapLoadResult? loaded = null)
    {
        string? path = mapFolder is null ? null : Path.Combine(mapFolder, "render", "lighting.json");
        if (path is null || !File.Exists(path))
        {
            return AiLightField.Unknown;
        }

        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
        AiLightField field = Parse(json.RootElement, collision);
        if (loaded is not null)
        {
            BindSwitches(field, loaded);
        }

        return field;
    }

    /// <summary>Has <paramref name="field"/> follow every STriggerLight of the map (its actor id names its light).</summary>
    public static void BindSwitches(AiLightField field, MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(loaded);
        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is STriggerLight light)
            {
                field.AddSwitch(light, record.Id);
            }
        }
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

                // Pawns take the zone ambient of their zone (RenderMap.AmbientAt), each channel at most 1.
                if (z.TryGetProperty("ambient", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 3)
                {
                    ambient[index] = new AiRgb(SvmMath.Min(1f, a[0].GetSingle() / 255f), SvmMath.Min(1f, a[1].GetSingle() / 255f),
                        SvmMath.Min(1f, a[2].GetSingle() / 255f));
                }
            }
        }

        var lights = new List<AiLight>();
        var spots = new List<AiSpotLight>();
        if (root.TryGetProperty("lights", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement l in list.EnumerateArray())
            {
                int effect = (int)Num(l, "effect");
                int type = (int)Num(l, "ltype", 1f);
                bool on = !(l.TryGetProperty("initially_on", out JsonElement io) && io.ValueKind == JsonValueKind.False);
                bool inGame = Bool(l, "apply_ingame");
                bool dynamic = Bool(l, "dynamic");
                float brightness = Num(l, "brightness", 64f);
                string? name = Str(l, "name");
                if (effect == ActorLightRules.LE_ESpotShadow)
                {
                    // Every LE_ESpotShadow light that gives light is drawn (MapSpotLights), whatever its flags.
                    if (brightness > 0f)
                    {
                        spots.Add(new AiSpotLight(Vec(l, "location"), Rot(l, "rotation"), brightness, (byte)Num(l, "hue"),
                            (byte)Num(l, "saturation", 255f), Frustum(l), type, Num(l, "light_period", 32f), Num(l, "light_phase"), name, on));
                    }

                    continue;
                }

                // As ActorLighting: a light not initially on stays in the field, off, so its STriggerLight can switch it on.
                if (!ActorLightRules.LightsActors(inGame, dynamic, type, effect))
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
                    brightness,
                    (byte)Num(l, "hue"),
                    (byte)Num(l, "saturation", 255f),
                    Num(l, "radius", 64f),
                    (byte)Num(l, "cone", 128f),
                    effect,
                    zone,
                    Bool(l, "own_zone_only"),
                    type,
                    Num(l, "light_period", 32f),
                    Num(l, "light_phase"),
                    name,
                    on));
            }
        }

        Func<FVector, int>? zoneOf = collision is null ? null : collision.GetZoneIndex;
        Func<FVector, FVector, bool>? lineOpen = collision is null
            ? null
            : (from, to) => !collision.LineCheck(out _, null, to, from, default, ShadowFlags);
        return new AiLightField(lights, ambient, zoneOf, spots, lineOpen);
    }

    /// <summary>
    /// The Echelon frustum of an LE_ESpotShadow light, with the Actor defaults (EchelonRange 1000, SpotWidth and
    /// SpotHeight 250, NearPlaneRatio 0.1, EchelonNormalInfluence 1) and EchAtten_Linear where absent (as the renderer's
    /// LightingDoc reads it).
    /// </summary>
    private static SpotShadowFrustum Frustum(JsonElement l)
    {
        float range = Num(l, "echelon_range");
        float width = Num(l, "spot_width");
        float height = Num(l, "spot_height");
        float influence = l.TryGetProperty("props", out JsonElement props) && props.ValueKind == JsonValueKind.Object
            ? Num(props, "EchelonNormalInfluence", 1f)
            : 1f;
        return new SpotShadowFrustum(
            range > 0f ? range : 1000f,
            width > 0f ? width : 250f,
            height > 0f ? height : 250f,
            Num(l, "near_plane_ratio", 0.1f),
            influence,
            (int)Num(l, "echelon_attenuation", 1f));
    }

    private static float Num(JsonElement e, string name, float fallback = 0f) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

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
