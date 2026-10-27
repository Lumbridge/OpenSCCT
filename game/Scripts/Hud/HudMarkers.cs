using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The spy's Enhanced Reality objective markers, drawn on the SHud canvas over the page (Bridge HUD notes, "Enhanced
/// Reality objective markers"): an icon per objective trigger at its projected location, coloured by state, with
/// brackets, a height arrow, and labels for the distance or the capture percent and "Under hacking" / "Access
/// denied". Markers fade while the spy moves. Needs a projector from the play view (world point to screen pixels).
/// </summary>
public sealed class HudMarkers
{
    public const string Package = "SHudTextures";
    public const string Atlas = "MN_boutons";

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: global ER fade, 0.5 s ramps, 0.99 still, 0 moving")]
    private const float FadeRate = 2f;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: global ER fade settles at 0.99 while still")]
    private const float FadeStill = 0.99f;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: ER labels are canvas text of height 13 page units")]
    private const float LabelHeight = 13f;

    private static readonly Color Hacked = Rgb(192, 40, 40);
    private static readonly Color Locked = Rgb(192, 192, 0);
    private static readonly Color Idle = Rgb(200, 246, 184);

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md GPO markers: colour (230, 100, 28)")]
    private static readonly Color GpoColour = Rgb(230, 100, 28);

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md GPO markers: skipped within 20 canvas px of the edge")]
    private const float GpoEdge = 20f;

    private float _fade = FadeStill;

    /// <summary>
    /// The MN_boutons tile (top-left, 32 x 32; the 30 x 30 icon starts one texel in) of a marker's class, the
    /// iconx / icony SHud.BuildObjectivesAndTriggersList stores per entry (game 0x10af80b0) and both the Enhanced
    /// Reality markers and the binocular markers draw: terminal 64, disk (SFlag) 0, bomb spot 32, drop zone 128, Disk
    /// Hunt disk 224, all on row 96. An objective's own marker takes its triggers' tile. Null: no entry, no marker.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af80b0: SDisk (0xe0,0x60), SComputerObjectiveTrigger 0x40, SFlag 0, SFlagDropZone 0x80, IsA SBombTargetObjectiveTrigger 0x20, row 0x60")]
    public static Vector2? IconTile(HudObjectiveKind kind) => kind switch
    {
        HudObjectiveKind.Terminal => new Vector2(64f, 96f),
        HudObjectiveKind.Disk => new Vector2(0f, 96f),
        HudObjectiveKind.Bomb => new Vector2(32f, 96f),
        HudObjectiveKind.DropZone => new Vector2(128f, 96f),
        HudObjectiveKind.HuntDisk => new Vector2(224f, 96f),
        _ => null,
    };

    /// <summary>World point (uu) to screen pixels; null when behind the camera.</summary>
    public Func<FVector, Vector2?>? Projector { get; set; }

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>Pixel size of the marker atlas (MN_boutons is 256 x 128).</summary>
    public Vector2 AtlasSize { get; set; } = new(256f, 128f);

    public float Fade => _fade;

    /// <summary>Reloaded's ws_er_small (first-run default on): the icons keep their 4:3 size on a wide screen.</summary>
    public bool ErSmall { get; set; } = true;

    /// <summary>Reloaded's widescreen_hud (first-run default on).</summary>
    public bool WidescreenHud { get; set; } = true;

    public void Update(HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(s);
        float target = s.PawnSpeed > 0f ? 0f : FadeStill;
        _fade = Math.Abs(target - _fade) <= FadeRate * dt ? target : _fade + Math.Sign(target - _fade) * FadeRate * dt;
    }

