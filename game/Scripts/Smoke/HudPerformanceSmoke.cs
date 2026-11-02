using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using Environment = System.Environment;

namespace OpenScctGame.Smoke;

#if !HUD_BASELINE
/// <summary>Ownership, invalidation and bounded-copy checks for the retained HUD command path.</summary>
public sealed class HudPerformanceSmoke : SmokeTest
{
    public const string TestName = "hud-performance";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        var canvas = new MagmaCanvas();
        var batch = new MagmaBatch();
        context.Tree.Root.AddChild(canvas);
        try
        {
            var list = new MagmaDrawList();
            MagmaDrawItem first = Image(list);
            Vector2[] points = first.Points;
            Require(batch.SetItems(list.Items, 0, 1), "first batch was not submitted");
            Require(!ReferenceEquals(batch.Items[0], first) && !ReferenceEquals(batch.Items[0].Points, points), "canvas aliases producer storage");
            Vector2 original = batch.Items[0].Points[0];
            first.Points[0] += Vector2.One;
            Require(batch.Items[0].Points[0] == original, "producer mutated a deferred command");
            Require(batch.SetItems(list.Items, 0, 1), "geometry change was missed");
            Require(!batch.SetItems(list.Items, 0, 1), "identical geometry submitted again");
            first.Colors[0] = Colors.Red;
            Require(batch.SetItems(list.Items, 0, 1), "vertex colour change was missed");
            first.Uvs[0] = new Vector2(.25f, .5f);
            Require(batch.SetItems(list.Items, 0, 1), "texture coordinate change was missed");
            first.Clamp = !first.Clamp;
            Require(batch.SetItems(list.Items, 0, 1), "texture addressing change was missed");

            list.Clear();
            MagmaDrawItem reused = Image(list);
            Require(ReferenceEquals(first, reused) && ReferenceEquals(points, reused.Points), "quad storage was not retained");
            for (int i = 0; i < 64; i++) { list.Clear(); Image(list); }
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) { list.Clear(); Image(list); }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Require(allocated <= 4096, $"retained quad loop allocated {allocated} bytes");

