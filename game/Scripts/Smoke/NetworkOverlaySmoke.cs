using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Smoke;

/// <summary>The network readout stays inside its compact panel at supported resolutions and large counters.</summary>
public sealed class NetworkOverlaySmoke : SmokeTest
{
    public const string TestName = "network-overlay";
    public override string Name => TestName;
    private SubViewport? _preview;
    private string? _output;
    private int _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_preview is not null)
        {
            if (++_frames < 8) return SmokeResult.Running;
            using Image image = _preview.GetTexture().GetImage();
            Error saved = image.SavePng(_output!);
            _preview.Free();
            _preview = null;
            return saved == Error.Ok ? SmokeResult.Pass("bounded network panel; preview=" + _output) : SmokeResult.Fail("Preview save: " + saved);
        }
        HudAssets? assets = HudAssets.TryLoad(null, out string? error);
        if (assets is null) return SmokeResult.Fail(error ?? "HUD assets missing");
        try
        {
            foreach (Vector2 size in new[] { new Vector2(320, 240), new Vector2(640, 480), new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(3440, 1440) })
            {
                Check(assets, size, false);
                Check(assets, size, true);
            }
            if (System.Environment.GetEnvironmentVariable("SVM_NETWORK_OVERLAY_PREVIEW") is { Length: > 0 } output)
            {
                MakePreview(context, assets, output);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass("12 normal/loss/host/large-counter layouts; all text inside the screen and compact panel; grouped metrics and smaller font");
        }
        catch (Exception ex) { return SmokeResult.Fail(ex.ToString()); }
    }

    private static HudNet Readout(HudAssets assets, Vector2 screen, bool stress)
    {
        NetReadout sample = new(true, 47000, 3000, 0, 0, 0, 0, 0, 0, 0, 60, BufferMs: 67);
        var net = new HudNet { Source = () => sample, ShowLoss = stress };
        net.Configure(assets);
        net.Draw(screen, 0f, true, true);
        sample = sample with
        {
            InBytes = stress ? 999999999 : 1946, OutBytes = stress ? 888888888 : 2355,
            InPackets = 60, OutPackets = 60, InPacketsLost = stress ? 3 : 0, OutPacketsLost = stress ? 2 : 0,
            Corrections = stress ? 120 : 0, CorrectionDistance = stress ? 99999 : 6.3f,
            BufferUnderflows = stress ? 999999 : 0, MovesReplayed = stress ? 9999999 : 0,
            AnimationGaps = stress ? 999999 : 0, StateGaps = stress ? 999999 : 0,
            PoseResets = stress ? 999999 : 0, PredictionResets = stress ? 999999 : 0,
            IsHost = stress, Clients = stress ? 12 : 0, BestClientPingMs = 27, WorstClientPingMs = 95,
        };
        net.Draw(screen, 1f, true, true);
        return net;
    }

    private static void Check(HudAssets assets, Vector2 screen, bool stress)
    {
        HudNet net = Readout(assets, screen, stress);
        MagmaDrawItem panel = net.DrawList.Find("Net/Panel") ?? throw new InvalidOperationException("Panel missing");
        Rect2 bounds = new(panel.Points[0], panel.Points[2] - panel.Points[0]);
        if (bounds.Position.X < 0 || bounds.Position.Y < 0 || bounds.End.X > screen.X || bounds.End.Y > screen.Y)
            throw new InvalidOperationException($"Panel exceeds {screen}: {bounds}");
        foreach (MagmaDrawItem item in net.DrawList.Items.Where(i => i.Kind == MagmaWidgetKind.Text))
        {
            MagmaFont font = assets.Font(item.Font)!;
            float measured = font.Measure(item.Text, item.HeightPx, item.XScale, item.TrackingPx);
            if (measured > item.Rect.Size.X + 0.1f || item.Rect.Position.X < bounds.Position.X || item.Rect.End.X > bounds.End.X + 0.1f || item.Rect.End.Y > bounds.End.Y + 0.1f)
                throw new InvalidOperationException($"{screen} {item.Path} exceeds its panel: {item.Text}");
        }
        if (!net.Text.Contains("jitter 3 ms", StringComparison.Ordinal) || !net.Text.Contains("down", StringComparison.Ordinal) || !net.Text.Contains("RESETS", StringComparison.Ordinal))
            throw new InvalidOperationException("Required metrics missing");
        if ((net.DrawList.Find("Net/LOSS") is not null) != stress || (net.DrawList.Find("Net/HOST") is not null) != stress)
            throw new InvalidOperationException("Optional loss/host rows are wrong");
        for (int frame = 0; frame < 10; frame++) net.Draw(screen, 0f, true, true);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 100; frame++) net.Draw(screen, 0f, true, true);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (allocated != 0) throw new InvalidOperationException($"Stable network panel allocated {allocated} bytes in 100 draws");
        NetReadout next = net.Source!()!.Value with { RttMicros = 123000 };
        net.Source = () => next;
        net.Draw(screen, 1f, true, true);
        if (!net.DrawList.Items.Any(item => item.Text.Contains("ping 123 ms", StringComparison.Ordinal)))
            throw new InvalidOperationException("Fresh network sample did not invalidate cached rows");
        net.Draw(screen, 0f, true, false);
        if (net.DrawList.Find("Net/Panel")!.Points[0].Y != 8f)
            throw new InvalidOperationException("FPS visibility did not invalidate panel placement");
        Vector2 resized = screen + new Vector2(100f, 100f);
        net.Draw(resized, 0f, true, true);
        if (Math.Abs(net.DrawList.Find("Net/Panel")!.Points[2].X - (resized.X - 8f)) > 0.1f)
            throw new InvalidOperationException("Resize did not invalidate panel placement");
        net.Draw(screen, 0f, false, true);
        if (net.DrawList.Items.Count != 0) throw new InvalidOperationException("Hidden readout drew items");
        net.Source = () => null;
        net.Draw(screen, 0f, true, true);
        if (net.DrawList.Items.Count != 0 || net.Text.Length != 0) throw new InvalidOperationException("Disconnected readout retained stale metrics");
    }

    private void MakePreview(SmokeContext context, HudAssets assets, string output)
    {
        _output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        Vector2 size = new(1280, 720);
        _preview = new SubViewport { Size = (Vector2I)size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_preview);
        _preview.AddChild(new ColorRect { Color = new Color(0.12f, 0.15f, 0.18f), Size = size });
        var canvas = new MagmaCanvas { Assets = assets };
        _preview.AddChild(canvas);
        var fps = new HudFps();
        fps.Add(1f / 210f);
        fps.Draw(size, true);
        HudNet net = Readout(assets, size, false);
        canvas.Submit(fps.DrawList.Items.Concat(net.DrawList.Items).ToArray());
    }
}