    public void Draw(HudSnapshot s, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(s);
        DrawList.Clear();
        if (Projector is null || !s.Valid || s.HideHud || s.Side != HudSide.Spy || !s.EnhancedReality || !s.HasPawn)
        {
            return;
        }

        // The ER canvas spans HudScreenRight x HudScreenBottom (HudLayout): with ws_er_small the icons keep the 4:3
        // scale H / 480, without it the 640 wide canvas is kept and they grow with the width.
        float pageS = HudLayout.ErScale(screen.X, screen.Y, widescreen: true, WidescreenHud, ErSmall);
        bool moving = s.PawnSpeed > 0f;
        DrawGpos(s, screen, pageS, moving);
        var labelled = new List<(float D, HudObjective O, Vector2 P)>();
        foreach (HudObjective o in s.Objectives)
        {
            if (o.Taken || Projector(o.Location) is not { } p || p.X <= 0f || p.Y <= 0f || p.X >= screen.X || p.Y >= screen.Y)
            {
                continue;
            }

            if (IconTile(o.Kind) is not { } tile)
            {
                continue;
            }

            float d = (o.Location - s.PawnLocation).Size();
            float scale = Math.Clamp((3000f - d) * 0.0006f, 0.8f, 1.5f) * pageS;
            float alpha = Falloff(d) / 255f * (moving ? 0.7f : 1f);
            // Disk Hunt disks and free drop zones are drawn at full spread (ERDrawObjectives passes 1 there).
            bool full = o.Triggering || o.Role is HudErRole.HuntDisk or HudErRole.DropZone;
            float spread = full ? 1f : _fade;
            string path = "ER/" + o.Role + "/" + o.Name;

            // Icon
            if (spread > 0f)
            {
                Color c = o.Role == HudErRole.DropZone ? (o.Alert ? Hacked : GpoColour)
                    : o.Triggering ? Hacked : o.Locked ? Locked : Idle;
                Quad(path + "/icon", p, new Vector2(30f, 30f) * scale, new Rect2(tile + Vector2.One, 30f, 30f), c with { A = alpha * spread });
            }

            // Brackets: full-spread markers (triggering) pulse between 20 and 13 units out, the others sit at
            // 11 + 9 x fade (game 0x10af8460, param 8 >= 1 pulses). An objective's own marker (the native's
            // bracket flag 0) has only the top-right and bottom-left corners and does not pulse (game 0x10af8460).
            var white = new Color(1f, 1f, 1f, alpha);
            if (o.Role == HudErRole.Objective)
            {
                float j = 11f + 9f * spread;
                Bracket(path + "/tr", p, new Vector2(j - 7f, -j), new Vector2(121f, 96f), scale, white);
                Bracket(path + "/bl", p, new Vector2(-j, j - 7f), new Vector2(96f, 121f), scale, white);
            }
            else
            {
                float k = spread >= 1f ? (s.TimeSeconds - MathF.Floor(s.TimeSeconds) <= 0.5f ? 20f : 13f) : 11f + 9f * spread;
                Bracket(path + "/tl", p, new Vector2(-k, -k), new Vector2(96f, 96f), scale, white);
                Bracket(path + "/tr", p, new Vector2(k - 7f, -k), new Vector2(121f, 96f), scale, white);
                Bracket(path + "/bl", p, new Vector2(-k, k - 7f), new Vector2(96f, 121f), scale, white);
                Bracket(path + "/br", p, new Vector2(k - 7f, k - 7f), new Vector2(121f, 121f), scale, white);
            }

            // Height arrow
            float dz = o.Location.Z - s.PawnLocation.Z;
            if (Math.Abs(dz) > 200f)
            {
                float lift = (9f * spread + 16f) * scale;
                var centre = new Vector2(p.X, dz > 0f ? p.Y - lift : p.Y + lift);
                var src = new Rect2(104f, 104f, 16f, 16f);
                Quad(path + "/arrow", centre, new Vector2(16f, dz > 0f ? 16f : -16f) * scale, src, white);
            }

            // Bomb spots never get a distance, percent or state label; disks get no state label (game 0x10af8c00).
            if (o.Kind == HudObjectiveKind.Bomb)
            {
                continue;
            }

            labelled.Add((d, o, p));
            if (o.Triggering && o.Kind != HudObjectiveKind.Disk && o.Role is HudErRole.Trigger or HudErRole.Objective)
            {
                Label(path + "/state", s.HackingText, p + new Vector2(0f, 20f * pageS), pageS, alpha * _fade);
            }
        }

        // Distance or percent labels on the two nearest markers.
        labelled.Sort((a, b) => a.D.CompareTo(b.D));
        for (int i = 0; i < labelled.Count && i < 2; i++)
        {
            (float d, HudObjective o, Vector2 p) = labelled[i];
            float alpha = Falloff(d) / 255f * (moving ? 0.7f : 1f) * _fade;
            bool percent = o.Percent >= 0 && ((int)s.TimeSeconds & 3) >= 2;
            string text = percent
                ? string.Create(CultureInfo.InvariantCulture, $"{o.Percent}%")
                : string.Create(CultureInfo.InvariantCulture, $"{(int)(MathF.Truncate(d) * s.DistanceScale)} {s.DistanceUnit}");
            Label("ER/" + o.Role + "/" + o.Name + "/distance", text, p - new Vector2(0f, 40f * pageS), pageS, alpha);
            if (!o.Triggering && o.Locked)
            {
                Label("ER/" + o.Role + "/" + o.Name + "/state", s.SecuredText, p + new Vector2(0f, 20f * pageS), pageS, alpha);
            }
        }
    }

