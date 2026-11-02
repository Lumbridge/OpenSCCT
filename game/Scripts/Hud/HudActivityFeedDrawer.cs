using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Hud.Magma;
using OpenScctGame.UI;

namespace OpenScctGame.Hud;

/// <summary>Compact match activity, composed with the HUD so gameplay screen effects remain above it.</summary>
public sealed class HudActivityFeedDrawer
{
    private static readonly Color Back = new(.025f, .035f, .045f, .86f);
    private static readonly Color TextColour = new(.92f, .95f, .97f, 1f);
    private static readonly Color Dim = new(.65f, .72f, .78f, 1f);
    private static readonly string[] ReservedAreas = ["Radar", "Objectif", "Chrono", "ChronoHack", "ChronoAlarm", "ChronoObjectif", "ChronoObjectif2"];
    private readonly Dictionary<HudPage, float> _reservedRight = new();
    private WhiteRegion? _white;
    private Font? _font;
    private MagmaFont? _bitmap;

    public MagmaDrawList DrawList { get; } = new();
    public Rect2 Bounds { get; private set; }
    public int VisibleCount { get; private set; }
    internal Rect2 AvailableBounds { get; private set; }

    public void Configure(HudAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _white = WhiteRegion.Find(assets);
        _font = UiArt.BodyFont;
        _bitmap = assets.Font(HudAssets.DefaultFont);
        _reservedRight.Clear();
    }

    /// <summary>Entries arrive newest first. Hiding the view never changes or extends their lifetime.</summary>
    public void Draw(Vector2 screen, IReadOnlyList<HudActivityEntry> entries, float now, HudPage? activePage,
        IReadOnlyList<MagmaDrawItem>? fpsItems = null, IReadOnlyList<MagmaDrawItem>? netItems = null, bool shown = true)
    {
        DrawList.Clear();
        Bounds = default;
        VisibleCount = 0;
        AvailableBounds = default;
        if (!shown || _white is null || entries.Count == 0 || screen.X <= 0f || screen.Y <= 0f) return;

        float scale = screen.Y / HudLayout.PageHeight;
        float gap = Math.Max(3f, MathF.Round(3f * scale));
        float margin = Math.Max(6f, MathF.Round(12f * scale));
        float h = Math.Clamp(MathF.Round(9f * scale), 11f, 26f);
        float small = Math.Max(10f, MathF.Round(h * .78f));
        float rowHeight = Math.Max(24f * scale, h + small + 6f);
        float right = RightEdge(screen, activePage, margin, scale);
        float width = Math.Min(280f * scale, right - margin);
        if (width < 96f) return;
        float left = right - width;
        float top = margin;
        top = Below(fpsItems, left, right, top, gap * 2f);
        top = Below(netItems, left, right, top, gap * 2f);
        float safeBottom = screen.Y * .45f;
        float bottom = safeBottom;
        AvoidLocalPrompts(activePage, left, right, gap, ref top, ref bottom);
        if (bottom - top < rowHeight && activePage is not null)
        {
            // At 4:3 with diagnostics, an alert can split the remaining strip in two. Try the space just below it
            // before giving up; do not move the feed across the aiming area or cover the warning.
            float nextTop = top;
            foreach (MagmaDrawItem item in activePage.DrawList.Items)
            {
                if (!IsLocalPrompt(item.Path)) continue;
                Rect2 prompt = ItemBounds(item);
                if (prompt.Position.X < right && prompt.End.X > left && prompt.End.Y > top && prompt.Position.Y < safeBottom)
                    nextTop = Math.Max(nextTop, prompt.End.Y + gap);
            }
            top = nextTop;
            bottom = safeBottom;
            AvoidLocalPrompts(activePage, left, right, gap, ref top, ref bottom);
        }
        int room = Math.Max(0, (int)MathF.Floor((bottom - top + gap) / (rowHeight + gap)));
        AvailableBounds = new Rect2(left, top, width, Math.Max(0f, bottom - top));
        int count = Math.Min(HudActivityFeed.Capacity, Math.Min(entries.Count, room));
        for (int i = 0; i < entries.Count && VisibleCount < count; i++)
        {
            HudActivityEntry entry = entries[i];
            float age = Math.Max(0f, now - entry.ReceivedAt);
            if (age >= HudActivityFeed.Lifetime) continue;
            float enter = Math.Clamp(age / HudActivityFeed.EnterTime, 0f, 1f);
            float fade = Math.Clamp((HudActivityFeed.Lifetime - age) / HudActivityFeed.FadeTime, 0f, 1f);
            float alpha = enter * fade;
            float slide = (1f - enter) * (1f - enter) * 8f * scale;
            var rect = new Rect2(left + slide, top + VisibleCount * (rowHeight + gap), width - slide, rowHeight);
            Row(entry, rect, h, small, scale, alpha);
            VisibleCount++;
        }
        if (VisibleCount > 0) Bounds = new Rect2(left, top, width, VisibleCount * (rowHeight + gap) - gap);
    }

