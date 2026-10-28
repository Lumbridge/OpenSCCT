using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Content.Loading;

/// <summary>
/// The map's rain volumes for the simulation (<see cref="RainVolumeList"/>): sim/rain.json (spec/svmap/1.1, written by
/// the converter), or, for a map converted before that file existed, the rain list of render/fx.json, which holds the
/// same values. A map with neither has no rain.
/// </summary>
public static class RainVolumeLoader
{
    /// <summary>The rain volumes of the SVMap folder <paramref name="mapRoot"/> (empty when it has none).</summary>
    public static RainVolumeList Load(string mapRoot)
    {
        var volumes = new List<ERainVolume>();
        string sim = Path.Combine(mapRoot, "sim", "rain.json");
        string render = Path.Combine(mapRoot, "render", "fx.json");
        if (File.Exists(sim))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(sim));
            if (doc.RootElement.TryGetProperty("volumes", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement v in list.EnumerateArray())
                {
                    volumes.Add(Make(v, v));
                }
            }
        }
        else if (File.Exists(render))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(render));
            if (doc.RootElement.TryGetProperty("rain", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement v in list.EnumerateArray())
                {
                    if (v.TryGetProperty("rain", out JsonElement rain) && rain.ValueKind == JsonValueKind.Object)
                    {
                        volumes.Add(Make(v, rain));
                    }
                }
            }
        }

        return new RainVolumeList(volumes);
    }

    // placement: location, scale; rain: size, falloff (the same keys in both files).
    private static ERainVolume Make(JsonElement placement, JsonElement rain)
    {
        var volume = new ERainVolume
        {
            Location = Vec(placement, "location", default),
            DrawScale = 1f,
            DrawScale3D = Vec(placement, "scale", new FVector(1f, 1f, 1f)),
        };
        volume.m_RainSize = Vec(rain, "size", volume.m_RainSize);
        if (rain.TryGetProperty("falloff", out JsonElement f) && f.ValueKind == JsonValueKind.Number)
        {
            volume.m_RainFalloff = f.GetSingle();
        }

        volume.RebuildRainVolume();
        return volume;
    }

    private static FVector Vec(JsonElement e, string name, FVector fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3
            ? new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())
            : fallback;
}
