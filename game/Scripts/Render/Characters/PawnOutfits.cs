using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Content;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// A map's outfits on the drawn pawns (render/outfits.json, spec/additions/map-outfits.md): the team's model swapped for
/// the outfit's, playing the team's own clips, and the outfit's slot textures as the body and head diffuse. The
/// cosmetics policy is applied where the look is read (<see cref="PawnCosmetics.LookOf"/>).
/// </summary>
public static class PawnOutfits
{
    // The stock surfaces an outfit slot covers, on either team's mesh (RE+ SCharacterOutfits Skins: the spy's 0 body and
    // 1 head; the merc's 0 and 2 body and 1 head).
    private static readonly string[] s_body = ["SPersoTextures.Shadw_agent", "SPersoTextures.DEF_01_A_Body"];
    private static readonly string[] s_head = ["SPersoTextures.Shadw_agent_2", "SPersoTextures.DEF_01_A_Face"];

    /// <summary>A pawn's model as the map dresses it.</summary>
    /// <param name="Model">The model drawn.</param>
    /// <param name="Glb">Its file.</param>
    /// <param name="Stock">The team's stock mesh, which an outfit's model stands in for.</param>
    /// <param name="Outfit">The outfit's own model is drawn (not the team's stock mesh).</param>
    /// <param name="DryBody">The body slot's texture (full path) when one is drawn, for the wet skins.</param>
    /// <param name="DryHead">The head slot's texture, likewise.</param>
    public sealed record Dressed(SkeletalModel Model, string Glb, string Stock, OutfitModel? Outfit, string? DryBody, string? DryHead);

    /// <summary>
    /// Loads the team's stock mesh <paramref name="stockGlb"/>, or the outfit's model playing its clips, and puts the
    /// outfit's slot textures on. A model that cannot be found or read leaves the stock mesh (with a warning).
    /// </summary>
    public static Dressed Load(string stockGlb, TeamOutfit? outfit, string? mapFolder, string? sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(stockGlb);
        if (outfit?.Model is { } wanted && ModelPath(wanted, mapFolder, sharedRoot) is { } glb &&
            !string.Equals(Path.GetFullPath(glb), Path.GetFullPath(stockGlb), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Dress(SkeletalModel.Load(glb, animationsFrom: stockGlb), glb, stockGlb, wanted, outfit, mapFolder);
            }
            catch (InvalidDataException e)
            {
                GD.PushWarning($"outfit model {glb} left out: {e.Message}");
            }
        }

        return Dress(SkeletalModel.Load(stockGlb), stockGlb, stockGlb, null, outfit, mapFolder);
    }

    /// <summary>The outfit model's file: the map's own glb under render/, or a stock mesh of the shared packs; null when missing.</summary>
    public static string? ModelPath(OutfitModel model, string? mapFolder, string? sharedRoot)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Glb is { } own)
        {
            if (mapFolder is null)
            {
                return null;
            }

            string path = Path.Combine(mapFolder, "render", own.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }

        return model.Stock is { } stock ? SkeletalModel.FindGlb(stock, sharedRoot) : null;
    }

    /// <summary>
    /// The texture of the merc's first-person arms (material <paramref name="armsMaterial"/> of the view model
    /// <paramref name="viewModelGlb"/>) in the merc outfit: its model's sleeves and gloves, or the stock mesh's with
    /// the outfit's slot textures. Null for a merc team without an outfit body or model, or when a mesh cannot be read.
    /// </summary>
    public static Texture2D? FirstPersonArms(string viewModelGlb, string armsMaterial, string stockGlb, TeamOutfit? merc, string? mapFolder,
        string? sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(stockGlb);
        if (merc is null || mapFolder is null || (merc.Body is null && merc.Head is null && merc.Model is null))
        {
            return null;
        }

        string glb = merc.Model is { } model && ModelPath(model, mapFolder, sharedRoot) is { } path ? path : stockGlb;
        var diffuse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((OutfitSlot? slot, string[] surfaces) in new[] { (merc.Body, s_body), (merc.Head, s_head) })
        {
            if (slot is not null && SlotPath(slot, mapFolder) is var texture && File.Exists(texture))
            {
                foreach (string surface in surfaces)
                {
                    diffuse[surface] = texture;
                }
            }
        }

        // A model of its own draws none of the stock surfaces, so the slot textures only dress a stock mesh.
        bool dressed = diffuse.Count > 0 || !string.Equals(Path.GetFullPath(glb), Path.GetFullPath(stockGlb), StringComparison.OrdinalIgnoreCase);
        return dressed ? OutfitSkins.Arms(viewModelGlb, armsMaterial, glb, diffuse) : null;
    }

    private static Dressed Dress(SkeletalModel model, string glb, string stockGlb, OutfitModel? own, TeamOutfit? outfit, string? mapFolder)
    {
        string? body = Slot(model, outfit?.Body, s_body, mapFolder);
        string? head = Slot(model, outfit?.Head, s_head, mapFolder);
        return new Dressed(model, glb, stockGlb, own, body, head);
    }

    private static string SlotPath(OutfitSlot slot, string mapFolder) =>
        Path.Combine(mapFolder, "render", slot.Texture.Replace('/', Path.DirectorySeparatorChar));

    // The slot's texture on whichever of its stock surfaces the model has; its full path when drawn.
    private static string? Slot(SkeletalModel model, OutfitSlot? slot, string[] surfaces, string? mapFolder)
    {
        if (slot is null || mapFolder is null)
        {
            return null;
        }

        string path = SlotPath(slot, mapFolder);
        bool drawn = false;
        foreach (string surface in surfaces)
        {
            drawn |= model.SetOutfitTexture(surface, path);
        }

        return drawn ? path : null;
    }
}
