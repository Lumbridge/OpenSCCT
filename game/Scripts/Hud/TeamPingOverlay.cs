using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScctGame.Hud;

public readonly record struct TeamPingVisual(TeamPingMark Mark, float Opacity);

internal readonly record struct TeamPingProjection(Vector2 Point, Vector2 Direction, bool AtEdge);

/// <summary>View-space badges and the compact directional wheel for team pings.</summary>
public partial class TeamPingOverlay : Control
{
    private static readonly Color Panel = new(.025f, .046f, .058f, .91f);
    private static readonly Color Outline = new(.42f, .56f, .60f, .78f);
    private static readonly Color Spy = new(.44f, .91f, .84f);
    private static readonly Color Merc = new(1f, .77f, .43f);
    private static readonly Color Danger = new(1f, .49f, .38f);
    private static readonly Color Text = new(.93f, .96f, .97f);

    private IReadOnlyList<TeamPingVisual> _markers = Array.Empty<TeamPingVisual>();
    private byte? _wheelTeam;
    private TeamPingKind? _selected;

    public TeamPingOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public Camera3D? Camera { get; set; }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    public void ShowFrame(IReadOnlyList<TeamPingVisual> markers, byte? wheelTeam, TeamPingKind? selected)
    {
        _markers = markers;
        _wheelTeam = wheelTeam;
        _selected = selected;
        Visible = wheelTeam.HasValue || markers.Count > 0;
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        float scale = Math.Clamp(Size.Y / 720f, .75f, 2.25f);
        if (_wheelTeam is { } team)
        {
            DrawWheel(team, scale);
            return;
        }

        if (Camera is { } camera)
        {
            foreach (TeamPingVisual marker in _markers)
            {
                if (marker.Opacity > 0f && TryProject(camera, marker.Mark.Location, Size, 31f * scale, out var projection))
                    DrawMarker(marker, projection, scale);
            }
        }
    }

    internal static bool TryProject(Camera3D camera, FVector location, Vector2 size, float margin, out TeamPingProjection projection)
    {
        projection = default;
        if (!GodotObject.IsInstanceValid(camera) || !camera.IsInsideTree() || size.X <= 2 * margin || size.Y <= 2 * margin)
            return false;

        Vector3 world = UeSpace.ToGodot(location);
        if (!world.IsFinite()) return false;

        Vector2 centre = size * .5f;
        Vector2 extent = centre - Vector2.One * margin;
        Vector2 direction;
        if (camera.IsPositionBehind(world))
        {
            Vector3 local = camera.ToLocal(world);
            direction = new Vector2(local.X, -local.Y);
        }
        else
        {
            Vector2 point = camera.UnprojectPosition(world);
            direction = point - centre;
            if (Mathf.Abs(direction.X) <= extent.X && Mathf.Abs(direction.Y) <= extent.Y)
            {
                projection = new TeamPingProjection(point, Vector2.Zero, false);
                return true;
            }
        }

        if (!direction.IsFinite()) return false;
        direction = direction.IsZeroApprox() ? Vector2.Down : direction.Normalized();
        float edge = 1f / MathF.Max(MathF.Abs(direction.X) / extent.X, MathF.Abs(direction.Y) / extent.Y);
        projection = new TeamPingProjection(centre + direction * edge, direction, true);
        return true;
    }

    private void DrawMarker(TeamPingVisual marker, TeamPingProjection projection, float scale)
    {
        float alpha = Math.Clamp(marker.Opacity, 0f, 1f);
        Color accent = Accent(marker.Mark.Team, marker.Mark.Kind) with { A = alpha };
        Vector2 point = projection.Point;
        float radius = 13f * scale;
        if (projection.AtEdge)
        {
            Vector2 direction = projection.Direction;
            Vector2 perpendicular = new(-direction.Y, direction.X);
            Vector2 basePoint = point + direction * (radius + 2f * scale);
            DrawColoredPolygon([
                basePoint + direction * (8f * scale),
                basePoint + perpendicular * (4.5f * scale),
                basePoint - perpendicular * (4.5f * scale),
            ], accent);
        }
        else
        {
            point -= Vector2.Down * (radius + 6f * scale);
            DrawLine(point + Vector2.Down * radius, projection.Point, accent, 1.5f * scale, true);
            DrawCircle(projection.Point, 2.3f * scale, accent, true, -1f, true);
        }

        DrawDisc(point, radius, Panel with { A = Panel.A * alpha }, accent, 1.5f * scale);
        DrawIcon(marker.Mark.Kind, point, .78f * scale, accent);
    }

