using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// The Stickess skins of a shocked pawn (SBasePawn.BeginStickySkin, spec/equipment/sticky-shock-effect.md rule 3.6):
/// SPersoTextures.alpha_ATT02.Stickess_ATT02_body and _head on the spy's Skins 0 and 1, alpha_DEF01.Stickess_DEF01_body
/// on the merc's Skins 0 and 2. Each is a Shader: the body's own Diffuse with SPersoTextures.sfx_perso.pan_sticky (a
/// TexPanner of shadwagent_sticky_1, rate 0.5 along V) as SelfIllumination and its mask. Read from the converted
/// effects (shared/fx/effects.json "skins"); built once per shared root.
/// </summary>
public sealed class StickySkins
{
    private static readonly Dictionary<string, StickySkins> s_cache = new(StringComparer.OrdinalIgnoreCase);

    // Surface material name ("SPersoTextures.Shadw_agent", the mesh export's package.name) to the skin drawn over it.
    private readonly Dictionary<string, Material> _byDiffuse = new(StringComparer.OrdinalIgnoreCase);

    private StickySkins()
    {
    }

    public int Count => _byDiffuse.Count;

    /// <summary>The skins of a shared content root (cached), or null when the effects were not converted.</summary>
    public static StickySkins? For(string? sharedRoot)
    {
        if (sharedRoot is null)
        {
            return null;
        }

        if (s_cache.TryGetValue(sharedRoot, out StickySkins? cached))
        {
            return cached;
        }

        StickySkins? skins = Load(sharedRoot);
        if (skins is not null)
        {
            s_cache[sharedRoot] = skins;
        }

        return skins;
    }

    /// <summary>The Stickess skin drawn over a surface of this material, or null.</summary>
    public Material? Over(string surfaceMaterial) => _byDiffuse.GetValueOrDefault(surfaceMaterial);

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.BeginStickySkin (Skins slots) and the Stickess Shader properties (Diffuse, SelfIllumination, SelfIlluminationMask, TexPanner PanDirection yaw 16384, PanRate 0.5)")]
    [Provenance(ProvenanceKind.Estimated, "The slots are matched by the surface's own Diffuse: the spy's slot 0 and 1 and the merc's slots 0 and 2 are the surfaces that draw the Stickess shader's Diffuse texture")]
    private static StickySkins? Load(string sharedRoot)
    {
        string fx = Path.Combine(sharedRoot, "fx");
        string path = Path.Combine(fx, "effects.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("skins", out JsonElement list) || list.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var skins = new StickySkins();
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(fx));
        foreach (JsonProperty p in list.EnumerateObject())
        {
            JsonElement s = p.Value;
            string? diffuse = MaterialsDoc.Str(s, "diffuse");
            string? diffuseObject = MaterialsDoc.Str(s, "diffuseObject");
            string? selfIllum = MaterialsDoc.Str(s, "selfIllum");
            if (diffuse is null || diffuseObject is null || selfIllum is null)
            {
                continue;
            }

            var entry = new MaterialEntry
            {
                Name = p.Name,
                Texture = diffuse,
                SelfIllum = selfIllum,
                SelfIllumMask = MaterialsDoc.Str(s, "selfIllumMask"),
            };
            Material material = compiler.Build(entry, LightSource.Actor);
            if (s.TryGetProperty("pan", out JsonElement pan) && pan.GetArrayLength() == 3 && material is ShaderMaterial shader)
            {
                float rate = pan[2].GetSingle();
                shader.SetShaderParameter("si_pan", new Vector2(pan[0].GetSingle() * rate, pan[1].GetSingle() * rate));
            }

            // "SPersoTextures.alpha_ATT02.Shadw_agent" draws on surfaces the mesh export names "SPersoTextures.Shadw_agent".
            int dot = diffuseObject.IndexOf('.', StringComparison.Ordinal);
            int last = diffuseObject.LastIndexOf('.');
            string key = dot > 0 ? diffuseObject[..dot] + diffuseObject[last..] : diffuseObject;
            skins._byDiffuse[key] = material;
        }

        return skins;
    }
}
