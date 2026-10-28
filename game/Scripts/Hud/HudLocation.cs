using System;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// Reloaded's and Enhanced's location readout (rl:qol.location, enh:ui.location): the name of the place the viewed
/// pawn is in, from the map's named volumes and zones, above the radar. The name is picked as
/// PlayerReplicationInfo.GetLocationName picks it (the highest-priority named volume the pawn touches, else its zone's
/// name), but on this machine from the pawn it sees, so it needs nothing from the server.
/// </summary>
public sealed class HudLocation
{
    /// <summary>Volume's class default LocationName: a volume left with it names no place.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Volume defaults LocationName=\"unspecified\"; GetLocationName skips it")]
    public const string Unspecified = "unspecified";

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The last name drawn ("" for none).</summary>
    public string Shown { get; private set; } = "";

    /// <summary>The name of the place <paramref name="pawn"/> is in, or "" when the map names none there.</summary>
    [Provenance(ProvenanceKind.Estimated, "Volume.Encompasses is native and not ported; a touching volume counts, as UpdatePlayerLocation's port does")]
    public static string NameAt(Pawn? pawn)
    {
        if (pawn is null)
        {
            return "";
        }

        Volume? best = null;
        foreach (Actor a in pawn.Touching)
        {
            if (a is Volume v && Named(v.LocationName) && (best is null || v.LocationPriority > best.LocationPriority))
            {
                best = v;
            }
        }

        if (best is not null)
        {
            return best.LocationName;
        }

        return pawn.Region.Zone is { } zone && Named(zone.LocationName) ? zone.LocationName : "";
    }

    private static bool Named(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !string.Equals(name, Unspecified, StringComparison.OrdinalIgnoreCase);

    /// <summary>The spy page's radar instance (P_HudATT A_Radar first key), used when the page drawn has no radar.</summary>
    [Provenance(ProvenanceKind.Measured, "Hud.mgm P_HudATT: the Radar instance's first key is at (29, 82), read from the loaded page")]
    public static readonly Vector2 DefaultRadarOrigin = new(29f, 82f);

    /// <summary>Text height in page units (the frame-rate readout's size, a fifth larger).</summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded's location font size is not decoded: the frame-rate readout's size x 1.2")]
    public const float TextHeight = HudK.FpsTextHeight * 1.2f;

    /// <summary>Page units between the text's foot and the top of the radar dish.</summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded screenshot: the name sits just above the radar")]
    public const float Gap = 2f;

    /// <summary>
    /// Draws <paramref name="name"/> left-aligned just above the radar dish of <paramref name="page"/>, in the page's
    /// colour, when shown. Without a page or a radar it goes where the spy page's radar is.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded screenshot: top left, above the radar, left-aligned over its left edge, in the HUD's text colour; font not decoded")]
    public void Draw(Vector2 screen, string name, bool shown, HudPage? page = null)
    {
        DrawList.Clear();
        Shown = shown ? name : "";
        if (Shown.Length == 0)
        {
            return;
        }

        Vector2 origin = DefaultRadarOrigin;
        if (page?.Area("Radar") is { } radar && page.Root.Area.Widgets[radar.WidgetIndex].Keys is { Count: > 0 } keys)
        {
            origin = radar.PositionOverride ?? keys[0].Pos;
        }

        // The dish is a circle of RadarRadius around RadarCentre inside the instance (HudRadar places blips so).
        float left = origin.X + HudK.RadarCentre.X - HudK.RadarRadius;
        float right = origin.X + HudK.RadarCentre.X + HudK.RadarRadius;
        float top = origin.Y + HudK.RadarCentre.Y - HudK.RadarRadius - Gap - TextHeight;
        MagmaRenderer? renderer = page?.Renderer;
        MagmaAnchor anchor = renderer is { StretchAll: true } ? MagmaAnchor.Stretch
            : right <= MagmaRenderer.PageWidth / 2f ? MagmaAnchor.Left
            : left >= MagmaRenderer.PageWidth / 2f ? MagmaAnchor.Right
            : MagmaAnchor.Center;
        float s = screen.Y / MagmaRenderer.PageHeight;
        Vector2 at = renderer?.ToScreen(new Vector2(left, top), anchor) ?? new Vector2(left * s, top * s);
        float h = Mathf.Round(TextHeight * s);
        Color colour = page?.Side == HudSide.Merc ? HudK.MercColour : HudK.SpyColour;
        DrawList.Items.Add(HudText.Item("Location", Shown, new Rect2(at.X, at.Y, Mathf.Max(1f, screen.X - at.X), h), h, MagmaAlign.Left, colour));
    }
}
