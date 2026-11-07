using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Server.Hub;
using OpenScct.Server;
using OpenScct.Net;
using OpenScctGame.Play;
using OpenScctGame.Smoke;

namespace OpenScctGame.Hub;

public sealed partial class HubSmoke
{
    // Opt-in local benchmark: same game build and cameras, with either the current or legacy map assets.
    private static string? RenderReport => System.Environment.GetEnvironmentVariable("SVM_HUB_RENDER_REPORT");
    private readonly ConcurrentBag<HubClient> _crowd = [];
    private readonly ConcurrentQueue<Action> _visitorCommands = new();
    private readonly List<double> _renderFrames = [];
    private double _captureSince = -1, _lastFrame;
    private int _capture;
    private double _nextVisitor;
    private MapCatalog? _crowdMaps;

    private SmokeResult CaptureCrowd(SmokeContext context, HubService service)
    {
        if (DisplayServer.GetName() == "headless") return Fail("render capture requires a window");
        if (_crowd.Count < 48)
        {
            // Admit the next synthetic client after the preceding map load, while
            // the player's ordinary update loop continues to service its socket.
            if (_crowd.Any(c => c.LocalPlayer?.Pawn is null)) return SmokeResult.Running;
            if (context.ElapsedSeconds >= _nextVisitor)
            {
                int i = _crowd.Count;
                if (i % 12 == 0) GD.Print("SVM_HUB crowd loading " + (i + 1) + "/48");
                // Loading a visitor's full simulation can take seconds. Never make
                // the real client's render/network thread wait behind that work.
                _crowd.Add(new HubClient(LiteNetLibTransport.CreateClient(loopbackOnly: true), "127.0.0.1:" + Port, _clock.NowMicroseconds,
                        new HubClientSettings { Maps = _crowdMaps ??= MapCatalog.CreateDefault(), PlayerName = "Crowd" + i, Team = i % 2, Voice = false }, ownsTransport: true));
                _nextVisitor = context.ElapsedSeconds + .35;
            }
            return SmokeResult.Running;
        }
        if (_crowd.Any(c => c.LocalPlayer?.Pawn is null)) return SmokeResult.Running;
        if (_capture > 0)
        {
            var arrival = _server!.Activities!.Layout.TeleportDestinations[_capture == 1 ? "plaza" : "hard"];
            if (service.Client?.LocalPlayer?.Pawn is not { } pawn || (pawn.Location - arrival.Station).Size() > 1000)
                return SmokeResult.Running;
        }
        if (_captureSince < 0) { _captureSince = _lastFrame = context.ElapsedSeconds; }
        double now = context.ElapsedSeconds;
        if (now - _captureSince >= 3) _renderFrames.Add((now - _lastFrame) * 1000);
        _lastFrame = now;
        if (now - _captureSince < 8) return SmokeResult.Running;
        var ordered = _renderFrames.Order().ToArray();
        string label = _capture switch { 0 => "50 concentrated", 1 => "50 spread / plaza", _ => "50 spread / observatory" };
        string report = FormattableString.Invariant($"{label}: frame p50={ordered[ordered.Length / 2]:F2}ms p99={ordered[(int)(ordered.Length * .99)]:F2}ms; draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} objects={Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):F0} video={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:F0}MiB managed={GC.GetTotalMemory(false) / 1048576:F0}MiB load={(context.Tree.CurrentScene as PlayRoot)?.LoadSeconds:F2}s");
        File.AppendAllText(RenderReport!, report + "\n");
        GD.Print("SVM_HUB_RENDER " + report);
        if (context.Tree.CurrentScene is PlayRoot root)
            root.GetViewport().GetTexture().GetImage().SavePng(RenderReport! + "." + _capture + ".png");
        if (_capture == 2 || service.Client!.Play.Destinations.Count == 0)
        {
            _notes += " render=50 visitors";
            service.Leave(); _phase = 9;
            return SmokeResult.Running;
        }
        if (_capture == 0)
        {
            var destinations = service.Client.Play.Destinations.Keys.ToArray();
            _visitorCommands.Enqueue(() =>
            {
                int i = 0;
                foreach (var visitor in _crowd) visitor.Travel(destinations[i++ % destinations.Length]);
                _guest!.Travel("garden");
            });
            service.Client.Travel("plaza");
        }
        else service.Client.Travel("hard");
        _capture++;
        _captureSince = -1; _renderFrames.Clear();
        return SmokeResult.Running;
    }
}
