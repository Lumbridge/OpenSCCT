using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

namespace OpenScctGame.Smoke;

/// <summary>Telemetry appearance, graph geometry, issue lifecycle and live settings across viewports.</summary>
public sealed class TelemetrySmoke : SmokeTest
{
    public const string TestName = "telemetry";
    public override string Name => TestName;
    private SubViewport? _preview;
    private string? _output;
    private int _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_preview is not null)
            {
                if (++_frames < 8) return SmokeResult.Running;
                using Image image = _preview.GetTexture().GetImage();
                Error result = image.SavePng(_output!);
                _preview.Free();
                _preview = null;
                return result == Error.Ok ? SmokeResult.Pass("telemetry controls and graphs; preview=" + _output)
                    : SmokeResult.Fail("Preview save: " + result);
            }
            HudAssets assets = HudAssets.TryLoad(null, out string? error) ?? throw new InvalidOperationException(error);
            foreach (Vector2 screen in new[] { new Vector2(320, 240), new Vector2(800, 600), new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(3840, 2160) })
                foreach (string style in new[] { "Off", "Line", "Bars" })
                    Layout(assets, screen, style);
            Visibility(assets);
            CheckFrameMetrics(assets);
            EditorLayout(assets);
            EditorNetworkAnchor(assets);
            if (System.Environment.GetEnvironmentVariable("SVM_TELEMETRY_PREVIEW") is { Length: > 0 } output)
            {
                Preview(context, assets, output);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass("15 viewport/graph combinations, independent placement/scale/opacity, FPS and network issue hold/recovery, hidden sampling and reconnect");
        }
        catch (Exception ex) { return SmokeResult.Fail(ex.ToString()); }
    }

    private static HudFps Fps(HudAssets assets)
    {
        var fps = new HudFps();
        fps.Configure(assets);
        for (int i = 0; i < 360; i++) fps.Add(i == 320 ? 0.045f : 1f / (140f + i % 45));
        return fps;
    }

    private static NetReadout Healthy => new(true, 47000, 3000, 10000, 5000, 60, 0, 60, 0, 0, 60, BufferMs: 67, SnapshotAgeMs: 40);

    private static HudNet Network(HudAssets assets, Vector2 screen)
    {
        NetReadout sample = Healthy;
        var net = new HudNet { Source = () => sample, ShowLoss = true };
        net.Configure(assets);
        net.Draw(screen, 0f, true, false);
        for (int i = 0; i < 60; i++)
        {
            sample = sample with { RttMicros = (45 + i % 20) * 1000, JitterMicros = (2 + i % 7) * 1000,
                InBytes = sample.InBytes + 250, OutBytes = sample.OutBytes + 100, InPackets = sample.InPackets + 6, OutPackets = sample.OutPackets + 6 };
            net.Draw(screen, 0.1f, true, false);
        }
        return net;
    }

    private static void Layout(HudAssets assets, Vector2 screen, string style)
    {
        HudFps fps = Fps(assets);
        HudNet net = Network(assets, screen);
        fps.Settings.Graph = net.Settings.Graph = style;
        fps.Settings.ShowAverage = fps.Settings.ShowPeakFrameTime = true;
        foreach (float scale in new[] { 50f, 100f, 200f })
        {
            fps.Settings.Scale = net.Settings.Scale = scale;
            foreach (Vector2 corner in new[] { Vector2.Zero, new Vector2(100, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(50, 50) })
            {
                fps.Settings.PositionX = net.Settings.PositionX = corner.X;
                fps.Settings.PositionY = net.Settings.PositionY = corner.Y;
                fps.Draw(screen, true);
                net.Draw(screen, 0f, true, false);
                Bounded(assets, screen, fps.Bounds, fps.DrawList);
                Bounded(assets, screen, net.Bounds, net.DrawList);
            }
        }
        if (style != "Off")
        {
            Require(fps.DrawList.Items.Any(item => item.Kind == MagmaWidgetKind.Image && item.Path.Contains("Graph", StringComparison.Ordinal)), "FPS graph missing");
            Require(net.DrawList.Items.Any(item => item.Path.StartsWith("Net/Graph/", StringComparison.Ordinal)), "Network graph missing");
        }
        float fpsAlpha = fps.DrawList.Items.First(item => item.Kind == MagmaWidgetKind.Text).Color.A;
        float netAlpha = net.DrawList.Items.First(item => item.Kind == MagmaWidgetKind.Text).Color.A;
        float panelAlpha = net.DrawList.Find("Net/Panel")!.Colors[0].A;
        fps.Settings.Opacity = 35f;
        net.Settings.Opacity = 65f;
        fps.Draw(screen, true);
        net.Draw(screen, 0f, true, false);
        Require(Math.Abs(fps.DrawList.Items.First(item => item.Kind == MagmaWidgetKind.Text).Color.A - fpsAlpha * 0.35f) < 0.001f, "FPS text opacity");
        Require(Math.Abs(net.DrawList.Items.First(item => item.Kind == MagmaWidgetKind.Text).Color.A - netAlpha * 0.65f) < 0.001f, "Network text opacity");
        Require(Math.Abs(net.DrawList.Find("Net/Panel")!.Colors[0].A - panelAlpha * 0.65f) < 0.001f, "Network panel opacity");
        fps.Settings.Opacity = net.Settings.Opacity = 0f;
        fps.Draw(screen, true);
        net.Draw(screen, 0f, true, false);
        Require(fps.DrawList.Items.Count == 0 && net.DrawList.Items.Count == 0, "Zero opacity should leave no draw commands");
        fps.Settings.Opacity = net.Settings.Opacity = 100f;
        net.Settings.Detailed = net.Settings.ShowTraffic = false;
        net.Draw(screen, 0f, true, false);
        Require(net.DrawList.Find("Net/SYNC") is null && net.DrawList.Find("Net/TRAFFIC") is null && net.DrawList.Find("Net/LINK") is not null, "Compact metrics update immediately");
        net.Settings.ShowPingGraph = net.Settings.ShowJitterGraph = net.Settings.ShowLossGraph = false;
        net.Draw(screen, 0f, true, false);
        Require(!net.DrawList.Items.Any(item => item.Path.StartsWith("Net/Graph", StringComparison.Ordinal)), "Disabled graph series still draw");
        if (screen.Y >= 720f)
        {
            fps.Settings.PositionX = net.Settings.PositionX = 100f;
            fps.Settings.PositionY = net.Settings.PositionY = 0f;
            fps.Settings.Scale = net.Settings.Scale = 100f;
            fps.Draw(screen, true);
            net.FpsBounds = fps.Bounds;
            net.Draw(screen, 0f, true, true);
            Require(net.Bounds.Position.Y >= fps.Bounds.End.Y + 3.9f, "Default network placement overlaps FPS panel or graph");
            fps.Settings.PositionX = 0f;
            fps.Draw(screen, true);
            net.FpsBounds = fps.Bounds;
            net.Draw(screen, 0f, true, true);
            Require(Math.Abs(net.Bounds.Position.Y - 8f) < 0.1f, "Moved FPS still reserves space at the upper right");
        }
    }

    private static void EditorLayout(HudAssets assets)
    {
        foreach (Vector2 screen in new[] { new Vector2(800, 600), new Vector2(1280, 720), new Vector2(3440, 1440) })
        {
            HudFps fps = Fps(assets);
            HudNet net = Network(assets, screen);
            var layout = new HudElementSettings { X = -0.25f, Y = 0.18f, Scale = 0.8f, Opacity = 0.5f };
            fps.Settings.Opacity = net.Settings.Opacity = 50f;
            fps.Draw(screen, true, layout);
            net.Draw(screen, 0f, true, true, layout);
            Rect2 fpsBounds = fps.Bounds, netBounds = net.Bounds;
            Rect2 fpsText = fps.DrawList.Find("Fps")!.Rect, netText = net.DrawList.Find("Net/Header")!.Rect;
            Require(Math.Abs(fps.DrawList.Find("Fps")!.Color.A - 0.25f) < 0.001f &&
                Math.Abs(net.DrawList.Find("Net/Header")!.Color.A - 0.25f) < 0.001f,
                "Editor and legacy telemetry opacity must combine exactly once");
            for (int frame = 0; frame < 30; frame++)
            {
                fps.Draw(screen, true, layout);
                net.FpsBounds = frame % 2 == 0 ? new Rect2(screen.X - 400f, 8f, 392f, 300f) : default(Rect2);
                net.Draw(screen, 0f, true, frame % 2 == 0, layout);
            }
            Require(fps.Bounds == fpsBounds && net.Bounds == netBounds && fps.DrawList.Find("Fps")!.Rect == fpsText &&
                net.DrawList.Find("Net/Header")!.Rect == netText && Math.Abs(net.DrawList.Find("Net/Header")!.Color.A - 0.25f) < 0.001f,
                "Retained telemetry moved, rescaled or faded again; edited network must ignore FPS visibility and size");
            fps.Add(0.25f);
            fps.Add(0.0001f);
            fps.Draw(screen, true, layout);
            Require(fps.Bounds.Position == fpsBounds.Position && fps.Bounds.Size.X == fpsBounds.Size.X,
                "Changing FPS digit counts moved its edited anchor");
            layout.X -= 0.05f;
            fps.Draw(screen, true, layout);
            net.Draw(screen, 0f, true, true, layout);
            Require(Math.Abs(fps.Bounds.Position.X - fpsBounds.Position.X + screen.X * 0.05f) < 0.1f &&
                Math.Abs(net.Bounds.Position.X - netBounds.Position.X + screen.X * 0.05f) < 0.1f,
                "Mutating shared editor settings did not invalidate telemetry caches");
            fpsBounds = fps.Bounds;
            netBounds = net.Bounds;
            Rect2 netEditorBounds = net.EditorBounds;
            fps.Settings.DisplayMode = net.Settings.DisplayMode = "Issues";
            fps.Draw(screen, false, layout, editorPreview: true);
            net.Draw(screen, 0f, false, false, layout, editorPreview: true);
            Require(fps.Bounds == fpsBounds && net.Bounds == netBounds && fps.DrawList.Items.Count > 0 && net.DrawList.Items.Count > 0,
                "Editor preview must share live coordinates and show globally disabled or conditional telemetry");
            layout.Visible = false;
            fps.Draw(screen, false, layout, editorPreview: true);
            net.Draw(screen, 0f, false, false, layout, editorPreview: true);
            Require(fps.DrawList.Items.Count == 0 && net.DrawList.Items.Count == 0 && fps.Bounds == default && net.Bounds == default &&
                fps.EditorBounds == fpsBounds && net.EditorBounds == netEditorBounds && fps.EditorBaseBounds.Size.X > 0f && net.EditorBaseBounds.Size.X > 0f,
                "Hidden telemetry must retain selectable editor bounds without draw commands");
            layout.Visible = true;
            fps.Draw(screen, false, layout);
            net.Draw(screen, 0f, false, false, layout);
            Require(fps.DrawList.Items.Count == 0 && net.DrawList.Items.Count == 0,
                "Editor visibility must not override global telemetry display options in gameplay");
        }
    }

    private static void EditorNetworkAnchor(HudAssets assets)
    {
        foreach (Vector2 screen in new[] { new Vector2(800, 600), new Vector2(1280, 720), new Vector2(3440, 1440) })
        foreach (float positionY in new[] { 50f, 100f })
        {
            NetReadout sample = Healthy;
            var net = new HudNet { Source = () => sample, ShowLoss = true };
            net.Configure(assets);
            net.Settings.PositionX = 60f;
            net.Settings.PositionY = positionY;
            net.Settings.IssueHoldSeconds = 0f;
            var layout = new HudElementSettings { X = -0.1f, Y = 0.15f, Scale = 0.9f };
            net.Draw(screen, 0f, true, false, layout);
            net.Draw(screen, 1f, true, false, layout);
            Rect2 reference = net.EditorBounds, baseline = net.EditorBaseBounds;
            Rect2 header = net.DrawList.Find("Net/Header")!.Rect;
            float normalHeight = net.Bounds.Size.Y;
            sample = sample with
            {
                IsHost = true, Clients = 123, BestClientPingMs = 47, WorstClientPingMs = 999,
                RttMicros = 999000, JitterMicros = 100000, SnapshotAgeMs = 500,
                BufferUnderflows = 12, PredictionResets = 14, StateGaps = 10, AnimationGaps = 15,
                InBytes = sample.InBytes + 99999999, OutBytes = sample.OutBytes + 9999999,
                InPackets = sample.InPackets + 1000, OutPackets = sample.OutPackets + 1000, InPacketsLost = 100,
            };
            net.Draw(screen, 1f, true, true, layout);
            Require(net.Bounds.Size.Y > normalHeight && net.Text.Contains("ISSUE", StringComparison.Ordinal) && net.HostText.Length > 0,
                "Network anchor regression did not exercise changing issue, host and wrapped rows");
            Require(net.EditorBounds == reference && net.EditorBaseBounds == baseline && net.Bounds.Position == reference.Position &&
                net.DrawList.Find("Net/Header")!.Rect == header,
                "Edited network moved when runtime rows grew at a legacy middle/bottom position");
            net.Draw(screen, 0f, false, false, layout, editorPreview: true);
            Require(net.EditorBounds == reference && net.DrawList.Find("Net/Header")!.Rect == header,
                "Network preview and gameplay reference footprints diverge");
            sample = sample with { IsHost = false, Clients = 0, RttMicros = 47000, JitterMicros = 3000, SnapshotAgeMs = 40 };
            net.Draw(screen, 1f, true, false, layout);
            Require(!net.HasIssue && net.EditorBounds == reference && net.DrawList.Find("Net/Header")!.Rect == header,
                "Network moved when temporary rows disappeared");
        }
    }

    private static void Visibility(HudAssets assets)
    {
        Vector2 screen = new(1280, 720);
        var fps = new HudFps();
        fps.Configure(assets);
        fps.Settings.DisplayMode = "Issues";
        fps.Settings.IssueHoldSeconds = 1f;
        fps.Settings.FrameTimeThresholdMs = 30f;
        fps.Add(0.01f);
        fps.Draw(screen, true);
        Require(fps.DrawList.Items.Count == 0, "Healthy FPS shown in issues mode");
        fps.Add(0.05f);
        fps.Draw(screen, false);
        Require(fps.DrawList.Items.Count == 0, "Disabled FPS drawn during issue");
        fps.Draw(screen, true);
        Require(fps.DrawList.Items.Count > 0, "Hidden FPS sampling failed to retain an issue");
        fps.Add(0.01f);
        fps.Draw(screen, true);
        Require(fps.DrawList.Items.Count > 0, "FPS issue hold disappeared early");
        for (int i = 0; i < 150; i++) fps.Add(0.01f);
        fps.Draw(screen, true);
        Require(fps.DrawList.Items.Count == 0, "Recovered FPS issue did not hide");
        fps.Add(float.NaN);
        fps.Add(float.PositiveInfinity);
        fps.Add(0f);
        fps.Measure();
        Require(float.IsFinite(fps.Rates.Now), "Invalid frame time poisoned metrics");

        NetReadout? sample = Healthy;
        var net = new HudNet { Source = () => sample, ShowLoss = false };
        net.Configure(assets);
        net.Settings.DisplayMode = "Issues";
        net.Settings.IssueHoldSeconds = 1f;
        net.Draw(screen, 0f, true, false);
        net.Draw(screen, 1f, true, false);
        Require(net.DrawList.Items.Count == 0, "Healthy network shown in issues mode");
        sample = sample.Value with { RttMicros = 250000 };
        net.Draw(screen, 0.1f, true, false);
        Require(net.HasIssue && net.DrawList.Items.Count > 0 && net.Text.Contains("ping 250 ms", StringComparison.Ordinal), "Ping issue did not show current link data");
        sample = sample.Value with { RttMicros = 47000 };
        net.Draw(screen, 0.2f, true, false);
        Require(net.DrawList.Items.Count > 0, "Network issue hold disappeared early");
        net.Draw(screen, 1.2f, true, false);
        Require(net.DrawList.Items.Count == 0, "Recovered network issue did not hide");
        sample = sample.Value with { InPackets = 150, InPacketsLost = 10 };
        net.Draw(screen, 1.1f, true, false);
        Require(net.HasIssue && net.DrawList.Items.Count > 0 && net.DrawList.Find("Net/LOSS") is null, "Loss detection must work when loss text is disabled");
        net.Draw(screen, 0f, false, false);
        Require(net.DrawList.Items.Count == 0, "Never should hide a network issue");
        sample = null;
        net.Draw(screen, 0f, true, false);
        Require(!net.HasIssue && net.DrawList.Items.Count == 0 && net.Text.Length == 0, "Disconnect retained stale telemetry");
        sample = Healthy;
        net.Draw(screen, 1.1f, true, false);
        Require(!net.HasIssue && net.DrawList.Items.Count == 0, "Reconnect retained old issue");
        sample = sample.Value with { JitterMicros = 100000 };
        net.Settings.IssueHoldSeconds = 0f;
        net.Draw(screen, 0f, true, false);
        Require(net.HasIssue && net.Text.Contains("high jitter", StringComparison.Ordinal), "Jitter issue missing");
        net.Settings.JitterThresholdMs = 150f;
        net.Draw(screen, 0f, true, false);
        Require(!net.HasIssue && net.DrawList.Items.Count == 0, "Changed warning threshold did not apply immediately");
        sample = sample.Value with { SnapshotAgeMs = 500f };
        net.Draw(screen, 0f, true, false);
        Require(net.HasIssue && net.Text.Contains("stale snapshots", StringComparison.Ordinal), "Stale updates not detected");
        sample = Healthy with { InPackets = 0, OutPackets = 0, InBytes = 0, OutBytes = 0 };
        net.Draw(screen, 0f, true, false);
        Require(!net.HasIssue && net.Text.Length == 0 && net.DrawList.Items.Count == 0, "Counter reset retained stale warnings");
    }

    private static void CheckFrameMetrics(HudAssets assets)
    {
        Vector2 screen = new(1280, 720);
        var fps = new HudFps();
        fps.Configure(assets);
        for (int i = 0; i < 98; i++) fps.Add(0.01f);
        fps.Add(0.005f);
        fps.Add(0.05f);
        fps.Measure();
        Require(Math.Abs(fps.Rates.Now - 20f) < 0.001f && Math.Abs(fps.Rates.Low - 20f) < 0.001f &&
            Math.Abs(fps.Rates.High - 200f) < 0.001f, "FPS instantaneous and 1% rates must use frame durations");
        Require(Math.Abs(fps.AverageFps - 100f / 1.035f) < 0.01f && Math.Abs(fps.PeakFrameTimeMs - 50f) < 0.001f &&
            Math.Abs(fps.FrameTimeMs - 50f) < 0.001f, "FPS average or peak frame time is wrong");
        fps.Settings.ShowAverage = fps.Settings.ShowPeakFrameTime = true;
        fps.Draw(screen, true);
        Require(fps.DrawList.Find("Fps")!.Text.Contains("50.0 ms", StringComparison.Ordinal) &&
            fps.DrawList.Find("Fps/Percentiles")!.Text.Contains("1% low 20", StringComparison.Ordinal) &&
            fps.DrawList.Find("Fps/Extra")!.Text.Contains("peak 50.0 ms", StringComparison.Ordinal), "FPS selected metrics are missing from the readout");
        for (int i = 0; i < 600; i++) fps.Add(0.01f);
        fps.Measure();
        Require(Math.Abs(fps.Rates.Low - 100f) < 0.001f && Math.Abs(fps.Rates.High - 100f) < 0.001f &&
            Math.Abs(fps.PeakFrameTimeMs - 10f) < 0.001f, "Expired frame spikes remain in the five-second statistics");

        var history = new HudFps();
        history.Configure(assets);
        history.Settings.Graph = "Bars";
        for (int i = 0; i < 200; i++) history.Add(0.01f);
        history.Add(0.075f);
        for (int i = 0; i < 100; i++) history.Add(0.01f);
        history.Draw(screen, true);
        Require(history.DrawList.Find("Fps/Graph/Label")!.Text.Contains("0-75.0 ms", StringComparison.Ordinal),
            "Frame graph averaged away a short hitch");
        MagmaDrawItem plot = history.DrawList.Find("Fps/Graph/Background")!;
        float graphTop = plot.Points[0].Y;
        Require(history.DrawList.Items.Where(item => item.Path == "Fps/Graph").Any(item => item.Points.Min(point => point.Y) < graphTop + 2f),
            "Retained frame hitch does not reach the graph's advertised maximum");
        for (int i = 0; i < 200; i++) history.Add(0.01f);
        history.Settings.GraphSeconds = 2f;
        history.Draw(screen, true);
        Require(history.DrawList.Find("Fps/Graph/Label")!.Text.Contains("2s  0-33.3 ms", StringComparison.Ordinal),
            "Short frame graph includes samples older than its selected history");
        history.Settings.GraphSeconds = 5f;
        history.Draw(screen, true);
        Require(history.DrawList.Find("Fps/Graph/Label")!.Text.Contains("5s  0-75.0 ms", StringComparison.Ordinal),
            "Changing history duration discarded a still-recent hitch");
        for (int i = 0; i < 3100; i++) history.Add(0.01f);
        history.Settings.GraphSeconds = 30f;
        history.Draw(screen, true);
        Require(history.DrawList.Find("Fps/Graph/Label")!.Text.Contains("30s  0-33.3 ms", StringComparison.Ordinal),
            "Frame graph retained a hitch beyond its maximum history");

        foreach (string style in new[] { "Off", "Line", "Bars" })
        {
            history.Settings.Graph = style;
            history.Draw(screen, true);
            for (int frame = 0; frame < 20; frame++) history.Draw(screen, true);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 100; frame++) history.Draw(screen, true);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0, $"Cached FPS {style} readout allocated {allocated} bytes in 100 draws");
        }
    }

    private static void Bounded(HudAssets assets, Vector2 screen, Rect2 bounds, MagmaDrawList list)
    {
        Require(bounds.Size.X > 0f && bounds.Size.Y > 0f && bounds.Position.X >= -0.1f && bounds.Position.Y >= -0.1f &&
            bounds.End.X <= screen.X + 0.1f && bounds.End.Y <= screen.Y + 0.1f, $"Panel exceeds {screen}: {bounds}");
        bool Contains(Vector2 point) => point.X >= bounds.Position.X - 0.15f && point.Y >= bounds.Position.Y - 0.15f &&
            point.X <= bounds.End.X + 0.15f && point.Y <= bounds.End.Y + 0.15f;
        foreach (MagmaDrawItem item in list.Items)
        {
            if (item.Kind == MagmaWidgetKind.Text)
            {
                Require(Contains(item.Rect.Position) && Contains(item.Rect.End), $"Text rect exceeds panel: {item.Path} {screen}");
                MagmaFont font = assets.Font(item.Font)!;
                Require(font.Measure(item.Text, item.HeightPx, item.XScale, item.TrackingPx) <= item.Rect.Size.X + 0.2f, $"Text exceeds available width: {item.Path} {screen}");
            }
            else foreach (Vector2 point in item.Points) Require(Contains(point), $"Graph or panel exceeds bounds: {item.Path} {screen}");
        }
    }

    private void Preview(SmokeContext context, HudAssets assets, string output)
    {
        _output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        Vector2 screen = new(1280, 720);
        _preview = new SubViewport { Size = (Vector2I)screen, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_preview);
        _preview.AddChild(new ColorRect { Size = screen, Color = new Color(0.09f, 0.12f, 0.15f) });
        var canvas = new MagmaCanvas { Assets = assets };
        _preview.AddChild(canvas);
        HudFps fps = Fps(assets);
        fps.Settings.Graph = "Line";
        fps.Settings.ShowAverage = fps.Settings.ShowPeakFrameTime = true;
        fps.Draw(screen, true);
        HudNet net = Network(assets, screen);
        net.Settings.Graph = "Bars";
        net.Settings.PositionX = 0f;
        net.Settings.PositionY = 100f;
        net.Settings.Detailed = false;
        net.Settings.Opacity = 80f;
        net.Draw(screen, 0f, true, false);
        canvas.Submit(fps.DrawList.Items.Concat(net.DrawList.Items).ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
