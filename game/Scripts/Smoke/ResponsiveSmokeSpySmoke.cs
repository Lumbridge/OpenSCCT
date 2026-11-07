using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.UI;
using PlayRules = OpenScctGame.Play.Session.PlayRules;

namespace OpenScctGame.Smoke;

/// <summary>
/// Actual spy smoke deployment: PlayInput quick-use and held/released alternate fire, a real projectile and its
/// collision/fuse, the ordinary spy camera, and Practice rules. No projectile, explosion or camera is fabricated.
/// SVM_SPY_SMOKE_CASES selects comma-separated case names; SVM_SPY_SMOKE_QUALITY selects Low/Medium/High;
/// SVM_SPY_SMOKE_FIRST_PERSON=1 uses the player's perspective toggle; SVM_PLAY_SHOTS selects PNG/CSV output.
/// SVM_SPY_SMOKE_PERF=1 records buffered timing without image readback; SVM_SPY_SMOKE_LEGACY=1 compares legacy smoke.
/// </summary>
public sealed class ResponsiveSmokeSpySmoke : SmokeTest
{
    public const string TestName = "responsive-smoke-spy";
    public override string Name => TestName;
    private sealed record Scenario(string Name, FVector Feet, int Yaw, int Pitch, bool Launcher, bool DuringDraw = false,
        int? RetreatYaw = null, int HoldTicks = 20, bool Stationary = false);
    private static readonly Scenario[] AllScenarios =
    [
        new("floor-quick", new FVector(3500, 3000, 0), 0, 0, false),
        new("floor-launcher", new FVector(3500, 3000, 0), 0, -6000, true),
        new("floor-launcher-far", new FVector(3500, 3000, 0), 0, 0, true, HoldTicks: 40, Stationary: true),
        new("floor-draw-launcher", new FVector(3500, 3000, 0), 0, -6000, true, true),
        new("wall-quick", new FVector(80, 3072, 0), 0, 0, false, RetreatYaw: 32768),
        new("wall-launcher", new FVector(210, 3072, 0), 32768, -4000, true),
        // Interior southwest corner and east doorway of TestRange's Gradient room (tools/testmap/layout.py).
        new("corner-quick", new FVector(310, 1590, 0), 8192, 0, false, RetreatYaw: 40960),
        new("corner-launcher", new FVector(580, 1792, 0), 40960, -4000, true),
        new("door-quick", new FVector(2320, 1792, 0), 0, 0, false),
        new("door-launcher", new FVector(2080, 1792, 0), 0, -2200, true),
    ];
    // Screenshot reproduction at the south mouth of SplitShaft192. Its 512 uu walls begin at y = 5632,
    // only about 90 uu behind the grenade, so visible wall edges test shallow sightlines through the cloud.
    // Opt in by name; preserve the established ten-case default validation matrix.
    private static readonly Scenario[] OptionalScenarios =
    [
        new("shaft-mouth-quick", new FVector(4608, 5532, 0), 16384, 0, false),
        new("shaft-mouth-launcher", new FVector(4608, 5310, 0), 16384, -6000, true, Stationary: true),
        new("shaft-inside-quick", new FVector(4608, 5740, 0), 16384, 0, false),
        new("shaft-inside-launcher", new FVector(4608, 5520, 0), 16384, -6000, true, Stationary: true),
        new("perf-repeat-quick", new FVector(3500, 3000, 0), 0, 0, false),
    ];
    private readonly List<Scenario> _scenarios = new();
    private readonly List<string> _failures = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private PlayRoot? _root;
    private ResponsiveSmokeQuality _oldQuality;
    private readonly bool _firstPerson = System.Environment.GetEnvironmentVariable("SVM_SPY_SMOKE_FIRST_PERSON") == "1";
    private readonly bool _perf = System.Environment.GetEnvironmentVariable("SVM_SPY_SMOKE_PERF") == "1";
    private readonly bool _legacy = System.Environment.GetEnvironmentVariable("SVM_SPY_SMOKE_LEGACY") == "1";
    private readonly List<PerfSample> _perfSamples = [];
    private readonly List<CpuPerfSample> _cpuPerfSamples = [];
    private readonly StringBuilder _perfEvents = new();
    private long _perfLastStamp, _perfLastTicks, _perfLastAllocated;
    private int _perfGc0, _perfGc1, _perfGc2;
    private double _perfLastPauseMs, _perfLastPipelines, _nextDriverTime;
    private double _pendingValidationMs;
    private int _oldFpsLimit, _oldMaxFps;
    private float _oldRenderScale, _oldViewportScale;
    private bool _oldVSync, _perfSettingsSaved, _perfViewInventoryRecorded;
    private DisplayServer.VSyncMode _oldVSyncMode;
    private ResponsiveSmokeWorld? _diagnosticServerWorld, _diagnosticClientWorld;
    private ResponsiveSmokeReplication? _diagnosticServerReplication, _diagnosticClientReplication;
    private float _popTime = float.NaN;
    private SSmokeEmitter? _clientEmitter;
    private string? _oldPerspectiveBehavior;
    private string? _folder;
    private int _warmup, _case = -1, _stage, _ticks, _captures, _teardown;
    private int _growthCapture;
    private static readonly float[] GrowthCaptureAges = [.1f, .2f, .4f, .6f];
    private SProjSmoke? _projectile;
    private SSmokeEmitter? _emitter;
    private ResponsiveSmokeField? _field;
    private FVector _lastProjectile;
    private FRotator _aim;
    private bool _clientFieldSeen, _readyRequested, _shockerChecked;
    private int _muzzleBefore, _initialCharge;
    private int _drawAltTick = -1, _drawHandTick = -1, _drawLaunchTick = -1;
    private float _floorDensity, _headDensity, _headTransmission = 1;
    private SmokeResult? _finished;
    private Scenario Current => _scenarios[_case];
    private readonly record struct PerfSample(int Frame, int Case, int Stage, int DriverTick,
        double Elapsed, double FrameMs, double SimMs, double ViewMs, long Ticks, float Age, int Clouds,
        bool Practice, int Charge, double ViewportGpu, double PrefixGpu, double FlowGpu, double UploadMs,
        long VolumeBytes, long PrefixBytes, long FlowBytes, long FieldAllocations, long PrefixAllocations,
        long FlowAllocations, int Gc0, int Gc1, int Gc2, double PauseMs, long AllocatedBytes,
        double PipelineCompilations, int Width, int Height, int BoundViews, int ActiveViews,
        int CacheViews, long PrefixVoxels, int MaxFps, int VSync, float RenderScale, double ValidationMs,
        int GrowthUploadBytes, long GrowthUploads, double FlowQueueMs, double FieldMs, double GrowthMs,
        double LightingMs, double FramePackingMs, double FrameUploadMs, double PrefixQueueMs, double ProfiledMs);
    private readonly record struct CpuPerfSample(int Frame, ResponsiveSmokeWorldDiagnostics ServerWorld,
        ResponsiveSmokeWorldDiagnostics ClientWorld, ResponsiveSmokeReplicationDiagnostics ServerReplication,
        ResponsiveSmokeReplicationDiagnostics ClientReplication);