    private float RightEdge(Vector2 screen, HudPage? page, float margin, float scale)
    {
        if (page is null) return screen.X - margin;
        if (!_reservedRight.TryGetValue(page, out float pageLeft))
        {
            pageLeft = HudLayout.PageWidth;
            foreach (string name in ReservedAreas)
            {
                if (page.Area(name) is not { } area) continue;
                MagmaWidget widget = page.Root.Area.Widgets[area.WidgetIndex];
                if (widget.Keys.Count == 0) continue;
                Vector2 at = area.PositionOverride ?? widget.Keys[0].Pos;
                if (at.X < HudLayout.PageWidth * .5f) continue;
                // Reserve hidden countdowns too: the feed must not jump sideways when one appears.
                float childLeft = area.ChildBounds() is { } bounds ? Math.Min(0f, bounds.L) : 0f;
                pageLeft = Math.Min(pageLeft, at.X + childLeft);
            }
            _reservedRight[page] = pageLeft;
        }
        if (pageLeft >= HudLayout.PageWidth) return screen.X - margin;
        float x = page.Renderer.StretchAll ? pageLeft * screen.X / HudLayout.PageWidth
            : screen.X - (HudLayout.PageWidth - pageLeft) * scale;
        return Math.Max(margin, x - Math.Max(6f, 8f * scale));
    }

    private static float Below(IReadOnlyList<MagmaDrawItem>? items, float left, float right, float top, float gap)
    {
        if (items is null) return top;
        foreach (MagmaDrawItem item in items)
        {
            Rect2 bounds = ItemBounds(item);
            if (bounds.Size.X > 0f && bounds.Position.X < right && bounds.End.X > left)
                top = Math.Max(top, bounds.End.Y + gap);
        }
        return top;
    }

    private static void AvoidLocalPrompts(HudPage? page, float left, float right, float gap, ref float top, ref float bottom)
    {
        if (page is null) return;
        // These remain immediate local warnings. They can occupy the central lane when network diagnostics
        // push activity down, especially the merc ALERT banner, so activity yields the space to them.
        foreach (MagmaDrawItem item in page.DrawList.Items)
        {
            if (!IsLocalPrompt(item.Path)) continue;
            Rect2 bounds = ItemBounds(item);
            if (bounds.Size.X <= 0f || bounds.Position.X >= right || bounds.End.X <= left || bounds.End.Y <= top) continue;
            if (bounds.Position.Y <= top) top = bounds.End.Y + gap;
        }
        foreach (MagmaDrawItem item in page.DrawList.Items)
        {
            if (!IsLocalPrompt(item.Path)) continue;
            Rect2 bounds = ItemBounds(item);
            if (bounds.Size.X > 0f && bounds.Position.X < right && bounds.End.X > left && bounds.End.Y > top)
                bottom = Math.Min(bottom, bounds.Position.Y - gap);
        }
    }

    internal static bool IsLocalPrompt(string path) => path.StartsWith("AlertMessage/", StringComparison.Ordinal) ||
        path.StartsWith("SystemMessage/", StringComparison.Ordinal) || path.StartsWith("StateMessage/", StringComparison.Ordinal) ||
        path.StartsWith("ZoneMessage/", StringComparison.Ordinal) || path.StartsWith("ReticuleMessage/", StringComparison.Ordinal);

    internal static Rect2 ItemBounds(MagmaDrawItem item)
    {
        if (item.Kind == MagmaWidgetKind.Text) return item.Rect;
        if (item.Points.Length == 0) return default;
        Vector2 min = item.Points[0], max = min;
        foreach (Vector2 p in item.Points) { min = min.Min(p); max = max.Max(p); }
        return new Rect2(min, max - min);
    }