            canvas.Submit(list.Items);
            long redraws = canvas.RedrawCount;
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws, "unchanged canvas queued a redraw");
            list.Items[0].Points[0] += new Vector2(.125f, 0f);
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws + 1, "subpixel motion was rounded away");

            list.Clear();
            MagmaDrawItem text = list.Add(MagmaWidgetKind.Text);
            text.Text = "alpha beta gamma\ndelta";
            text.Font = HudAssets.DefaultFont;
            text.Rect = new Rect2(20f, 30f, 55f, 120f);
            text.HeightPx = 15f;
            text.XScale = 1.1f;
            text.TrackingPx = .3f;
            text.Color = Colors.White;
            canvas.Submit(list.Items);
            redraws = canvas.RedrawCount;
            text.Text += "!";
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws + 1, "dynamic text was not redrawn");
            redraws = canvas.RedrawCount;
            text.Color = new Color(1f, 1f, 1f, .4999f);
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws + 1, "text fade was not redrawn");

            HudAssets? assets = HudAssets.TryLoad(null, out string? error);
            MagmaFont? font = assets?.Font(HudAssets.DefaultFont);
            Require(font is not null, error ?? "default HUD font unavailable");
            MagmaCanvas.TextLine[] layout = canvas.CachedLayout(font!, text);
            List<(string Line, float Top)> reference = MagmaCanvas.Layout(font!, text);
            Require(layout.Length == reference.Count, "cached wrapping differs");
            for (int i = 0; i < layout.Length; i++)
                Require(layout[i].Text == reference[i].Line && layout[i].Top == reference[i].Top &&
                    layout[i].Width == font!.Measure(reference[i].Line, text.HeightPx, text.XScale, text.TrackingPx), "cached text placement differs");
            Require(ReferenceEquals(layout, canvas.CachedLayout(font!, text)), "text layout cache missed identical input");
            text.Color = Colors.Red;
            Require(ReferenceEquals(layout, canvas.CachedLayout(font!, text)), "text colour unnecessarily invalidated layout");
            text.Rect = new Rect2(20f, 30f, 100f, 120f);
            Require(!ReferenceEquals(layout, canvas.CachedLayout(font!, text)), "text resize did not invalidate wrapping");

            canvas.Assets = assets;
            canvas.Submit(list.Items);
            MagmaCanvas.TextLine[] beforeSwap = canvas.CachedLayout(font!, text);
            redraws = canvas.RedrawCount;
            HudAssets? replacement = HudAssets.TryLoad(assets!.Root, out string? replacementError);
            Require(replacement is not null && !ReferenceEquals(replacement, assets), replacementError ?? "replacement HUD assets unavailable");
            canvas.Assets = replacement;
            // Identical text/geometry must still rebuild native draw commands, which reference the old textures.
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws + 1, "asset replacement did not redraw unchanged commands");
            Require(!ReferenceEquals(beforeSwap, canvas.CachedLayout(font!, text)), "asset replacement did not clear text layouts");
            redraws = canvas.RedrawCount;
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws, "unchanged replacement assets queued another redraw");

            text.NativeFont = OpenScctGame.UI.UiArt.BodyFont;
            Require(text.NativeFont is not null, "bundled Inter font unavailable");
            text.Text = "Modern HUD";
            text.XScale = 1f;
            text.TrackingPx = 0f;
            canvas.Submit(list.Items);
            redraws = canvas.RedrawCount;
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws, "unchanged native text redrew");
            Require(batch.SetItems(list.Items, 0, 1) && ReferenceEquals(batch.Items[0].NativeFont, text.NativeFont),
                "native font was omitted from the retained command snapshot");
            text.NativeFont = OpenScctGame.UI.UiArt.TitleFont;
            Require(batch.SetItems(list.Items, 0, 1), "native font replacement was not detected");
            canvas.Submit(list.Items);
            Require(canvas.RedrawCount == redraws + 1, "native font replacement did not redraw");

            list.Clear();
            MagmaDrawItem dodge = Image(list);
            Require(dodge.NativeFont is null, "pooled draw item retained a native font");
            dodge.Blend = MagmaBlend.Dodge;
            var viewport = new Rect2(0f, 0f, 1280f, 720f);
            Require(MagmaCanvas.TryCopyRect(list.Items, 0, 1, viewport, out Rect2 rect), "small dodge run used a full copy");
            foreach (Vector2 p in dodge.Points) Require(rect.HasPoint(p), "copy bounds omitted a sampled pixel");
            dodge.Points[0] = new Vector2(float.NaN, 0f);
            Require(!MagmaCanvas.TryCopyRect(list.Items, 0, 1, viewport, out _), "invalid geometry did not fall back");
            dodge.SetGeometry([Vector2.Zero, new Vector2(1280f, 0f), new Vector2(1280f, 720f), new Vector2(0f, 720f)],
                [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down], [Colors.White, Colors.White, Colors.White, Colors.White]);
            Require(!MagmaCanvas.TryCopyRect(list.Items, 0, 1, viewport, out _), "full-screen run did not use the full-copy fallback");
            return SmokeResult.Pass($"retained ownership, exact invalidation, asset replacement, text layout, regional copy bounds; 1024 quads allocated {allocated} bytes");
        }
        catch (Exception error)
        {
            return SmokeResult.Fail(error.Message);
        }
        finally
        {
            batch.Free();
            canvas.Free();
        }
    }

    private static MagmaDrawItem Image(MagmaDrawList list)
    {
        MagmaDrawItem item = list.Add(MagmaWidgetKind.Image);
        item.Path = "test/quad";
        item.SetGeometry([new Vector2(10f, 20f), new Vector2(50f, 20f), new Vector2(50f, 60f), new Vector2(10f, 60f)],
            [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down], [Colors.White, Colors.White, Colors.White, Colors.White]);
        return item;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

/// <summary>A real-renderer pixel comparison of regional copies with the original full-viewport copy path.</summary>
public sealed class HudCopyPixelsSmoke : SmokeTest
{
    public const string TestName = "hud-copy-pixels";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private MagmaCanvas? _canvas;
    private HudAssets? _assets;
    private readonly List<MagmaDrawItem> _items = new();
    private readonly List<ColorRect> _backdrop = new();
    private ColorRect? _movingBackdrop;
    private byte[]? _reference;
    private Color _initialDodgePixel;
    private long _retainedRedraws;
    private int _case;
    private int _phase;
    private int _settle;
    private static readonly Vector2I[] Sizes = [new(640, 480), new(1920, 1080), new(3440, 1440), new(1920, 1080)];

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("hud-copy-pixels requires a real renderer");
            if (_viewport is null)
            {
                _assets ??= HudAssets.TryLoad(null, out _);
                if (_assets is null || WhiteRegion.Find(_assets) is not { } white || _assets.Texture(white.Package, white.Texture) is null)
                    return SmokeResult.Fail("white HUD texture unavailable");
                Create(context, white);
                return SmokeResult.Running;
            }
            if (_settle-- > 0) return SmokeResult.Running;
            using Image image = _viewport.GetTexture().GetImage();
            if (image.IsEmpty()) return Finish(SmokeResult.Fail("empty copy comparison image"));
            string variant = _phase switch { 0 => "full-static", 1 => "regional-static", 2 => "regional-moving", _ => "full-moving" };
            if (Environment.GetEnvironmentVariable("SVM_HUD_PIXELS_OUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                image.SavePng(Path.Combine(output, $"case-{_case}-{variant}.png"));
            }
            byte[] pixels = image.GetData();
            if (_phase == 0)
            {
                _reference = pixels;
                _initialDodgePixel = DodgePixel(image);
                SubmitRetained(fullCopies: false);
                if ((_case < 3) != (_canvas!.RegionalCopyCount > 0))
                    return Finish(SmokeResult.Fail("regional-copy selection or transformed-canvas fallback was not exercised"));
                _phase = 1;
                _settle = 3;
                return SmokeResult.Running;
            }
            if (_phase == 1)
            {
                if (!_reference.AsSpan().SequenceEqual(pixels))
                    return Finish(SmokeResult.Fail($"regional copy changed static pixels in case {_case} ({Sizes[_case]})"));
                // Change the rendered scenery without changing any HUD command or its material. The existing
                // regional BackBufferCopy must sample this new frame even though the HUD does not QueueRedraw.
                MoveBackdrop();
                SubmitRetained(fullCopies: false);
                _phase = 2;
                _settle = 3;
                return SmokeResult.Running;
            }
            if (_phase == 2)
            {
                if (_reference.AsSpan().SequenceEqual(pixels) || DodgePixel(image) == _initialDodgePixel)
                    return Finish(SmokeResult.Fail($"changed scenery was not refreshed beneath retained dodge commands in case {_case}"));
                _reference = pixels;
                // Keep the changed scene and identical HUD commands; the full-copy reference is captured only
                // after it has rendered. A stale regional copy cannot pass merely because other pixels moved.
                SubmitRetained(fullCopies: true);
                _phase = 3;
                _settle = 3;
                return SmokeResult.Running;
            }
            if (!_reference.AsSpan().SequenceEqual(pixels))
                return Finish(SmokeResult.Fail($"regional copy changed moving-backdrop pixels in case {_case} ({Sizes[_case]})"));
            _viewport.Free();
            _viewport = null;
            _canvas = null;
            _reference = null;
            _phase = 0;
            if (++_case < Sizes.Length) return SmokeResult.Running;
            return SmokeResult.Pass("full/regional copies are pixel-identical with static and changing scenery at 640x480, 1920x1080 and 3440x1440; retained HUD does not redraw; transformed canvas falls back");
        }
        catch (Exception error)
        {
            return Finish(SmokeResult.Fail(error.Message));
        }
    }

    private void Create(SmokeContext context, WhiteRegion white)
    {
        Vector2I size = Sizes[_case];
        float scale = size.Y / 480f;
        _viewport = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _backdrop.Clear();
        for (int i = 0; i < 12; i++)
        {
            var stripe = new ColorRect
            {
                Position = new Vector2(i * size.X / 12f, 0f), Size = new Vector2(size.X / 12f + 1f, size.Y),
                Color = new Color(.02f + i * .013f, .12f + i * .007f, .06f + i * .011f),
            };
            _viewport.AddChild(stripe);
            _backdrop.Add(stripe);
        }
        _movingBackdrop = new ColorRect
        {
            Position = new Vector2(80f, 90f) * scale, Size = new Vector2(60f, 60f) * scale,
            Color = new Color(.31f, .07f, .25f),
        };
        _viewport.AddChild(_movingBackdrop);
        _canvas = new MagmaCanvas { Assets = _assets, FullViewportCopies = true };
        if (_case == 3) _canvas.Position = new Vector2(7.25f, 3.5f);
        _viewport.AddChild(_canvas);
        _items.Clear();
        Add(new Vector2(40f, 50f), new Vector2(190f, 180f), new Color(.15f, .23f, .34f, .65f), MagmaBlend.Normal, true);
        MagmaDrawItem nativeText = HudText.Item("copy-native-text", "Modern HUD: sleep / knockout",
            new Rect2(new Vector2(48f, 65f) * scale, new Vector2(220f, 16f) * scale), 12f * scale, MagmaAlign.Left, Colors.White);
        nativeText.NativeFont = OpenScctGame.UI.UiArt.BodyFont;
        if (nativeText.NativeFont is null) throw new InvalidOperationException("bundled Inter font unavailable");
        nativeText.TrackingPx = 0f;
        _items.Add(nativeText);
        Add(new Vector2(70.25f, 80.75f), new Vector2(190.5f, 170.25f), new Color(.4f, .15f, .3f, 1f), MagmaBlend.Dodge, true);
        Add(new Vector2(120f, 130f), new Vector2(140f, 100f), new Color(.13f, .2f, .31f, .4f), MagmaBlend.Normal, true);
        Add(new Vector2(-12.5f, 190.25f), new Vector2(190.5f, 150.25f), new Color(.15f, .4f, .21f, 1f), MagmaBlend.Dodge2X, false);
        Add(new Vector2(140.5f, 210.25f), new Vector2(190.5f, 150.25f), new Color(.22f, .19f, .4f, 1f), MagmaBlend.Dodge4X, true);
        MagmaDrawItem rotated = _items[^1];
        Vector2 centre = new Vector2(235.75f, 285.375f) * scale;
        for (int i = 0; i < rotated.Points.Length; i++) rotated.Points[i] = centre + (rotated.Points[i] - centre).Rotated(.27f);
        _canvas.Submit(_items);
        _retainedRedraws = _canvas.RedrawCount;
        _settle = 3;

        void Add(Vector2 at, Vector2 extent, Color colour, MagmaBlend blend, bool clamp)
        {
            MagmaDrawItem item = white.Quad("copy-fixture", at * scale, extent * scale, colour);
            item.Blend = blend;
            item.Clamp = clamp;
            _items.Add(item);
        }
    }

    private Color DodgePixel(Image image)
    {
        float scale = Sizes[_case].Y / 480f;
        // Strictly inside the first dodge polygon in both the normal and translated-canvas fixtures.
        return image.GetPixel((int)(100f * scale), (int)(100f * scale));
    }

    private void MoveBackdrop()
    {
        for (int i = 0; i < _backdrop.Count; i++)
            _backdrop[i].Color = new Color(.16f + i * .009f, .04f + i * .011f, .2f - i * .007f);
        _movingBackdrop!.Position += new Vector2(90f, 20f) * (Sizes[_case].Y / 480f);
        _movingBackdrop.Color = new Color(.07f, .3f, .11f);
    }

    private void SubmitRetained(bool fullCopies)
    {
        _canvas!.FullViewportCopies = fullCopies;
        _canvas.Submit(_items);
        if (_canvas.RedrawCount != _retainedRedraws)
            throw new InvalidOperationException("unchanged HUD commands redrew during backdrop/copy-mode changes");
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _viewport?.Free();
        _viewport = null;
        return result;
    }
}
#endif

