using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-vision</c>: the vision goggles through the default cycle key, as a player presses it. SteeE
/// offline with the Play Level rules and the listen server on its own thread; Tab is sent as a Godot key event
/// (<see cref="Input.ParseInputEvent"/>), read by <see cref="PlayInput"/> through the side's key layout
/// (<c>IK_Tab=SwitchHeadset 3</c>) and run on the local controller. Each press must move the goggles through the
/// team's cycle (spy: normal, night, thermal, normal; merc: normal, motion tracking, EMF, normal) and show it at
/// every stage: the client's SGoggle and RendMap, the play view's RendMap, the vision pass the post effect last drew,
/// and on the server the goggle head clip the client's action stream plays on the player's pawn (the mode itself is
/// local to the client, vision-goggles.md Network) and, for a spy, its EMF visibility.
/// </summary>
public sealed class PlayVisionSmoke : SmokeTest
{
    public const string TestName = "play-vision";

    private const int TeardownFrames = 4;

    /// <summary>Client ticks the pawn stands after it spawns before the first press (the spawn resets the goggles).</summary>
    private const int SettleTicks = 90;

    /// <summary>Client ticks one press may take to show everywhere (the goggle clip is about 1 s).</summary>
    private const int StepTimeoutTicks = 240;

    private readonly List<string> _report = new();
    private readonly string[] _sides;
    private int _sideIndex;
    private VisionLeg? _leg;
    private int _teardown = -1;
    private SmokeResult? _final;

    public PlayVisionSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_VISION_SIDES");
        _sides = string.IsNullOrWhiteSpace(only) ? ["spy", "merc"] : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            if (++_teardown < TeardownFrames)
            {
                return SmokeResult.Running;
            }