    public override SmokeResult Step(SmokeContext context)
    {
        if (_finished is { } finished) return ++_teardown < 6 ? SmokeResult.Running : finished;
        if (DisplayServer.GetName() == "headless") return Finish(SmokeResult.Fail("requires a renderer and TestRange assets"));
        if (!_legacy && ResponsiveSmokeRenderer.UnsupportedReason is { } unsupported) return Finish(SmokeResult.Fail(unsupported));
        if (_root is null)
        {
            string? filter = System.Environment.GetEnvironmentVariable("SVM_SPY_SMOKE_CASES");
            if (_perf && string.IsNullOrWhiteSpace(filter)) filter = "floor-quick,perf-repeat-quick";
            var names = new HashSet<string>((filter ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
            foreach (Scenario scenario in AllScenarios) if (names.Count == 0 || names.Contains(scenario.Name)) _scenarios.Add(scenario);
            foreach (Scenario scenario in OptionalScenarios) if (names.Contains(scenario.Name)) _scenarios.Add(scenario);
            if (_scenarios.Count == 0) return Finish(SmokeResult.Fail("SVM_SPY_SMOKE_CASES matched no cases"));
            _oldQuality = SettingsStore.Current.SmokeQuality;
            string? quality = System.Environment.GetEnvironmentVariable("SVM_SPY_SMOKE_QUALITY");
            if (!string.IsNullOrWhiteSpace(quality) && (!Enum.TryParse(quality,true,out ResponsiveSmokeQuality parsed) || !Enum.IsDefined(parsed)))
                return Finish(SmokeResult.Fail("SVM_SPY_SMOKE_QUALITY must be Low, Medium or High"));
            SettingsStore.Current.SmokeQuality = Enum.TryParse(quality,true,out ResponsiveSmokeQuality selected) ? selected : ResponsiveSmokeQuality.Medium;
            _oldPerspectiveBehavior = SettingsStore.Current.SpyKbm.PerspectiveBehavior;
            SettingsStore.Current.SpyKbm.PerspectiveBehavior = "Toggle";
            if (_perf)
            {
                _oldFpsLimit = SettingsStore.Current.FpsLimit; _oldVSync = SettingsStore.Current.VSync;
                _oldMaxFps = Engine.MaxFps; _oldVSyncMode = DisplayServer.WindowGetVsyncMode();
                _oldRenderScale = SettingsStore.Current.RenderScale; _oldViewportScale = context.Tree.Root.Scaling3DScale;
                _perfSettingsSaved = true;
                SettingsStore.Current.FpsLimit = DisplayFrameCap.Unlimited;
                SettingsStore.Current.VSync = false;
                SettingsStore.Current.RenderScale = 1;
                context.Tree.Root.Scaling3DScale = 1;
                Engine.MaxFps = 0;
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            }
            PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with
            {
                Settings = launcher.Settings with
                {
                    Map = "TestRange", Side = PlaySide.Spy, Mode = "Story", Rules = PlayRules.PlayLevel,
                    // Practice must come from the same TestRange launch normalization as ordinary local play.
                    HostMode = PlayHostMode.Inline, Bots = 0, ResponsiveSmoke = !_legacy,
                },
                Render = true, Hud = true, Audio = true, TicksPerFrame = _perf ? 0 : 1, Pilot = null, CaptureFrameHistory = !_perf,
            });
            context.Tree.Root.AddChild(_root);
            _folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ??
                Path.Combine(RenderSmokeArgs.RepoRoot, "local", "responsive-smoke", "spy");
            Directory.CreateDirectory(_folder);
            if (!_perf) File.WriteAllText(Path.Combine(_folder, "frames.csv"),
                "frame,case,quality,first_person,stage,tick,seconds,projectile_seen,projectile_x,projectile_y,projectile_z,emitter_x,emitter_y,emitter_z,field_center_x,field_center_y,field_center_z,cloud_age,floor_z,floor_sample_z,head_z,server_floor_density,server_head_density,client_head_density,head_transmission,carves,pawn_x,pawn_y,pawn_z,camera_x,camera_y,camera_z,weapon_loaded,muzzle_events,charge,practice,draw_alt_tick,draw_hand_tick,draw_launch_tick,gpu_surface_ms,gpu_smoke_ms,baffle_left_T,baffle_right_T\n");
            if (!_perf) File.WriteAllText(Path.Combine(_folder, "growth.csv"),
                "case,target_age,server_age,client_age,client_fields,server_head,client_head,server_200,client_200,server_250,client_250,image\n");
            if (_perf)
            {
                _perfSamples.EnsureCapacity(32768);
                _cpuPerfSamples.EnsureCapacity(32768);
                RenderingServer.ViewportSetMeasureRenderTime(_root.GetViewport().GetViewportRid(),true);
                _perfLastStamp = Stopwatch.GetTimestamp();
                _perfLastAllocated = GC.GetTotalAllocatedBytes(false);
                _perfLastPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
                _perfGc0 = GC.CollectionCount(0); _perfGc1 = GC.CollectionCount(1); _perfGc2 = GC.CollectionCount(2);
                _perfLastPipelines = PipelineCount();
                GD.Print($"SVM_RESPONSIVE_SPY_PERF legacy={_legacy} real_clock=true driver_max_hz=60 readbacks=0 buffered=true cases={_scenarios.Count}");
            }
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } loadError) return Finish(SmokeResult.Fail(loadError));
        if (!_root.IsLoaded || _root.Session is not { } session) return SmokeResult.Running;
        if (ResponsiveSmokeRenderer.VolumeError is { } volumeError) return Finish(SmokeResult.Fail(volumeError));
        if (session.ServerFailure is { } serverFailure) return Finish(SmokeResult.Fail(serverFailure.ToString()));
        if (session.IsStopped || session.Client.State == EClientState.Disconnected)
            return Finish(SmokeResult.Fail("session stopped: " + session.Client.Connection?.CloseReason));
        if (session.LocalPawn is not SPawnAttaque pawn || session.LocalController is not { } pc ||
            session.ServerController is not { Pawn: SPawnAttaque serverPawn } || _root.Controls is not { } input)
            return SmokeResult.Running;
        ResponsiveSmokeWorld serverSmoke = ResponsiveSmokeWorld.For(session.Host.World.Level);
        ResponsiveSmokeWorld clientSmoke = ResponsiveSmokeWorld.For(session.Client.World.Level);
        if (_case >= 0) Track(session, serverSmoke, clientSmoke);
        if (_perf)
        {
            if (_diagnosticServerWorld is null)
            {
                _diagnosticServerWorld = serverSmoke; _diagnosticClientWorld = clientSmoke;
                serverSmoke.DiagnosticsEnabled = clientSmoke.DiagnosticsEnabled = true;
                _diagnosticServerReplication = ResponsiveSmokeReplication.For(session.Host.Driver);
                _diagnosticClientReplication = ResponsiveSmokeReplication.For(session.Client.Driver);
                _diagnosticServerReplication.DiagnosticsEnabled = _diagnosticClientReplication.DiagnosticsEnabled = true;
            }
            RecordPerformance(context,session,serverSmoke,pawn);
            // Real simulation keeps its ordinary wall clock. Inputs are sampled at most once per 60 Hz
            // driver step, with no catch-up burst that could press and release a button before a game tick.
            if (context.ElapsedSeconds < _nextDriverTime) return SmokeResult.Running;
            _nextDriverTime = context.ElapsedSeconds + 1.0/60.0;
        }
        if (++_warmup < 300 || (_case < 0 && !TestRange.Walking(pc))) return SmokeResult.Running;
        input.Enabled = true;
        input.RequireCapture = false;
        input.CapturePointer = false;
        if (!PracticeRules.IsOn(pawn) || !PracticeRules.IsOn(serverPawn)) return Finish(SmokeResult.Fail("Practice was not enabled on both machines"));
        if (ResponsiveSmokeWorld.IsEnabled(session.Host.World.Level) == _legacy) return Finish(SmokeResult.Fail("requested smoke option was not installed"));
        if (pawn.MyWeapon is not SStickyShocker) return Finish(SmokeResult.Fail("default spy Shocker was not equipped"));
        if (_case < 0) BeginCase(session);
        pc.SetViewRotation(_aim);
        pc.RendMap = RenderK.RendMapNormal;
        // This is deliberately the player's camera. A free camera or ViewOverride would miss the floor-smoke bug.
        if (session.ViewOverride is not null || pc.FreeCameraFlying != 0) return Finish(SmokeResult.Fail("normal spy view was replaced"));

        Track(session, serverSmoke, clientSmoke);
        if (!_perf)
        {
            CaptureGrowth(pawn, serverSmoke, clientSmoke);
            if (_ticks % 4 == 0) Capture(session, serverSmoke, clientSmoke);
        }
        switch (_stage)
        {
            case 0:
                // A fresh spy starts crouched. Quick throws stand automatically; an isolated launcher case
                // otherwise compared a crouched camera/head sample against the preceding standing quick case.
                if (_ticks == 2 && pawn.bIsCrouched) Key("crouch",true);
                if (_ticks == 4) Key("crouch",false);
                if (_ticks == 5) Key("slot1", true);
                if (_ticks == 8) Key("slot1", false);
                if (_ticks == 25 && pc.SpyFirstPersonActive != _firstPerson) Key("perspective",true);
                if (_ticks == 28) Key("perspective",false);
                if (_ticks == 45 && (pawn.WeaponLoaded == 2) != (Current.Launcher && !Current.DuringDraw)) { Key("ready", true); _readyRequested = true; }
                if (_ticks == 48) Key("ready", false);
                if (_ticks >= 90 && !pawn.bIsCrouched && pawn.SelectedGadget is SGadSmokeGrenade && pawn.WeaponLoaded == (Current.Launcher && !Current.DuringDraw ? 2 : 0))
                {
                    if (pc.SpyFirstPersonActive != _firstPerson)
                        return Finish(SmokeResult.Fail($"{Current.Name}: ordinary perspective input did not select {(_firstPerson ? "first" : "third")} person"));
                    if (_firstPerson && Current.Launcher && !Current.DuringDraw && _root.SpyFirstPerson is not { Available: true, Shown: true })
                        return Finish(SmokeResult.Fail($"{Current.Name}: armed first-person rig is not available and shown"));
                    _initialCharge = pawn.SelectedGadget.Charge;
                    _stage = 1; _ticks = -1;
                    Log("input-ready");
                }
                else if (_ticks > 240) return Finish(SmokeResult.Fail($"{Current.Name}: unable to prepare smoke, weapon={pawn.WeaponLoaded}, gadget={pawn.SelectedGadget?.GetType().Name}, state={pc.GetStateName()}, ready={_readyRequested}"));
                break;
            case 1:
                if (Current.DuringDraw)
                {
                    if (_ticks == 0) { Key("ready", true); Log("draw-input"); }
                    if (_ticks == 3) Key("ready", false);
                    if (_drawAltTick < 0 && pc.StateNode is SPlayerController.s_PlayerWalkingMario_T_0HTo2H && pawn.WeaponLoaded == 0)
                    { _drawAltTick = _ticks; Key("altfire", true); Log("altfire-during-draw-input"); }
                    if (_drawAltTick >= 0 && _ticks == _drawAltTick + 2) Key("altfire", false);
                    if (_drawAltTick >= 0 && _drawHandTick < 0 && pawn.WeaponLoaded != 0) { _drawHandTick = _ticks; Log("shocker-attached-to-hand"); }
                    if (_drawHandTick >= 0 && _ticks > _drawHandTick + 8 && _projectile is null)
                        return Finish(SmokeResult.Fail($"{Current.Name}: tapped grenade was not launched within eight ticks of the draw notify"));
                    if (_ticks > 90 && _drawAltTick < 0) return Finish(SmokeResult.Fail("missed the real spy draw-animation input window"));
                }
                else
                {
                    if (_ticks == 0) { Key(Current.Launcher ? "altfire" : "quick1", true); Log("deploy-input"); }
                    if (_ticks == (Current.Launcher ? Current.HoldTicks : 3)) Key(Current.Launcher ? "altfire" : "quick1", false);
                }
                if (_legacy ? _emitter is not null : _field is not null)
                {
                    if (_projectile is null) return Finish(SmokeResult.Fail($"{Current.Name}: cloud appeared without an observed real projectile"));
                    if (_projectile.QuickLaunch == Current.Launcher) return Finish(SmokeResult.Fail($"{Current.Name}: wrong grenade deployment path"));
                    ReleaseKeys();
                    _aim = new FRotator(0, Current.Yaw, 0);
                    _stage = 2; _ticks = -1;
                    Log("real-projectile-detonated");
                }
                else if (_ticks > 420) return Finish(SmokeResult.Fail($"{Current.Name}: real input produced no smoke, projectile={_projectile is not null}, state={pc.GetStateName()}, weapon={pawn.WeaponLoaded}, using={pc.bUsingNow}"));
                break;
            case 2:
                Observe(pawn, serverSmoke);
                if (_ticks >= 640)
                {
                    if (_legacy ? _clientEmitter is null : !_clientFieldSeen) _failures.Add(Current.Name + ": no replicated client smoke");
                    if (_legacy ? _emitter is not { bDeleteMe: true } || _clientEmitter is not { bDeleteMe: true }
                        : serverSmoke.Fields.Count != 0 || clientSmoke.Fields.Count != 0) _failures.Add(Current.Name + ": cloud did not expire after its ten-second lifetime");
                    if (pawn.SelectedGadget?.Charge != _initialCharge) _failures.Add(Current.Name + ": Practice consumed a smoke charge");
                    Log("complete");
                    if (_case + 1 == _scenarios.Count)
                        return Finish(_failures.Count == 0 ? SmokeResult.Pass($"{_scenarios.Count} actual spy deployments, Shocker fire, Practice and lifetime checks; legacy={_legacy}, perf={_perf}, {_captures} image frames, {_perfSamples.Count} timing frames in {_folder}") : SmokeResult.Fail(string.Join("; ", _failures)));
                    BeginCase(session);
                }
                break;
        }
        _ticks++;
        return SmokeResult.Running;
    }

