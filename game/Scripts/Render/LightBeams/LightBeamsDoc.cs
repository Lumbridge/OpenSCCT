using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render.LightBeams;

/// <summary>
/// One light with a beam (UsesLightBeam and a LightBeamMesh) of render/lightbeams.json, with the beam values as
/// placed (stored, else the class default). <see cref="LightOn"/> is LightType != LT_None, <see cref="InitiallyOn"/>
/// STriggerLight.bInitialyOn (true for other classes). docs/RENDERING.md#light-beams.
/// </summary>
public sealed record LightBeamEntry(
    string Name,
    string Mesh,
    FVector Location,
    FRotator Rotation,
    float EchelonRange,
    float SpotWidth,
    float SpotHeight,
    float NearPlaneRatio,
    float FalloffScale,
    int Alpha,
    float Brightness,
    Color Colour,
    bool LightOn,
    bool InitiallyOn,
    bool Gameplay);

/// <summary>render/lightbeams.json: the beams and the slice meshes they name (positions in mesh space, triangles).</summary>
public sealed class LightBeamsDoc
{
    public const string File = "render/lightbeams.json";

    public List<LightBeamEntry> Beams { get; } = [];

    public Dictionary<string, (Vector3[] Positions, int[] Indices)> Meshes { get; } = [];

    /// <summary>The map's beams, or null when the content has no lightbeams.json.</summary>
    public static LightBeamsDoc? Load(string mapFolder)
    {
        string path = Path.Combine(mapFolder, File);
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var doc = new LightBeamsDoc();
        using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        JsonElement root = json.RootElement;
        if (root.TryGetProperty("meshes", out JsonElement meshes))
        {
            foreach (JsonProperty m in meshes.EnumerateObject())
            {
                JsonElement p = m.Value.GetProperty("positions");
                var positions = new Vector3[p.GetArrayLength() / 3];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = new Vector3(p[i * 3].GetSingle(), p[(i * 3) + 1].GetSingle(), p[(i * 3) + 2].GetSingle());
                }

                JsonElement ix = m.Value.GetProperty("indices");
                var indices = new int[ix.GetArrayLength()];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = ix[i].GetInt32();
                }

                doc.Meshes[m.Name] = (positions, indices);
            }
        }

        if (root.TryGetProperty("beams", out JsonElement beams))
        {
            foreach (JsonElement b in beams.EnumerateArray())
            {
                JsonElement c = b.TryGetProperty("color", out JsonElement ce) ? ce : default;
                Color colour = c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 3
                    ? new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle())
                    : new Color(1f, 1f, 1f);
                doc.Beams.Add(new LightBeamEntry(
                    b.GetProperty("name").GetString() ?? "",
                    b.GetProperty("mesh").GetString() ?? "",
                    Projectors.ProjectorsDoc.Vec(b.GetProperty("location")),
                    Projectors.ProjectorsDoc.Rot(b.GetProperty("rotation")),
                    b.GetProperty("echelonRange").GetSingle(),
                    b.GetProperty("spotWidth").GetSingle(),
                    b.GetProperty("spotHeight").GetSingle(),
                    b.GetProperty("nearPlaneRatio").GetSingle(),
                    b.GetProperty("falloffScale").GetSingle(),
                    b.GetProperty("alpha").GetInt32(),
                    b.GetProperty("brightness").GetSingle(),
                    colour,
                    b.GetProperty("lightOn").ValueKind == JsonValueKind.True,
                    b.GetProperty("initiallyOn").ValueKind == JsonValueKind.True,
                    b.GetProperty("gameplay").ValueKind == JsonValueKind.True));
            }
        }

        return doc;
    }
}