            _teardown = -1;
            if (_final is { } done)
            {
                return done;
            }
        }

        if (_leg is null)
        {
            _leg = new VisionLeg(context, _sides[_sideIndex] == "merc" ? PlaySide.Merc : PlaySide.Spy);
            return SmokeResult.Running;
        }

        SmokeResult? result = _leg.Step(context);
        if (result is null)
        {
            return SmokeResult.Running;
        }

        _leg.Dispose();
        _leg = null;
        _teardown = 0;
        if (result.Value.State == SmokeState.Failed)
        {
            _final = SmokeResult.Fail($"{_sides[_sideIndex]}: {result.Value.Detail} | done: {string.Join(" || ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        GD.Print($"SVM_SMOKE {TestName} {_sides[_sideIndex]} {result.Value.Detail}");
        if (++_sideIndex >= _sides.Length)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }

    /// <summary>What the server's copy of the local player's pawn showed since the step began (server thread writes).</summary>
    private sealed class ServerWatch
    {
        private readonly object _gate = new();
        private string _goggleClip = "";
        private bool _emf;
        private int _clipStarts;
        private string _lastSeq = "";
        private float _lastFrame;

        /// <summary>Called on the server thread before each world tick.</summary>
        public void Sample(SPlayerController? pc)
        {
            if (pc?.Pawn is not SBasePawn pawn)
            {
                return;
            }

            bool playing = pawn.PawnAnim.IsAnimating(SBasePawn.GOGGLECHANNEL);
            string seq = playing ? pawn.PawnAnim[SBasePawn.GOGGLECHANNEL].Sequence.ToString() : "";
            float frame = playing ? pawn.PawnAnim[SBasePawn.GOGGLECHANNEL].Frame : 0f;
            lock (_gate)
            {
                // A start: the channel begins playing, changes clip, or its frame goes back (the same clip again).
                if (playing && (seq != _lastSeq || frame < _lastFrame))
                {
                    _goggleClip = seq;
                    _clipStarts++;
                }

                _lastSeq = seq;
                _lastFrame = frame;
                _emf = pawn.bEMFIsOn;
            }
        }

        public (int ClipStarts, string Clip, bool Emf) Read()
        {
            lock (_gate)
            {
                return (_clipStarts, _goggleClip, _emf);
            }
        }
    }

    private sealed class VisionLeg : IDisposable
    {
        private readonly PlaySide _side;
        private readonly List<string> _results = new();
        private readonly ServerWatch _server = new();
        private readonly int[] _cycle;
        private PlayRoot? _root;
        private bool _hooked;
        private long _spawnTick = -1;
        private int _step;
        private int _frame;
        private long _stepStart;
        private int _clipStartsAtStep;
        private int _shots;
        private bool _tabDown;

        public VisionLeg(SmokeContext context, PlaySide side)
        {
            _side = side;
            _cycle = side == PlaySide.Merc
                ? [SGoggle.REN_MotionTrackingVision, SGoggle.REN_EMFVision, SGoggle.REN_DynLight]
                : [SGoggle.REN_NightVision, SGoggle.REN_ThermalVision, SGoggle.REN_DynLight];
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "SteeE",
                    Side = side,
                    Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Thread,
                    MapRoots = [.. context.App.Options.MapRoots],
                    Bots = 0,
                },
                Render = true,
                Hud = true,
                TicksPerFrame = 0,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
        }

        public SmokeResult? Step(SmokeContext context)
        {
            if (_root is null)
            {
                return SmokeResult.Fail("no play scene");
            }

            if (_root.LoadError is { } error)
            {
                return SmokeResult.Fail("play scene did not load: " + error);
            }

            if (!_root.IsLoaded || _root.Session is not { } session || _root.Controls is not { } controls)
            {
                return null;
            }

            if (session.ServerFailure is { } failure)
            {
                return SmokeResult.Fail("server failed: " + failure.Message);
            }

            if (session.LoginError is { } login)
            {
                return SmokeResult.Fail("login refused: " + login);
            }

            if (!_hooked)
            {
                session.Host.BeforeWorldTick += _ => _server.Sample(session.ServerController);
                controls.Enabled = true;
                controls.CapturePointer = false;
                controls.RequireCapture = DisplayServer.GetName() != "headless";
                if (controls.RequireCapture)
                {
                    OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
                }

                _hooked = true;
            }

            SPlayerController? pc = session.LocalController;
            if (pc?.Pawn is not SBasePawn pawn || !pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal))
            {
                if (session.TicksRun > 60 * 60)
                {
                    return SmokeResult.Fail($"the local pawn never walked in 60 s (state {pc?.GetStateName()})");
                }

                return null;
            }

            if (_spawnTick < 0)
            {
                _spawnTick = session.TicksRun;
            }

            if (session.TicksRun - _spawnTick < SettleTicks)
            {
                return null;
            }

            if (_step == 0 && _frame == 0)
            {
                string? key = controls.Bindings?.Layout.Bindings.TryGetValue("Tab", out string? command) == true ? command : null;
                if (!string.Equals(key, "SwitchHeadset 3", StringComparison.OrdinalIgnoreCase))
                {
                    return SmokeResult.Fail($"Tab is bound to '{key ?? "nothing"}' in {controls.Bindings?.Source}, not SwitchHeadset 3");
                }

                if (pc.Goggle is null)
                {
                    return SmokeResult.Fail("the local controller has no goggles");
                }

                Shot(context, "start");
            }

            int want = _cycle[_step];
            if (_frame == 0)
            {
                _stepStart = session.TicksRun;
                _clipStartsAtStep = _server.Read().ClipStarts;
            }

            // Press Tab on the first frame of the step, release it a few frames later.
            if (_frame == 1)
            {
                SendTab(true);
            }
            else if (_frame == 5)
            {
                SendTab(false);
            }

            _frame++;
            long ticks = session.TicksRun - _stepStart;
            SGoggle? goggle = pc.Goggle;
            FaithfulView? view = _root.View;
            (int clipStarts, string clip, bool emf) = _server.Read();
            // A press from the normal view or back to it plays the head clip on every machine; a switch between two
            // modes is immediate (SA_GOG_SWITCH_WITHOUTANIM, no clip).
            bool clipWanted = _step != 1;
            bool serverOk = !clipWanted || clipStarts > _clipStartsAtStep;
            bool emfOk = _side == PlaySide.Merc || emf == (want != SGoggle.REN_DynLight);
            int drawn = view?.Effect.LastDrawnRendMap ?? -1;
            bool clientOk = goggle is not null && goggle.CurrentMode == want && pc.RendMap == want;
            bool viewOk = view is not null && view.RendMap == want && view.Effect.Settings.RendMap == want &&
                (DisplayServer.GetName() == "headless" || drawn == want);
            if (_frame > 6 && clientOk && viewOk && serverOk && emfOk && !_tabDown)
            {
                string detail = string.Create(CultureInfo.InvariantCulture,
                    $"Tab -> {Mode(want)}: client goggle {goggle!.CurrentMode} ({goggle.GetStateName()}) RendMap {pc.RendMap}, view {view!.RendMap}, pass drew {drawn}, server clip {(clipWanted ? clip : "none")} emf {emf} ({ticks} ticks)");
                _results.Add(detail);
                GD.Print($"SVM_SMOKE {TestName} {_side} ok {detail}");
                Shot(context, Mode(want));
                _step++;
                _frame = 0;
                if (_step >= _cycle.Length)
                {
                    return SmokeResult.Pass($"{_side}: " + string.Join("; ", _results));
                }

                return null;
            }

            if (ticks > StepTimeoutTicks)
            {
                Shot(context, Mode(want) + " FAILED");
                return SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture,
                    $"Tab -> {Mode(want)} not shown after {ticks} ticks: client goggle {goggle?.CurrentMode} ({goggle?.GetStateName()}) RendMap {pc.RendMap} gogglesUp {pawn.bGogglesUp} c18 {(pawn.PawnAnim.IsAnimating(SBasePawn.GOGGLECHANNEL) ? pawn.PawnAnim[SBasePawn.GOGGLECHANNEL].Sequence.ToString() : "idle")}, " +
                    $"view {view?.RendMap} settings {view?.Effect.Settings.RendMap} pass drew {drawn}, server clips {clipStarts - _clipStartsAtStep} ({clip}) emf {emf}, " +
                    $"input commands [{string.Join(",", session.Input.Commands)}] log [{string.Join("|", session.Log)}] captured {controls.IsCaptured} focus {_root.GetViewport().GuiGetFocusOwner()?.GetPath()} | done: {string.Join("; ", _results)}"));
            }

            return null;
        }

        private static string Mode(int m) => m switch
        {
            SGoggle.REN_NightVision => "night",
            SGoggle.REN_ThermalVision => "thermal",
            SGoggle.REN_MotionTrackingVision => "motion",
            SGoggle.REN_EMFVision => "emf",
            _ => "normal",
        };

        /// <summary>Tab as the window sends it: a key event with the key code and the physical key code.</summary>
        private void SendTab(bool pressed)
        {
            _tabDown = pressed;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = pressed });
        }

        /// <summary>With a window and SVM_PLAY_SHOTS naming a folder (keep it under local/), saves the window.</summary>
        private void Shot(SmokeContext context, string label)
        {
            string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
            if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            Image image = _root.GetViewport().GetTexture().GetImage();
            string safe = string.Concat(label.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_side}-{_shots++:D2}-{safe}.png")));
        }

        public void Dispose()
        {
            if (_tabDown)
            {
                SendTab(false);
            }

            if (DisplayServer.GetName() != "headless")
            {
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
            }

            _root?.QueueFree();
            _root = null;
        }
    }
}