    private void BeginCase(PlaySession session)
    {
        ReleaseKeys();
        _case++;
        _stage = _ticks = 0;
        _growthCapture = 0;
        _projectile = null; _emitter = null; _field = null; _lastProjectile = default;
        _clientEmitter = null; _popTime = float.NaN;
        _perfViewInventoryRecorded = false;
        _drawAltTick = _drawHandTick = _drawLaunchTick = -1;
        _clientFieldSeen = _readyRequested = _shockerChecked = false;
        _floorDensity = _headDensity = 0; _headTransmission = 1;
        _aim = new FRotator(Current.Pitch, Current.Yaw, 0);
        if (_case == 0 && _root is not null) RenderingServer.ViewportSetMeasureRenderTime(_root.GetViewport().GetViewportRid(),true);
        TestRange.Place(session, _ => session.ServerController, Current.Feet, Current.Yaw);
        Log("place-player");
    }

    private void Track(PlaySession session, ResponsiveSmokeWorld serverSmoke, ResponsiveSmokeWorld clientSmoke)
    {
        if (_projectile is null)
            foreach (SProjSmoke projectile in session.Host.World.Level.AllActors<SProjSmoke>())
                if (!projectile.bDeleteMe && ReferenceEquals(projectile.Instigator, session.ServerController?.Pawn))
                {
                    _projectile = projectile;
                    if (Current.DuringDraw)
                    {
                        _drawLaunchTick = _ticks;
                        // QuickChoiceTests.AnAimedGrenadeTapDuringDrawFiresAsSoonAsTheShockerIsInHand:
                        // the accepted early throw must happen after attach, while the draw is still in transit.
                        if (session.ServerController?.Pawn is not SPawnAttaque { WeaponLoaded: > 0, bWeaponTransit: true })
                            _failures.Add(Current.Name + ": grenade did not launch in the attached-but-still-drawing window");
                    }
                    Log("real-projectile-launched"); break;
                }
        if (_projectile is not null) _lastProjectile = _projectile.Location;
        if (_legacy && _emitter is null)
            foreach (SSmokeEmitter emitter in session.Host.World.Level.AllActors<SSmokeEmitter>())
                if (!emitter.bDeleteMe && ReferenceEquals(emitter.Instigator,session.ServerController?.Pawn)) { _emitter = emitter; break; }
        if (_legacy && _clientEmitter is null)
            foreach (SSmokeEmitter emitter in session.Client.World.Level.AllActors<SSmokeEmitter>())
                if (!emitter.bDeleteMe) { _clientEmitter = emitter; break; }
        if (_field is null)
            foreach (var (emitter, field) in serverSmoke.Fields)
                if (ReferenceEquals(emitter.Instigator, session.ServerController?.Pawn))
                { _emitter = emitter; _field = field; break; }
        _clientFieldSeen |= clientSmoke.Fields.Count > 0;
        if (_emitter is not null && float.IsNaN(_popTime)) _popTime = _field?.CreatedAt ?? serverSmoke.CurrentTime;
    }