    private void Row(HudActivityEntry entry, Rect2 rect, float h, float small, float scale, float alpha)
    {
        string path = "Activity/" + entry.Id.ToString(CultureInfo.InvariantCulture) + "/";
        Color tone = Tone(entry.Kind);
        Quad(path + "Panel", rect, Tint(Back, alpha));
        Quad(path + "Accent", new Rect2(rect.Position, new Vector2(Math.Max(2f, 1.5f * scale), rect.Size.Y)), Tint(tone, alpha));
        if (entry.InvolvesLocalPlayer)
        {
            Quad(path + "Local", new Rect2(rect.Position + new Vector2(2f, 0f), rect.Size - new Vector2(2f, 0f)),
                Tint(new Color(tone.R, tone.G, tone.B, .10f), alpha));
        }
        float pad = Math.Max(6f, 7f * scale), icon = Math.Max(12f, 11f * scale);
        bool combat = entry.Kind is HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide or HudActivityKind.Resign
            or HudActivityKind.Sleep or HudActivityKind.Knockout;
        if (!combat)
        {
            OutcomeIcon(path + "Icon/", entry.Kind, new Vector2(rect.Position.X + pad + icon * .5f, rect.GetCenter().Y), icon, Tint(tone, alpha));
            float x = rect.Position.X + pad * 2f + icon;
            float available = rect.End.X - pad - x;
            Text(path + "Label", entry.Label, new Rect2(x, rect.Position.Y + 3f * scale, available, small), small, Tint(tone, alpha));
            Text(path + "Notice", entry.Text, new Rect2(x, rect.End.Y - h - 4f * scale, available, h), h, Tint(TextColour, alpha));
            return;
        }

        string cause = CauseLabel(entry.Cause);
        float actionWidth = Measure(entry.Label, small);
        float centreWidth = Math.Max(actionWidth + icon + pad, cause.Length == 0 ? 0f : Measure(cause, small) + icon + pad);
        centreWidth = Math.Min(centreWidth, rect.Size.X * .46f);
        float nameWidth = Math.Max(0f, (rect.Size.X - pad * 4f - centreWidth) * .5f);
        float middleLeft = rect.Position.X + pad * 2f + nameWidth;
        float actorX = rect.Position.X + pad;
        float victimX = middleLeft + centreWidth + pad;
        float nameY = rect.GetCenter().Y - h * .55f;
        HudActivityParticipant? actor = entry.Actor;
        HudActivityParticipant? victim = entry.Victim;
        Text(path + "Actor", actor?.Name ?? (victim is null ? "Unknown" : ""), new Rect2(actorX, nameY, nameWidth, h), h,
            Tint(TeamColour(actor?.Team), alpha), MagmaAlign.Right);
        Text(path + "Victim", victim?.Name ?? "Unknown", new Rect2(victimX, nameY, nameWidth, h), h,
            Tint(TeamColour(victim?.Team), alpha));
        float actionY = cause.Length > 0 ? rect.Position.Y + 3f * scale : rect.GetCenter().Y - small * .55f;
        float groupWidth = Math.Min(centreWidth, actionWidth + icon + pad * .5f);
        float groupX = middleLeft + (centreWidth - groupWidth) * .5f;
        OutcomeIcon(path + "Icon/", entry.Kind, new Vector2(groupX + icon * .5f, actionY + small * .55f), icon, Tint(tone, alpha));
        Text(path + "Action", entry.Label, new Rect2(groupX + icon + pad * .5f, actionY, Math.Max(0f, groupWidth - icon - pad * .5f), small), small,
            Tint(tone, alpha));
        if (cause.Length > 0)
        {
            float causeY = rect.End.Y - small - 4f * scale;
            float causeWidth = Math.Min(centreWidth, Measure(cause, small) + icon + pad * .5f);
            float causeX = middleLeft + (centreWidth - causeWidth) * .5f;
            CauseIcon(path + "CauseIcon/", entry.Cause, new Vector2(causeX + icon * .5f, causeY + small * .55f), icon * .8f, Tint(Dim, alpha));
            Text(path + "Cause", cause, new Rect2(causeX + icon + pad * .5f, causeY,
                Math.Max(0f, causeWidth - icon - pad * .5f), small), small, Tint(Dim, alpha));
        }
    }

    private static Color TeamColour(int? team) => team switch { 0 => HudK.SpyColour, 1 => HudK.MercColour, _ => TextColour };
    private static Color Tone(HudActivityKind kind) => kind switch
    {
        HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide => new Color(1f, .47f, .43f),
        HudActivityKind.Sleep => new Color(.55f, .77f, 1f),
        HudActivityKind.Knockout => new Color(1f, .76f, .38f),
        HudActivityKind.Objective => new Color(.53f, .91f, .71f),
        HudActivityKind.Alarm or HudActivityKind.LastLife => new Color(1f, .76f, .38f),
        _ => new Color(.69f, .79f, .86f),
    };