    private void DrawWheel(byte team, float scale)
    {
        Vector2 centre = Size * .5f;
        float spread = 63f * scale;
        DrawCircle(centre, (spread + 22f * scale), Panel with { A = .57f }, true, -1f, true);
        DrawArc(centre, spread, 0f, Mathf.Tau, 64, Outline with { A = .35f }, scale, true);
        DrawWheelOption(TeamPingKind.Watch, centre + Vector2.Up * spread, team, scale);
        DrawWheelOption(TeamPingKind.Check, centre + Vector2.Right * spread, team, scale);
        if (team == 0)
        {
            DrawWheelOption(TeamPingKind.CoopBoost, centre + Vector2.Down * spread, team, scale);
            DrawWheelOption(TeamPingKind.Mine, centre + Vector2.Left * spread, team, scale);
        }

        DrawDisc(centre, 14f * scale, Panel, Outline, scale);
        Color cancel = _selected is null ? Text : Outline;
        Vector2 cross = new(4.5f * scale, 4.5f * scale);
        DrawLine(centre - cross, centre + cross, cancel, 1.6f * scale, true);
        cross.Y = -cross.Y;
        DrawLine(centre - cross, centre + cross, cancel, 1.6f * scale, true);
    }

    private void DrawWheelOption(TeamPingKind kind, Vector2 point, byte team, float scale)
    {
        bool selected = kind == _selected;
        Color accent = Accent(team, kind);
        float radius = (selected ? 21f : 19f) * scale;
        DrawDisc(point, radius, selected ? accent : Panel, selected ? Text : Outline, (selected ? 1.8f : 1f) * scale);
        DrawIcon(kind, point, scale, selected ? Panel : accent);

        Font font = GetThemeDefaultFont();
        int fontSize = Math.Max(10, Mathf.RoundToInt(12f * scale));
        string label = Label(kind);
        float width = font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize).X;
        Vector2 text = point + new Vector2(-width * .5f, radius + fontSize + scale);
        DrawString(font, text + Vector2.One * scale, label, HorizontalAlignment.Left, -1, fontSize, Colors.Black);
        DrawString(font, text, label, HorizontalAlignment.Left, -1, fontSize, selected ? accent : Text);
    }

    private void DrawDisc(Vector2 centre, float radius, Color fill, Color outline, float stroke)
    {
        DrawCircle(centre, radius, fill, true, -1f, true);
        DrawArc(centre, radius, 0f, Mathf.Tau, 36, outline, stroke, true);
    }

    private static string Label(TeamPingKind kind) => kind switch
    {
        TeamPingKind.Watch => "Watch here",
        TeamPingKind.Check => "Check here",
        TeamPingKind.CoopBoost => "Co-op boost",
        TeamPingKind.Mine => "Mine",
        _ => "Ping",
    };

    private static Color Accent(byte team, TeamPingKind kind) => kind == TeamPingKind.Mine ? Danger : team == 0 ? Spy : Merc;

    private static readonly Vector2[] Pin = [new(-8f, -1f), new(0f, -9f), new(8f, -1f), new(0f, 9f), new(-8f, -1f)];
    private static readonly Vector2[] Eye = [new(-11f, 0f), new(-6f, -4.4f), new(0f, -6f), new(6f, -4.4f), new(11f, 0f),
        new(6f, 4.4f), new(0f, 6f), new(-6f, 4.4f), new(-11f, 0f)];
    private static readonly Vector2[] Question = [new(-4.5f, -4f), new(-3f, -7f), new(0f, -8f), new(3f, -7f),
        new(4.5f, -4.5f), new(3.5f, -2f), new(0f, .5f), new(0f, 3f)];
    private static readonly Vector2[] Boost = [new(-9f, 0f), new(0f, -8f), new(9f, 0f)];

    private void DrawIcon(TeamPingKind kind, Vector2 centre, float scale, Color color)
    {
        DrawSetTransform(centre, 0f, Vector2.One * scale);
        switch (kind)
        {
            case TeamPingKind.Watch:
                DrawPolyline(Eye, color, 1.6f, true);
                DrawCircle(Vector2.Zero, 2.6f, color, true, -1f, true);
                break;
            case TeamPingKind.Check:
                DrawPolyline(Question, color, 2f, true);
                DrawCircle(new Vector2(0f, 7.5f), 1.2f, color, true, -1f, true);
                break;
            case TeamPingKind.CoopBoost:
                DrawPolyline(Boost, color, 2f, true);
                DrawSetTransform(centre + Vector2.Down * (6f * scale), 0f, Vector2.One * scale);
                DrawPolyline(Boost, color, 2f, true);
                break;
            case TeamPingKind.Mine:
                if (HudActivityIcons.Load("proximity-mine") is { } mine)
                    DrawTextureRect(mine, new Rect2(-11f, -11f, 22f, 22f), false, color);
                break;
            default:
                DrawPolyline(Pin, color, 1.6f, true);
                DrawCircle(Vector2.Zero, 1.9f, color, true, -1f, true);
                break;
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
