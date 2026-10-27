using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>
/// Materials gameplay actors put in their Skins at run time that the gameplay meshes do not carry, built from the
/// render pack. Only the unarmed bomb spot's ghost for now (SBombTargetObjectiveTrigger.AbsentSkin,
/// spec/objectives/bomb-target.md rule 14); any other skin draws the mesh's own materials.
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
