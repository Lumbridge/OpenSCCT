using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// The wet skins of a pawn in the rain (SBasePawn.BeginWetSkin, spec/equipment/rain.md rule 3): on the spy
/// SPersoTextures.alpha_ATT02.HEAT_SN02_SI_wet over Skins 0 where this machine's player is a spy, HEAT_sn02_wet
/// elsewhere, and heat_sn02_2_wet_head over Skins 1; on the merc DEF01_heat_bodu_sv over Skins 0 and 2. Each is a
/// SpecialVisionTextureModifier: in thermal and EMF its heat and EMF textures (shared/render vision.json, under the
/// base materials wet_lit_ATT_SI, wet_lit_ATT, wet_lit_ATT_2 and lit_def_fx), in normal vision its base material. The
/// base materials are LitSpecularMaterials: the dry skin's own diffuse plus the wet env map (SPersoTextures.wet_envmap,
/// looked up by the camera-space reflection) added through a SpecularityMask, and on wet_lit_ATT_SI and lit_def_fx a
/// self-illumination. The chars step writes their textures with the mesh (glb extras svmap.wetSkins); an older build
/// without them draws the surface's own textures with only the wet modifier's vision textures.
/// Built once per mesh file and side of the local player.
/// </summary>
public sealed class WetSkins
{
    private static readonly Dictionary<(string Glb, bool LocalSpy), WetSkins> s_cache = new();

    // Surface material (as the mesh export names it) to the wet skin's base material, by mesh and local player's side.
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.BeginWetSkin: the skins per slot and the local player's side for the spy's body")]
    private static readonly (string Surface, string SpyMachine, string MercMachine)[] s_spy =
    [
        ("SPersoTextures.Shadw_agent", "SPersoTextures.alpha_ATT02.wet_lit_ATT_SI", "SPersoTextures.alpha_ATT02.wet_lit_ATT"),
        ("SPersoTextures.Shadw_agent_2", "SPersoTextures.alpha_ATT02.wet_lit_ATT_2", "SPersoTextures.alpha_ATT02.wet_lit_ATT_2"),
    ];

    private static readonly (string Surface, string SpyMachine, string MercMachine)[] s_merc =
    [
        ("SPersoTextures.DEF_01_A_Body", "SPersoTextures.alpha_DEF01.lit_def_fx", "SPersoTextures.alpha_DEF01.lit_def_fx"),
    ];

    private readonly Dictionary<string, Material> _bySurface = new(StringComparer.OrdinalIgnoreCase);

    private WetSkins()
    {
    }

    public int Count => _bySurface.Count;

    /// <summary>The wet skins of the mesh <paramref name="glbPath"/> (spy or merc), or null when it cannot be read.</summary>
    public static WetSkins? For(string? glbPath, bool spy, bool localSpy)
    {
        if (glbPath is null || !File.Exists(glbPath))
        {
            return null;
        }

        string full = Path.GetFullPath(glbPath);
        if (s_cache.TryGetValue((full, localSpy), out WetSkins? cached))
        {
            return cached;
        }

        WetSkins skins = Load(full, spy ? s_spy : s_merc, localSpy);
        s_cache[(full, localSpy)] = skins;
        return skins;
    }

    /// <summary>The wet skin drawn over a surface of this material, or null.</summary>
    public Material? Over(string surfaceMaterial) => _bySurface.GetValueOrDefault(surfaceMaterial);

    private static WetSkins Load(string glbPath, (string Surface, string SpyMachine, string MercMachine)[] table, bool localSpy)
    {
        var skins = new WetSkins();
        string folder = Path.GetDirectoryName(glbPath) ?? ".";

        // shared/chars/<Package>/<Mesh>.glb: the render pack with the vision modifiers is shared/render.
        string? shared = Path.GetDirectoryName(Path.GetDirectoryName(folder));
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(folder))
        {
            Pack = shared is null ? null : RenderPack.Load(shared),
            VisionLookup = VisionLookup.SkinBase,
        };

        using GlbDocument glb = GlbDocument.Load(glbPath);
        Dictionary<string, MaterialEntry> own = OwnMaterials(glb.Root);
        Dictionary<string, WetInputs> wet = WetMaterials(glb.Root);
        foreach ((string surface, string spyMachine, string mercMachine) in table)
        {
            if (!own.TryGetValue(surface, out MaterialEntry? e))
            {
                continue;
            }

            // Under the wet skin's name, so the vision lookup finds the wet modifier's textures.
            string name = localSpy ? spyMachine : mercMachine;
            skins._bySurface[surface] = compiler.Build(wet.TryGetValue(name, out WetInputs? w)
                ? new MaterialEntry
                {
                    Name = name,
                    Texture = e.Texture,
                    SelfIllum = w.SelfIllum,
                    SelfIllumMask = w.SelfIllumMask,
                    SpecMask = w.SpecMask,
                    EnvTexture = w.Env,
                    EnvColor = w.EnvColor,
                    Masked = e.Masked,
                    Translucent = e.Translucent,
                    TwoSided = e.TwoSided,
                }
                : new MaterialEntry
                {
                    Name = name,
                    Texture = e.Texture,
                    SelfIllum = e.SelfIllum,
                    SelfIllumMask = e.SelfIllumMask,
                    Masked = e.Masked,
                    Translucent = e.Translucent,
                    TwoSided = e.TwoSided,
                }, LightSource.Actor);
        }

