using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Diagnostic frame captures at every authored Cinema doorway panel; no performance acceptance claim.</summary>
public sealed class Ps2PortProfileSmoke : SmokeTest
{
    public const string TestName = "ps2-port-profile";
    public override string Name => TestName;
    private PlayRoot? _root;
    private SInterrupteur[]? _panels;
    private int _site, _frame, _settle, _teardown;
    private long _stamp;
    private KeyPilot? _keys;
    private readonly bool _hack = System.Environment.GetEnvironmentVariable("SVM_PS2_PROFILE_HACK") == "1";
    private readonly string _renderer = System.Environment.GetEnvironmentVariable("SVM_PS2_PROFILE_RENDERER") == "Fast" ? "Fast" : "Standard";
    private double _start;
    private double _menuAt = -1;
    private readonly HashSet<string> _states = [];
    private readonly List<object> _rows = [];
    private readonly List<double> _times = [], _sim = [], _view = [];
    private readonly List<double> _gpu = [];
    private Mover[] _doors = [];
    private int _doorKeys;
    private bool _doorsMoved;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass("Cinema diagnostic capture completed; inspect measurements") : SmokeResult.Running;
        if (_root is null)
        {
            Diagnostics.FpsBenchmark.Configure(context.Tree.Root);
            Options.SettingsStore.Current.Renderer = _renderer;
            Options.SettingsApplier.ApplyRendering(Options.SettingsStore.Current, context.Tree.Root);
            DisplayServer.WindowSetSize(new Vector2I(2560, 1440));
            context.Tree.Root.Size = new Vector2I(2560, 1440);
            RenderingServer.ViewportSetMeasureRenderTime(context.Tree.Root.GetViewportRid(), true);
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with { Settings = launch.Settings with { Map = "INI01P", Mode = "Neutralization", Side = PlaySide.Spy,
                Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline, Bots = 0 }, Audio = true, Render = true, Hud = true });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session?.LocalController?.SBPawn is null || _root.View is null) return SmokeResult.Running;
        var session = _root.Session;
        _keys ??= KeyPilot.Attach(_root); _keys.Update();
        if (!_keys.Ready) return SmokeResult.Running;
        if (session.TicksRun < 360) return SmokeResult.Running;
        _panels ??= session.Host.World.Level.Actors.OfType<SInterrupteur>().OrderBy(x => x.Location.Z).ThenBy(x => x.Location.X).ThenBy(x => x.Location.Y).ToArray();
        if (_site >= _panels.Length * (_hack ? 1 : 4))
        {
            string path = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? "local/ps2-port-profile";
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "cinema-profile.json"), JsonSerializer.Serialize(_rows, new JsonSerializerOptions { WriteIndented = true }));
            _root.QueueFree(); _teardown = 1; return SmokeResult.Running;
        }
        var panel = _panels[_hack ? _site : _site / 4];
        int yaw = _hack ? panel.Rotation.Yaw + 32768 : _site % 4 * 16384;
        if (_frame == 0 && _settle == 0)
        {
            FVector near = panel.Location + panel.Rotation.Vector() * (_hack ? 65 : 130);
            MovementNatives.Trace(session.ServerController!.SBPawn!, out FVector hit, out _, near - new FVector(0,0,300), near + new FVector(0,0,80), false);
            near.Z = hit.Z + session.ServerController.SBPawn!.CollisionHeight + 1;
            foreach (var pc in new[] {session.ServerController, session.LocalController!})
            {
                var pawn = pc.SBPawn!;
                bool moved = MovementNatives.Driver(pawn)?.FarMoveActor(pawn, near) ?? pawn.SetLocation(near);
                pawn.Velocity = pawn.Acceleration = default;
                pc.Rotation = pawn.Rotation = new FRotator(0, yaw, 0);
                GD.Print($"PS2_PROFILE_POSE panel={panel.ActorId} at={near} moved={moved}");
            }
            session.Host.Movement.EndpointOf(session.ServerController)?.Server?.ReinitPawn();
            _times.Clear(); _sim.Clear(); _view.Clear(); _gpu.Clear();
            _states.Clear(); _start = context.ElapsedSeconds;
            _menuAt = -1;
            _doors = session.Host.World.Level.Actors.OfType<Mover>().Where(m => m.Tag == panel.Event).ToArray();
            _doorKeys = 0; _doorsMoved = false;
            _keys.ReleaseAll();
        }
        // Leave the player's ordinary third-person camera, collision and visibility path active.
        long now = Stopwatch.GetTimestamp();
        if (++_settle <= 45) { _stamp = now; return SmokeResult.Running; }
        if (_hack && session.LocalController.SBPawn!.bIsCrouched && _settle == 46) _keys.Tap("bCrouch");
        if (_hack)
        {
            if (context.ElapsedSeconds - _start < 1.5) _keys.Press("BT_Up");
            else
            {
                _keys.Release("BT_Up");
                if (session.LocalController.IsInState("s_MenuInteraction") && _menuAt < 0)
                { _keys.Release("bUsing"); _menuAt = context.ElapsedSeconds; }
                else if (_menuAt < 0 || context.ElapsedSeconds - _menuAt > .2) _keys.Press("bUsing");
            }
        }
        _states.Add(session.LocalController.GetStateName().ToString());
        foreach (Mover door in _doors) { _doorKeys |= 1 << door.KeyNum; _doorsMoved |= door.bInterpolating; }
        _times.Add((now - _stamp) * 1000.0 / Stopwatch.Frequency); _stamp = now;
        _sim.Add(_root.Stats.LastSimMs); _view.Add(_root.Stats.LastViewMs);
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(context.Tree.Root.GetViewportRid()));
        ++_frame;
        if (_hack ? context.ElapsedSeconds - _start < 10 : _frame < 90) return SmokeResult.Running;
        double P95(List<double> values) { var sorted=values.Order().ToArray(); return sorted[(int)(sorted.Length*.95)]; }
        var row = new { panel = panel.ActorId.ToString(), at = panel.Location.ToString(), yaw, hack = _hack, renderer = _renderer, audio = true, states = _states.ToArray(),
            useEntryAt = panel.TriggerInteraction?.Location.ToString(), canUse = panel.TriggerInteraction?.CanUse(session.ServerController),
            entryState = panel.TriggerInteraction?.GetStateName().ToString(), pawnAt = session.ServerController!.SBPawn!.Location.ToString(),
            doorCount = _doors.Length, doorKeyBits = _doorKeys, doorsMoved = _doorsMoved,
            frames = _times.Count, meanMs = _times.Average(), p95Ms = P95(_times), maxMs = _times.Max(),
            slow16Ms = _times.Count(t => t > 16.7), slow33Ms = _times.Count(t => t > 33.4),
            simMeanMs = _sim.Average(), simP95Ms = P95(_sim), viewMeanMs = _view.Average(), viewP95Ms = P95(_view),
            gpuMeanMs = _gpu.Average(), gpuP95Ms = P95(_gpu), width = context.Tree.Root.Size.X, height = context.Tree.Root.Size.Y,
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) };
        _rows.Add(row); GD.Print("PS2_PROFILE " + JsonSerializer.Serialize(row));
        string shots = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? "local/ps2-port-profile";
        Directory.CreateDirectory(shots);
        using (Image image = context.Tree.Root.GetTexture().GetImage()) image.SavePng(Path.Combine(shots, $"panel-{_site}.png"));
        _keys.ReleaseAll();
        _site++; _frame = _settle = 0;
        return SmokeResult.Running;
    }
}
