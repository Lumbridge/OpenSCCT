using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Smoke;

/// <summary>Checks the authored poison animation, screen overlay, health anchor and cure lifecycle.</summary>
public sealed class PoisonHudSmoke : SmokeTest
{
    public const string TestName = "poison-hud";
    public override string Name => TestName;
    private static readonly Vector2 Screen = new(1280, 720);
    private HudAssets? _assets;
    private SubViewport? _viewport;
    private MagmaCanvas? _canvas;
    private SpyHudPage? _page;
    private readonly HudSnapshot _snapshot = Snapshot();
    private int _stage;
    private int _wait;
    private byte[]? _cleanPixels;
    private byte[]? _poisonPixels;
    private static readonly string[] Shots = ["healthy", "poison-start", "poison-pulse", "cured", "poison-hidden-gauge"];

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_assets is null)
            {
                _assets = HudAssets.TryLoad(null, out string? error) ?? throw new InvalidOperationException(error);
                CheckDraws(_assets);
                if (DisplayServer.GetName() == "headless") return SmokeResult.Pass("poison animation, overlay, health layout and cure at 30/60/144 fps");
                _viewport = new SubViewport { Size = (Vector2I)Screen, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                context.Tree.Root.AddChild(_viewport);
                _viewport.AddChild(new ColorRect { Size = Screen, Color = new Color(.12f, .18f, .24f) });
                _canvas = new MagmaCanvas { Assets = _assets };
                _viewport.AddChild(_canvas);
                _page = new SpyHudPage(_assets);
                Configure();
                return SmokeResult.Running;
            }
            if (_wait-- > 0) return SmokeResult.Running;
            using Image image = _viewport!.GetTexture().GetImage();
            Require(!image.IsEmpty(), "empty rendered poison capture");
            byte[] pixels = image.GetData();
            if (_stage == 0) _cleanPixels = pixels;
            if (_stage == 1)
            {
                Require(!pixels.AsSpan().SequenceEqual(_cleanPixels), "poison overlay changed no pixels");
                _poisonPixels = pixels;
            }
            if (_stage == 2) Require(!pixels.AsSpan().SequenceEqual(_poisonPixels), "poison pulse did not animate pixels");
            if (_stage == 3) Require(pixels.AsSpan().SequenceEqual(_cleanPixels), "cure left poison pixels behind");
            string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "local", "poison-hud"));
            Directory.CreateDirectory(folder);
            image.SavePng(Path.Combine(folder, Shots[_stage] + ".png"));
            if (++_stage < Shots.Length)
            {
                Configure();
                return SmokeResult.Running;
            }
            _viewport.QueueFree();
            return SmokeResult.Pass("poison draw checks and rendered animation/cure captures passed");
        }
        catch (Exception e)
        {
            _viewport?.QueueFree();
            return SmokeResult.Fail(e.Message);
        }
    }

    private void Configure()
    {
        _snapshot.Poisoned = _stage is 1 or 2 or 4;
        if (_stage == 4) _page!.Options.Layout.Element(false, "Vie").Visible = false;
        _page!.Update(_snapshot, _stage == 2 ? .45f : 0f);
        _page.Draw(Screen, _snapshot);
        // Keep the capture focused on health and poison; other page animations have independent clocks.
        var list = new MagmaDrawList();
        list.Items.AddRange(_page.DrawList.Items.Where(i => i.Path.StartsWith("Vie/", StringComparison.Ordinal)));
        _canvas!.Submit(list.Items);
        _wait = 4;
    }

    private static HudSnapshot Snapshot() => new() { Valid = true, HasPawn = true, Health = 73, EnergyLevel = 100, EnergyLevelMax = 100 };

    private static void CheckDraws(HudAssets assets)
    {
        foreach (bool merc in new[] { false, true })
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (Vector2 screen in new[] { new Vector2(640, 480), Screen, new Vector2(3440, 1440) })
        {
            HudPage page = merc ? new MercHudPage(assets) : new SpyHudPage(assets);
            HudSnapshot s = Snapshot();
            page.Update(s, 0);
            page.Draw(screen, s);
            Vector2 gauge = page.DrawList.Find("Vie/jaugeinterne")!.Points[0];
            Require(!page.DrawList.Any("Vie/poison/"), "healthy pawn drew poison");
            s.Poisoned = true;
            page.Update(s, 0);
            var poison = page.Area("Vie/poison")!;
            Require(poison.Visible && poison.Playing && poison.Frame == 1, "infection did not start poison at frame 1");
            page.Draw(screen, s);
            var first = page.DrawList.Find("Vie/poison/effet01")!;
            Vector2 start = first.Points[0];
            Require(first.Anchor == MagmaAnchor.Stretch && first.Blend == MagmaBlend.Normal, "normal overlay mapping/blend changed");
            Require(page.DrawList.Find("Vie/poison/effet02_dodge") is { Anchor: MagmaAnchor.Stretch, Blend: MagmaBlend.Dodge }, "missing dodge overlay");
            Require(page.DrawList.Find("Vie/jaugeinterne")!.Points[0].IsEqualApprox(gauge), "poison moved the health gauge");
            Require(page.LayoutBounds(screen)["Vie"].Size.X < screen.X * .2f, "screen overlay entered health editor bounds");
            for (int frame = 0; frame < fps / 3; frame++) page.Update(s, 1f / fps);
            page.Draw(screen, s);
            Require(!page.DrawList.Find("Vie/poison/effet01")!.Points[0].IsEqualApprox(start), "overlay geometry did not pulse");
            for (int frame = 0; frame < 3 * fps; frame++) page.Update(s, 1f / fps);
            Require(poison.Playing && poison.Frame is >= 1 and < 10, "poison loop stopped");
            page.Draw(screen, s);
            Vector2[] overlay = (Vector2[])page.DrawList.Find("Vie/poison/effet01")!.Points.Clone();
            var edit = page.Options.Layout.Element(merc, "Vie");
            edit.X = -.4f; edit.Y = -.2f; edit.Scale = .5f; edit.Opacity = .25f;
            page.Draw(screen, s);
            Require(page.DrawList.Find("Vie/poison/effet01")!.Points.AsSpan().SequenceEqual(overlay), "editing health moved/scaled poison overlay");
            edit.Visible = false;
            page.Draw(screen, s);
            Require(page.DrawList.Find("Vie/jaugeinterne") is null && page.DrawList.Find("Vie/poison/effet01") is not null, "hidden health removed poison overlay");
            s.Poisoned = false;
            page.Update(s, 0);
            page.Draw(screen, s);
            Require(!page.DrawList.Any("Vie/poison/"), "cure retained poison overlay");
            s.Poisoned = true;
            page.Update(s, 0);
            Require(poison.Frame == 1 && poison.Playing, "reinfection did not restart poison");
            page.Update(s, .35f);
            s.HasPawn = false;
            page.Update(s, 0);
            page.Draw(screen, s);
            Require(!page.DrawList.Any("Vie/poison/"), "missing pawn retained poison overlay");
            s.HasPawn = true;
            page.Update(s, 0);
            Require(poison.Frame == 1 && poison.Playing, "new poisoned pawn resumed the previous pawn's pulse");
            HudPage fresh = merc ? new MercHudPage(assets) : new SpyHudPage(assets);
            fresh.Update(s, 0);
            Require(fresh.Area("Vie/poison") is { Frame: 1, Playing: true }, "first snapshot already poisoned froze at frame 0");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
