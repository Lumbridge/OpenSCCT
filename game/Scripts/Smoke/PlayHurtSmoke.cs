using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-hurt</c>: the spy's damage overlay (spec/hud/screen-effects.md rule 7) as a player sees it. SteeE
/// as the spy with no bots, set up as the launchers do (host thread, wall clock, HUD). The spy walks at the keys (W)
/// while the server, on its own thread, hits him with a body shot (the merc rifle's PawnTakeDamage, SDTBase): the
/// hit reaches his machine as ClientDamage and his HUD must show the red wash (90, 0, 0) through the canvas at 120,
/// fading to 0 over 0.5 s. Once at 16:9 (1280 x 720) and once at 4:3 (1024 x 768) with a window. Passes when the HUD
/// runs the wash after each hit and, with a window, the frame at the wash is redder than the one before the hit.
/// With SVM_PLAY_SHOTS the frames before and at each wash are saved.
/// </summary>
public sealed class PlayHurtSmoke : PlayPoseSmoke
{
    public const string TestName = "play-hurt";

    private static readonly (string Step, Vector2I Size)[] Legs = [("hurt 16x9", new(1280, 720)), ("hurt 4x3", new(1024, 768))];

    private readonly Dictionary<string, float> _peak = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _redBefore = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _redAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hitAsked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _stepAt = new(StringComparer.Ordinal);
    private int _pendingHits;
    private bool _hooked;
    private string _step = "";
    private double _clock;

    public override string Name => TestName;

    protected override int Bots => 0;

    protected override List<KeyStep> Timeline(SBasePawn local) =>
    [
        new KeyStep("stand", 1.0) { Tap = ["Crouch"] },
        new KeyStep(Legs[0].Step, 3.0) { Hold = ["BT_Up"] },
        new KeyStep("back", 3.0) { Hold = ["BT_Down"] },
        new KeyStep(Legs[1].Step, 3.0) { Hold = ["BT_Up"] },
        new KeyStep("end", 1.0),
    ];

    protected override void OnFrame(PlaySession session, string step)
    {
        _clock = Time.GetTicksMsec() / 1000.0;
        if (!_hooked)
        {
            _hooked = true;
            session.Host.BeforeWorldTick += _ => HitOnServer(session);
        }

        if (step != _step)
        {
            _step = step;
            _stepAt[step] = _clock;
            foreach ((string leg, Vector2I size) in Legs)
            {
                if (leg == step && DisplayServer.GetName() != "headless" && Root is { } root)
                {
                    root.GetTree().Root.Size = size;
                }
            }
        }

        if (Array.FindIndex(Legs, l => l.Step == step) < 0 || Root?.Hud is not { } hud)
        {
            return;
        }

        // Half a second into the leg (the window has its new size): the frame before the hit, then the hit.
        double into = _clock - _stepAt[step];
        if (!_hitAsked.Contains(step) && into >= 0.5)
        {
            _redBefore[step] = Redness(Shot(step + "-before"));
            _hitAsked.Add(step);
            Interlocked.Increment(ref _pendingHits);
            return;
        }

        int slot = hud.Effects.Sfx.Find(SFXType.SFXT_TextureColorRed);
        float v = slot >= 0 ? hud.Effects.Sfx.ValueOf(slot) : 0f;
        if (v > _peak.GetValueOrDefault(step))
        {
            _peak[step] = v;
        }

        if (v >= 60f && !_redAt.ContainsKey(step))
        {
            _redAt[step] = Redness(Shot(step + "-wash"));
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"SVM_HURT {step}: wash value {v:F0} at {into:F2} s into the leg, window {Root.GetViewport().GetVisibleRect().Size}"));
        }
    }

    /// <summary>On the server thread: the asked hits, a 10-point body shot on the spy through the controller.</summary>
    private void HitOnServer(PlaySession session)
    {
        if (Interlocked.Exchange(ref _pendingHits, 0) == 0 || session.ServerController is not { Pawn: { } spy } server)
        {
            return;
        }

        server.PawnTakeDamage(10, null, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTBase, 2, 70);
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        bool window = DisplayServer.GetName() != "headless";
        var problems = new List<string>();
        var detail = new List<string>();
        foreach ((string leg, _) in Legs)
        {
            float peak = _peak.GetValueOrDefault(leg);
            float before = _redBefore.GetValueOrDefault(leg, float.NaN);
            float at = _redAt.GetValueOrDefault(leg, float.NaN);
            detail.Add(string.Create(CultureInfo.InvariantCulture, $"{leg}: wash peak {peak:F0}, redness before {before:F3} at the wash {at:F3}"));
            if (peak < 60f)
            {
                problems.Add($"{leg}: the HUD showed no red wash after the hit (peak {peak:F0})");
            }
            else if (window && !(at - before >= 0.05f))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{leg}: the frame at the wash is not redder (before {before:F3}, at {at:F3})"));
            }
        }

        string text = string.Join("; ", detail);
        GD.Print("SVM_HURT " + text);
        return problems.Count == 0 ? SmokeResult.Pass(text) : SmokeResult.Fail(string.Join("; ", problems) + " (" + text + ")");
    }

    /// <summary>The window's picture, saved when SVM_PLAY_SHOTS names a folder (null without a window).</summary>
    private Image? Shot(string label)
    {
        if (DisplayServer.GetName() == "headless" || Root is null)
        {
            return null;
        }

        Image image = Root.GetViewport().GetTexture().GetImage();
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            System.IO.Directory.CreateDirectory(folder);
            image.SavePng(System.IO.Path.Combine(folder, $"{Name}-{label.Replace(' ', '-')}.png"));
        }

        return image;
    }

    /// <summary>The mean of red minus the mean of green and blue over a grid of the picture (0 with no picture).</summary>
    private static float Redness(Image? image)
    {
        if (image is null)
        {
            return float.NaN;
        }

        double sum = 0;
        int n = 0;
        int w = image.GetWidth();
        int h = image.GetHeight();
        for (int y = h / 16; y < h; y += h / 8)
        {
            for (int x = w / 16; x < w; x += w / 8)
            {
                Color c = image.GetPixel(x, y);
                sum += c.R - ((c.G + c.B) / 2f);
                n++;
            }
        }

        return n == 0 ? 0f : (float)(sum / n);
    }
}
