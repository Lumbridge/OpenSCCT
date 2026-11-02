using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Gadgets;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Smoke;

/// <summary>Feed layout across every HUD page, plus optional actual-renderer captures and native-font checks.</summary>
public sealed class HudActivityFeedSmoke : SmokeTest
{
    public const string TestName = "hud-activity-feed";
    public override string Name => TestName;
    private static readonly Vector2I[] Sizes = [new(640, 480), new(1280, 720), new(1920, 1080), new(3440, 1440)];
    private static readonly string[] Pages = ["spy", "merc", "spectator", "spy-scope", "merc-scope", "sticky-camera", "camera-network"];
    private static int CaptureCount => Sizes.Length * Pages.Length + Sizes.Length;
    private HudAssets? _assets;
    private SubViewport? _viewport;
    private Rect2 _textProbe;
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
            _viewport.Free();
            _viewport = null;
            if (++_case < CaptureCount) return SmokeResult.Running;
            return SmokeResult.Pass("32 real-renderer HUD captures with visible Inter text, including diagnostics/long names/bright scenery; layout, animation and visibility checks passed");
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
            Require(feed.DrawList.TextOf("Activity/5/Action") == "KILL", "kill action missing: " + name + " " + resolution + "; text=" + feed.DrawList.TextOf("Activity/5/Action"));
            Require(feed.DrawList.TextOf("Activity/5/Cause") == "HEADSHOT", "cause missing: " + name + " " + resolution + "; text=" + feed.DrawList.TextOf("Activity/5/Cause"));
            Require(feed.DrawList.Any("Activity/4/Icon/") && feed.DrawList.Any("Activity/3/Icon/"), "sleep/KO icons missing");
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
        (Vector2I size, string name, bool diagnostics) = Case(_case);
        HudPage page = Page(_assets!, name, size);
        var feed = new HudActivityFeedDrawer();
        feed.Configure(_assets!);
        (HudFps fps, HudNet net) = Diagnostics(_assets!, size);
        feed.Draw(size, Entries(longNames: diagnostics), 11f, page, diagnostics ? fps.DrawList.Items : null, diagnostics ? net.DrawList.Items : null);
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
        var items = new List<MagmaDrawItem>(page.DrawList.Items);
        items.AddRange(feed.DrawList.Items);
        if (diagnostics) { items.AddRange(fps.DrawList.Items); items.AddRange(net.DrawList.Items); }
        canvas.Submit(items);
        _settle = 3;
    }

    private static string CaseName(int index)
    {
        (Vector2I size, string name, bool diagnostics) = Case(index);
        return string.Create(CultureInfo.InvariantCulture, $"{name}-{size.X}x{size.Y}{(diagnostics ? "-diagnostics-long-names" : "")}");
    }

    private static (Vector2I Size, string Page, bool Diagnostics) Case(int index) => index < Sizes.Length * Pages.Length
        ? (Sizes[index / Pages.Length], Pages[index % Pages.Length], false)
        : (Sizes[index - Sizes.Length * Pages.Length], index - Sizes.Length * Pages.Length == 2 ? "spy" : "merc", true);

    private static (HudFps Fps, HudNet Net) Diagnostics(HudAssets assets, Vector2 screen)
    {
        var fps = new HudFps();
        fps.Add(1f / 60f);
        fps.Draw(screen, true);
        long packets = 10;
        var net = new HudNet { ShowLoss = true, Source = () => new NetReadout(true, 42000, 5000, packets * 1024, packets * 512,
            packets, 0, packets, 0, 0, 60, BufferMs: 55, SnapshotAgeMs: 25) };
        net.Configure(assets);
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
