using System;
using System.Globalization;
using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The radar (A_Radar) of both pages (Bridge radar notes 3.1 to 3.3): the dish turns with the player's yaw; each blip
/// is placed at its projected position, as the plain, Higher or Lower icon by height, or as an edge arrow (enemies
/// and alarms) when out of range. The merc's alarm blips include the terminals being hacked (SHud.ObjectivesList),
/// which is the merc's hack marker.
/// </summary>
public sealed class HudRadar
{
    private const float ArrowSize = 10f;

    private readonly MagmaNode _radar;
    private readonly bool _merc;

    public HudRadar(MagmaNode radar, bool merc)
    {
        ArgumentNullException.ThrowIfNull(radar);
        _radar = radar;
        _merc = merc;
        // Original scanner captures and IMAGE draw 0x10a54600 use integer rect edges, with no extra unit.
        // Scope this to the spy radar; other HUD areas retain their existing measured placement.
        _radar.NativeImageRendering = !merc;
    }

    /// <summary>Blips placed by the last update, per kind.</summary>
    public int Teammates { get; private set; }

    public int Enemies { get; private set; }

    public int Alarms { get; private set; }

    /// <summary>The radar's unrotated frame, shared by editor selection and gameplay layout transforms.</summary>
    public static Rect2? LayoutBounds(HudPage page, Vector2 screen)
    {
        if (page.Area("Radar") is not { } radar || radar.Find("fond") is not { } background)
            return null;
        var radarKeys = page.Root.Area.Widgets[radar.WidgetIndex].Keys;
        var backgroundKeys = radar.Area.Widgets[background.WidgetIndex].Keys;
        if (radarKeys.Count == 0 || backgroundKeys.Count == 0) return null;
        Vector2 origin = (radar.PositionOverride ?? radarKeys[0].Pos)
            + (background.PositionOverride ?? backgroundKeys[0].Pos);
        Rect2? bounds = null;
        // The rotating ring's polygon changes its axis-aligned bounds throughout its animation. Using that
        // changing box as a scale pivot or screen clamp makes the whole radar drift. The first-key art frame
        // is fixed; blips, pulse rings and perturbation effects can animate around it without moving the HUD.
        foreach (MagmaWidget widget in background.Area.Widgets)
        {
            if (widget.Hidden || widget.Kind != MagmaWidgetKind.Image || widget.Mask == MagmaMask.Set || widget.Keys.Count == 0)
                continue;
            Rect4 rect = widget.Keys[0].Rect;
            var at = new Vector2(Math.Min(rect.L, rect.R), Math.Min(rect.T, rect.B)) + origin;
            float expansion = radar.UsesNativeImageRendering ? 0f : 1f;
            var size = new Vector2(Math.Abs(rect.R - rect.L) + expansion, Math.Abs(rect.B - rect.T) + expansion);
            var image = new Rect2(at, size);
            bounds = bounds is { } prior ? prior.Merge(image) : image;
        }
        if (bounds is not { } frame) return null;
        float scale = screen.Y / MagmaRenderer.PageHeight;
        bool stretch = page.Renderer.StretchAll || page.Options.StretchHud;
        float x = stretch ? frame.Position.X * screen.X / MagmaRenderer.PageWidth
            : frame.End.X <= MagmaRenderer.PageWidth / 2f ? frame.Position.X * scale
            : frame.Position.X >= MagmaRenderer.PageWidth / 2f ? screen.X - (MagmaRenderer.PageWidth - frame.Position.X) * scale
            : screen.X * .5f + (frame.Position.X - MagmaRenderer.PageWidth / 2f) * scale;
        return new Rect2(new Vector2(x, frame.Position.Y * scale),
            new Vector2(frame.Size.X * (stretch ? screen.X / MagmaRenderer.PageWidth : scale), frame.Size.Y * scale));
    }

    public void Update(HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _radar.SetVisible(true, children: false);
        int teammates = 0, enemies = 0, alarms = 0;
        bool placed = false;
        foreach (HudBlip blip in s.Blips)
        {
            switch (blip.Kind)
            {
                case HudBlipKind.Teammate when teammates < HudK.RadarTeammates:
                    placed |= Place("IconTeammate", ++teammates, null, blip, s);
                    break;
                case HudBlipKind.Enemy when enemies < HudK.RadarEnemies:
                    placed |= Place("IconEnemy", ++enemies, "IconEnemyArrow", blip, s);
                    break;
                case HudBlipKind.Alarm when _merc && alarms < HudK.RadarAlarms:
                    placed |= Place("IconAlarm", ++alarms, "IconAlarmArrow", blip, s);
                    break;
                default:
                    break;
            }
        }

        HideFrom("IconTeammate", teammates + 1, null);
        HideFrom("IconEnemy", enemies + 1, "IconEnemyArrow");
        HideFrom("IconAlarm", alarms + 1, "IconAlarmArrow");
        Teammates = teammates;
        Enemies = enemies;
        Alarms = alarms;

        // The dish shows only when something is on it; then the spy's radar area sits stopped at frame 0.
        MagmaNode? dish = _radar.Find("fond");
        bool heart = _radar.Find("Heart") is { Visible: true } || _radar.Find("Heart1") is { Visible: true };
        bool dishShown = placed || heart;
        if (dish is not null)
        {
            dish.SetVisible(dishShown);
            float th = (-s.Yaw - 16384) * MathF.PI / 32768f;
            dish.Widget("fond").Rotation = th * 180f / MathF.PI;
        }

        // The spy's radar code rewinds the area; the merc's does not (so a perturbation or shutdown plays it).
        if (dishShown && !_merc)
        {
            _radar.SetFrame(0);
            _radar.Playing = false;
        }

        // The heart driver applies cone visibility after the radar update. The spy-bullet trace is not driven yet.
        _radar.Find("Heart")?.SetVisible(false);
        _radar.Find("Heart1")?.SetVisible(false);
        _radar.Find("piege")?.SetVisible(false);
    }

