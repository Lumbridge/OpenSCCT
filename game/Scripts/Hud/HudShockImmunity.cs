using System;
using System.Globalization;
using Godot;
using OpenScct.Game.Combat;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>The player's own shock-immunity status, beside the health gauge and below the HUD's screen effects.</summary>
public sealed class HudShockImmunity
{
    // Left-anchored page coordinates: beside the health gauge, above the weapon/vision/ammunition block.
    public static readonly Rect2 PageBounds = new(78f, 274f, 170f, 25f);
    public static readonly Color Amber = new(1f, 0.72f, 0.27f, 1f);

    public MagmaDrawList DrawList { get; } = new();
    public string Label { get; private set; } = "";
    public float Progress { get; private set; }
    public Rect2 Bounds { get; private set; }
    public bool Shown => DrawList.Items.Count != 0;

    public void Draw(Vector2 screen, HudSnapshot snapshot, WhiteRegion? region, bool stretch, bool suppressed = false)
    {
        DrawList.Clear();
        Label = "";
        Progress = 0f;
        Bounds = default;
        ShockImmunityView immunity = snapshot.ShockImmunity;
        if (region is not { } white || suppressed || !snapshot.Valid || snapshot.HideHud ||
            snapshot.Spectator.Active || snapshot.Spectator.GameEnded ||
            !snapshot.HasPawn || snapshot.Health <= 0 || !immunity.Active || screen.X <= 0f || screen.Y <= 0f)
        {
            return;
        }

        float sy = HudLayout.PageScale(screen.Y);
        // Match MagmaRenderer's left anchor. Any ultrawide picture stretch happens after HUD composition.
        float sx = stretch ? screen.X / HudLayout.PageWidth : sy;
        // Spy alarms stack below the radar through page y=305; keep this below that stack.
        float offsetY = snapshot.Side == HudSide.Spy ? 100f * sy : 0f;
        Vector2 Point(float x, float y) => new(x * sx, y * sy);
        Vector2 At(float x, float y) => Point(x, y) + new Vector2(0f, offsetY);
        Bounds = new Rect2(At(PageBounds.Position.X, PageBounds.Position.Y), Point(PageBounds.Size.X, PageBounds.Size.Y));
        Progress = immunity.StateProtected ? 1f : Math.Clamp(immunity.Fraction, 0f, 1f);
        // A server-held final frame remains visibly immune at 0.0s; the presentation flag decides when it clears.
        double seconds = Math.Ceiling(Math.Max(0d, immunity.RemainingSeconds) * 10d) / 10d;
        Label = immunity.StateProtected ? "STATE PROTECTED" :
            "SHOCK IMMUNE · " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

        DrawList.Items.Add(white.Quad("ShockImmunity/Panel", Bounds.Position, Bounds.Size, new Color(.035f, .027f, .015f, .65f)));
        DrawList.Items.Add(white.Quad("ShockImmunity/Accent", Bounds.Position, Point(1.5f, PageBounds.Size.Y), Amber));

        // A crossed lightning bolt rather than a generic shield: this status protects against shock only.
        Stroke("BoltA", At(92f, 278f), At(87f, 285f), 1.6f * sy, Amber, white);
        Stroke("BoltB", At(87f, 285f), At(93f, 285f), 1.6f * sy, Amber, white);
        Stroke("BoltC", At(93f, 285f), At(88f, 292f), 1.6f * sy, Amber, white);
        Stroke("CrossShadow", At(85f, 278f), At(96f, 292f), 3.4f * sy, new Color(.035f, .027f, .015f, 1f), white);
        Stroke("Cross", At(85f, 278f), At(96f, 292f), 1.2f * sy, Amber, white);

        Vector2 textAt = At(101f, 277f);
        MagmaDrawItem text = HudText.Item("ShockImmunity/Label", Label,
            new Rect2(textAt, Point(141f, 11f)), 8.5f * sy, MagmaAlign.Left, Amber);
        text.XScale = sx / sy;
        text.TrackingPx *= text.XScale;
        DrawList.Items.Add(text);
        DrawList.Items.Add(white.Quad("ShockImmunity/Track", At(101f, 291f), Point(141f, 2f), new Color(.5f, .36f, .14f, .35f)));
        if (Progress > 0f)
        {
            DrawList.Items.Add(white.Quad("ShockImmunity/Progress", At(101f, 291f), Point(141f * Progress, 2f), Amber));
        }
    }

    private void Stroke(string name, Vector2 from, Vector2 to, float thickness, Color colour, WhiteRegion white)
    {
        Vector2 direction = (to - from).Normalized();
        Vector2 side = new Vector2(-direction.Y, direction.X) * (thickness / 2f);
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = "ShockImmunity/" + name,
            Kind = MagmaWidgetKind.Image,
            Package = white.Package,
            Texture = white.Texture,
            Points = [from + side, to + side, to - side, from - side],
            Uvs = [white.Uv, white.Uv, white.Uv, white.Uv],
            Colors = [colour, colour, colour, colour],
        });
    }
}
