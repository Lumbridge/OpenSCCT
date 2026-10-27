using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>The deathmatch respawn weapon the SHud canvas shows: its icon tile and the key that cycles it.</summary>
public sealed record HudRespawnWeapon(int IconU, int IconV, string Key);

/// <summary>
/// <c>SHud.DrawRespawnWeapon</c> (spec/hud/counters-and-prompts.md rule 9): while a dead merc may pick his respawn
/// weapon (Merc Deathmatch), the weapon's 64 x 64 tile of Tex_All_Weapons is drawn 48 units square 8 units from the
/// bottom right corner, in the merc HUD colour, with the Use key's name under it.
/// </summary>
public static class HudRespawnWeaponDrawer
{
    public const string Package = "SHudTextures";
    public const string Atlas = "Icones_Weapons";

    [Provenance(ProvenanceKind.Decoded, "SHud.DrawRespawnWeapon: Size = 48, 8 from the right and bottom, tile 64 x 64")]
    public const float Size = 48f;

    [Provenance(ProvenanceKind.Decoded, "SHud.DrawRespawnWeapon")]
    public const float Margin = 8f;

    [Provenance(ProvenanceKind.Decoded, "SHud.PostRender: merc Hud_R/G/B (128, 93, 34)")]
    public static readonly Color MercHud = HudK.CanvasColour(128, 93, 34);

    /// <summary>Key text height (Canvas.TinyFont; not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "TinyFont height not decoded")]
    public const float KeyHeight = 8f;

    private static readonly UcClassIndex Classes = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
    private static readonly Dictionary<string, (int U, int V)> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the canvas shows for <paramref name="pc"/>, or null.</summary>
    [Uc("SBase.SHud.DrawRespawnWeapon", Sha1 = "207660039affca4dddc8ecd9d5a00e4828259154")]
    [PortOf("uc:SBase.SHud:DrawRespawnWeapon", Spec = "spec/hud/counters-and-prompts.md#rules")]
    public static HudRespawnWeapon? Read(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (pc.RespawnWeapon.Path is not { } path || pc.GameReplicationInfo is null || pc.PlayerReplicationInfo is null ||
            !pc.CanPickRespawnWeapon())
        {
            return null;
        }

        if (!Icons.TryGetValue(path, out (int U, int V) icon))
        {
            icon = Classes.Find(path)?.Create() is Inventory inv ? (inv.IconU, inv.IconV) : (0, 0);
            Icons[path] = icon;
        }

        return new HudRespawnWeapon(icon.U, icon.V, pc.RespawnWeaponKey.Length > 0 ? pc.RespawnWeaponKey : "Use");
    }

    /// <summary>The icon and its key text in the bottom right corner (page units scaled by the screen height).</summary>
    public static void Draw(MagmaDrawList into, Vector2 screen, HudRespawnWeapon w, Vector2 atlas)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(w);
        float s = screen.Y / MagmaRenderer.PageHeight;
        float size = Size * s;
        float keyH = Mathf.Round(KeyHeight * s);
        var topLeft = new Vector2(screen.X - Margin * s - size, screen.Y - Margin * s - keyH - size);
        float u0 = w.IconU / atlas.X, v0 = w.IconV / atlas.Y, u1 = (w.IconU + 64f) / atlas.X, v1 = (w.IconV + 64f) / atlas.Y;
        into.Items.Add(new MagmaDrawItem
        {
            Path = "Respawn/icon",
            Kind = MagmaWidgetKind.Image,
            Package = Package,
            Texture = Atlas,
            Points = [topLeft, topLeft + new Vector2(size, 0f), topLeft + new Vector2(size, size), topLeft + new Vector2(0f, size)],
            Uvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
            Colors = [MercHud, MercHud, MercHud, MercHud],
        });
        into.Items.Add(HudText.Item("Respawn/key", w.Key, new Rect2(topLeft.X - size, topLeft.Y + size, size * 3f, keyH), keyH,
            MagmaAlign.Center, MercHud));
    }
}