    private void Observe(SPawnAttaque pawn, ResponsiveSmokeWorld serverSmoke)
    {
        if (!_legacy && _field is null) return;
        FVector head = HeadPoint(pawn);
        if (!_legacy && _ticks is >= 110 and <= 130)
        {
            long validationStart = _perf ? Stopwatch.GetTimestamp() : 0;
            _floorDensity = Math.Max(_floorDensity, serverSmoke.SampleDensity(new FVector(head.X, head.Y, Current.Feet.Z + 4)));
            _headDensity = Math.Max(_headDensity, serverSmoke.SampleDensity(head));
            _headTransmission = Math.Min(_headTransmission, HeadTransmission(serverSmoke, head));
            if (_perf) _pendingValidationMs += Stopwatch.GetElapsedTime(validationStart).TotalMilliseconds;
        }
        if (_ticks == 135)
        {
            // The open floor case catches a flat pancake cloud that never conceals a standing player. Confined
            // cases retain their numerical diagnostics: walls can legitimately block some of the test segment.
            if (!_legacy && (Current.Name.StartsWith("floor-", StringComparison.Ordinal) || Current.Name == "perf-repeat-quick") && (_floorDensity < .5f || _headDensity < .5f || _headTransmission > .1f))
                _failures.Add(string.Create(CultureInfo.InvariantCulture, $"{Current.Name}: floor/standing-height smoke too thin: floor={_floorDensity:F3}, head={_headDensity:F3}, T={_headTransmission:F3}"));
            Log("mature-at-standing-head-height");
        }
        if (_ticks == 140 && pawn.WeaponLoaded == 0) Key("ready", true);
        if (_ticks == 143) Key("ready", false);
        if (_ticks is >= 165 and <= 245 && !Current.Stationary)
        {
            FVector direction = head - (pawn.Location + new FVector(0, 0, pawn.EyeHeight));
            if (direction.SizeSquared() > 1) _aim = direction.Rotation();
        }
        if (_ticks == 175)
        {
            _muzzleBefore = _root?.Fx?.MuzzleMeshes ?? 0;
            if (_firstPerson && (_root?.Session?.LocalController?.SpyFirstPersonActive != true || _root.SpyFirstPerson is not { Available: true, Shown: true }))
                _failures.Add(Current.Name + ": first-person camera or armed rig was absent at the Shocker shot");
        }
        if (_ticks == 180) { Key("fire", true); Log("default-shocker-fire-input"); }
        if (_ticks == 183) Key("fire", false);
        if (_field is not null && _ticks is >= 180 and <= 245 && !_shockerChecked)
        {
            foreach (ResponsiveSmokeCarve carve in _field.Carves)
                if (carve.Kind == ResponsiveSmokeCarveKind.Bullet)
                { _failures.Add(Current.Name + ": default Shocker incorrectly cleared smoke"); _shockerChecked = true; break; }
        }
        if (_ticks == 245)
        {
            if ((_root?.Fx?.MuzzleMeshes ?? 0) <= _muzzleBefore)
                _failures.Add(Current.Name + ": Shocker fire input produced no rendered muzzle event");
            Log("shocker-does-not-clear-smoke");
        }
        // Back out with ordinary movement input to reveal the same floor cloud from outside. No observer camera.
        if (_ticks == 270 && !Current.Stationary) { _aim = new FRotator(0, Current.RetreatYaw ?? Current.Yaw, 0); Key("back", true); Log("back-away-input"); }
        if (_ticks == 450 && !Current.Stationary) { Key("back", false); Log("outside-view"); }
        if (_ticks == 480) Log("late-cloud");
    }

