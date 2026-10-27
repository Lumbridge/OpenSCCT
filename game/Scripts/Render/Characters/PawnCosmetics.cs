using System;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Options;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// OpenSCCT's own cosmetics on the drawn models: a pawn's look (replicated on its PlayerReplicationInfo) turned into
/// camo textures, tints and light hues. Lights change hue only: brightness and size stay retail.
/// </summary>
public static class PawnCosmetics
{
    /// <summary>
    /// The retail goggle glow's hue, degrees: a yellow green, not pure green. Measured on the converted textures,
    /// weighted by saturation: the lens self-illumination (Shadw_agent under its SI mask) 75, the beam (goggle_green) 67.
    /// At 120 every light colour came out about 50 degrees short (amber pink, cyan green, violet blue).
    /// </summary>
    public const float SpyRetailHue = 71f;

    /// <summary>The retail merc torch and lamp orange's hue, degrees (RenderK.TorchHue 21 of 255).</summary>
    public const float MercRetailHue = 21f * 360f / 255f;

    /// <summary>How strongly a tint recolours (the rest keeps the retail colours).</summary>
    public const float TintStrength = 0.8f;

    /// <summary>
    /// The look drawn on the pawn (retail when there is none). The local player's own pawn wears the Armoury's choice
    /// while "hide mine" keeps it off the servers; other players' pawns are drawn retail while "hide others" is on.
    /// </summary>
    public static CosmeticLook LookOf(Pawn? pawn)
    {
        if (pawn?.PlayerReplicationInfo is not { } pri)
        {
            return CosmeticLook.Default;
        }

        GameSettings settings = SettingsStore.Current;
        bool mine = ReferenceEquals(pawn.Level?.GetLocalPlayerController()?.PlayerReplicationInfo, pri);
        if (mine)
        {
            return settings.CosmeticsHideMine ? settings.OwnLook(SideOf(pawn)) : new CosmeticLook(pri.SvmLook);
        }

        return settings.CosmeticsHideOthers ? CosmeticLook.Default : new CosmeticLook(pri.SvmLook);
    }

    public static CosmeticSide SideOf(Pawn pawn) => pawn is SPAWNDEFENSE ? CosmeticSide.Merc : CosmeticSide.Spy;

    /// <summary>The look's gun item for the merc's current gun class, or the spy's pistol item.</summary>
    public static CosmeticItem? GunItem(Pawn pawn)
    {
        CosmeticLook look = LookOf(pawn);
        if (pawn is not SBasePawn sb)
        {
            return null;
        }

        if (pawn is SPawnAttaque)
        {
            return look[CosmeticSlot.Pistol];
        }

        return SGameInfo.GunClassOf(sb.MyWeapon) switch
        {
            "ar" => look[CosmeticSlot.GunAr],
            "smg" => look[CosmeticSlot.GunSmg],
            "shotgun" => look[CosmeticSlot.GunShotgun],
            _ => null,
        };
    }

    /// <summary>The hue turn (radians) from the side's retail light colour to the item's, or null for retail.</summary>
    public static float? HueTurn(CosmeticItem? light, CosmeticSide side)
    {
        if (light is not { Kind: CosmeticKind.Hue })
        {
            return null;
        }

        float from = side == CosmeticSide.Spy ? SpyRetailHue : MercRetailHue;
        return Mathf.DegToRad(light.Hue - from);
    }

    /// <summary>Draws a body or gun item (camo or tint) on a model; null puts the retail look back.</summary>
    public static void ApplyItem(SkeletalModel model, CosmeticItem? item)
    {
        ArgumentNullException.ThrowIfNull(model);
        switch (item?.Kind)
        {
            case CosmeticKind.Texture:
                model.SetCosmetic(item.Pattern);
                model.SetTint(Vector3.One, 0f);
                break;
            case CosmeticKind.Tint:
                (float r, float g, float b) = item.Colour;
                model.SetCosmetic(null);
                model.SetTint(new Vector3(r, g, b), TintStrength);
                break;
            default:
                model.SetCosmetic(null);
                model.SetTint(Vector3.One, 0f);
                break;
        }
    }
}
