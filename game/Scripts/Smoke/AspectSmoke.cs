using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=aspect</c>: the play scene at 4:3, 16:10, 16:9 and 21:9 (1024 x 768, 1152 x 720, 1280 x 720,
/// 1680 x 720), as the spy and as the merc, set up as the launchers do. For each window it checks Reloaded's
/// widescreen rules (Hud/HudLayout, docs/HUD.md): the camera keeps the 4:3 vertical angle and opens up to 16:9; a
/// wider window shows the 16:9 picture stretched across it; every HUD area keeps its side at the 4:3 scale and stays
/// on the picture; the full-frame overlays (sticky cam, camera network, both binoculars) cover the picture from edge to
/// edge; the gas mask area is drawn. With a window and <c>SVM_PLAY_SHOTS</c> (a folder under local/) it saves each
/// view, the 21:9 ones stretched to the window as the player sees them. Headless it lays the HUD out for each size.
/// </summary>
public sealed class AspectSmoke : SmokeTest
{
    public const string TestName = "aspect";

    private static readonly Vector2I[] Sizes = [new(1024, 768), new(1152, 720), new(1280, 720), new(1680, 720)];

    private static readonly string[] Labels = ["4x3", "16x10", "16x9", "21x9"];

    /// <summary>Seconds a window size is held before its checks (the window, the projection and the HUD settle).</summary>
    private const double SettleSeconds = 0.8;

    /// <summary>Seconds an overlay is shown before its checks and picture.</summary>
    private const double OverlaySeconds = 0.35;

    /// <summary>HUD placement tolerance in pixels.</summary>
    private const float Tolerance = 1.5f;

    private readonly List<string> _failures = [];
    private readonly List<string> _notes = [];
    private readonly ForcedPage _forced = new();
    private readonly List<float> _verticals = [];

    private PlayRoot? _root;
    private PlaySide _side = PlaySide.Spy;
    private int _size = -1;
    private int _view = -1;
    private double _phaseStart;
    private int _teardown = -1;
    private SmokeResult _final;
    private Vector2I _originalSize;
    private bool _forcedAdded;
    private List<(string Name, HudPage Page, string[] FrameAreas)> _views = [];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < 6 ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _originalSize = context.Tree.Root.Size;
            _root = Start(context, _side);
            _size = -1;
            _forcedAdded = false;
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return SmokeResult.Fail(error);
        }

        if (!_root.IsLoaded || _root.Session?.LocalPawn is null || _root.Hud?.Assets is not { } assets || _root.View is null)
        {
            return SmokeResult.Running;
        }

        if (!_forcedAdded)
        {
            _root.Hud.AddPageOverride(_forced);
            _views = Views(assets, _root.Hud.Options);
            _forcedAdded = true;
        }

        double now = context.ElapsedSeconds;
        if (_size < 0)
        {
            NextSize(context, 0, now);
            return SmokeResult.Running;
        }

        if (_view < 0)
        {
            if (now - _phaseStart < SettleSeconds)
            {
                return SmokeResult.Running;
            }

            CheckPlayerView();
            Shot("play");
            _view = 0;
            Show(0, now);
            return SmokeResult.Running;
        }

        if (now - _phaseStart < OverlaySeconds)
        {
            return SmokeResult.Running;
        }

        CheckOverlay(_views[_view]);
        Shot(_views[_view].Name);
        if (_view + 1 < _views.Count)
        {
            Show(_view + 1, now);
            return SmokeResult.Running;
        }

        _forced.Page = null;
        if (_size + 1 < Sizes.Length)
        {
            NextSize(context, _size + 1, now);
            return SmokeResult.Running;
        }

        // Next side, or done.
        context.Tree.Root.Size = _originalSize;
        _root.QueueFree();
        _root = null;
        if (_side == PlaySide.Spy)
        {
            _side = PlaySide.Merc;
            return SmokeResult.Running;
        }

        string verticals = string.Join(",", _verticals.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)));
        _final = _failures.Count == 0
            ? SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture, $"views={_notes.Count} verticalFov={verticals}"))
            : SmokeResult.Fail(string.Join(" | ", _failures.Take(20)) + (_failures.Count > 20 ? $" (+{_failures.Count - 20})" : ""));
        foreach (string note in _notes)
        {
            GD.Print("SVM_ASPECT " + note);
        }

        foreach (string failure in _failures)
        {
            GD.Print("SVM_ASPECT fail " + failure);
        }

        _teardown = 0;
        return SmokeResult.Running;
    }

    private static PlayRoot Start(SmokeContext context, PlaySide side)
    {
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = "SteeE",
                Side = side,
                Mode = "Story",
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                Bots = 0,
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
            Pilot = null,
        };
        PlayRoot root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(root);
        return root;
    }

    /// <summary>The overlays of the side: the page, and the areas that make its full-frame picture.</summary>
    private List<(string, HudPage, string[])> Views(HudAssets assets, HudOptions options)
    {
        var views = new List<(string, HudPage, string[])>();
        if (_side == PlaySide.Spy)
        {
            views.Add(("stickycam", Shown(new Hud.Gadgets.StickyCamPage(assets, options), "StickyCam"), ["StickyCam"]));
            views.Add(("binoculars", Shown(new SpyBinocularsPage(assets, options), "frame", "ZoomAtt"), ["frame"]));
        }
        else
        {
            views.Add(("camnetwork", Shown(new Hud.Gadgets.CamNetworkPage(assets, options), "Camnetwork"), ["Camnetwork"]));
            views.Add(("binoculars", Shown(new MercBinocularsPage(assets, options), "frame", "ZoomDef"), ["frame"]));
            views.Add(("gasmask", Shown(new MercHudPage(assets, options), "MaskGas", "Vie", "Gadget", "Ammo", "Objectif"), []));
        }

        return views;
    }

    /// <summary>Words only the console pages' texts use.</summary>
    private static readonly string[] ConsolePrompts = ["Trigger", "thumbstick"];

    private static HudPage Shown(HudPage page, params string[] areas)
    {
        // The page manager makes a new binocular page object on each entry; its creation hides the console texts.
        if (page is BinocularsPage binoculars)
        {
            binoculars.Enter();
        }

        page.Drivers.Add(new ShowAreas(areas));
        return page;
    }

    private void NextSize(SmokeContext context, int index, double now)
    {
        _size = index;
        _view = -1;
        _phaseStart = now;
        Vector2I size = Sizes[index];
        if (DisplayServer.GetName() == "headless")
        {
            // No window to resize: lay the HUD out for the picture the window would show.
            _root!.Hud!.ScreenSize = new Vector2(HudLayout.LayoutWidth(size.X, size.Y), size.Y);
        }
        else
        {
            context.Tree.Root.Size = size;
        }
    }

    private void Show(int index, double now)
    {
        _view = index;
        _phaseStart = now;
        _forced.Page = _views[index].Page;
    }

    private string Tag => _side.ToString().ToLowerInvariant() + "-" + Labels[_size];

    private Vector2 Picture()
    {
        Vector2I size = Sizes[_size];
        return new Vector2(HudLayout.LayoutWidth(size.X, size.Y), size.Y);
    }

    /// <summary>The camera, the window stretch and the player page's areas.</summary>
    private void CheckPlayerView()
    {
        Vector2I size = Sizes[_size];
        Vector2 picture = Picture();
        Window root = _root!.GetTree().Root;
        bool headless = DisplayServer.GetName() == "headless";
        if (!headless)
        {
            bool wide = size.X > size.Y * HudLayout.WidestAspect + 1f;
            bool stretched = root.ContentScaleMode == Window.ContentScaleModeEnum.Viewport && root.ContentScaleAspect == Window.ContentScaleAspectEnum.Ignore;
            Expect(wide == stretched, $"{Tag}: window {size.X} x {size.Y} stretched={stretched}");
            if (wide)
            {
                Expect(root.ContentScaleSize == new Vector2I((int)picture.X, (int)picture.Y), $"{Tag}: drawn at {root.ContentScaleSize}, expected {picture}");
            }

            Vector2 visible = _root.GetViewport().GetVisibleRect().Size;
            Expect(MathF.Abs(visible.X - picture.X) <= 1f && MathF.Abs(visible.Y - picture.Y) <= 1f, $"{Tag}: the play view is {visible}, expected {picture}");
        }

        // The vertical angle: the 4:3 one at every size (Hor+ to 16:9, stretched past it).
        Camera3D camera = _root.View!.Camera;
        float vertical = Mathf.RadToDeg(2f * MathF.Atan(camera.Size * 0.5f / camera.Near));
        float fov = _root.Camera?.Current.Fov ?? 0f;
        float expected = Options.ViewFov.VerticalOf(fov, 4f / 3f);
        _verticals.Add(vertical);
        Expect(headless || MathF.Abs(vertical - expected) <= 0.05f, $"{Tag}: vertical FOV {vertical:F2} for FovAngle {fov:F1}, the 4:3 angle is {expected:F2}");

        HudPage? page = _root.Hud!.ActivePage;
        Expect(page is not null, $"{Tag}: no HUD page");
        if (page is not null)
        {
            int checkedItems = CheckAnchors(page.DrawList, picture, onPicture: true);
            _notes.Add(string.Create(CultureInfo.InvariantCulture, $"{Tag} play: window {size.X}x{size.Y}, picture {picture.X}x{picture.Y}, FovAngle {fov:F1}, vertical {vertical:F2}, horizontal {2f * Mathf.RadToDeg(MathF.Atan(MathF.Tan(Mathf.DegToRad(vertical) * 0.5f) * picture.X / picture.Y)):F2}, page {page.GetType().Name} {checkedItems} areas placed"));
        }
    }

    private void CheckOverlay((string Name, HudPage Page, string[] FrameAreas) view)
    {
        Vector2 picture = Picture();
        HudPage? active = _root!.Hud!.ActivePage;
        Expect(ReferenceEquals(active, view.Page), $"{Tag} {view.Name}: the overlay page is not the one drawn");
        MagmaDrawList list = view.Page.DrawList;
        Expect(list.Items.Count > 0, $"{Tag} {view.Name}: nothing drawn");
        // The pages' gamepad texts (textZoomAtt1, textZoomDef3 "Left Trigger to hold breath"...) are hidden on a PC.
        foreach (MagmaDrawItem item in list.Items)
        {
            if (item.Kind == MagmaWidgetKind.Text && ConsolePrompts.Any(c => item.Text.Contains(c, StringComparison.OrdinalIgnoreCase)))
            {
                Expect(false, $"{Tag} {view.Name}: a gamepad prompt is drawn: '{item.Text}' ({item.Path})");
            }
        }

        // Full-frame overlays may run their frame and animated lines past the picture; only the anchors are checked.
        int placed = CheckAnchors(list, picture, onPicture: false);
        if (view.FrameAreas.Length > 0)
        {
            float left = float.MaxValue;
            float right = float.MinValue;
            foreach (MagmaDrawItem item in list.Items)
            {
                if (item.Kind != MagmaWidgetKind.Image || !view.FrameAreas.Any(a => item.Path.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (Vector2 p in item.Points)
                {
                    left = MathF.Min(left, p.X);
                    right = MathF.Max(right, p.X);
                }
            }

            Expect(left <= 1f && right >= picture.X - 1f,
                string.Create(CultureInfo.InvariantCulture, $"{Tag} {view.Name}: the frame covers x {left:F0} to {right:F0} of a {picture.X:F0} wide picture"));
            _notes.Add(string.Create(CultureInfo.InvariantCulture, $"{Tag} {view.Name}: frame x {left:F0}..{right:F0} of {picture.X:F0}, {placed} areas placed, {list.Items.Count} items"));
        }
        else
        {
            bool mask = list.Any("MaskGas");
            Expect(mask, $"{Tag} {view.Name}: the gas mask area was not drawn");
            _notes.Add(string.Create(CultureInfo.InvariantCulture, $"{Tag} {view.Name}: mask drawn={mask}, {placed} areas placed, {list.Items.Count} items"));
        }
    }

    /// <summary>
    /// Every unrotated image of a left- or right-hand area sits where HudLayout puts its page rect, and with
    /// <paramref name="onPicture"/> no side area leaves the picture. Returns how many images were checked.
    /// </summary>
    private int CheckAnchors(MagmaDrawList list, Vector2 picture, bool onPicture)
    {
        int count = 0;
        foreach (MagmaDrawItem item in list.Items)
        {
            if (item.Kind != MagmaWidgetKind.Image || item.Points.Length != 4)
            {
                continue;
            }

            float minX = item.Points.Min(p => p.X);
            float maxX = item.Points.Max(p => p.X);
            // Side areas must stay on the picture; centred full-screen effects (perturbation, droplets) scroll past it.
            bool side = item.Anchor is MagmaAnchor.Left or MagmaAnchor.Right;
            Expect(!onPicture || !side || (minX >= -Tolerance && maxX <= picture.X + Tolerance),
                string.Create(CultureInfo.InvariantCulture, $"{Tag}: {item.Path} drawn at x {minX:F0} to {maxX:F0}, off the {picture.X:F0} wide picture"));
            bool axisAligned = MathF.Abs(item.Points[0].Y - item.Points[1].Y) < 0.01f || MathF.Abs(item.Points[0].X - item.Points[1].X) < 0.01f;
            if (!axisAligned || item.Anchor is not (MagmaAnchor.Left or MagmaAnchor.Right) || item.PageRect.R <= item.PageRect.L)
            {
                continue;
            }

            HudAnchor anchor = item.Anchor == MagmaAnchor.Left ? HudAnchor.Left : HudAnchor.Right;
            float l = HudLayout.PageToScreenX(item.PageRect.L, anchor, picture.X, picture.Y);
            float r = HudLayout.PageToScreenX(item.PageRect.R, anchor, picture.X, picture.Y);
            // Masks and moved widgets may cut or shift an image inside its rect; its edges stay within the rect's.
            Expect(minX >= l - Tolerance && maxX <= r + Tolerance,
                string.Create(CultureInfo.InvariantCulture, $"{Tag}: {item.Path} ({item.Anchor}) drawn at x {minX:F1} to {maxX:F1}, its page rect maps to {l:F1} to {r:F1}"));
            count++;
        }

        return count;
    }

    private void Shot(string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        Vector2I window = Sizes[_size];
        if (image.GetWidth() != window.X || image.GetHeight() != window.Y)
        {
            // The window shows the drawn picture stretched to its own size.
            image.Resize(window.X, window.Y, Image.Interpolation.Bilinear);
        }

        image.SavePng(System.IO.Path.Combine(folder, $"aspect-{Tag}-{label}.png"));
    }

    private void Expect(bool condition, string message)
    {
        if (!condition)
        {
            _failures.Add(message);
        }
    }

    /// <summary>Puts a chosen page over the player page while set.</summary>
    private sealed class ForcedPage : IHudPageOverride
    {
        public HudPage? Page { get; set; }

        public HudPage? ActivePage(OpenScct.Game.SBase.SPlayerController? pc, HudSnapshot s) => Page;
    }

    /// <summary>Shows the named areas of a page (restarted on first show), as the area's own driver does while its view is up.</summary>
    private sealed class ShowAreas(string[] areas) : IHudAreaDriver
    {
        private readonly HashSet<string> _started = new(StringComparer.Ordinal);

        public HudSide? Side => null;

        public IReadOnlyCollection<string> Areas { get; } = areas;

        public void Read(OpenScct.Game.SBase.SPlayerController? pc, HudSnapshot s)
        {
        }

        public void Drive(HudPage page, HudSnapshot s, float dt)
        {
            foreach (string name in Areas)
            {
                if (page.Area(name) is { } area && _started.Add(name))
                {
                    area.Restart();
                }
            }
        }
    }
}
