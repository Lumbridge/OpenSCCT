using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>
/// Materials gameplay actors put in their Skins at run time that the gameplay meshes do not carry, built from the
/// render pack: the unarmed bomb spot's ghost (SBombTargetObjectiveTrigger.AbsentSkin, spec/objectives/bomb-target.md
/// rule 14), and the skins the renderpack step exports with the gameplay meshes (gameplay/materials.json "skins": the
/// camera and sensor LEDs, spec/devices/security-camera.md rule 9). Any other skin draws the mesh's own material.
/// </summary>
public static class GameplaySkins
{
    /// <summary>SBombTargetObjectiveTrigger.AbsentSkin: a SpecialVisionTextureModifier over the Shader FX_pose_bomb_fx.</summary>
    public const string BombGhost = "SGameplayObjectsTex.Object.FX_pose_bomb_heat";

    // FX_pose_bomb (AnimNext FX_pose_bomb_2, Min/MaxFrameRate 3): a pale green, ~25% alpha, and a darker, fainter frame.
    private static readonly string[] s_bombGhostFrames =
    [
        "textures/SGameplayObjectsTex_FX_pose_bomb.png",
        "textures/SGameplayObjectsTex_FX_pose_bomb_2.png",
    ];

    private const float BombGhostFrameRate = 3f;

    private static readonly Dictionary<string, Material[]> s_cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, PackSkins> s_packSkins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The material of the pack-exported skin <paramref name="skin"/> (an object path such as
    /// SGameplayObjectsTex_led.SGPO.heat_SI_alarm_greenA) lit by <paramref name="light"/>, or null when the pack does
    /// not carry it.
    /// </summary>
    public static Material? Skin(RenderPack? pack, string? skin, LightSource light)
    {
        if (pack is null || string.IsNullOrEmpty(skin) || pack.GameplayGlb is not { } glb)
        {
            return null;
        }

        if (!s_packSkins.TryGetValue(pack.Root, out PackSkins? skins))
        {
            skins = PackSkins.Load(pack, glb);
            s_packSkins[pack.Root] = skins;
        }

        return skins.Get(skin, light);
    }

    /// <summary>The gameplay pack's skin table (object path -> material name) and the compiler that builds them.</summary>
    private sealed class PackSkins
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
        private MaterialCompiler? _compiler;

        public static PackSkins Load(RenderPack pack, string glb)
        {
            var skins = new PackSkins();
            string folder = Path.GetDirectoryName(glb)!;
            string path = Path.Combine(folder, "materials.json");
            if (!File.Exists(path))
            {
                return skins;
            }

            using (var json = JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                if (json.RootElement.TryGetProperty("skins", out JsonElement table) && table.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in table.EnumerateObject())
                    {
                        if (p.Value.GetString() is { } name)
                        {
                            skins._names[p.Name] = name;
                        }
                    }
                }
            }

            if (skins._names.Count > 0)
            {
                skins._compiler = new MaterialCompiler(MaterialsDoc.Load(path), new TextureCache(folder))
                {
                    Pack = pack,
                    VisionLookup = VisionLookup.ModifierName,
                };
            }

            return skins;
        }

        // The compiler caches each (material, light) it builds.
        public Material? Get(string skin, LightSource light) =>
            _compiler is not null && _names.TryGetValue(skin, out string? name) ? _compiler.Get(name, light) : null;
    }

    /// <summary>
    /// The material of <paramref name="skin"/> at <paramref name="seconds"/> (animated skins step their frames), or null
    /// when it is not one of these skins or the pack lacks its textures.
    /// </summary>
    public static Material? For(RenderPack? pack, string? skin, double seconds)
    {
        if (pack is null || !string.Equals(skin, BombGhost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Material[] frames = BombGhostFrames(pack);
        return frames.Length == 0 ? null : frames[(int)(Math.Max(seconds, 0d) * BombGhostFrameRate) % frames.Length];
    }

    /// <summary>
    /// FX_pose_bomb_fx: Diffuse, Opacity and SelfIllumination all FX_pose_bomb, TwoSided, so a fullbright two-sided
    /// alpha blend of the texture; the modifier's heat and EMF texture is the same. The frames the pack has (the
    /// renderpack step exports the second one; an older pack draws the first alone).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SGameplayObjectsTex.Object.FX_pose_bomb_fx Shader (Diffuse = Opacity = SelfIllumination = FX_pose_bomb, TwoSided); FX_pose_bomb AnimNext FX_pose_bomb_2 at 3 fps; FX_pose_bomb_heat HeatTexture = EMFTexture = FX_pose_bomb")]
    private static Material[] BombGhostFrames(RenderPack pack)
    {
        string key = pack.Root + "|" + BombGhost;
        if (s_cache.TryGetValue(key, out Material[]? cached))
        {
            return cached;
        }

        var compiler = new MaterialCompiler(new MaterialsDoc(), pack.Textures) { Pack = pack, VisionLookup = VisionLookup.ModifierName };
        var frames = new List<Material>();
        foreach (string uri in s_bombGhostFrames)
        {
            if (!File.Exists(Path.Combine(pack.Root, uri)))
            {
                continue;
            }

            // Named after the modifier, so the compiler finds its heat and EMF textures.
            frames.Add(compiler.Build(new MaterialEntry
            {
                Name = BombGhost,
                Texture = uri,
                Unlit = true,
                TwoSided = true,
                Translucent = true,
            }, LightSource.None));
        }

        Material[] result = [.. frames];
        s_cache[key] = result;
        return result;
    }
}
