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
    private static readonly string[] s_weapons = ["SWeaponFirst.p190", "SWeaponFirst.M160", "SWeaponFirst.Hk81", "SWeaponFirst.BM401"];

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

    /// <summary>
    /// Loads both teams' rigs, their common attachments, and map outfits before reporting scene readiness. Stock
    /// rigs need the same preparation as custom outfits: waiting until the first replicated pawn appears blocks
    /// the client after the server has already started the round. Rain also prepares custom outfits' wet skins.
    /// </summary>
    public static void Prepare(MapOutfits outfits, string mapFolder, string? sharedRoot, bool rain)
    {
        ArgumentNullException.ThrowIfNull(outfits);
        foreach (bool spy in new[] { true, false })
        {
            if (SkeletalModel.FindGlb(StockAsset(spy), sharedRoot) is not { } stock)
            {
                continue;
            }

            TeamOutfit? outfit = outfits.For(spy);
            Dressed dressed = Load(stock, outfit, mapFolder, sharedRoot);
            dressed.Model.Root.Free();
            if (rain && dressed.Outfit is not null)
            {
                WetSkins.For(dressed.Glb, localSpy: true, dressed.DryBody, dressed.DryHead, dressed.Stock);
                WetSkins.For(dressed.Glb, localSpy: false, dressed.DryBody, dressed.DryHead, dressed.Stock);
            }
        }

        // PawnAttachments and GoggleGlowView create these on the first drawn pawn. Sidearm models are already
        // prepared with their first-person counterparts by FirstPersonView.Preload (when the round can have them).
        foreach (string asset in s_weapons)
            if (SkeletalModel.FindGlb(asset, sharedRoot) is { } attachment)
                SkeletalModel.Load(attachment).Root.Free();
        foreach (string asset in new[] { "SGoggle.ATT01_beams", "SGoggle.DEF01_beams" })
            if (SkeletalModel.FindGlb(asset, sharedRoot) is { } glow)
                SkeletalModel.Load(glow).Root.Free();
    }

    /// <summary>
    /// Draws the prepared bodies and carried meshes in the private loading viewport. Reading a template alone
    /// cannot compile its skinned-mesh draw pipelines; their first appearance must happen before scene readiness.
    /// <paramref name="sidearms"/> false leaves out the pistols of a round that cannot give them (FirstPersonNeeds).
    /// </summary>
    public static void Warmup(MapOutfits outfits, string mapFolder, string? sharedRoot, Node3D samples, bool sidearms = true)
    {
        foreach (bool spy in new[] { true, false })
        {
            if (SkeletalModel.FindGlb(StockAsset(spy), sharedRoot) is { } stock)
                Add(Load(stock, outfits.For(spy), mapFolder, sharedRoot).Model);

            // Use the live glow view's material setup (alpha blended spy, added merc), not the raw glTF skin.
            var glow = new GoggleGlowView(samples, sharedRoot, _ => (true, Transform3D.Identity));
            glow.DrawStill(merc: !spy, hueTurn: null);
            if (glow.Model is { } model) Fit(model);
        }
        foreach (string asset in s_weapons)
            if (SkeletalModel.FindGlb(asset, sharedRoot) is { } glb) Add(SkeletalModel.Load(glb));
        if (sidearms)
            foreach (var sidearm in OpenScct.Core.Engine.Sidearms.All)
                if (SkeletalModel.FindGlb(sidearm.Asset, sharedRoot) is { } glb) Add(SkeletalModel.Load(glb));

        void Add(SkeletalModel model)
        {
            samples.AddChild(model.Root);
            Fit(model);
        }

        static void Fit(SkeletalModel model)
        {
            // Keep every mesh in the tiny viewport's frustum, including differently sized map outfits.
            Aabb? bounds = null;
            Transform3D local = model.Root.GlobalTransform.AffineInverse();
            foreach (MeshInstance3D mesh in model.Meshes)
            {
                Aabb box = local * mesh.GlobalTransform * mesh.GetAabb();
                bounds = bounds is { } combined ? combined.Merge(box) : box;
            }
            if (bounds is not { } extent) return;
            float size = Math.Max(extent.Size.X, Math.Max(extent.Size.Y, extent.Size.Z));
            if (size <= 0f) return;
            float scale = 1.5f / size;
            model.Root.Scale = Vector3.One * scale;
            model.Root.Position = -extent.GetCenter() * scale;
        }
    }

    /// <summary>The team's stock mesh: SPerso.ATT_01 for the spies, SPerso.DEF_01 for the mercs.</summary>
    public static string StockAsset(bool spy) => spy ? "SPerso.ATT_01" : "SPerso.DEF_01";

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
    /// the outfit's body texture. Null when the outfit leaves the merc's body as it is (no model of his own, no body
    /// texture: a head texture alone), when a mesh cannot be read, or when the view model's arms are not the merc
    /// rig's.
    /// </summary>
    public static OutfitSkins.ArmsSkin? FirstPersonArms(string viewModelGlb, string armsMaterial, string stockGlb, TeamOutfit? merc,
        string? mapFolder, string? sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(stockGlb);
        if (merc is null || mapFolder is null)
        {
            return null;
        }

        string glb = merc.Model is { } model && ModelPath(model, mapFolder, sharedRoot) is { } path ? path : stockGlb;
        bool ownModel = !string.Equals(Path.GetFullPath(glb), Path.GetFullPath(stockGlb), StringComparison.OrdinalIgnoreCase);
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

        // The sleeves and gloves are body: a head texture alone leaves them as they are.
        bool dressed = ownModel || diffuse.ContainsKey(s_body[0]);
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