    private FVector HeadPoint(SPawnAttaque pawn)
    {
        FVector center = _field?.Center ?? _emitter?.Location ?? pawn.Location;
        return new FVector(center.X,center.Y,Current.Feet.Z+pawn.CollisionHeight+pawn.BaseEyeHeight);
    }

    private float HeadTransmission(ResponsiveSmokeWorld smoke, FVector point)
    {
        FVector direction = new FRotator(0, Current.Yaw, 0).Vector();
        float opticalDepth = 0;
        for (int i = -320; i < 320; i += 2) opticalDepth += smoke.SampleDensity(point + direction * (i + 1)) * 2;
        return MathF.Exp(-ResponsiveSmokeField.Extinction * opticalDepth);
    }

    private void Capture(PlaySession session, ResponsiveSmokeWorld serverSmoke, ResponsiveSmokeWorld clientSmoke)
    {
        if (_root is null || _folder is null || session.LocalPawn is not SPawnAttaque pawn) return;
        using Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_folder, $"frame-{_captures:D5}.png"));
        FVector center = _field?.Center ?? default;
        FVector emitter = _emitter?.Location ?? default;
        FVector head = HeadPoint(pawn);
        FVector floor = new(center.X, center.Y, Current.Feet.Z + 4);
        FVector camera = _root.Camera?.Current.Location ?? session.View.Location;
        bool shaft = Current.Name.StartsWith("shaft-", StringComparison.Ordinal);
        float leftBaffle = shaft ? SightlineTransmission(serverSmoke,camera,new FVector(4496,5632,head.Z)) : -1;
        float rightBaffle = shaft ? SightlineTransmission(serverSmoke,camera,new FVector(4720,5632,head.Z)) : -1;
        string row = string.Create(CultureInfo.InvariantCulture,
            $"{_captures},{Current.Name},{ResponsiveSmokeRenderer.Quality},{(session.LocalController?.SpyFirstPersonActive == true ? 1 : 0)},{_stage},{_ticks},{serverSmoke.CurrentTime:F4},{(_projectile is null ? 0 : 1)},{_lastProjectile.X:F3},{_lastProjectile.Y:F3},{_lastProjectile.Z:F3},{emitter.X:F3},{emitter.Y:F3},{emitter.Z:F3},{center.X:F3},{center.Y:F3},{center.Z:F3},{(_field is null ? -1 : serverSmoke.CurrentTime - _field.CreatedAt):F4},{Current.Feet.Z:F3},{floor.Z:F3},{head.Z:F3},{serverSmoke.SampleDensity(floor):F5},{serverSmoke.SampleDensity(head):F5},{clientSmoke.SampleDensity(head):F5},{HeadTransmission(serverSmoke, head):F5},{_field?.Carves.Count ?? 0},{pawn.Location.X:F3},{pawn.Location.Y:F3},{pawn.Location.Z:F3},{camera.X:F3},{camera.Y:F3},{camera.Z:F3},{pawn.WeaponLoaded},{_root.Fx?.MuzzleMeshes ?? 0},{pawn.SelectedGadget?.Charge ?? -1},{(PracticeRules.IsOn(pawn) ? 1 : 0)},{_drawAltTick},{_drawHandTick},{_drawLaunchTick},{RenderingServer.ViewportGetMeasuredRenderTimeGpu(_root.GetViewport().GetViewportRid()):F4},{ResponsiveSmokeRenderer.LastVolumeGpuMilliseconds:F4}\n");
        row = row.TrimEnd('\n') + string.Create(CultureInfo.InvariantCulture,$",{leftBaffle:F5},{rightBaffle:F5}\n");
        File.AppendAllText(Path.Combine(_folder, "frames.csv"), row);
        _captures++;
    }

    private void CaptureGrowth(SPawnAttaque pawn, ResponsiveSmokeWorld serverSmoke, ResponsiveSmokeWorld clientSmoke)
    {
        if (_root is null || _folder is null || _field is null || _growthCapture >= GrowthCaptureAges.Length) return;
        float age = serverSmoke.CurrentTime - _field.CreatedAt;
        float target = GrowthCaptureAges[_growthCapture];
        if (age + .0001f < target) return;
        string name = $"{Current.Name}-growth-{(int)(target * 1000):D4}.png";
        using Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_folder, name));
        FVector head = HeadPoint(pawn), direction = new FRotator(0, Current.Yaw, 0).Vector();
        FVector outer200 = head + direction * 200, outer250 = head + direction * 250;
        float clientAge = -1;
        foreach (ResponsiveSmokeField field in clientSmoke.Fields.Values)
            if ((field.Center - _field.Center).SizeSquared() < 1) { clientAge = clientSmoke.CurrentTime - field.CreatedAt; break; }
        string row = string.Create(CultureInfo.InvariantCulture,
            $"{Current.Name},{target:F3},{age:F4},{clientAge:F4},{clientSmoke.Fields.Count},{serverSmoke.SampleDensity(head):F5},{clientSmoke.SampleDensity(head):F5},{serverSmoke.SampleDensity(outer200):F5},{clientSmoke.SampleDensity(outer200):F5},{serverSmoke.SampleDensity(outer250):F5},{clientSmoke.SampleDensity(outer250):F5},{name}\n");
        File.AppendAllText(Path.Combine(_folder, "growth.csv"), row);
        _growthCapture++;
    }

    private static float SightlineTransmission(ResponsiveSmokeWorld smoke, FVector eye, FVector surface)
    {
        FVector delta = surface-eye;
        float length = delta.Size();
        if (length < .001f) return 1;
        FVector direction = delta/length;
        float depth = 0;
        for (float distance = 0; distance < length; distance += 1)
        {
            float step = Math.Min(1,length-distance);
            depth += smoke.SampleDensity(eye+direction*(distance+step*.5f))*step;
        }
        return MathF.Exp(-ResponsiveSmokeField.Extinction*depth);
    }

    private static double PipelineCount() =>
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);

    private void RecordPerformance(SmokeContext context, PlaySession session, ResponsiveSmokeWorld smoke, SPawnAttaque pawn)
    {
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetTotalAllocatedBytes(false);
        double pause = GC.GetTotalPauseDuration().TotalMilliseconds, pipelines = PipelineCount();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        PlayFrameStats stats = _root!.Stats;
        Vector2 size = _root.GetViewport().GetVisibleRect().Size;
        _perfSamples.Add(new PerfSample(context.Frame,_case,_case < 0 ? -1 : _stage,_ticks,
            context.ElapsedSeconds,_perfSamples.Count == 0 ? 0 : Stopwatch.GetElapsedTime(_perfLastStamp,now).TotalMilliseconds,
            stats.LastSimMs,stats.LastViewMs,stats.Ticks-_perfLastTicks,
            float.IsNaN(_popTime) ? -1 : smoke.CurrentTime-_popTime,
            _legacy ? _emitter is { bDeleteMe: false } ? 1 : 0 : smoke.Fields.Count,
            PracticeRules.IsOn(pawn),pawn.SelectedGadget?.Charge ?? -1,
            RenderingServer.ViewportGetMeasuredRenderTimeGpu(_root.GetViewport().GetViewportRid()),
            ResponsiveSmokeVolumeCache.LastGpuMilliseconds,ResponsiveSmokeVisualFlow.LastGpuMilliseconds,
            ResponsiveSmokeRenderer.LastUploadMilliseconds,ResponsiveSmokeRenderer.AllocatedFieldBytes,
            ResponsiveSmokeVolumeCache.AllocatedBytes,ResponsiveSmokeVisualFlow.AllocatedBytes,
            ResponsiveSmokeRenderer.FieldAllocationCount,ResponsiveSmokeVolumeCache.AllocationCount,
            ResponsiveSmokeVisualFlow.AllocationCount,gc0-_perfGc0,gc1-_perfGc1,gc2-_perfGc2,
            pause-_perfLastPauseMs,allocated-_perfLastAllocated,pipelines-_perfLastPipelines,(int)size.X,(int)size.Y,
            ResponsiveSmokeRenderer.LastBoundViewCount,ResponsiveSmokeRenderer.LastActiveViewCount,
            ResponsiveSmokeRenderer.LastCacheViewCount,ResponsiveSmokeRenderer.LastPrefixVoxelCount,
            Engine.MaxFps,(int)DisplayServer.WindowGetVsyncMode(),_root.GetViewport().Scaling3DScale,_pendingValidationMs,
            ResponsiveSmokeRenderer.LastGrowthUploadBytes,ResponsiveSmokeRenderer.GrowthUploadCount,
            ResponsiveSmokeRenderer.LastFlowQueueMilliseconds,ResponsiveSmokeRenderer.LastFieldMilliseconds,
            ResponsiveSmokeRenderer.LastGrowthMilliseconds,ResponsiveSmokeRenderer.LastLightingMilliseconds,
            ResponsiveSmokeRenderer.LastFramePackingMilliseconds,ResponsiveSmokeRenderer.LastFrameUploadMilliseconds,
            ResponsiveSmokeRenderer.LastPrefixQueueMilliseconds,ResponsiveSmokeRenderer.LastProfiledMilliseconds));
        _pendingValidationMs = 0;
        _cpuPerfSamples.Add(new(context.Frame,_diagnosticServerWorld!.Diagnostics,_diagnosticClientWorld!.Diagnostics,
            _diagnosticServerReplication!.Diagnostics,_diagnosticClientReplication!.Diagnostics));
        if (!_perfViewInventoryRecorded && !float.IsNaN(_popTime) && smoke.CurrentTime-_popTime > .1f)
        {
            _perfViewInventoryRecorded = true;
            _perfEvents.AppendLine("View inventory: " + ResponsiveSmokeRenderer.DescribeViews());
        }
        _perfLastStamp = now; _perfLastTicks = stats.Ticks; _perfLastAllocated = allocated;
        _perfGc0 = gc0; _perfGc1 = gc1; _perfGc2 = gc2;
        _perfLastPauseMs = pause; _perfLastPipelines = pipelines;
    }

    private void WritePerformance()
    {
        if (_folder is null || _perfSamples.Count == 0) return;
        using (var writer = new StreamWriter(Path.Combine(_folder,"perf.csv"),false,new UTF8Encoding(false),65536))
        {
            writer.WriteLine("frame,case,stage,driver_tick,wall_seconds,frame_ms,sim_ms,view_ms,simulation_ticks,smoke_age,clouds,practice,charge,gpu_viewport_ms,gpu_prefix_ms,gpu_flow_ms,upload_ms,reported_volume_bytes,prefix_bytes,flow_bytes,field_allocations,prefix_allocations,flow_allocations,gc0,gc1,gc2,gc_pause_ms,allocated_bytes,pipeline_compilations,viewport_width,viewport_height,bound_views,active_views,cache_views,prefix_voxels,max_fps,vsync,render_scale,harness_validation_ms,growth_upload_bytes,growth_uploads,flow_queue_ms,field_ms,growth_ms,lighting_ms,frame_packing_ms,frame_upload_ms,prefix_queue_ms,profiled_ms");
            foreach (PerfSample p in _perfSamples)
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{p.Frame},{(p.Case < 0 ? "warmup" : _scenarios[p.Case].Name)},{p.Stage},{p.DriverTick},{p.Elapsed:F6},{p.FrameMs:F5},{p.SimMs:F5},{p.ViewMs:F5},{p.Ticks},{p.Age:F5},{p.Clouds},{(p.Practice ? 1 : 0)},{p.Charge},{p.ViewportGpu:F5},{p.PrefixGpu:F5},{p.FlowGpu:F5},{p.UploadMs:F5},{p.VolumeBytes},{p.PrefixBytes},{p.FlowBytes},{p.FieldAllocations},{p.PrefixAllocations},{p.FlowAllocations},{p.Gc0},{p.Gc1},{p.Gc2},{p.PauseMs:F5},{p.AllocatedBytes},{p.PipelineCompilations:F0},{p.Width},{p.Height},{p.BoundViews},{p.ActiveViews},{p.CacheViews},{p.PrefixVoxels},{p.MaxFps},{p.VSync},{p.RenderScale:F3},{p.ValidationMs:F5},{p.GrowthUploadBytes},{p.GrowthUploads},{p.FlowQueueMs:F5},{p.FieldMs:F5},{p.GrowthMs:F5},{p.LightingMs:F5},{p.FramePackingMs:F5},{p.FrameUploadMs:F5},{p.PrefixQueueMs:F5},{p.ProfiledMs:F5}"));
        }
        WriteCpuPerformance();
        File.WriteAllText(Path.Combine(_folder,"events.txt"),_perfEvents.ToString());
        var summary = new StringBuilder();
        summary.AppendLine($"legacy={_legacy}; quality={ResponsiveSmokeRenderer.Quality}; real_clock=true; driver_max_hz=60; image_readbacks=0; samples={_perfSamples.Count}");
        summary.AppendLine("GPU columns are completed-frame timestamp measurements and can lag the CPU sample. Reported volume bytes already include flow storage; prefix bytes are separate. Input driver never catches up multiple presses in one rendered frame.");
        summary.AppendLine("The unchanged CPU opacity assertions are timed separately; frames containing that harness-only validation are excluded from summaries, and remain in the raw CSV.");
        for (int index = -1; index < _scenarios.Count; index++)
        {
            string name = index < 0 ? "warmup" : _scenarios[index].Name;
            var empty = new List<PerfSample>(); var birth = new List<PerfSample>(); var mature = new List<PerfSample>();
            foreach (PerfSample sample in _perfSamples)
            {
                if (sample.Case != index) continue;
                if (sample.ValidationMs > 0) continue;
                if (index < 0 && sample.Elapsed > _perfSamples[0].Elapsed+1 || sample.Age < 0 && sample.Stage == 0 && sample.DriverTick >= 30) empty.Add(sample);
                if (sample.Age is >= 0 and <= .5f) birth.Add(sample);
                if (sample.Age is >= 1 and <= 7 && sample.Clouds > 0) mature.Add(sample);
            }
            AppendPerformanceSummary(summary,name+" empty",empty);
            AppendPerformanceSummary(summary,name+" first_half_second",birth);
            AppendPerformanceSummary(summary,name+" mature_1_to_7_seconds",mature);
        }
        string text = summary.ToString();
        File.WriteAllText(Path.Combine(_folder,"perf-summary.txt"),text);
        GD.Print("SVM_RESPONSIVE_SPY_PERF " + text);
    }

    private static void AppendPerformanceSummary(StringBuilder output, string label, List<PerfSample> samples)
    {
        if (samples.Count == 0) return;
        var frames = new List<double>(samples.Count); var gpu = new List<double>(samples.Count);
        double sum = 0, sim = 0, view = 0, viewport = 0, prefix = 0, flow = 0, maximum = 0;
        foreach (PerfSample sample in samples)
        {
            frames.Add(sample.FrameMs); gpu.Add(sample.ViewportGpu+sample.PrefixGpu+sample.FlowGpu);
            sum += sample.FrameMs; sim += sample.SimMs; view += sample.ViewMs;
            viewport += sample.ViewportGpu; prefix += sample.PrefixGpu; flow += sample.FlowGpu;
            maximum = Math.Max(maximum,sample.FrameMs);
        }
        frames.Sort(); gpu.Sort(); int p95 = Math.Min(samples.Count-1,(int)(samples.Count*.95));
        output.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"{label}: frames={samples.Count}, frame_mean_ms={sum/samples.Count:F3}, frame_p95_ms={frames[p95]:F3}, frame_max_ms={maximum:F3}, sim_mean_ms={sim/samples.Count:F3}, view_mean_ms={view/samples.Count:F3}, gpu_p95_ms={gpu[p95]:F3}, viewport_mean_ms={viewport/samples.Count:F3}, prefix_mean_ms={prefix/samples.Count:F3}, flow_mean_ms={flow/samples.Count:F3}"));
    }

    private void WriteCpuPerformance()
    {
        using var writer = new StreamWriter(Path.Combine(_folder!,"perf-cpu.csv"),false,new UTF8Encoding(false),65536);
        writer.WriteLine("frame,machine,stage,calls,milliseconds,allocated_bytes");
        CpuPerfSample previous = default;
        foreach (CpuPerfSample sample in _cpuPerfSamples)
        {
            WriteWorld("server",sample.ServerWorld,previous.ServerWorld);
            WriteWorld("client",sample.ClientWorld,previous.ClientWorld);
            WriteReplication("server",sample.ServerReplication,previous.ServerReplication);
            WriteReplication("client",sample.ClientReplication,previous.ClientReplication);
            previous = sample;

            void WriteWorld(string machine, ResponsiveSmokeWorldDiagnostics current, ResponsiveSmokeWorldDiagnostics prior)
            {
                WriteStage(machine,"register",current.Register,prior.Register);
                WriteStage(machine,"geometry_refresh",current.GeometryRefresh,prior.GeometryRefresh);
                WriteStage(machine,"classify_front",current.ClassifyFront,prior.ClassifyFront);
                WriteStage(machine,"exposure",current.Exposure,prior.Exposure);
                WriteStage(machine,"capture",current.Capture,prior.Capture);
                WriteStage(machine,"apply",current.Apply,prior.Apply);
            }
            void WriteReplication(string machine, ResponsiveSmokeReplicationDiagnostics current, ResponsiveSmokeReplicationDiagnostics prior)
            {
                // Flush includes capture and encoding; its sub-stages must not be added to its total.
                WriteStage(machine,"replication_flush_inclusive",current.Flush,prior.Flush);
                WriteStage(machine,"baseline_encode",current.BaselineEncode,prior.BaselineEncode);
                WriteStage(machine,"delta_encode",current.DeltaEncode,prior.DeltaEncode);
                WriteStage(machine,"carve_encode",current.CarveEncode,prior.CarveEncode);
                WriteStage(machine,"baseline_decode",current.BaselineDecode,prior.BaselineDecode);
                WriteStage(machine,"delta_decode",current.DeltaDecode,prior.DeltaDecode);
                WriteStage(machine,"carve_decode",current.CarveDecode,prior.CarveDecode);
            }
            void WriteStage(string machine, string stage, ResponsiveSmokeStageDiagnostics current, ResponsiveSmokeStageDiagnostics prior)
            {
                long calls = current.Calls-prior.Calls;
                if (calls == 0) return; // Missing rows mean zero work in that stage/frame.
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.Frame},{machine},{stage},{calls},{current.Milliseconds-prior.Milliseconds:F6},{current.AllocatedBytes-prior.AllocatedBytes}"));
            }
        }
    }

    private void Key(string action, bool down)
    {
        if (down ? !_held.Add(action) : !_held.Remove(action)) return;
        _root?.Controls?.Key(ControlActions.VirtualPrefix + "smoke:" + action, down);
    }

    private void ReleaseKeys()
    {
        foreach (string action in _held) _root?.Controls?.Key(ControlActions.VirtualPrefix + "smoke:" + action, false);
        _held.Clear();
    }

    private void Log(string phase)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"SVM_RESPONSIVE_SPY case={Current.Name} quality={ResponsiveSmokeRenderer.Quality} first_person={_firstPerson} phase={phase} tick={_ticks} projectile={_lastProjectile} cloud={_field?.Center} floor_density={_floorDensity:F4} head_density={_headDensity:F4} head_T={_headTransmission:F4}");
        if (_perf) _perfEvents.AppendLine(line);
        else
        {
            GD.Print(line);
            if (_folder is not null) File.AppendAllText(Path.Combine(_folder, "events.txt"), line + "\n");
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        ReleaseKeys();
        if (_perf && _folder is not null) WritePerformance();
        if (_perfSettingsSaved)
        {
            SettingsStore.Current.FpsLimit = _oldFpsLimit; SettingsStore.Current.VSync = _oldVSync;
            SettingsStore.Current.RenderScale = _oldRenderScale;
            if (_root is not null) _root.GetViewport().Scaling3DScale = _oldViewportScale;
            Engine.MaxFps = _oldMaxFps; DisplayServer.WindowSetVsyncMode(_oldVSyncMode);
        }
        if (_oldPerspectiveBehavior is not null) SettingsStore.Current.SpyKbm.PerspectiveBehavior = _oldPerspectiveBehavior;
        if (_root is not null) { SettingsStore.Current.SmokeQuality = _oldQuality; _root.QueueFree(); }
        _finished = result;
        return SmokeResult.Running;
    }
}
