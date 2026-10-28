using System;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// Reloaded's and Enhanced's location readout (rl:qol.location, enh:ui.location): the name of the place the viewed
/// pawn is in, from the map's named volumes and zones, at the top of the screen. The name is picked as
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

    /// <summary>Draws <paramref name="name"/> centred at the top of the screen, when shown.</summary>
    [Provenance(ProvenanceKind.Estimated, "Enhanced's location element's place and font are not decoded: top centre, the frame-rate readout's size")]
    public void Draw(Vector2 screen, string name, bool shown)
    {
        DrawList.Clear();
        Shown = shown ? name : "";
        if (Shown.Length == 0)
        {
            return;
        }

        float h = Mathf.Round(HudK.FpsTextHeight * 1.2f * screen.Y / MagmaRenderer.PageHeight);
        DrawList.Items.Add(HudText.Item("Location", Shown, new Rect2(0f, 6f, screen.X, h), h, MagmaAlign.Center, new Color(1f, 1f, 1f, 0.85f)));
    }
}
