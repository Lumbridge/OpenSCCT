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
    private readonly List<Vector2> _blockedBands = new(16);
    private readonly List<HudActivityEntry> _activeEntries = new(HudActivityFeed.Capacity);
    private readonly float[] _rowHeights = new float[HudActivityFeed.Capacity];
    private readonly bool[] _wrapCauses = new bool[HudActivityFeed.Capacity];
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
        float feedScale = scale * .65f;
        float gap = Math.Max(3f, MathF.Round(3f * feedScale));
        float margin = Math.Max(6f, MathF.Round(12f * scale));
        float h = Math.Clamp(MathF.Round(10f * feedScale), 12f, 26f);
        float small = Math.Max(10f, MathF.Round(h * .78f));
        float rowHeight = Math.Max(36f, Math.Max(36f * feedScale, h + small + 12f * feedScale));
        float right = RightEdge(screen, activePage, margin, scale);
        float width = Math.Min(300f * feedScale, right - margin);
        if (width < 96f) return;
        float textWidth = width - Math.Max(24f, 22f * feedScale) - 3f * Math.Max(6f, 6f * feedScale);
        _activeEntries.Clear();
        foreach (HudActivityEntry entry in entries)
        {
            if (now - entry.ReceivedAt >= HudActivityFeed.Lifetime) continue;
            bool wrap = HudActivityPresentation.UsesVictimFirstMessage(entry)
                ? Measure(HudActivityPresentation.ContactLead(entry), small) + Measure(" ", small) +
                  Measure(HudActivityPresentation.ContactTarget(entry), small) + 2f > textWidth
                : entry.IsCombat && HudActivityPresentation.Cause(entry).Length > 0 &&
                  Measure(HudActivityPresentation.Action(entry.Kind), small) + Measure(" · ", small) +
                  Measure(HudActivityPresentation.Cause(entry), small) + 4f > textWidth;
            _wrapCauses[_activeEntries.Count] = wrap;
            int extraLines = (wrap ? 1 : 0) + (HudActivityPresentation.HasTriggerDetail(entry) ? 1 : 0);
            _rowHeights[_activeEntries.Count] = rowHeight + extraLines * (small + 3f * feedScale);
            _activeEntries.Add(entry);
            if (_activeEntries.Count == HudActivityFeed.Capacity) break;
        }
        if (_activeEntries.Count == 0) return;
        float left = right - width;
        float safeBottom = screen.Y * .45f;
        int desiredRows = _activeEntries.Count;
        Rect2 fpsBounds = TelemetryBounds(fpsItems), netBounds = TelemetryBounds(netItems);
        Vector2 available = AvailableBand(activePage, fpsBounds, netBounds, left, right, margin, safeBottom,
            gap, desiredRows);
        int room = Rows(available);
        float preferredRight = right;
        // Tall graphs can fill the entire usual lane. Keep notifications above the crosshair by trying
        // space beside those panels before dropping rows; equal-capacity choices retain the usual lane.
        TryBeside(fpsBounds);
        TryBeside(netBounds);
        float top = available.X, bottom = available.Y;
        AvailableBounds = new Rect2(left, top, width, Math.Max(0f, bottom - top));
        int count = Math.Min(desiredRows, room);
        float cursor = top;
        for (int i = 0; i < count; i++)
        {
            HudActivityEntry entry = _activeEntries[i];
            float age = Math.Max(0f, now - entry.ReceivedAt);
            float enter = Math.Clamp(age / HudActivityFeed.EnterTime, 0f, 1f);
            float fade = Math.Clamp((HudActivityFeed.Lifetime - age) / HudActivityFeed.FadeTime, 0f, 1f);
            float alpha = enter * fade;
            float slide = (1f - enter) * (1f - enter) * 8f * feedScale;
            float fittedWidth = width;
            var rect = new Rect2(right - fittedWidth + slide, cursor, fittedWidth - slide, _rowHeights[i]);
            Row(entry, rect, h, small, feedScale, alpha, _wrapCauses[i]);
            cursor += _rowHeights[i] + gap;
            VisibleCount++;
        }
        if (VisibleCount > 0) Bounds = new Rect2(left, top, width, cursor - top - gap);

        int Rows(Vector2 band) => RowsThatFit(band.Y - band.X, gap, desiredRows);

        void TryBeside(Rect2 panel)
        {
            if (room >= desiredRows || panel.Size.X <= 0f || panel.Size.Y <= 0f ||
                panel.Position.Y >= safeBottom || panel.End.Y <= margin) return;
            float candidateRight = Math.Min(preferredRight, panel.Position.X - gap * 2f);
            float candidateLeft = candidateRight - width;
            if (candidateLeft < margin || candidateRight >= preferredRight) return;
            Vector2 band = AvailableBand(activePage, fpsBounds, netBounds, candidateLeft, candidateRight,
                margin, safeBottom, gap, desiredRows);
            int rows = Rows(band);
            if (rows <= room) return;
            left = candidateLeft;
            right = candidateRight;
            available = band;
            room = rows;
        }
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

    private int RowsThatFit(float availableHeight, float gap, int desiredRows)
    {
        float used = 0f;
        for (int i = 0; i < desiredRows; i++)
        {
            used += _rowHeights[i] + (i > 0 ? gap : 0f);
            if (used > availableHeight + .01f) return i;
        }
        return desiredRows;
    }

    private Vector2 AvailableBand(HudPage? page, Rect2 fpsBounds, Rect2 netBounds,
        float left, float right, float top, float bottom, float gap, int desiredRows)
    {
        _blockedBands.Clear();
        AddBlockedBand(fpsBounds, left, right, top, bottom, gap * 2f);
        AddBlockedBand(netBounds, left, right, top, bottom, gap * 2f);
        if (page is not null)
        {
            foreach (MagmaDrawItem item in page.DrawList.Items)
                if (IsLocalPrompt(item.Path) || IsReservedArea(item.Path))
                    AddBlockedBand(ItemBounds(item), left, right, top, bottom, gap);
        }
        _blockedBands.Sort(static (a, b) => a.X.CompareTo(b.X));

        // Within this candidate lane, prefer the upper free interval when both fit the same number of rows,
        // but use space below an obstruction when that preserves more activity. Bottom telemetry cannot
        // push the feed offscreen, and centre telemetry cannot conceal otherwise usable space above it.
        Vector2 best = new(top, top);
        int bestRows = 0;
        float cursor = top;
        foreach (Vector2 blocked in _blockedBands)
        {
            Consider(cursor, blocked.X);
            cursor = Math.Max(cursor, blocked.Y);
        }
        Consider(cursor, bottom);
        return best;

        void Consider(float begin, float end)
        {
            int rows = RowsThatFit(end - begin, gap, desiredRows);
            if (rows <= bestRows) return;
            bestRows = rows;
            best = new Vector2(begin, end);
        }
    }

    private static Rect2 TelemetryBounds(IReadOnlyList<MagmaDrawItem>? items)
    {
        if (items is null) return default;
        Rect2 union = default;
        bool any = false;
        // A graph can contain hundreds of segments. Reserve its element once rather than sorting every segment.
        foreach (MagmaDrawItem item in items)
        {
            Rect2 bounds = ItemBounds(item);
            if (bounds.Size.X <= 0f || bounds.Size.Y <= 0f) continue;
            union = any ? union.Merge(bounds) : bounds;
            any = true;
        }
        return union;
    }

    private static bool IsReservedArea(string path)
    {
        foreach (string name in ReservedAreas)
            if (path.Length > name.Length && path[name.Length] == '/' && path.StartsWith(name, StringComparison.Ordinal)) return true;
        return false;
    }

    private void AddBlockedBand(Rect2 bounds, float left, float right, float top, float bottom, float gap)
    {
        if (bounds.Size.X <= 0f || bounds.Size.Y <= 0f || bounds.Position.X >= right || bounds.End.X <= left) return;
        float begin = Math.Max(top, bounds.Position.Y - gap), end = Math.Min(bottom, bounds.End.Y + gap);
        if (end > begin) _blockedBands.Add(new Vector2(begin, end));
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

    private void Row(HudActivityEntry entry, Rect2 rect, float h, float small, float scale, float alpha, bool wrapCause)
    {
        string path = "Activity/" + entry.Id.ToString(CultureInfo.InvariantCulture) + "/";
        Color tone = Tone(entry.Kind);
        Quad(path + "Panel", rect, Tint(Back, alpha));
        Quad(path + "Accent", new Rect2(rect.Position, new Vector2(Math.Max(2f, 1.5f * scale), rect.Size.Y)), Tint(tone, alpha));
        if (entry.InvolvesLocalPlayer)
            Quad(path + "Local", new Rect2(rect.Position + new Vector2(2f, 0), rect.Size - new Vector2(2f, 0)), Tint(new Color(tone.R, tone.G, tone.B, .10f), alpha));

        float pad = Math.Max(6f, 6f * scale);
        float icon = Math.Min(rect.Size.Y - 2 * pad, Math.Max(24f, 22f * scale));
        Icon(path + "Icon/" + HudActivityIcons.NameFor(entry), entry,
            new Rect2(rect.Position.X + pad, rect.GetCenter().Y - icon * .5f, icon, icon), Tint(tone, alpha));
        float x = rect.Position.X + icon + pad * 2f;
        float available = Math.Max(0f, rect.End.X - pad - x);
        bool hasTrigger = HudActivityPresentation.HasTriggerDetail(entry);
        int extraLines = (hasTrigger ? 1 : 0) + (wrapCause ? 1 : 0);
        float top = rect.Position.Y + (rect.Size.Y - h - small - 3f * scale - extraLines * (small + 3f * scale)) * .5f;
        float subtitleY = top + h + 3f * scale;
        if (!entry.IsCombat)
        {
            Text(path + "Label", HudActivityPresentation.NoticeTitle(entry), new Rect2(x, top, available, h), h,
                Tint(entry.Actor is null ? TextColour : TeamColour(entry.Actor.Team), alpha));
            Text(path + "Notice", HudActivityPresentation.NoticeDetail(entry), new Rect2(x, subtitleY, available, small), small, Tint(Dim, alpha));
            return;
        }

        if (HudActivityPresentation.UsesVictimFirstMessage(entry))
        {
            // These deaths describe the victim's contact with another player, so name the dead player first.
            // Reserve the verb even when a long player name needs an ellipsis.
            float diedWidth = Measure("died", h) + 1f;
            float space = Measure(" ", h) + 1f;
            string victim = HudActivityPresentation.ParticipantName(entry.Victim);
            float victimWidth = Math.Min(Measure(victim, h) + 1f, Math.Max(0f, available - diedWidth - space));
            Text(path + "Victim", victim, new Rect2(x, top, victimWidth, h), h, Tint(TeamColour(entry.Victim?.Team), alpha));
            Text(path + "Action", "died", new Rect2(x + victimWidth + space, top, diedWidth, h), h, Tint(tone, alpha));
            string lead = HudActivityPresentation.ContactLead(entry), target = HudActivityPresentation.ContactTarget(entry);
            float leadWidth = Math.Min(available, Measure(lead, small) + 1f);
            float targetGap = Measure(" ", small) + 1f;
            Text(path + "Cause", lead, new Rect2(x, subtitleY, leadWidth, small), small, Tint(Dim, alpha));
            Text(path + "Actor", target,
                wrapCause ? new Rect2(x, subtitleY + small + 3f * scale, available, small) :
                    new Rect2(x + leadWidth + targetGap, subtitleY, Math.Max(0f, available - leadWidth - targetGap), small),
                small, Tint(entry.Actor is null ? Dim : TeamColour(entry.Actor.Team), alpha));
            if (hasTrigger)
                Text(path + "Trigger", HudActivityPresentation.TriggerDetail(entry),
                    new Rect2(x, subtitleY + (wrapCause ? 2 : 1) * (small + 3f * scale), available, small), small, Tint(Dim, alpha));
            return;
        }

        bool pair = entry.Actor is not null && entry.Victim is not null && entry.Actor.PlayerId != entry.Victim.PlayerId;
        if (pair)
        {
            string actor = HudActivityPresentation.ParticipantName(entry.Actor);
            string victim = HudActivityPresentation.ParticipantName(entry.Victim);
            float arrowWidth = Measure("→", h) + pad * 2;
            float namesWidth = Math.Max(0, available - arrowWidth);
            float actorWanted = Measure(actor, h) + 1, victimWanted = Measure(victim, h) + 1;
            // Guarantee both participants a useful share when one name is much longer.
            float actorWidth = actorWanted + victimWanted <= namesWidth ? actorWanted :
                Math.Clamp(namesWidth * actorWanted / Math.Max(1, actorWanted + victimWanted), namesWidth * .3f, namesWidth * .7f);
            Text(path + "Actor", actor, new Rect2(x, top, actorWidth, h), h, Tint(TeamColour(entry.Actor!.Team), alpha));
            Text(path + "Direction", "→", new Rect2(x + actorWidth, top, arrowWidth, h), h, Tint(Dim, alpha), MagmaAlign.Center);
            Text(path + "Victim", victim, new Rect2(x + actorWidth + arrowWidth, top, namesWidth - actorWidth, h), h, Tint(TeamColour(entry.Victim!.Team), alpha));
        }
        else
        {
            var participant = entry.Victim ?? entry.Actor;
            Text(path + "Victim", HudActivityPresentation.ParticipantName(participant), new Rect2(x, top, available, h), h,
                Tint(TeamColour(participant?.Team), alpha));
        }
        string action = HudActivityPresentation.Action(entry.Kind), cause = HudActivityPresentation.Cause(entry);
        float actionWidth = Math.Min(available, Measure(action, small) + 1);
        Text(path + "Action", action, new Rect2(x, subtitleY, actionWidth, small), small, Tint(tone, alpha));
        if (wrapCause)
            Text(path + "Cause", cause, new Rect2(x, subtitleY + small + 3f * scale, available, small), small, Tint(Dim, alpha));
        else if (cause.Length > 0)
        {
            float dotWidth = Measure(" · ", small) + 2;
            Text(path + "Separator", " · ", new Rect2(x + actionWidth, subtitleY, Math.Min(dotWidth, Math.Max(0, available - actionWidth)), small), small, Tint(Dim, alpha));
            Text(path + "Cause", cause, new Rect2(x + actionWidth + dotWidth, subtitleY, Math.Max(0, available - actionWidth - dotWidth), small), small, Tint(Dim, alpha));
        }
        if (hasTrigger)
            Text(path + "Trigger", HudActivityPresentation.TriggerDetail(entry),
                new Rect2(x, subtitleY + (wrapCause ? 2 : 1) * (small + 3f * scale), available, small), small, Tint(Dim, alpha));
    }

    private void Icon(string path, HudActivityEntry entry, Rect2 rect, Color colour)
    {
        if (HudActivityIcons.For(entry) is not { } texture) return;
        MagmaDrawItem item = DrawList.Add(MagmaWidgetKind.Image);
        item.Path = path; item.NativeTexture = texture; item.Clamp = true;
        item.SetGeometry([rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y)],
            [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down], [colour, colour, colour, colour]);
    }

    private static Color TeamColour(int? team) => team switch { 0 => HudK.SpyColour, 1 => HudK.MercColour, _ => TextColour };
    private static Color Tone(HudActivityKind kind) => kind switch
    {
        HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide => new Color(1f, .47f, .43f),
        HudActivityKind.Sleep => new Color(.55f, .77f, 1f),
        HudActivityKind.Knockout => new Color(1f, .76f, .38f),
        HudActivityKind.LastLife => new Color(1f, .76f, .38f),
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

}
