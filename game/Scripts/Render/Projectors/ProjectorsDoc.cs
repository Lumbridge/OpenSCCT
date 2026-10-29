using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Projectors;

/// <summary>An animated texture's frames in one atlas (Texture.AnimNext chain): cols x rows cells, frames used, fps.</summary>
public sealed record ProjectorFlipbook(string Texture, int Cols, int Rows, int Frames, float Fps);

/// <summary>
/// One texture projector of render/projectors.json (Projector, DynamicProjector; docs/RENDERING.md#projectors).
/// Scale is DrawScale x DrawScale3D, Size the texture's USize and VSize, the blend ops are EProjectorBlending
/// (0 PB_None, 1 PB_Modulate, 2 PB_AlphaBlend, 3 PB_Add). UvMatrix is a static TexScaler / TexRotator over the
/// texture (null for none), applied to the projected coordinates. ProjectStaticMesh is bProjectStaticMesh (false: static
/// meshes do not receive the projection; 39 placed, the Mission bunker signs among them).
/// </summary>
public sealed record ProjectorEntry(
    string Name,
    FVector Location,
    FRotator Rotation,
    FVector Scale,
    string Texture,
    Vector2 Size,
    float Fov,
    float MaxTrace,
    int FrameBufferBlending,
    int MaterialBlending,
    bool ProjectOnBackfaces,
    UvAnim? UvAnim,
    ProjectorFlipbook? Flipbook,
    UvAffine? UvMatrix = null,
    bool ProjectStaticMesh = true);

/// <summary>render/projectors.json (optional: older content has none and keeps its PROJ_ decal meshes).</summary>
public static class ProjectorsDoc
{
    public const string File = "render/projectors.json";

    /// <summary>The map's projectors, or null when the content has no projectors.json.</summary>
    public static List<ProjectorEntry>? Load(string mapFolder)
    {
        string path = Path.Combine(mapFolder, File);
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var list = new List<ProjectorEntry>();
        using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        if (!json.RootElement.TryGetProperty("projectors", out JsonElement items))
        {
            return list;
        }

        foreach (JsonElement p in items.EnumerateArray())
        {
            JsonElement size = p.GetProperty("size");
            ProjectorFlipbook? flipbook = null;
            if (p.TryGetProperty("flipbook", out JsonElement fb) && fb.ValueKind == JsonValueKind.Object)
            {
                flipbook = new ProjectorFlipbook(fb.GetProperty("texture").GetString() ?? "", fb.GetProperty("cols").GetInt32(),
                    fb.GetProperty("rows").GetInt32(), fb.GetProperty("frames").GetInt32(), fb.GetProperty("fps").GetSingle());
            }

            list.Add(new ProjectorEntry(
                p.GetProperty("name").GetString() ?? "",
                Vec(p.GetProperty("location")),
                Rot(p.GetProperty("rotation")),
                Vec(p.GetProperty("scale")),
                p.GetProperty("texture").GetString() ?? "",
                new Vector2(size[0].GetSingle(), size[1].GetSingle()),
                p.GetProperty("fov").GetSingle(),
                p.GetProperty("maxTrace").GetSingle(),
                p.GetProperty("fbBlend").GetInt32(),
                p.GetProperty("matBlend").GetInt32(),
                p.TryGetProperty("backfaces", out JsonElement bf) && bf.ValueKind == JsonValueKind.True,
                p.TryGetProperty("uvAnim", out JsonElement uv) ? UvAnim.From(uv) : null,
                flipbook,
                p.TryGetProperty("uvMatrix", out JsonElement m) ? UvAffine.From(m) : null,
                !p.TryGetProperty("projectStaticMesh", out JsonElement psm) || psm.ValueKind != JsonValueKind.False));
        }

        return list;
    }

    internal static FVector Vec(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());

    internal static FRotator Rot(JsonElement v) => new(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32());
}
