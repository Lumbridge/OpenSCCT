using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>
/// Visible DT_Sprite Light actors, including sky moons and Stee's crane lamps. LT_None disables illumination,
/// not sprite drawing. These use the authored texture material and ordinary depth testing, unlike coronas.
/// </summary>
public sealed class MapLightSprites
{
    private readonly Dictionary<string, MeshInstance3D> _nodes = new(StringComparer.OrdinalIgnoreCase);

    internal MapLightSprites(RenderMap map)
    {
        string path = Path.Combine(map.Folder, "render", "lighting.json");
        if (!File.Exists(path)) return;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("lights", out JsonElement lights)) return;
        foreach (JsonElement light in lights.EnumerateArray()) Add(map, light);
    }

    public IReadOnlyDictionary<string, MeshInstance3D> Nodes => _nodes;

    /// <summary>Texture references absent from an older render pack; updated preparation exports sprite materials.</summary>
    public List<string> Missing { get; } = [];

    public void SetPlace(string id, FVector location)
    {
        if (_nodes.TryGetValue(id, out MeshInstance3D? node)) node.Position = UeSpace.ToGodot(location);
    }

    [Provenance(ProvenanceKind.Decoded, "Visible map Light props override bHidden; Actor defaults DT_Sprite; texture material blend overridden by Actor.Style when non-normal")]
    [Provenance(ProvenanceKind.Decoded, "game DrawSprite 0x10a142d0 uses view axes times DrawScale/DrawScale3D and half the texture size, and constant neutral vertex colour; no bUnlit test or per-actor light selection")]
    private void Add(RenderMap map, JsonElement light)
    {
        if (Text(light, "class") is not "Light" || Text(light, "name") is not { } id ||
            light.TryGetProperty("corona", out _) ||
            !light.TryGetProperty("props", out JsonElement props) ||
            Flag(props, "bCorona") ||
            !props.TryGetProperty("bHidden", out var hidden) || hidden.ValueKind != JsonValueKind.False ||
            Number(props, "DrawType", (int)EDrawType.DT_Sprite) != (int)EDrawType.DT_Sprite ||
            !props.TryGetProperty("Texture", out var textureRef) || Text(textureRef, "$asset") is not { } source)
        {
            return;
        }

        light.TryGetProperty("sprite", out JsonElement sprite);
        string? uri = Text(sprite, "texture_uri");
        // Existing packs already contain the crane's texture for the merc torch. Newly prepared maps carry
        // every visible sprite's resolved texture/material, including the custom-map sky textures.
        if (uri is null && (map.Pack?.TextureUri(source) ?? map.Pack?.TextureUri(RenderPack.ShortPath(source))) is { } shared)
        {
            uri = Path.Combine(map.Pack!.Root, shared);
        }

        if (uri is null || map.Textures.Get(uri) is not { } texture)
        {
            Missing.Add(id + ": " + source);
            return;
        }

        MaterialBlend blend = Text(sprite, "blend") switch
        {
            "masked" => MaterialBlend.Masked,
            "translucent" => MaterialBlend.Translucent,
            "additive" => MaterialBlend.Additive,
            "modulate" => MaterialBlend.Modulate,
            "darken" => MaterialBlend.Darken,
            _ => MaterialBlend.Opaque,
        };
        blend = (ERenderStyle)Number(props, "Style", (int)ERenderStyle.STY_Normal) switch
        {
            ERenderStyle.STY_Masked => MaterialBlend.Masked,
            ERenderStyle.STY_Translucent or ERenderStyle.STY_Additive => MaterialBlend.Additive,
            ERenderStyle.STY_Alpha => MaterialBlend.Translucent,
            ERenderStyle.STY_Modulated => MaterialBlend.Modulate,
            _ => blend,
        };
        var entry = new MaterialEntry
        {
            Name = "LightSprite_" + id,
            Source = source,
            Texture = uri,
            // Native DrawSprite supplies neutral vertex colour, even when bUnlit is false. Mesh lighting
            // would make the authored GAR_Moon sprites black in their zero-ambient sky zones.
            Unlit = true,
            TwoSided = true,
            ListBlend = blend,
        };
        float scale = Number(props, "DrawScale", 1f);
        float x = 1f, y = 1f;
        if (props.TryGetProperty("DrawScale3D", out var scale3D))
        {
            x = Number(scale3D, "X", 1f);
            y = Number(scale3D, "Y", 1f);
        }

        // Native DrawSprite uses the texture's same size query for both view axes (all current assets are square).
        float textureSize = Number(sprite, "width", texture.GetWidth());
        var size = new Vector2(UeSpace.LengthToGodot(textureSize * scale * x),
            UeSpace.LengthToGodot(textureSize * scale * y));
        FVector location = light.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Array
            ? new FVector(loc[0].GetSingle(), loc[1].GetSingle(), loc[2].GetSingle()) : default;
        var node = new MeshInstance3D
        {
            Name = "LightSprite_" + id,
            Mesh = new QuadMesh { Size = size },
            MaterialOverride = map.Compiler.Build(entry, LightSource.None, billboard: true),
            Position = UeSpace.ToGodot(location),
            // The shader rotates the quad to the view; its unrotated zero-depth AABB must cover every orientation.
            ExtraCullMargin = size.Length() * .5f,
            Layers = Flag(light, "sky_zone") ? RenderLayers.Sky : RenderLayers.Effects,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        map.Root.AddChild(node);
        _nodes.Add(id, node);
    }

    private static string? Text(JsonElement props, string name) => props.ValueKind == JsonValueKind.Object &&
        props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement props, string name) => props.ValueKind == JsonValueKind.Object &&
        props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static float Number(JsonElement props, string name, float fallback) => props.ValueKind == JsonValueKind.Object &&
        props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : fallback;
}