    private bool Place(string prefix, int slot, string? arrowPrefix, HudBlip blip, HudSnapshot s)
    {
        MagmaNode? plain = _radar.Find(IconName(prefix, slot, ""));
        MagmaNode? higher = _radar.Find(IconName(prefix, slot, "Higher"));
        MagmaNode? lower = _radar.Find(IconName(prefix, slot, "Lower"));
        MagmaNode? arrow = arrowPrefix is null ? null : _radar.Find(IconName(arrowPrefix, slot, ""));
        Vector2 xy = HudK.RadarProject(blip.Location, s.PawnLocation, s.Yaw);
        float dz = (blip.Location.Z - s.PawnLocation.Z) / HudK.RadarScale;
        bool inRange = Math.Abs(xy.X) <= HudK.RadarRadius && Math.Abs(xy.Y) <= HudK.RadarRadius && xy.Length() <= HudK.RadarRadius;
        if (inRange)
        {
            MagmaNode? icon = dz < HudK.RadarLower ? lower : dz > HudK.RadarHigher ? higher : plain;
            HideUnless(plain, icon);
            HideUnless(higher, icon);
            HideUnless(lower, icon);
            HideUnless(arrow, icon);

            if (icon is not null)
            {
                icon.PositionOverride = new Vector2(MathF.Truncate(xy.X + HudK.RadarCentre.X), MathF.Truncate(xy.Y + HudK.RadarCentre.Y));
                if (!icon.Visible)
                {
                    icon.Restart();
                }
            }

            return true;
        }

        plain?.SetVisible(false);
        higher?.SetVisible(false);
        lower?.SetVisible(false);
        if (arrow is null)
        {
            return false;
        }

        // At the edge, pointing at the blip.
        float a = MathF.Atan(Math.Abs(xy.Y) / Math.Max(1e-6f, Math.Abs(xy.X)));
        float ax = HudK.RadarRadius * MathF.Cos(a) * (xy.X > 0f ? 1f : -1f);
        float ay = HudK.RadarRadius * MathF.Sin(a) * (xy.Y > 0f ? 1f : -1f);
        arrow.PositionOverride = new Vector2(MathF.Truncate(ax + HudK.RadarCentre.X - ArrowSize / 2f),
            MathF.Truncate(ay + HudK.RadarCentre.Y - ArrowSize / 2f));
        arrow.Widget("Arrow").Rotation = MathF.Atan2(ay, ax) * 180f / MathF.PI;
        if (!arrow.Visible)
        {
            arrow.Restart();
        }

        return true;
    }

    private void HideFrom(string prefix, int first, string? arrowPrefix)
    {
        for (int i = first; i <= 8; i++)
        {
            _radar.Find(IconName(prefix, i, ""))?.SetVisible(false);
            _radar.Find(IconName(prefix, i, "Higher"))?.SetVisible(false);
            _radar.Find(IconName(prefix, i, "Lower"))?.SetVisible(false);
            if (arrowPrefix is not null)
            {
                _radar.Find(IconName(arrowPrefix, i, ""))?.SetVisible(false);
            }
        }
    }

    private static void HideUnless(MagmaNode? node, MagmaNode? shown)
    {
        if (node is not null && !ReferenceEquals(node, shown))
        {
            node.SetVisible(false);
        }
    }

    /// <summary>Slot names (IconEnemy3Higher), made once: building them every frame made strings per blip per frame.</summary>
    private static readonly System.Collections.Generic.Dictionary<(string Prefix, string Suffix), string[]> s_iconNames = [];

    private static string IconName(string prefix, int slot, string suffix)
    {
        if (!s_iconNames.TryGetValue((prefix, suffix), out string[]? names))
        {
            names = new string[10];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = prefix + i.ToString(CultureInfo.InvariantCulture) + suffix;
            }

            s_iconNames[(prefix, suffix)] = names;
        }

        return slot >= 0 && slot < names.Length ? names[slot] : prefix + slot.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
