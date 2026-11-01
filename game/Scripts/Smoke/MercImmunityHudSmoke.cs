using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Smoke;

/// <summary>Checks the immunity HUD through real Magma pages; renderer runs also check and capture its pixels.</summary>
public sealed class MercImmunityHudSmoke : SmokeTest
{
    public const string TestName = "merc-immunity-hud";
    public override string Name => TestName;

    private static readonly Vector2I StandardSize = new(1280, 720);
    private static readonly ShockImmunityView Timed = new(true, false, 6.11f, 6.11f / 12.5f);
    private static readonly Case[] Cases =
    [
        new("timed-4x3", new(1024, 768), Timed),
        new("timed-16x9", StandardSize, Timed),
        new("timed-21x9", new(1680, 720), Timed),
        new("state", StandardSize, new(true, true, 0f, 0f)),
        new("final-zero", StandardSize, new(true, false, 0f, 0f)),
        new("scope", StandardSize, Timed, Page: "scope"),
        new("camera-network", StandardSize, Timed, Page: "camera"),
        new("stretched", new(1680, 720), Timed, Stretch: true),
        new("hidden", StandardSize, Timed, Hidden: true, Shown: false),
        new("dead", StandardSize, Timed, Health: 0, Shown: false),
        new("spectator", StandardSize, Timed, Spectator: true, Shown: false),
        new("end-game", StandardSize, Timed, Ended: true, Shown: false),
        new("inactive", StandardSize, default, Shown: false),
        new("spy", StandardSize, Timed, Spy: true, Shown: false),
        new("invalid", StandardSize, Timed, Valid: false, Shown: false),
        new("shutdown", StandardSize, Timed, Shutdown: true, Shown: false),
    ];