    /// <summary>
    /// The orange GPO markers (Bridge HUD notes, "GPO markers"): SHud.MalfunctionGamePlayObjects as MN_boutons diamonds
    /// that blink each second, with a distance label while the spy stands still; skipped within 20 canvas units of the
    /// screen edge. Drawn before the objective markers, as the native HudAtt does.
    /// </summary>
    private void DrawGpos(HudSnapshot s, Vector2 screen, float pageS, bool moving)
    {
        float edgeX = GpoEdge * screen.X / MagmaRenderer.PageWidth, edgeY = GpoEdge * screen.Y / MagmaRenderer.PageHeight;
        float blink = 1f - (s.TimeSeconds - MathF.Floor(s.TimeSeconds));
        int i = 0;
        foreach (FVector v in s.Gpos)
        {
            i++;
            if (Projector!(v) is not { } p || p.X < edgeX || p.Y < edgeY || p.X > screen.X - edgeX || p.Y > screen.Y - edgeY)
            {
                continue;
            }

            float d = (v - s.PawnLocation).Size();
            float scale = Math.Clamp((3000f - d) * 0.0006f, 0.6f, 1.5f) * pageS;
            float alpha = (Math.Clamp((2500f - d) * 0.068f, 0f, 135f) + (moving ? 90f : 120f)) / 255f;
            string path = "GPO/" + i.ToString(CultureInfo.InvariantCulture);
            Quad(path + "/icon", p, new Vector2(32f, 32f) * scale, new Rect2(160f, 96f, 32f, 32f), GpoColour with { A = alpha * blink });
            if (!moving)
            {
                string text = string.Create(CultureInfo.InvariantCulture, $"{d * s.DistanceScale:0} {s.DistanceUnit}");
                Label(path + "/distance", text, p - new Vector2(0f, (16f * scale / pageS + 15f) * pageS), pageS, alpha, GpoColour);
            }
        }
    }

    /// <summary>Marker alpha by distance, 0..255.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: f(d) = d < 450 ? max(0, (d - 150) 0.85) : clamp((2500 - d) 0.068, 0, 135) + 120")]
    public static float Falloff(float d) =>
        d < 450f ? Math.Max(0f, (d - 150f) * 0.85f) : Math.Clamp((2500f - d) * 0.068f, 0f, 135f) + 120f;

    private void Bracket(string path, Vector2 centre, Vector2 offset, Vector2 tile, float scale, Color color)
    {
        Vector2 topLeft = centre + offset * scale;
        Quad(path, topLeft + new Vector2(3.5f, 3.5f) * scale, new Vector2(7f, 7f) * scale, new Rect2(tile, 7f, 7f), color);
    }

    private void Quad(string path, Vector2 centre, Vector2 size, Rect2 src, Color color)
    {
        if (color.A <= 0f)
        {
            return;
        }

        Vector2 h = size / 2f;
        Vector2 tex = AtlasSize;
        float u0 = src.Position.X / tex.X, v0 = src.Position.Y / tex.Y, u1 = src.End.X / tex.X, v1 = src.End.Y / tex.Y;
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Image,
            Package = Package,
            Texture = Atlas,
            Points = [centre + new Vector2(-h.X, -h.Y), centre + new Vector2(h.X, -h.Y), centre + new Vector2(h.X, h.Y), centre + new Vector2(-h.X, h.Y)],
            Uvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
            Colors = [color, color, color, color],
        });
    }

    private void Label(string path, string text, Vector2 centre, float pageS, float alpha) =>
        Label(path, text, centre, pageS, alpha, new Color(1f, 1f, 1f, 1f));

    private void Label(string path, string text, Vector2 centre, float pageS, float alpha, Color colour)
    {
        if (text.Length == 0 || alpha <= 0f)
        {
            return;
        }

        float h = MathF.Round(LabelHeight * pageS);
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Text,
            Text = text,
            Font = HudAssets.DefaultFont,
            Rect = new Rect2(centre.X - 200f * pageS, centre.Y - h / 2f, 400f * pageS, h),
            HeightPx = h,
            Align = MagmaAlign.Center,
            Color = colour with { A = alpha },
            TrackingPx = HudText.Tracking * h,
        });
    }

    private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