/// <summary>
/// Deterministic command fingerprints using only pre-optimization public APIs. Compile with HUD_BASELINE to run
/// this same fixture in the frozen source tree; identical fingerprints cover every submitted visual field.
/// </summary>
public sealed class HudOutputReplaySmoke : SmokeTest
{
    public const string TestName = "hud-output-replay";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        HudAssets? assets = HudAssets.TryLoad(null, out string? error);
        if (assets is null) return SmokeResult.Fail(error ?? "no HUD assets");
        var cases = new List<object>();
        foreach (Vector2 screen in new[] { new Vector2(640, 480), new Vector2(1920, 1080), new Vector2(3440, 1440) })
        foreach (HudSide side in new[] { HudSide.Spy, HudSide.Merc })
        {
            HudPage page = side == HudSide.Spy ? new SpyHudPage(assets) : new MercHudPage(assets);
            var interaction = new InteractionPage(assets);
            var markers = new HudMarkers { Projector = p => new Vector2(screen.X * .5f + p.Y * .1f, screen.Y * .5f - p.Z * .1f) };
            var s = new HudSnapshot
            {
                Valid = true, Side = side, HasPawn = true, Health = 100, HasWeapon = true, ClipAmmo = 30, Ammo = 90,
                ShowReticle = true, EnergyLevel = 75f, EnergyLevelMax = 100, EnhancedReality = true,
                TimeLimit = 10, ElapsedTime = 15, ObjectivesLeft = 3, NumObjectives = 4,
                HackingText = "Under hacking", SecuredText = "Access denied", DistanceUnit = "m", DistanceScale = .02f,
                Gadget = new HudGadget("Sticky Camera", 3, "StickyCam", 0, 0, true),
            };
            s.Objectives.Add(new HudObjective("Terminal", HudObjectiveKind.Terminal, new FVector(900f, -450f, 350f), false, true, false, 45));
            s.Objectives.Add(new HudObjective("Disk", HudObjectiveKind.Disk, new FVector(1500f, 350f, -300f), true, false, false, -1));
            s.Gpos.Add(new FVector(1000f, 800f, 250f));
            s.Interactions.AddRange(["Hack terminal", "Place camera", "Return"]);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            for (int frame = 0; frame < 240; frame++)
            {
                s.TimeSeconds = frame / 60f;
                s.Health = 100 - frame / 4;
                s.ClipAmmo = 30 - frame % 30;
                s.PawnSpeed = frame is >= 40 and < 110 ? 150f : 0f;
                s.ReticleFrame = frame % 10;
                s.ReticleHit = frame % 40 < 10;
                s.Merc.Wet = frame < 120;
                s.Merc.Perturbed = frame is >= 60 and < 100;
                s.Merc.StickyShocking = frame is >= 150 and < 180;
                s.Poisoned = frame is >= 120 and < 170;
                s.ShowInteractions = frame is >= 25 and < 210;
                s.SelectedInteraction = frame / 30 % 3;
                s.InteractionFocus = frame % 50 < 25;
                s.HideHud = frame is >= 220 and < 225;
                page.Update(s, 1f / 60f);
                page.Draw(screen, s);
                interaction.Update(s, 1f / 60f);
                interaction.Draw(screen);
                markers.Update(s, 1f / 60f);
                markers.Draw(s, screen);
                Write(writer, page.DrawList.Items);
                Write(writer, interaction.DrawList.Items);
                Write(writer, markers.DrawList.Items);
            }
            writer.Flush();
            cases.Add(new { side = side.ToString(), width = screen.X, height = screen.Y, frames = 240,
                sha256 = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)))) });
        }
        string json = JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("SVM_HUD_REPLAY_OUT") is { Length: > 0 } output)
        {
            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        GD.Print("SVM_HUD_REPLAY " + json);
        return SmokeResult.Pass("fingerprinted 1440 HUD frames across both roles and three resolutions");
    }

    private static void Write(BinaryWriter w, IReadOnlyList<MagmaDrawItem> items)
    {
        w.Write(items.Count);
        foreach (MagmaDrawItem item in items)
        {
            w.Write(item.Path);
            w.Write((int)item.Kind);
            if (item.Kind == MagmaWidgetKind.Text)
            {
                w.Write(item.Text); w.Write(item.Font);
                Vec(w, item.Rect.Position); Vec(w, item.Rect.Size);
                w.Write(item.HeightPx); w.Write((int)item.Align);
                Colour(w, item.Color); Colour(w, item.Shadow); Vec(w, item.ShadowOffsetPx);
                w.Write(item.XScale); w.Write(item.TrackingPx);
            }
            else
            {
                w.Write(item.Package); w.Write(item.Texture); w.Write((int)item.Blend); w.Write(item.Clamp);
                w.Write(item.Points.Length); foreach (Vector2 v in item.Points) Vec(w, v);
                w.Write(item.Uvs.Length); foreach (Vector2 v in item.Uvs) Vec(w, v);
                w.Write(item.Colors.Length); foreach (Color c in item.Colors) Colour(w, c);
            }
        }
    }

    private static void Vec(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
    private static void Colour(BinaryWriter w, Color c) { w.Write(c.R); w.Write(c.G); w.Write(c.B); w.Write(c.A); }
}