    private readonly List<string> _failures = [];
    private readonly Source _source = new();
    private readonly ForcedPage _forced = new();
    private SubViewport? _viewport;
    private ColorRect? _background;
    private HudLayer? _hud;
    private int _case = -1;
    private int _settle;
    private int _rendered;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_hud is null)
            {
                _hud = HudLayer.Create(out string? error);
                if (_hud is null) return SmokeResult.Fail(error ?? "HUD content is missing");
                if (_hud.Binoculars?.Merc is null || _hud.GadgetViews?.CamNetwork is null)
                    return Finish(SmokeResult.Fail("scope or camera-network HUD content is missing"));
                _viewport = new SubViewport { Size = StandardSize, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                context.Tree.Root.AddChild(_viewport);
                _background = new ColorRect { Size = StandardSize, Color = new Color(.07f, .1f, .13f) };
                _viewport.AddChild(_background);
                _hud.Source = _source;
                _hud.AddPageOverride(_forced);
                _viewport.AddChild(_hud);
                SelectCase(0);
                return SmokeResult.Running;
            }

            _hud.Step(1f / 60f);
            if (++_settle < 5) return SmokeResult.Running;
            Check(context);
            if (_case + 1 < Cases.Length)
            {
                SelectCase(_case + 1);
                return SmokeResult.Running;
            }

            return Finish(_failures.Count == 0
                ? SmokeResult.Pass($"{Cases.Length} HUD cases; {_rendered} rendered captures checked")
                : SmokeResult.Fail(string.Join("; ", _failures)));
        }
        catch (Exception error)
        {
            return Finish(SmokeResult.Fail(error.ToString()));
        }
    }

    private void SelectCase(int index)
    {
        _case = index;
        _settle = 0;
        Case c = Cases[index];
        _source.Value = c;
        _viewport!.Size = c.Size;
        _background!.Size = c.Size;
        _hud!.ScreenSize = c.Size;
        _hud.Options.StretchHud = c.Stretch;
        _forced.Page = c.Page == "camera" ? _hud.GadgetViews!.CamNetwork : null;
        // This synthetic source has no controller to enter the camera state; show the real page's frame explicitly.
        if (c.Page == "camera" && _hud.GadgetViews!.CamNetwork!.Frame is { } frame)
        {
            frame.Restart();
            foreach (string widget in new[] { "IconStickyCamUp", "IconCamNetA", "IconCamNetB", "IconCamNetX",
                "textCamNetwork1", "textCamNetwork2", "textCamNetwork3", "textCamNetwork5" })
                frame.Widget(widget).Hidden = true;
            frame.Widget("textCamNetwork4").Text = "";
        }
        _hud.Step(1f / 60f);
    }

    private void Check(SmokeContext context)
    {
        Case c = Cases[_case];
        HudShockImmunity drawer = _hud!.ShockImmunity;
        Expect(drawer.Shown == c.Shown, "visibility");
        MagmaDrawItem? label = _hud.Items.FirstOrDefault(i => i.Path == "ShockImmunity/Label");
        Expect((label is not null) == c.Shown, "composited visibility");
        if (!c.Shown)
        {
            Expect(drawer.Label.Length == 0 && drawer.Progress == 0f && drawer.Bounds == default, "stale diagnostics cleared");
            return;
        }

        string expected = c.Immunity.StateProtected ? "STATE PROTECTED" : c.Immunity.RemainingSeconds == 0f ? "SHOCK IMMUNE · 0.0s" : "SHOCK IMMUNE · 6.2s";
        Expect(label?.Text == expected, "countdown/state label");
        float fraction = c.Immunity.StateProtected ? 1f : c.Immunity.Fraction;
        Expect(Math.Abs(drawer.Progress - fraction) < .0001f, "progress fraction");
        MagmaDrawItem? bar = drawer.DrawList.Find("ShockImmunity/Progress");
        MagmaDrawItem? track = drawer.DrawList.Find("ShockImmunity/Track");
        Expect((bar is not null) == (fraction > 0f), "empty/full bar presence");
        if (bar is not null && track is not null)
            Expect(Math.Abs((bar.Points[1].X - bar.Points[0].X) / (track.Points[1].X - track.Points[0].X) - fraction) < .001f, "drawn progress width");
        Expect(drawer.DrawList.Find("ShockImmunity/Cross") is not null, "crossed-lightning icon");
        Rect2 bounds = drawer.Bounds;
        Expect(bounds.Position.X >= 0 && bounds.Position.Y >= 0 && bounds.End.X <= c.Size.X && bounds.End.Y <= c.Size.Y, "screen bounds");
        if (label is not null && _hud.Assets?.Font(label.Font) is { } font)
        {
            foreach ((string line, float _) in MagmaCanvas.Layout(font, label))
            {
                float width = font.Measure(line, label.HeightPx, label.XScale, label.TrackingPx);
                Expect(width <= label.Rect.Size.X + 1f, "label fits panel");
            }
        }
        if (c.Page == "scope") Expect(ReferenceEquals(_hud.ActivePage, _hud.Binoculars!.Merc), "scope page active");
        if (c.Page == "camera")
        {
            Expect(ReferenceEquals(_hud.ActivePage, _hud.GadgetViews!.CamNetwork), "camera page active");
            Expect(_hud.GadgetViews!.CamNetwork!.Frame is { Visible: true } && _hud.ActivePage!.DrawList.Items.Count > 10,
                "camera frame is drawn");
        }

        if (DisplayServer.GetName() == "headless") return;
        using Image image = _viewport!.GetTexture().GetImage();
        Expect(!image.IsEmpty(), "rendered image");
        if (image.IsEmpty()) return;
        int amber = 0;
        for (int y = (int)bounds.Position.Y; y < (int)bounds.End.Y; y++)
            for (int x = (int)bounds.Position.X; x < (int)bounds.End.X; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (pixel.R > .5f && pixel.G > .3f && pixel.G < pixel.R * .9f && pixel.B < .4f) amber++;
            }
        Expect(amber > 20, "visible amber pixels");
        _rendered++;
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
            image.SavePng(Path.Combine(folder, "merc-immunity-hud-" + c.Name + ".png"));
        }
    }

    private void Expect(bool condition, string message)
    {
        if (!condition) _failures.Add(Cases[_case].Name + ": " + message);
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _viewport?.QueueFree();
        _viewport = null;
        if (_hud is { } hud && !hud.IsInsideTree()) hud.QueueFree();
        return result;
    }

    private sealed record Case(string Name, Vector2I Size, ShockImmunityView Immunity, string Page = "normal",
        bool Stretch = false, bool Hidden = false, int Health = 100, bool Spectator = false, bool Ended = false,
        bool Spy = false, bool Valid = true, bool Shutdown = false, bool Shown = true);

    private sealed class Source : IHudSource
    {
        public Case Value { get; set; } = Cases[0];
        public void Fill(HudSnapshot snapshot)
        {
            Case c = Value;
            snapshot.Valid = c.Valid;
            snapshot.Side = c.Spy ? HudSide.Spy : HudSide.Merc;
            snapshot.HasPawn = true;
            snapshot.Health = c.Health;
            snapshot.HideHud = c.Hidden;
            snapshot.TimeSeconds = 100f;
            snapshot.ShockImmunity = c.Immunity;
            snapshot.Spectator.Active = c.Spectator;
            snapshot.Spectator.GameEnded = c.Ended;
            snapshot.Binoculars.Mode = c.Page == "scope" ? HudBinocularMode.Merc : HudBinocularMode.None;
            snapshot.Merc.ShutDowns = c.Shutdown ? 1 : 0;
        }
    }

    private sealed class ForcedPage : IHudPageOverride
    {
        public HudPage? Page { get; set; }
        public HudPage? ActivePage(SPlayerController? pc, HudSnapshot snapshot) => Page;
    }
}
