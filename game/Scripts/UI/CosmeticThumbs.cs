using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.UI;

/// <summary>
/// Small pictures of cosmetics for the menus (Armoury, Achievements): a camo item's built texture for its slot, or a
/// colour swatch for a tint or light hue. Made once per item and size.
/// </summary>
public static class CosmeticThumbs
{
    private static readonly Dictionary<(string Id, int Size), Texture2D?> s_cache = new();

    /// <summary>The item's picture at <paramref name="size"/> pixels square, or null when its texture was not built.</summary>
    public static Texture2D? For(CosmeticItem item, int size = 40)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (s_cache.TryGetValue((item.Id, size), out Texture2D? cached))
        {
            return cached;
        }

        Image? image = null;
        switch (item.Kind)
        {
            case CosmeticKind.Texture when ThumbFile(item) is { } path && File.Exists(path):
                image = Image.LoadFromFile(path);
                image?.Resize(size, size, Image.Interpolation.Lanczos);
                break;
            case CosmeticKind.Texture when CamoTexture(item) is { } path && File.Exists(path):
                image = Image.LoadFromFile(path);
                image?.Resize(size, size, Image.Interpolation.Bilinear);
                break;
            case CosmeticKind.Tint:
                (float r, float g, float b) = item.Colour;
                image = Swatch(new Color(r, g, b), size);
                break;
            case CosmeticKind.Hue:
                image = Swatch(Color.FromHsv(item.Hue / 360f, 0.85f, 1f), size);
                break;
        }

        Texture2D? texture = image is null ? null : ImageTexture.CreateFromImage(image);
        s_cache[(item.Id, size)] = texture;
        return texture;
    }

    private static Image Swatch(Color colour, int size)
    {
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(colour);
        return image;
    }

    /// <summary>The content builder's flat picture of the item's pattern (chars/thumbs/cosmetic_&lt;pattern&gt;.png).</summary>
    private static string? ThumbFile(CosmeticItem item) => item.Pattern.Length == 0
        ? null
        : Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "thumbs", "cosmetic_" + item.Pattern + ".png");

    /// <summary>The built camo texture that shows the item best (the content builder's file for the slot's main texture).</summary>
    private static string? CamoTexture(CosmeticItem item)
    {
        (string package, string stem) = (item.Slot, item.Side) switch
        {
            (CosmeticSlot.Body, CosmeticSide.Spy) => ("SPerso", "SPersoTextures_Shadw_agent"),
            (CosmeticSlot.Body, CosmeticSide.Merc) => ("SPerso", "SPersoTextures_DEF_01_A_Body"),
            (CosmeticSlot.Pistol, _) => ("SWeaponFirst", "SWeaponTex_p190"),
            (CosmeticSlot.GunAr, _) => ("SWeaponFirst", "SWeaponTex_M160"),
            (CosmeticSlot.GunSmg, _) => ("SWeaponFirst", "SWeaponTex_Hk81"),
            (CosmeticSlot.GunShotgun, _) => ("SWeaponFirst", "SWeaponTex_shotgun"),
            _ => (string.Empty, string.Empty),
        };
        return package.Length == 0
            ? null
            : Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", package, "textures", "cosmetic_" + item.Pattern + "_" + stem + ".png");
    }
}
