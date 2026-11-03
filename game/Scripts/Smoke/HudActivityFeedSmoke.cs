using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Gadgets;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

namespace OpenScctGame.Smoke;

/// <summary>Feed layout across every HUD page, plus optional actual-renderer captures and native-font checks.</summary>
public sealed class HudActivityFeedSmoke : SmokeTest
{
    public const string TestName = "hud-activity-feed";
    public override string Name => TestName;
    private static readonly Vector2I[] Sizes = [new(640, 480), new(1280, 720), new(1920, 1080), new(3440, 1440)];
    private static readonly string[] Pages = ["spy", "merc", "spectator", "spy-scope", "merc-scope", "sticky-camera", "camera-network"];
    private static int StandardCaptureCount => Sizes.Length * Pages.Length + Sizes.Length;
    private static int PreviewCaptureCount => StandardCaptureCount + Sizes.Length;
    private static readonly string[] Galleries = ["mines-and-grenades", "environment-and-fire", "melee-and-movement", "weapons-and-headshots", "clarified-causes"];
    private static int CaptureCount => PreviewCaptureCount + Galleries.Length;
    private HudAssets? _assets;
    private SubViewport? _viewport;
    private Rect2 _textProbe;
    private Rect2 _feedProbe;
    private int _case;
    private int _settle;
    private bool _checked;
    private string? _capture;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            _assets ??= HudAssets.TryLoad(null, out string? error) ?? throw new InvalidOperationException(error);
            if (!_checked)
            {
                CheckLayouts(_assets);
                _checked = true;
                if (DisplayServer.GetName() == "headless") return SmokeResult.Pass("28 page/resolution layouts, long names, diagnostics, stretch, animation, expiry and hiding; pixel checks require a renderer");
                _capture = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.Combine(RenderSmokeArgs.RepoRoot, "local", "test-results", TestName);
                Directory.CreateDirectory(_capture);
            }
            if (_viewport is null)
            {
                CreateCapture(context);
                return SmokeResult.Running;
            }
            if (_settle-- > 0) return SmokeResult.Running;
            using Image image = _viewport.GetTexture().GetImage();
            Require(!image.IsEmpty(), "empty renderer capture");
            int lit = 0;
            for (int y = Math.Max(0, (int)_textProbe.Position.Y); y < Math.Min(image.GetHeight(), (int)MathF.Ceiling(_textProbe.End.Y)); y++)
            for (int x = Math.Max(0, (int)_textProbe.Position.X); x < Math.Min(image.GetWidth(), (int)MathF.Ceiling(_textProbe.End.X)); x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > .6f) lit++;
            }
            Require(lit > 5, "native Inter text did not render in " + CaseName(_case));
            Require(image.SavePng(Path.Combine(_capture!, CaseName(_case) + ".png")) == Error.Ok, "could not save activity feed capture");
            if (CaseName(_case) is "spy-1920x1080" or "kill-types-1920x1080")
            {
                using Image detail = image.GetRegion(new Rect2I((Vector2I)_feedProbe.Position.Floor(), (Vector2I)_feedProbe.Size.Ceil()));
                Require(detail.SavePng(Path.Combine(_capture!, _case >= StandardCaptureCount ? "kill-types-detail.png" : "event-feed-detail.png")) == Error.Ok, "could not save feed detail");
            }
            _viewport.Free();
            _viewport = null;
            if (++_case < CaptureCount) return SmokeResult.Running;
            return SmokeResult.Pass($"{CaptureCount} real-renderer HUD captures with visible Inter text, detailed kills/triggers and galleries; layout, animation and visibility checks passed");
        }
        catch (Exception error)
        {
            _viewport?.Free();
            _viewport = null;
            return SmokeResult.Fail(error.ToString());
        }
    }

    private static void CheckLayouts(HudAssets assets)
    {
        CheckIconResources();
        HudActivityEntry[] entries = Entries(longNames: true);
        var diagnosticCounts = new List<string>();
        foreach (Vector2I resolution in Sizes)
        foreach (string name in Pages)
        {
            Vector2 screen = resolution;
            HudPage page = Page(assets, name, screen);
            var feed = new HudActivityFeedDrawer();
            feed.Configure(assets);
            feed.Draw(screen, entries, 11f, page);
            Require(feed.VisibleCount is >= 3 and <= 5, "combat rows not shown: " + name + " " + resolution);
            if (!page.DrawList.Items.Any(i => HudActivityFeedDrawer.IsLocalPrompt(i.Path)))
                Require(feed.VisibleCount == 5, "five rows not shown with clear space: " + name + " " + resolution);
            CheckBounds(feed, screen);
            CheckText(feed);
            Require(feed.DrawList.TextOf("Activity/5/Actor")?.EndsWith("...", StringComparison.Ordinal) == true, "long name was not truncated");
            Require(feed.DrawList.TextOf("Activity/5/Action") == "Eliminated", "kill action missing: " + name + " " + resolution + "; text=" + feed.DrawList.TextOf("Activity/5/Action"));
            Require(feed.DrawList.TextOf("Activity/5/Cause") == "Headshot", "cause missing: " + name + " " + resolution + "; text=" + feed.DrawList.TextOf("Activity/5/Cause"));
            Require(feed.DrawList.Any("Activity/4/Icon/") && feed.DrawList.Any("Activity/3/Icon/"), "sleep/KO icons missing");
            foreach (MagmaDrawItem icon in feed.DrawList.Items.Where(i => i.Path.Contains("/Icon/", StringComparison.Ordinal)))
            {
                Require(icon.NativeTexture is not null, "native icon texture missing: " + icon.Path);
                Require(HudActivityFeedDrawer.ItemBounds(icon).Size.X >= 24f, "icon shrank below its readable minimum: " + icon.Path);
            }
            Require(feed.DrawList.Any("Activity/5/Local"), "local participation accent missing");
            Require(feed.DrawList.Items.Any(i => i.Kind == MagmaWidgetKind.Text && i.NativeFont is not null), "native font missing");
            foreach (MagmaDrawItem item in page.DrawList.Items)
            {
                string prefix = item.Path.Split('/')[0];
                if (prefix is not ("Radar" or "Objectif" or "Chrono" or "ChronoHack" or "ChronoAlarm" or "ChronoObjectif" or "ChronoObjectif2")) continue;
                Require(!feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "feed overlaps " + item.Path + " on " + name);
            }

            // Real diagnostic formatting, including the maximum-detail network panel, exercises its actual bounds.
            (HudFps fps, HudNet net) = Diagnostics(assets, screen);
            feed.Draw(screen, entries, 11f, page, fps.DrawList.Items, net.DrawList.Items);
            Require(feed.VisibleCount >= 1, "network diagnostics hid every activity row: " + name + " " + resolution + "; prompts=" +
                string.Join(";", page.DrawList.Items.Where(i => HudActivityFeedDrawer.IsLocalPrompt(i.Path)).Select(i => i.Path + "=" + HudActivityFeedDrawer.ItemBounds(i))) +
                "; net=" + net.DrawList.Find("Net/Panel")?.Points.LastOrDefault() + "; room=" + feed.AvailableBounds);
            if (name is "spy" or "merc") diagnosticCounts.Add(resolution + " " + name + "=" + feed.VisibleCount);
            CheckBounds(feed, screen);
            foreach (MagmaDrawItem item in net.DrawList.Items.Concat(fps.DrawList.Items))
                Require(feed.VisibleCount == 0 || !feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "feed overlaps diagnostics");
            foreach (MagmaDrawItem item in page.DrawList.Items.Where(i => HudActivityFeedDrawer.IsLocalPrompt(i.Path)))
                Require(!feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "diagnostics-shifted feed overlaps " + item.Path);

            page.Options.StretchHud = true;
            page.Draw(screen, Snapshot(page.Side));
            feed.Draw(screen, entries, 11f, page);
            CheckBounds(feed, screen);
            CheckText(feed);
            feed.Draw(screen, entries, 11f, page, shown: false);
            Require(feed.DrawList.Items.Count == 0 && feed.VisibleCount == 0, "hidden feed left commands behind");
            feed.Draw(screen, entries, 16f, page);
            Require(feed.DrawList.Items.Count == 0, "expired entries were drawn");
        }

        CheckMovedDiagnostics(assets, entries);
        CheckTelemetryGraphs(assets, entries);
        CheckEditedPlacement(assets);
        CheckSingleParticipantAndNotices(assets);
        CheckDetailedCauses(assets);

        var animation = new HudActivityFeedDrawer();
        animation.Configure(assets);
        Vector2 reference = new(1280, 720);
        animation.Draw(reference, entries, 10f, null);
        Require(animation.DrawList.Items.Where(i => i.Kind == MagmaWidgetKind.Text).All(i => i.Color.A == 0f), "entrance did not begin transparent");
        animation.Draw(reference, entries, 10.075f, null);
        Require(animation.DrawList.Items.Where(i => i.Kind == MagmaWidgetKind.Text).All(i => Math.Abs(i.Color.A - .5f) < .002f), "entrance midpoint incorrect");
        animation.Draw(reference, entries, 15.8f, null);
        Require(animation.DrawList.Items.Where(i => i.Kind == MagmaWidgetKind.Text).All(i => Math.Abs(i.Color.A - .5f) < .002f), "fade midpoint incorrect");
        GD.Print("SVM_ACTIVITY_DIAGNOSTIC_ROWS " + string.Join("; ", diagnosticCounts));
    }

    private static void CheckIconResources()
    {
        var participant = new HudActivityParticipant(1, "Ghost", 0);
        foreach (HudActivityKind kind in Enum.GetValues<HudActivityKind>())
        foreach (HudActivityCause cause in Enum.GetValues<HudActivityCause>())
        foreach (CombatDeathTrigger trigger in Enum.GetValues<CombatDeathTrigger>())
        foreach (bool headshot in new[] { false, true })
        {
            var entry = new HudActivityEntry(1, kind, participant, null, cause, "", "", 0f, false, trigger, Headshot: headshot);
            Texture2D? texture = HudActivityIcons.For(entry);
            Require(texture is not null && texture.GetWidth() == 256 && texture.GetHeight() == 256,
                "embedded SVG icon did not load: " + kind + "/" + cause);
            Require(ReferenceEquals(texture, HudActivityIcons.For(entry)), "icon was not cached: " + kind + "/" + cause);
        }
        var distinct = Enum.GetValues<HudActivityCause>().Where(c => c is not (HudActivityCause.None or HudActivityCause.Other))
            .Select(c => HudActivityIcons.NameFor(new(1, HudActivityKind.Kill, participant, null, c, "", "", 0f, false))).ToArray();
        Require(distinct.Distinct().Count() == distinct.Length, "two different kill causes share an icon");
    }

    private static void CheckDetailedCauses(HudAssets assets)
    {
        var actor = new HudActivityParticipant(1, "Sentinel", 1);
        var victim = new HudActivityParticipant(2, "Echo", 0);
        var trigger = new HudActivityParticipant(3, "Raven", 0);
        var feed = new HudActivityFeedDrawer();
        feed.Configure(assets);
        foreach (Vector2I size in Sizes)
        {
            foreach (HudActivityCause cause in Enum.GetValues<HudActivityCause>().Where(c => c is not (HudActivityCause.None or HudActivityCause.Other)))
            foreach (HudActivityKind kind in new[] { HudActivityKind.Kill, HudActivityKind.Suicide })
            {
                var entry = new HudActivityEntry(1, kind, kind == HudActivityKind.Kill ? actor : null, victim, cause, "", "", 10f, true,
                    CombatDeathTrigger.Shot, trigger);
                feed.Draw(size, [entry], 11f, null);
                CheckBounds(feed, size);
                CheckText(feed);
                if (HudActivityPresentation.UsesVictimFirstMessage(entry))
                {
                    Require(feed.DrawList.TextOf("Activity/1/Action") == "died", "contact death must explicitly identify who died");
                    Require(!feed.DrawList.Any("Activity/1/Direction"), "contact death kept the ambiguous killer arrow");
                    Require(feed.DrawList.TextOf("Activity/1/Cause") == HudActivityPresentation.ContactLead(entry) &&
                        feed.DrawList.TextOf("Activity/1/Actor") == HudActivityPresentation.ContactTarget(entry), "contact cause or target was truncated: " + size);
                }
                else
                    Require(feed.DrawList.TextOf("Activity/1/Cause") == HudActivityPresentation.Cause(entry), "kill cause was truncated: " + kind + "/" + cause + " " + size + ": " + feed.DrawList.TextOf("Activity/1/Cause"));
                Require(feed.DrawList.TextOf("Activity/1/Trigger") == HudActivityPresentation.TriggerDetail(entry), "trigger detail was lost: " + cause + " " + size);
            }
            feed.Draw(size, DetailedEntries(), 11f, null);
            CheckBounds(feed, size);
            CheckText(feed);
            Require(feed.VisibleCount >= 4, "detailed kills wasted available space: " + size);
            var panels = feed.DrawList.Items.Where(i => i.Path.EndsWith("/Panel", StringComparison.Ordinal)).Select(HudActivityFeedDrawer.ItemBounds).ToArray();
            for (int i = 1; i < panels.Length; i++)
                Require(!panels[i - 1].Intersects(panels[i]), "variable-height kill rows overlap");
            foreach (HudActivityCause cause in new[] { HudActivityCause.ShockCollision, HudActivityCause.StickyBlast })
            {
                var longName = new HudActivityParticipant(1, "A_very_long_player_name_with_Unicode_e\u0301_猫_and_more_characters", 0);
                var entry = new HudActivityEntry(1, HudActivityKind.Kill, longName, longName with { PlayerId = 2 }, cause, "", "", 10f, false);
                feed.Draw(size, [entry], 11f, null);
                CheckBounds(feed, size); CheckText(feed);
                Require(feed.DrawList.TextOf("Activity/1/Action") == "died", "long names hid the contact death verb");
                Require(feed.DrawList.Any("Activity/1/Victim") && feed.DrawList.Any("Activity/1/Actor"), "long names hid contact participants");
            }
        }
    }

    private static void CheckSingleParticipantAndNotices(HudAssets assets)
    {
        var player = new HudActivityParticipant(1, "Ghost", 0);
        HudActivityEntry[] entries =
        [
            new(1, HudActivityKind.Death, null, player, HudActivityCause.Fall, "", "", 10f, false),
            new(2, HudActivityKind.Suicide, player, player, HudActivityCause.Explosion, "", "", 10f, false),
            new(3, HudActivityKind.Resign, player, null, HudActivityCause.None, "", "", 10f, false),
            new(4, HudActivityKind.Match, null, null, HudActivityCause.None, "", "Round starts soon", 10f, false),
            new(5, HudActivityKind.Leave, player, null, HudActivityCause.None, "", "", 10f, false),
        ];
        var feed = new HudActivityFeedDrawer();
        feed.Configure(assets);
        feed.Draw(new Vector2(1280, 720), entries, 11f, null);
        CheckText(feed);
        Require(feed.VisibleCount == 5, "single participant and notice rows missing");
        foreach (int id in new[] { 1, 2, 3 })
        {
            Require(feed.DrawList.TextOf($"Activity/{id}/Victim") == "Ghost", "single participant missing");
            Require(!feed.DrawList.Any($"Activity/{id}/Actor") && !feed.DrawList.Any($"Activity/{id}/Direction"),
                "single participant row reserved an attacker or arrow");
        }
        Require(feed.DrawList.TextOf("Activity/1/Action") == "Died", "death outcome missing");
        Require(feed.DrawList.TextOf("Activity/2/Action") == "Self-eliminated", "suicide outcome missing");
        Require(feed.DrawList.TextOf("Activity/3/Action") == "Resigned", "resignation outcome missing");
        Require(feed.DrawList.TextOf("Activity/4/Label") == "Match update" && feed.DrawList.TextOf("Activity/4/Notice") == "Round starts soon",
            "match notice lost its message");
        Require(feed.DrawList.TextOf("Activity/5/Label") == "Ghost" && feed.DrawList.TextOf("Activity/5/Notice") == "Left the match",
            "notice relied on an empty legacy label");
    }

    private static void CheckMovedDiagnostics(HudAssets assets, HudActivityEntry[] entries)
    {
        foreach (Vector2I resolution in Sizes)
        {
            Vector2 screen = resolution;
            var feed = new HudActivityFeedDrawer();
            feed.Configure(assets);
            feed.Draw(screen, entries, 11f, null);
            int unobstructedRows = feed.VisibleCount;
            (HudFps fps, HudNet net) = Diagnostics(assets, screen);
            foreach (float positionY in new[] { 100f, 50f })
            {
                fps.Settings.PositionY = net.Settings.PositionY = positionY;
                fps.Draw(screen, true);
                net.FpsBounds = fps.Bounds;
                net.Draw(screen, 0f, true, true);
                feed.Draw(screen, entries, 11f, null, fps.DrawList.Items, net.DrawList.Items);
                Require(feed.VisibleCount >= 1, "Moved telemetry hid activity despite space above: " + resolution + " y=" + positionY);
                if (positionY == 100f)
                    Require(feed.VisibleCount == unobstructedRows, "Bottom telemetry reduced activity rows: " + resolution);
                CheckBounds(feed, screen);
                foreach (MagmaDrawItem item in net.DrawList.Items.Concat(fps.DrawList.Items))
                    Require(!feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "Activity overlaps moved telemetry: " + resolution);
            }
        }

        // A panel inside the lane leaves too little space above it, but enough below for all five rows.
        Vector2 reference = new(1280, 720);
        var below = new HudActivityFeedDrawer();
        below.Configure(assets);
        MagmaDrawItem obstacle = HudText.Item("Fps", "Performance", new Rect2(950f, 40f, 320f, 30f), 12f, MagmaAlign.Left, Colors.White);
        below.Draw(reference, entries, 11f, null, [obstacle]);
        Require(below.VisibleCount == 5 && below.Bounds.Position.Y > obstacle.Rect.End.Y && !below.Bounds.Intersects(obstacle.Rect),
            "Activity did not use available space below a moved telemetry panel");
        CheckBounds(below, reference);
    }

    private static void CheckTelemetryGraphs(HudAssets assets, HudActivityEntry[] entries)
    {
        foreach (Vector2I resolution in Sizes)
        foreach (string mode in new[] { "Always", "Issues" })
        foreach (float scale in resolution.X >= 1280 ? new[] { 100f, 200f } : new[] { 100f })
        foreach (Vector2 position in new[] { new Vector2(100f, 0f), new Vector2(70f, 0f), new Vector2(100f, 50f), new Vector2(100f, 100f) })
        {
            Vector2 screen = resolution;
            HudPage page = Page(assets, "spy", screen);
            (HudFps fps, HudNet net) = Diagnostics(assets, screen);
            // The reported layout: an FPS history and all three network graphs, with compact network text.
            fps.Settings.Graph = net.Settings.Graph = "Line";
            fps.Settings.DisplayMode = net.Settings.DisplayMode = mode;
            fps.Settings.Scale = net.Settings.Scale = scale;
            fps.Settings.PositionX = net.Settings.PositionX = position.X;
            fps.Settings.PositionY = net.Settings.PositionY = position.Y;
            fps.Settings.Opacity = net.Settings.Opacity = 50f;
            fps.Settings.ShowFrameTime = fps.Settings.ShowPercentiles = false;
            fps.Settings.ShowAverage = true;
            net.Settings.Detailed = net.Settings.ShowTraffic = net.ShowLoss = false;
            fps.Settings.FrameTimeThresholdMs = net.Settings.PingThresholdMs = 1f;
            fps.Add(1f / 60f);
            fps.Draw(screen, true);
            net.FpsBounds = fps.Bounds;
            net.Draw(screen, 1f, true, true);
            Require(fps.DrawList.Items.Count > 0 && net.DrawList.Items.Count > 0, "Graph telemetry was not visible.");
            var feed = new HudActivityFeedDrawer();
            feed.Configure(assets);
            feed.Draw(screen, entries, 11f, page, fps.DrawList.Items, net.DrawList.Items);
            string layout = resolution + " " + mode + " scale=" + scale + " position=" + position;
            Require(feed.VisibleCount > 0 && feed.DrawList.Any("Activity/5/"), "Telemetry graphs hid the latest kill: " + layout);
            CheckBounds(feed, screen);
            CheckText(feed);
            foreach (MagmaDrawItem item in fps.DrawList.Items.Concat(net.DrawList.Items))
                Require(!feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "Kill feed overlaps telemetry graph: " + layout);
            foreach (MagmaDrawItem item in page.DrawList.Items)
            {
                string prefix = item.Path.Split('/')[0];
                if (!HudActivityFeedDrawer.IsLocalPrompt(item.Path) && prefix is not ("Radar" or "Objectif" or "Chrono" or "ChronoHack" or "ChronoAlarm" or "ChronoObjectif" or "ChronoObjectif2")) continue;
                Require(!feed.Bounds.Intersects(HudActivityFeedDrawer.ItemBounds(item)), "Graph-shifted feed overlaps " + item.Path + ": " + layout);
            }
        }
    }

    private static void CheckEditedPlacement(HudAssets assets)
    {
        foreach (Vector2I resolution in Sizes)
        foreach (string role in new[] { "spy", "merc" })
        {
            Vector2 screen = resolution;
            HudPage page = Page(assets, role, screen);
            HudPage scope = Page(assets, role + "-scope", screen);
            HudActivityEntry[] shortEntries = Entries(false), detailedEntries = DetailedEntries();
            var preview = new HudActivityFeedDrawer(); preview.Configure(assets);
            var live = new HudActivityFeedDrawer(); live.Configure(assets);
            // Cover the entire display: user positioning takes precedence over automatic avoidance.
            MagmaDrawItem obstruction = HudText.Item("Fps", "Diagnostics", new Rect2(Vector2.Zero, screen), 12f, MagmaAlign.Left, Colors.White);
            foreach (float scale in new[] { .5f, 1f, 1.5f, 3f })
            foreach (float y in new[] { -.1f, .12f, .9f })
            {
                var layout = new HudElementSettings { X = -.2f, Y = y, Scale = scale, Opacity = .7f };
                preview.Draw(screen, shortEntries[..2], 11f, page, layout: layout, editorPreview: true);
                Vector2 anchor = preview.Bounds.Position;
                foreach (HudActivityEntry[] entries in new[] { shortEntries[..1], shortEntries, detailedEntries })
                foreach (HudPage activePage in new[] { page, scope })
                {
                    live.Draw(screen, entries, 11f, activePage, [obstruction], [obstruction], layout: layout, layoutPage: page);
                    Require(live.VisibleCount > 0, "edited feed lost its newest event: " + resolution + " " + role + " " + scale);
                    Require(live.Bounds.Position.IsEqualApprox(anchor), "edited feed moved away from editor anchor after content/telemetry/page changed");
                    Require(live.Bounds.Position.X >= -.01f && live.Bounds.Position.Y >= -.01f &&
                        live.Bounds.End.X <= screen.X + .01f && live.Bounds.End.Y <= screen.Y + .01f, "edited feed escaped the display");
                    foreach (MagmaDrawItem item in live.DrawList.Items)
                        Require(live.Bounds.Grow(scale + 1f).Encloses(HudActivityFeedDrawer.ItemBounds(item)), "edited feed item escaped its selection outline");
                }
                live.Draw(screen, shortEntries, 10.075f, scope, layout: layout, layoutPage: page);
                Require(live.Bounds.Position.IsEqualApprox(anchor), "event entrance animation moved the edited feed origin");
                Require(layout.X == -.2f && layout.Y == y && layout.Scale == scale, "drawing rewrote saved relative offsets");
                layout.Visible = false;
                preview.Draw(screen, shortEntries, 11f, page, layout: layout, editorPreview: true);
                Require(preview.DrawList.Items.Count == 0 && preview.Bounds.Position.IsEqualApprox(anchor) && preview.Bounds.HasArea(),
                    "hidden edited feed lost its selectable outline");
            }
            // A saved layout is an explicit placement, even when grid snapping returns its offsets to zero.
            // Opacity/visibility-only edits must also match the editor's stable sample origin.
            (HudFps fps, HudNet net) = Diagnostics(assets, screen);
            foreach (float opacity in new[] { 1f, .5f })
            {
                var layout = new HudElementSettings { Opacity = opacity };
                preview.Draw(screen, shortEntries, 11f, page, layout: layout, editorPreview: true);
                live.Draw(screen, shortEntries, 11f, scope, fps.DrawList.Items, net.DrawList.Items, layout: layout, layoutPage: page);
                Require(live.Bounds.IsEqualApprox(preview.Bounds) && live.VisibleCount == preview.VisibleCount,
                    "zero-offset saved layout did not match the editor's pinned event feed");
            }
        }
    }

    private static void CheckBounds(HudActivityFeedDrawer feed, Vector2 screen)
    {
        if (feed.VisibleCount == 0) return;
        Require(feed.Bounds.Position.X >= 0f && feed.Bounds.Position.Y >= 0f && feed.Bounds.End.X <= screen.X && feed.Bounds.End.Y <= screen.Y * .45f + 1f,
            "feed escaped its upper-right safe region");
        foreach (MagmaDrawItem item in feed.DrawList.Items)
        {
            Rect2 bounds = HudActivityFeedDrawer.ItemBounds(item);
            Require(feed.Bounds.Grow(1f).Encloses(bounds), "draw item escaped feed bounds: " + item.Path);
        }
    }

    private static void CheckText(HudActivityFeedDrawer feed)
    {
        foreach (MagmaDrawItem item in feed.DrawList.Items)
        {
            if (item.Kind != MagmaWidgetKind.Text || item.NativeFont is null) continue;
            float width = item.NativeFont.GetStringSize(item.Text, HorizontalAlignment.Left, -1f, Mathf.RoundToInt(item.HeightPx)).X;
            Require(width <= item.Rect.Size.X + .1f, "text escaped its measured column: " + item.Path);
            Require(item.HeightPx >= 10f && item.XScale == 1f, "text was reduced or stretched to fit");
        }
    }

    private void CreateCapture(SmokeContext context)
    {
        if (_case >= PreviewCaptureCount) { CreateGallery(context, _case - PreviewCaptureCount); return; }
        (Vector2I size, string name, bool diagnostics) = Case(_case);
        HudPage page = Page(_assets!, name, size);
        var feed = new HudActivityFeedDrawer();
        feed.Configure(_assets!);
        (HudFps fps, HudNet net) = Diagnostics(_assets!, size);
        bool detailed = _case >= StandardCaptureCount;
        feed.Draw(size, detailed ? DetailedEntries() : Entries(longNames: diagnostics), 11f, detailed ? null : page,
            diagnostics ? fps.DrawList.Items : null, diagnostics ? net.DrawList.Items : null);
        _feedProbe = feed.Bounds;
        _textProbe = feed.DrawList.Find("Activity/5/Actor")?.Rect ?? throw new InvalidOperationException("actor text missing");
        _viewport = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        bool bright = diagnostics && size.X == 1920;
        _viewport.AddChild(new ColorRect { Size = size, Color = bright ? new Color(.55f, .65f, .7f) : new Color(.045f, .065f, .075f) });
        // A quiet, varied backing makes translucent rows and small symbols inspectable without shipping new art.
        for (int i = 0; i < 9; i++)
            _viewport.AddChild(new ColorRect { Position = new Vector2(i * size.X / 9f, 0), Size = new Vector2(size.X / 18f, size.Y),
                Color = bright ? new Color(.64f + i * .002f, .71f + i * .003f, .74f + i * .003f) : new Color(.06f + i * .002f, .08f + i * .003f, .09f + i * .003f) });
        var canvas = new MagmaCanvas { Assets = _assets };
        _viewport.AddChild(canvas);
        var items = detailed ? new List<MagmaDrawItem>() : new List<MagmaDrawItem>(page.DrawList.Items);
        items.AddRange(feed.DrawList.Items);
        if (diagnostics) { items.AddRange(fps.DrawList.Items); items.AddRange(net.DrawList.Items); }
        canvas.Submit(items);
        _settle = 3;
    }

    private static string CaseName(int index)
    {
        if (index >= PreviewCaptureCount) return "gallery-" + Galleries[index - PreviewCaptureCount];
        (Vector2I size, string name, bool diagnostics) = Case(index);
        if (index >= StandardCaptureCount) return $"kill-types-{size.X}x{size.Y}";
        return string.Create(CultureInfo.InvariantCulture, $"{name}-{size.X}x{size.Y}{(diagnostics ? "-diagnostics-long-names" : "")}");
    }

    private static (Vector2I Size, string Page, bool Diagnostics) Case(int index) => index >= StandardCaptureCount
        ? (Sizes[index - StandardCaptureCount], "spy", false)
        : index < Sizes.Length * Pages.Length
        ? (Sizes[index / Pages.Length], Pages[index % Pages.Length], false)
        : (Sizes[index - Sizes.Length * Pages.Length], index - Sizes.Length * Pages.Length == 2 ? "spy" : "merc", true);

    private static HudActivityEntry[] DetailedEntries()
    {
        var merc = new HudActivityParticipant(1, "Sentinel", 1);
        var spy = new HudActivityParticipant(2, "Echo", 0);
        var trigger = new HudActivityParticipant(3, "Raven", 0);
        return
        [
            new(5, HudActivityKind.Kill, merc, spy, HudActivityCause.LaserMine, "", "", 10f, false, CombatDeathTrigger.Shot, trigger),
            new(4, HudActivityKind.Kill, merc, spy, HudActivityCause.ProximityMine, "", "", 10f, false),
            new(3, HudActivityKind.Kill, merc, spy, HudActivityCause.PoisonMine, "", "", 10f, false),
            new(2, HudActivityKind.Kill, merc, spy, HudActivityCause.FragGrenade, "", "", 10f, false),
            new(1, HudActivityKind.Kill, trigger, merc, HudActivityCause.MolotovFire, "", "", 10f, true, CombatDeathTrigger.Shot, trigger),
        ];
    }

    private void CreateGallery(SmokeContext context, int group)
    {
        if (group == 4) { CreateClarifiedCausePreview(context); return; }
        var size = new Vector2I(920, 420);
        _viewport = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _viewport.AddChild(new ColorRect { Size = size, Color = new Color(.045f, .065f, .075f) });
        string[][] titles =
        [
            ["Mines and grenades", "Shot and chain detonations"],
            ["Fire and environmental damage", "Destructible props"],
            ["Melee", "Grabs and movement"],
            ["Primary weapons", "Sniper and sidearms"],
        ];
        for (int column = 0; column < 2; column++)
        {
            var title = new Label { Text = titles[group][column], Position = new Vector2(12 + 460 * column, 10) };
            title.AddThemeFontOverride("font", OpenScctGame.UI.UiArt.BodyFont);
            title.AddThemeFontSizeOverride("font_size", 18);
            _viewport.AddChild(title);
            var feed = new HudActivityFeedDrawer(); feed.Configure(_assets!);
            feed.Draw(new Vector2(1920, 1080), GalleryEntries(group, column), 11f, null);
            Require(feed.VisibleCount == 5, "gallery should display all five examples");
            Vector2 offset = new Vector2(12 + 460 * column, 44) - feed.Bounds.Position;
            var canvas = new MagmaCanvas { Assets = _assets, Position = offset };
            _viewport.AddChild(canvas);
            canvas.Submit(feed.DrawList.Items);
            if (column == 0)
            {
                Rect2 first = feed.DrawList.Items.First(i => i.Kind == MagmaWidgetKind.Text).Rect;
                _textProbe = new Rect2(first.Position + offset, first.Size);
            }
        }
        _settle = 3;
    }

    private static HudActivityEntry[] GalleryEntries(int group, int column)
    {
        HudActivityCause[][] sets =
        [
            [HudActivityCause.ProximityMine, HudActivityCause.LaserMine, HudActivityCause.PoisonMine, HudActivityCause.FragGrenade, HudActivityCause.Smoke],
            [HudActivityCause.ProximityMine, HudActivityCause.LaserMine, HudActivityCause.LaserMine, HudActivityCause.BarrelExplosion, HudActivityCause.MolotovExplosion],
            [HudActivityCause.MolotovFire, HudActivityCause.Fire, HudActivityCause.Crush, HudActivityCause.Hazard, HudActivityCause.Poison],
            [HudActivityCause.BarrelExplosion, HudActivityCause.GasExplosion, HudActivityCause.ExplosiveExtinguisher, HudActivityCause.SodaExplosion, HudActivityCause.ExplosiveProp],
            [HudActivityCause.Charge, HudActivityCause.Elbow, HudActivityCause.DropAttack, HudActivityCause.NeckBreak, HudActivityCause.ShockCollision],
            [HudActivityCause.Choke, HudActivityCause.LedgePull, HudActivityCause.Grab, HudActivityCause.Fall, HudActivityCause.StickyCamera],
            [HudActivityCause.Rifle, HudActivityCause.Rifle, HudActivityCause.Shotgun, HudActivityCause.Shotgun, HudActivityCause.Smg],
            [HudActivityCause.Sniper, HudActivityCause.Sniper, HudActivityCause.Pistol, HudActivityCause.Pistol, HudActivityCause.Revolver],
        ];
        var merc = new HudActivityParticipant(1, "Sentinel", 1);
        var spy = new HudActivityParticipant(2, "Echo", 0);
        var raven = new HudActivityParticipant(3, "Raven", 0);
        var entries = new HudActivityEntry[5];
        for (int i = 0; i < 5; i++)
        {
            HudActivityCause cause = sets[group * 2 + column][i];
            bool environmental = group == 1 || group == 0 && column == 1 && i >= 3;
            bool spyAttack = environmental || cause is HudActivityCause.DropAttack or HudActivityCause.NeckBreak or HudActivityCause.Choke or
                HudActivityCause.LedgePull or HudActivityCause.Grab or HudActivityCause.ShockCollision or HudActivityCause.Smoke or HudActivityCause.StickyCamera;
            bool anonymous = cause is HudActivityCause.Fire or HudActivityCause.Hazard or HudActivityCause.Fall or HudActivityCause.Crush or HudActivityCause.Poison;
            CombatDeathTrigger trigger = group == 0 && column == 1 || group == 1 && (column == 1 || i == 0)
                ? group == 0 && i == 2 ? CombatDeathTrigger.ChainReaction : CombatDeathTrigger.Shot : CombatDeathTrigger.None;
            HudActivityKind kind = anonymous ? HudActivityKind.Death : cause == HudActivityCause.Grab ? HudActivityKind.Knockout :
                cause is HudActivityCause.StickyCamera or HudActivityCause.Smoke ? HudActivityKind.Sleep : HudActivityKind.Kill;
            entries[i] = new(5 - i, kind, anonymous ? null : spyAttack ? raven : merc, spyAttack ? merc : spy,
                cause, "", "", 10f, false, trigger, trigger != CombatDeathTrigger.None ? raven : null,
                Headshot: group == 3 && (i is 1 or 3 || column == 1 && i == 4));
        }
        return entries;
    }

    private void CreateClarifiedCausePreview(SmokeContext context)
    {
        var merc = new HudActivityParticipant(1, "Sentinel", 1);
        var spy = new HudActivityParticipant(2, "Raven", 0);
        var echo = new HudActivityParticipant(3, "Echo", 0);
        HudActivityEntry[] entries =
        [
            new(4, HudActivityKind.Kill, merc, echo, HudActivityCause.Taser, "", "", 10f, false),
            new(3, HudActivityKind.Kill, spy, merc, HudActivityCause.ShockCollision, "", "", 10f, false),
            new(2, HudActivityKind.Kill, merc, spy, HudActivityCause.StickyBlast, "", "", 10f, false),
            new(1, HudActivityKind.Kill, spy, merc, HudActivityCause.ExplosiveExtinguisher, "", "", 10f, false,
                CombatDeathTrigger.Shot, spy),
        ];
        var feed = new HudActivityFeedDrawer(); feed.Configure(_assets!);
        feed.Draw(new Vector2(1920, 1080), entries, 11f, null);
        Require(feed.VisibleCount == entries.Length, "clarified cause examples missing");
        var size = new Vector2I(464, 300);
        _viewport = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _viewport.AddChild(new ColorRect { Size = size, Color = new Color(.045f, .065f, .075f) });
        var title = new Label { Text = "Clarified event labels", Position = new Vector2(12, 10) };
        title.AddThemeFontOverride("font", OpenScctGame.UI.UiArt.BodyFont);
        title.AddThemeFontSizeOverride("font_size", 18);
        _viewport.AddChild(title);
        Vector2 offset = new Vector2(12, 44) - feed.Bounds.Position;
        var canvas = new MagmaCanvas { Assets = _assets, Position = offset };
        _viewport.AddChild(canvas); canvas.Submit(feed.DrawList.Items);
        Rect2 first = feed.DrawList.Items.First(i => i.Kind == MagmaWidgetKind.Text).Rect;
        _textProbe = new Rect2(first.Position + offset, first.Size);
        _settle = 3;
    }

    private static (HudFps Fps, HudNet Net) Diagnostics(HudAssets assets, Vector2 screen)
    {
        var fps = new HudFps();
        fps.Configure(assets);
        fps.Add(1f / 60f);
        fps.Draw(screen, true);
        long packets = 10;
        var net = new HudNet { ShowLoss = true, Source = () => new NetReadout(true, 42000, 5000, packets * 1024, packets * 512,
            packets, 0, packets, 0, 0, 60, BufferMs: 55, SnapshotAgeMs: 25) };
        net.Configure(assets);
        net.FpsBounds = fps.Bounds;
        net.Draw(screen, 1f, true, true);
        packets = 80;
        net.Draw(screen, 1f, true, true);
        return (fps, net);
    }

    private static HudSnapshot Snapshot(HudSide side)
    {
        var snapshot = new HudSnapshot
        {
            Valid = true, Side = side, HasPawn = true, Health = 85, HasWeapon = true, ClipAmmo = 23, Ammo = 90,
            EnergyLevel = 70, EnergyLevelMax = 100, ShowReticle = true, TimeLimit = 10, ElapsedTime = 75,
            ObjectivesLeft = 3, NumObjectives = 4, CounterMode = HudCounterMode.Story,
            HackCountdown = new HudCountdown(24), AlarmSeconds = 16,
            Gadget = new HudGadget("Sticky Camera", 3, "StickyCam", 0, 0, true),
        };
        snapshot.ObjectiveCountdowns.Add(new HudCountdown(45));
        snapshot.ObjectiveCountdowns.Add(new HudCountdown(90));
        snapshot.Spectator.RemainingTime = "8:45";
        snapshot.Spectator.Objectives = "3 objectives remaining";
        snapshot.Spectator.Watched = "Nightshade";
        snapshot.Spectator.Lives = "3";
        snapshot.Spectator.ShowWatching = true;
        return snapshot;
    }

    private static HudPage Page(HudAssets assets, string name, Vector2 screen)
    {
        HudPage page = name switch
        {
            "spy" => new SpyHudPage(assets),
            "merc" => new MercHudPage(assets),
            "spectator" => new SpectatorHudPage(assets),
            "spy-scope" => new SpyBinocularsPage(assets),
            "merc-scope" => new MercBinocularsPage(assets),
            "sticky-camera" => new StickyCamPage(assets),
            _ => new CamNetworkPage(assets),
        };
        HudSnapshot snapshot = Snapshot(page.Side);
        if (page is SpectatorHudPage) snapshot.Spectator.Active = true;
        if (page is BinocularsPage binoculars)
        {
            snapshot.Binoculars.Mode = page.Side == HudSide.Spy ? HudBinocularMode.Spy : HudBinocularMode.Merc;
            binoculars.Enter();
        }
        if (page is GadgetViewPage gadget)
        {
            var hud = new SHud();
            hud.GotoState(page is StickyCamPage ? GadgetStates.s_Hud_UseObject_StickyCam : GadgetStates.s_Hud_UseObject_CamNetwork);
            var pc = new SPlayerController { myHUD = hud, PlayerReplicationInfo = new PlayerReplicationInfo { Team = new TeamInfo { TeamIndex = page.Side == HudSide.Merc ? 1 : 0 } } };
            Require(ReferenceEquals(gadget.ActivePage(pc, snapshot), page), "camera page did not activate");
        }
        for (int frame = 0; frame < 30; frame++) page.Update(snapshot, 1f / 60f);
        page.Draw(screen, snapshot);
        return page;
    }

    private static HudActivityEntry[] Entries(bool longNames)
    {
        var spy = new HudActivityParticipant(1, longNames ? "Nightshade_With_A_Very_Long_Combining_Name_e\u0301_🕵" : "Nightshade", 0);
        var merc = new HudActivityParticipant(2, longNames ? "Bulwark_Another_Unusually_Long_Player_Name" : "Bulwark", 1);
        return
        [
            new(5, HudActivityKind.Kill, spy, merc, HudActivityCause.Headshot, "KILL", "", 10f, true),
            new(4, HudActivityKind.Sleep, spy, merc, HudActivityCause.StickyCamera, "SLEEP", "", 10f, false),
            new(3, HudActivityKind.Knockout, merc, spy, HudActivityCause.Elbow, "KO", "", 10f, false),
            new(2, HudActivityKind.LastLife, spy, null, HudActivityCause.None, "LAST LIFE", spy.Name + " used their last life", 10f, false),
            new(1, HudActivityKind.Join, spy, null, HudActivityCause.None, "JOINED", "Ghost joined the match", 10f, false),
        ];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