        return skins;
    }

    /// <summary>The inputs of one wet LitSpecularMaterial as the chars step wrote them (texture paths beside the mesh).</summary>
    private sealed record WetInputs(string? Env, Vector3? EnvColor, string? SpecMask, string? SelfIllum, string? SelfIllumMask);

    /// <summary>The glb's svmap.wetSkins: wet material path to its env map, env colour, specularity mask and glow.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPersoTextures wet_lit_ATT_SI, wet_lit_ATT, wet_lit_ATT_2, lit_def_fx: LitSpecularMaterial Specular (TexEnvMap envmap_wet), SpecularityMask, SelfIllumination(Mask)")]
    [Provenance(ProvenanceKind.Estimated, "The specular term is drawn as the map Shaders' env maps are (env texture by the camera-space reflection, added through the mask's alpha)")]
    private static Dictionary<string, WetInputs> WetMaterials(JsonElement root)
    {
        var result = new Dictionary<string, WetInputs>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("extras", out JsonElement ex) || !ex.TryGetProperty("svmap", out JsonElement svmap) ||
            !svmap.TryGetProperty("wetSkins", out JsonElement wet) || wet.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty m in wet.EnumerateObject())
        {
            Vector3? colour = null;
            if (m.Value.TryGetProperty("envColor", out JsonElement c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 3)
            {
                colour = new Vector3(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
            }

            string? env = MaterialsDoc.Str(m.Value, "env");
            string? mask = MaterialsDoc.Str(m.Value, "specMask");
            result[m.Name] = new WetInputs(
                env is not null && mask is not null ? env : null,
                env is not null && mask is not null ? colour ?? Vector3.Zero : null,
                env is not null ? mask : null,
                MaterialsDoc.Str(m.Value, "selfIllum"),
                MaterialsDoc.Str(m.Value, "selfIllumMask"));
        }

        return result;
    }

    /// <summary>The mesh's own materials as the character loader reads them (diffuse, self-illumination, blending).</summary>
    private static Dictionary<string, MaterialEntry> OwnMaterials(JsonElement root)
    {
        var result = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);
        var images = new List<string?>();
        if (root.TryGetProperty("images", out JsonElement imgs))
        {
            foreach (JsonElement i in imgs.EnumerateArray())
            {
                images.Add(i.TryGetProperty("uri", out JsonElement u) ? u.GetString() : null);
            }
        }

        var textures = new List<int>();
        if (root.TryGetProperty("textures", out JsonElement texs))
        {
            foreach (JsonElement t in texs.EnumerateArray())
            {
                textures.Add(t.TryGetProperty("source", out JsonElement src) ? src.GetInt32() : -1);
            }
        }

        if (!root.TryGetProperty("materials", out JsonElement mats))
        {
            return result;
        }

        foreach (JsonElement m in mats.EnumerateArray())
        {
            string name = m.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
            string? texture = null;
            if (m.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorTexture", out JsonElement bct))
            {
                int ti = bct.GetProperty("index").GetInt32();
                int ii = ti >= 0 && ti < textures.Count ? textures[ti] : -1;
                texture = ii >= 0 && ii < images.Count ? images[ii] : null;
            }

            JsonElement ex = m.TryGetProperty("extras", out JsonElement x) ? x : default;
            bool hasEx = ex.ValueKind == JsonValueKind.Object;
            string mode = m.TryGetProperty("alphaMode", out JsonElement am) ? am.GetString() ?? "" : "";
            result[name] = new MaterialEntry
            {
                Name = name,
                Texture = texture,
                SelfIllum = hasEx ? MaterialsDoc.Str(ex, "scct_self_illum") : null,
                SelfIllumMask = hasEx ? MaterialsDoc.Str(ex, "scct_self_illum_mask") : null,
                Masked = mode == "MASK",
                Translucent = mode == "BLEND",
                TwoSided = m.TryGetProperty("doubleSided", out JsonElement ds) && ds.GetBoolean(),
            };
        }

        return result;
    }
}