    private static Color Tint(Color colour, float alpha) => new(colour.R, colour.G, colour.B, colour.A * alpha);
    private float Measure(string text, float h) => _font?.GetStringSize(text, HorizontalAlignment.Left, -1f, Math.Max(1, Mathf.RoundToInt(h))).X
        ?? _bitmap?.Measure(text, h, 1f, 0f) ?? text.Length * h * .6f;

    private string Fit(string text, float width, float h)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        if (width <= 0f) return "";
        if (Measure(text, h) <= width) return text;
        const string ellipsis = "...";
        if (Measure(ellipsis, h) > width) return "";
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = starts.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            int end = mid < starts.Length ? starts[mid] : text.Length;
            if (Measure(text[..end] + ellipsis, h) <= width) low = mid; else high = mid - 1;
        }
        int cut = low < starts.Length ? starts[low] : text.Length;
        return text[..cut] + ellipsis;
    }

    private void Text(string path, string value, Rect2 rect, float h, Color colour, MagmaAlign align = MagmaAlign.Left)
    {
        string shown = Fit(value, rect.Size.X, h);
        if (shown.Length == 0) return;
        MagmaDrawItem item = DrawList.Add(MagmaWidgetKind.Text);
        item.Path = path;
        item.Text = shown;
        item.Font = HudAssets.DefaultFont;
        item.NativeFont = _font;
        item.Rect = new Rect2(rect.Position.Round(), rect.Size);
        item.HeightPx = h;
        item.Align = align;
        item.Color = colour;
        item.Shadow = new Color(0f, 0f, 0f, colour.A * .6f);
        item.ShadowOffsetPx = Vector2.Down;
    }

    private void Quad(string path, Rect2 rect, Color colour)
    {
        if (_white is not { } white || rect.Size.X <= 0f || rect.Size.Y <= 0f) return;
        MagmaDrawItem item = DrawList.Add(MagmaWidgetKind.Image);
        item.Path = path; item.Package = white.Package; item.Texture = white.Texture;
        item.SetGeometry([rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y)],
            [white.Uv, white.Uv, white.Uv, white.Uv], [colour, colour, colour, colour]);
    }

    private void Stroke(string path, Vector2 from, Vector2 to, float thickness, Color colour)
    {
        if (_white is not { } white) return;
        Vector2 d = (to - from).Normalized();
        Vector2 side = new Vector2(-d.Y, d.X) * thickness * .5f;
        MagmaDrawItem item = DrawList.Add(MagmaWidgetKind.Image);
        item.Path = path; item.Package = white.Package; item.Texture = white.Texture;
        item.SetGeometry([from + side, to + side, to - side, from - side],
            [white.Uv, white.Uv, white.Uv, white.Uv], [colour, colour, colour, colour]);
    }

    private void Shape(string path, Vector2 centre, float size, Color colour, ReadOnlySpan<Vector2> points, bool closed = false)
    {
        for (int i = 1; i < points.Length; i++)
            Stroke(path + i.ToString(CultureInfo.InvariantCulture), centre + points[i - 1] * size, centre + points[i] * size,
                Math.Max(1f, size * .10f), colour);
        if (closed && points.Length > 2)
            Stroke(path + "Close", centre + points[^1] * size, centre + points[0] * size, Math.Max(1f, size * .10f), colour);
    }

    private void OutcomeIcon(string path, HudActivityKind kind, Vector2 at, float size, Color colour)
    {
        switch (kind)
        {
            case HudActivityKind.Kill:
            case HudActivityKind.Death:
            case HudActivityKind.Suicide:
                Shape(path + "Skull", at, size, colour, [new(-.30f, -.38f), new(.30f, -.38f), new(.40f, -.10f), new(.24f, .18f), new(.24f, .4f), new(-.24f, .4f), new(-.24f, .18f), new(-.40f, -.10f)], true);
                Stroke(path + "EyeL", at + new Vector2(-.22f, -.04f) * size, at + new Vector2(-.08f, -.04f) * size, Math.Max(1f, size * .12f), colour);
                Stroke(path + "EyeR", at + new Vector2(.08f, -.04f) * size, at + new Vector2(.22f, -.04f) * size, Math.Max(1f, size * .12f), colour);
                break;
            case HudActivityKind.Sleep:
                Shape(path + "Sleep", at, size, colour, [new(-.30f, -.28f), new(.28f, -.28f), new(-.30f, .30f), new(.28f, .30f)]);
                break;
            case HudActivityKind.Knockout:
                Shape(path + "Impact", at, size, colour, [new(0f, -.48f), new(.12f, -.16f), new(.42f, -.24f), new(.24f, .04f), new(.42f, .27f), new(.10f, .24f), new(0f, .48f), new(-.13f, .18f), new(-.43f, .25f), new(-.25f, -.03f), new(-.42f, -.30f), new(-.12f, -.20f)], true);
                break;
            case HudActivityKind.Objective:
                Shape(path + "Objective", at, size, colour, [new(-.30f, .1f), new(-.08f, .34f), new(.36f, -.30f)]);
                break;
            case HudActivityKind.Join:
            case HudActivityKind.Leave:
            case HudActivityKind.Resign:
                float direction = kind == HudActivityKind.Join ? 1f : -1f;
                Shape(path + "Arrow", at, size, colour, [new(-.35f * direction, 0), new(.35f * direction, 0), new(.10f * direction, -.25f)]);
                Shape(path + "ArrowTip", at, size, colour, [new(.35f * direction, 0), new(.10f * direction, .25f)]);
                break;
            case HudActivityKind.Alarm:
            case HudActivityKind.LastLife:
                Shape(path + "Warning", at, size, colour, [new(0, -.45f), new(.43f, .36f), new(-.43f, .36f)], true);
                Stroke(path + "Mark", at + new Vector2(0f, -.15f) * size, at + new Vector2(0f, .1f) * size, Math.Max(1f, size * .12f), colour);
                break;
            default:
                Shape(path + "Info", at, size, colour, [new(0, -.40f), new(.40f, 0), new(0, .40f), new(-.40f, 0)], true);
                break;
        }
    }

    private void CauseIcon(string path, HudActivityCause cause, Vector2 at, float size, Color colour)
    {
        switch (cause)
        {
            case HudActivityCause.Firearm:
            case HudActivityCause.Headshot:
                Shape(path + "Aim", at, size, colour, [new(-.35f, -.35f), new(.35f, -.35f), new(.35f, .35f), new(-.35f, .35f)], true);
                Stroke(path + "AimH", at - Vector2.Right * size * .5f, at + Vector2.Right * size * .5f, Math.Max(1f, size * .09f), colour);
                Stroke(path + "AimV", at - Vector2.Down * size * .5f, at + Vector2.Down * size * .5f, Math.Max(1f, size * .09f), colour);
                break;
            case HudActivityCause.Explosion:
            case HudActivityCause.StickyBlast:
                OutcomeIcon(path, HudActivityKind.Knockout, at, size, colour);
                break;
            case HudActivityCause.Smoke:
            case HudActivityCause.Poison:
            case HudActivityCause.StickyCamera:
                Shape(path + "Cloud", at, size, colour, [new(-.45f, .30f), new(-.45f, -.03f), new(-.26f, -.12f), new(-.20f, -.38f), new(.08f, -.38f), new(.25f, -.12f), new(.44f, -.08f), new(.44f, .30f)], true);
                break;
            case HudActivityCause.StickyShock:
                Shape(path + "Bolt", at, size, colour, [new(.14f, -.48f), new(-.28f, .04f), new(.13f, .04f), new(-.12f, .48f)]);
                break;
            case HudActivityCause.DropAttack:
            case HudActivityCause.Fall:
                Shape(path + "Down", at, size, colour, [new(0, -.42f), new(0, .34f), new(-.25f, .08f)]);
                Shape(path + "Tip", at, size, colour, [new(0, .34f), new(.25f, .08f)]);
                break;
            default:
                Shape(path + "Contact", at, size, colour, [new(-.35f, -.22f), new(-.08f, -.40f), new(.35f, -.03f), new(.12f, .30f), new(-.35f, .30f)], true);
                break;
        }
    }

    private static string CauseLabel(HudActivityCause cause) => cause switch
    {
        HudActivityCause.None => "",
        HudActivityCause.Firearm => "FIREARM",
        HudActivityCause.Headshot => "HEADSHOT",
        HudActivityCause.Explosion => "EXPLOSION",
        HudActivityCause.Charge => "CHARGE",
        HudActivityCause.DropAttack => "DROP ATTACK",
        HudActivityCause.Elbow => "ELBOW",
        HudActivityCause.Grab => "GRAB",
        HudActivityCause.NeckBreak => "NECK BREAK",
        HudActivityCause.Choke => "CHOKE",
        HudActivityCause.Fall => "FALL",
        HudActivityCause.Crush => "CRUSH",
        HudActivityCause.Poison => "POISON",
        HudActivityCause.StickyShock => "STICKY SHOCK",
        HudActivityCause.StickyBlast => "STICKY BLAST",
        HudActivityCause.Smoke => "SMOKE",
        HudActivityCause.StickyCamera => "CAMERA GAS",
        _ => "OTHER",
    };
}
