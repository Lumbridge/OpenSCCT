using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using Environment = System.Environment;

namespace OpenScctGame.Smoke;

/// <summary>
/// Replays the actual play scene's live HUD through CanvasLayer-backed viewports every frame. Full-screen
/// copies are compared against regional copies with retained draw commands, including cold texture creation.
/// </summary>
public sealed class HudGameplayParitySmoke(bool merc = false) : SmokeTest
{
    public const string SpyName = "hud-gameplay-spy-parity";
    public const string MercName = "hud-gameplay-merc-parity";
    public override string Name => merc ? MercName : SpyName;
    private static readonly string[] Modes = ["full-copy-reference", "regional-copy"];
    private readonly List<SubViewport> _viewports = new();
    private readonly List<MagmaCanvas> _canvases = new();
    private PlayRoot? _play;
    private int _frames;
    private int _compared;
    private static readonly Vector2I Size = new(1920, 1080);

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("HUD gameplay parity requires a real renderer");
            if (context.ElapsedSeconds > 120) return Finish(SmokeResult.Fail("timed out waiting for live HUD"));
            if (_play is null)
            {
                _play = PlayRoot.Create(new PlayConfig
                {
                    Render = true, Hud = true, TicksPerFrame = 1,
                    Settings = new PlaySettings
                    {
                        Map = "SteeE", Mode = "Story", Side = merc ? PlaySide.Merc : PlaySide.Spy,
                        HostMode = PlayHostMode.Inline, MapRoots = [.. context.App.Options.MapRoots], Bots = 0,
                    },
                });
                context.Tree.Root.AddChild(_play);
                return SmokeResult.Running;
            }
            if (_play.LoadError is { } error) return Finish(SmokeResult.Fail(error));
            if (!_play.IsLoaded || _play.Session?.LocalPawn is null || _play.Hud is not { } hud || !hud.Snapshot.Valid || hud.Items.Count < 5)
                return SmokeResult.Running;

            if (_viewports.Count == 0)
            {
                hud.ScreenSize = Size;
                for (int i = 0; i < Modes.Length; i++)
                {
                    var viewport = new SubViewport { Size = Size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                    context.Tree.Root.AddChild(viewport);
                    viewport.AddChild(new ColorRect { Size = Size, Color = new Color(.08f, .13f, .19f) });
                    // Match the real HUD's canvas-layer boundary, including continuously submitted commands.
                    var layer = new CanvasLayer { Layer = 10 };
                    viewport.AddChild(layer);
                    var canvas = new MagmaCanvas
                    {
                        // Each variant loads its own textures on the first _Draw so the comparison covers
                        // initial asset loading as well as steady frames with retained native draw commands.
                        Assets = HudAssets.TryLoad(hud.Assets!.Root, out string? assetError) ?? throw new InvalidOperationException(assetError),
                        FullViewportCopies = i == 0,
                    };
                    layer.AddChild(canvas);
                    _viewports.Add(viewport);
                    _canvases.Add(canvas);
                }
            }

            if (_frames >= 3 && _frames % 12 == 0)
            {
                using Image reference = _viewports[0].GetTexture().GetImage();
                if (reference.IsEmpty()) return Finish(SmokeResult.Fail("reference HUD capture was empty"));
                byte[] expected = reference.GetData();
                for (int i = 1; i < _viewports.Count; i++)
                {
                    using Image actual = _viewports[i].GetTexture().GetImage();
                    if (!expected.AsSpan().SequenceEqual(actual.GetData()))
                    {
                        Save(context, "mismatch");
                        return Finish(SmokeResult.Fail($"{Modes[i]} differs from full-copy live {Name} at frame {_frames}, HUD items={hud.Items.Count}"));
                    }
                    _compared++;
                }
            }

            foreach (MagmaCanvas canvas in _canvases) canvas.Submit(hud.Items);
            if (++_frames < 360) return SmokeResult.Running;
            Save(context, "final");
            return Finish(SmokeResult.Pass($"{_compared} pixel-identical live HUD comparisons over {_frames} continuously submitted frames; cold assets, full/regional copies"));
        }
        catch (Exception error)
        {
            return Finish(SmokeResult.Fail(error.ToString()));
        }
    }

    private void Save(SmokeContext context, string suffix)
    {
        string folder = Environment.GetEnvironmentVariable("SVM_HUD_GAMEPLAY_OUT") ??
            Path.Combine(RenderSmokeArgs.RepoRoot, "local", "performance-overnight", "hud-gameplay");
        Directory.CreateDirectory(folder);
        for (int i = 0; i < _viewports.Count; i++)
        {
            using Image image = _viewports[i].GetTexture().GetImage();
            image.SavePng(Path.Combine(folder, $"{Name}-{suffix}-{Modes[i]}.png"));
        }
        using Image game = context.Tree.Root.GetTexture().GetImage();
        game.SavePng(Path.Combine(folder, $"{Name}-{suffix}-game.png"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        foreach (SubViewport viewport in _viewports) viewport.QueueFree();
        _viewports.Clear();
        _canvases.Clear();
        _play?.QueueFree();
        _play = null;
        return result;
    }
}
