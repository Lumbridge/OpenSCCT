using System;
using System.Text.Json;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame;

/// <summary>Browser services survive leaving a round and opening the native menus.</summary>
public partial class BrowserRuntime : Node
{
    private double _reportAt, _flushAt;
    private PlayRoot? _play;
    private BrowserLesson? _lesson;
    private bool _grabOverlayReady;
    private double _simMs, _viewMs;
    private int _sampleFrames;
    private double _frameMaxMs;

    public override void _Ready()
    {
        BrowserPlatform.StartRound = kind =>
        {
            if (GetTree().CurrentScene is PlayRoot) StartRound(kind);
            else Online.AccountService.Get(GetTree()).PrepareAppearance(() => StartRound(kind));
        };
        BrowserPlatform.JoinOnline = (name, address, password, ticket, playerName) =>
            Online.OnlineService.Get(GetTree()).JoinBrowser(name, address, password, ticket, playerName);
        BrowserPlatform.LessonAction = action =>
        {
            if (action == "reset" && _lesson?.Kind != "vision-cancelling") StartRound(BrowserPlatform.Lesson);
            else if (action == "debug" && _play is not null) _play.ShowGrabArea(!_play.GrabAreaShown);
            else _lesson?.Action(action);
        };
    }

    private void StartRound(string kind)
    {
        if (kind is not ("spy" or "merc" or "vision-cancelling" or "spy-grab")) return;
        _lesson?.Dispose();
        _lesson = null;
        _grabOverlayReady = false;
        BrowserPlatform.Lesson = kind;
        BrowserPlatform.InputActivated = false;
        var settings = new PlaySettings
        {
            Map = "TestRange", MapRoots = ["/content/svmap"],
            Side = kind == "merc" ? PlaySide.Merc : PlaySide.Spy,
            Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            Bots = kind == "spy-grab" ? 1 : 0,
            PlayerName = BrowserPlatform.AccountName ?? Options.SettingsStore.Current.PlayerName,
            StartAt = kind is "vision-cancelling" or "spy-grab" ? BrowserLesson.Start(kind) : null,
        };
        _play = PlayRoot.Enter(GetTree(), new PlayConfig { Settings = settings, Audio = true });
        BrowserPlatform.CurrentPlay = _play;
        if (kind is "vision-cancelling" or "spy-grab" && _play.Session is { } session)
            _lesson = new BrowserLesson(session, kind);

    }

    public override void _ExitTree()
    {
        _lesson?.Dispose();
        BrowserPlatform.CurrentPlay = null;
        BrowserPlatform.StartRound = null;
        BrowserPlatform.JoinOnline = null;
        BrowserPlatform.LessonAction = null;
    }

    public override void _Process(double delta)
    {
        var current = GetTree().CurrentScene as PlayRoot;
        if (current != _play)
        {
            _lesson?.Dispose();
            _lesson = null;
            _play = current;
            BrowserPlatform.CurrentPlay = current;
            BrowserPlatform.InputActivated = false;
        }
        if (_play?.Menu?.IsOpen == true) BrowserPlatform.InputActivated = false;
        _flushAt += delta;
        _reportAt += delta;
        _simMs += _play?.Stats.LastSimMs ?? 0;
        _viewMs += _play?.Stats.LastViewMs ?? 0;
        _sampleFrames++;
        _frameMaxMs = Math.Max(_frameMaxMs, delta * 1000);
        if (_flushAt >= 2)
        {
            _flushAt = 0;
            BrowserPlatform.FlushSettings?.Invoke();
        }
        if (_reportAt < 0.2) return;
        _reportAt = 0;
        if (_lesson is { Kind: "spy-grab", Ready: true } && _play is not null)
        {
            if (!_grabOverlayReady) _grabOverlayReady = _play.ShowGrabArea(true);
            _lesson.Debug = _play.GrabAreaShown;
        }
        if (_lesson is { Kind: "vision-cancelling", Ready: true } && _play is not null &&
            _play.GetNodeOrNull<Node3D>("ReturnPortal") is null)
            _play.AddChild(new OpenScctGame.Play.BrowserReturnPortal());
        var pawn = _play?.Session?.LocalPawn;
        var weapon = pawn?.MyWeapon as OpenScct.Game.SBase.SWeaponDefense;
        string state = JsonSerializer.Serialize(new
        {
            driver = RenderingServer.GetCurrentRenderingDriverName(),
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            loaded = _play?.IsLoaded ?? false,
            accountConflict = GetTree().Root.GetNodeOrNull<Online.AccountService>(Online.AccountService.NodeName)?.AppearanceConflict == true,
            error = _play?.LoadError,
            ticks = _play?.Session?.TicksRun ?? 0,
            map = _play?.Session?.Settings.Map,
            vision = _play?.View?.RendMap,
            scoreboardSequence = _play?.Session?.Client.Scoreboard?.Sequence,
            scoreboardRows = _play?.Session?.Client.Scoreboard?.Rows.Count,
            pawn = pawn is not null,
            x = pawn?.Location.X, y = pawn?.Location.Y, z = pawn?.Location.Z,
            heat = pawn?.HeatIntensity, heatOpacity = pawn?.HeatOpacity,
            postError = _play?.View?.Effect.Error,
            compositor = _play?.View?.Camera.Compositor is not null,
            controls = _play?.Controls?.Enabled,
            requireCapture = _play?.Controls?.RequireCapture,
            focused = GetWindow().HasFocus(),
            captured = Pointer.IsCaptured,
            menu = _play?.Menu?.IsOpen,
            inputActive = BrowserPlatform.InputActivated,
            audio = _play?.Audio?.Sound?.StartCount ?? 0,
            voices = _play?.Audio?.Sound?.ActiveVoices ?? 0,
            weapon = weapon?.Mesh.Path,
            weaponDraw = weapon?.bDrawWeapon,
            weaponLoaded = pawn?.WeaponLoaded,
            shots = weapon?.ShotCounter,
            firstPersonShown = _play?.FirstPerson?.Shown,
            firstPersonModel = _play?.FirstPerson?.Model is not null,
            behindView = _play?.Session?.LocalController?.bBehindView,
            playerName = Options.SettingsStore.Current.PlayerName,
            invertY = Options.SettingsStore.Current.InvertY,
            fps = Engine.GetFramesPerSecond(),
            frameMaxMs = _frameMaxMs,
            simMs = _simMs / _sampleFrames,
            viewMs = _viewMs / _sampleFrames,
            processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,
            physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000,
            renderSetupMs = RenderingServer.GetFrameSetupTimeCpu(),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            drawPipelines = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw),
            meshPipelines = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh),
            surfacePipelines = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface),
            renderObjects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
            primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
            managedMb = GC.GetTotalMemory(false) / 1048576,
            lesson = _lesson is null ? null : new
            {
                kind = _lesson.Kind, ready = _lesson.Ready, feedback = _lesson.Feedback,
                state = _lesson.State, vision = _lesson.Vision, distance = _lesson.Distance,
                moving = _lesson.Moving, debug = _lesson.Debug, attempts = _lesson.Attempts,
                grabs = _lesson.Grabs, recoveryMs = _lesson.RecoveryMs,
                baselineMs = _lesson.BaselineMs, bestMs = _lesson.BestMs, history = _lesson.History,
                returns = _lesson.Returns,
            },
        });
        BrowserPlatform.ReportState?.Invoke(state);
        _simMs = _viewMs = 0;
        _sampleFrames = 0;
        _frameMaxMs = 0;
    }

}
