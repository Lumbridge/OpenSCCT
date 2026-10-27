using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=goggle-glow</c>: the goggle glow on the 3D pawns as players see it (spec/equipment/goggle-glow.md).
/// SteeE offline, the listen server on its own thread as the launcher runs it, bots on the server.
/// <para>Spy leg (2v2: a spy mate and two mercs): the local spy is put in front of a merc bot that faces it. The merc's
/// glow is drawn orange on the spy's machine; the bot's own goggle keys (SwitchHeadset 1, 2, 2 on the server, the action
/// stream to the client) turn it red, blue and orange again. Then in front of the spy mate: its green glow is drawn.
/// The spy's own night vision key (KeyPilot: a Godot key event through PlayInput and the client) keeps the glows drawn;
/// thermal leaves them out (HeatOpacity 0); the key again returns to the normal view.</para>
/// <para>Merc leg (1v1 with a spy bot): the spy's glow does not exist on the merc's machine; the merc's own key
/// (SwitchHeadset 1) turns its own glow red on its machine and on the server's copy (the other players' view), the
/// key again orange; its own glow is not drawn in first person.</para>
/// With a window and SVM_PLAY_SHOTS naming a folder under local/, each step saves the game's frame.
/// </summary>
public sealed class GoggleGlowSmoke : SmokeTest
{
    public const string TestName = "goggle-glow";

    private const int TeardownFrames = 6;

    private readonly List<string> _report = [];
    private readonly string[] _legs;
    private int _legIndex;
    private Leg? _leg;
    private int _teardown = -1;
    private SmokeResult? _final;

    public GoggleGlowSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_GLOW_LEGS");
        _legs = string.IsNullOrWhiteSpace(only) ? ["spy", "merc"] : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            _leg = new Leg(context, _legs[_legIndex] == "merc" ? PlaySide.Merc : PlaySide.Spy);
            return SmokeResult.Running;
        }

        SmokeResult? result = _leg.Step();
        if (result is null)
        {
            return SmokeResult.Running;
        }

        _leg.Dispose();
        _leg = null;
        _teardown = 0;
        if (result.Value.State == SmokeState.Failed)
        {
            _final = SmokeResult.Fail($"{_legs[_legIndex]}: {result.Value.Detail} | done: {string.Join(" || ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        GD.Print($"SVM_SMOKE {TestName} {_legs[_legIndex]} {result.Value.Detail}");
        if (++_legIndex >= _legs.Length)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }

    private static string Key(string? path) => GoggleGlowView.SkinKey(path) ?? "own";

    private sealed class Leg : IDisposable
    {
        private const string Orange = "SGoggleTextures.TRD_goggle_orange";
        private const string Motion = "SGoggleTextures.TRD_goggle_motion";
        private const string Emf = "SGoggleTextures.TRD_goggle_EMF";
        private const string Green = "SGoggleTextures.TRA_goggle_green";

        private readonly PlaySide _side;
        private readonly List<string> _results = [];
        private readonly List<(string Name, double Timeout, Action Begin, Func<string?> Done)> _steps = [];
        private readonly object _gate = new();
        private PlayRoot? _root;
        private KeyPilot? _keys;
        private bool _hooked;
        private int _step = -1;
        private double _stepAt;
        private double _readyAt = -1;
        private int _shots;

        // Main thread to server thread.
        private volatile int _targetKind;      // 0 none, 1 a merc bot, 2 a spy bot
        private volatile int _command;         // a SwitchHeadset argument for the target bot, 0 none
        private volatile bool _placed;
        private volatile int _yawToTarget;

        // Server thread to main thread (under _gate).
        private string _serverOwnSkin = "";
        private int _serverOwnMode;
        private string _serverWhy = "";
        private FVector _serverTargetAt;
        private bool _serverTargetKnown;
        private string? _serverTargetName;

        public Leg(SmokeContext context, PlaySide side)
        {
            _side = side;
            PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
            PlayConfig config = launcher with
            {
                Settings = launcher.Settings with
                {
                    Map = "SteeE",
                    Side = side,
                    Mode = "Story",
                    Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Thread,
                    Bots = side == PlaySide.Spy ? 3 : 1,
                    Difficulty = "normal",
                },
                Render = true,
                Hud = true,
                TicksPerFrame = 0,
                Pilot = null,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
        }

        private static double Now => Time.GetTicksMsec() / 1000.0;

        public SmokeResult? Step()
        {
            if (_root is null)
            {
                return SmokeResult.Fail("no play scene");
            }

            if (_root.LoadError is { } error)
            {
                return SmokeResult.Fail("play scene did not load: " + error);
            }

            if (!_root.IsLoaded || _root.Session is not { } session)
            {
                return null;
            }

            if (session.ServerFailure is { } failure)
            {
                return SmokeResult.Fail("server failed: " + failure.Message);
            }

            if (!_hooked)
            {
                _hooked = true;
                session.Host.BeforeWorldTick += _ => DriveBots(session);
            }

            if (_keys is null)
            {
                SPlayerController? pc = session.LocalController;
                if (pc is null || session.LocalPawn is null || !pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal))
                {
                    if (_readyAt < 0)
                    {
                        _readyAt = Now;
                    }

                    return Now - _readyAt > 90.0 ? SmokeResult.Fail("the local pawn never walked") : null;
                }

                _keys = KeyPilot.Attach(_root);
                Build(session);
                return null;
            }

            _keys.Update();
            if (!_keys.Ready)
            {
                return null;
            }

            if (_step < 0)
            {
                Next(0);
            }

            (string name, double timeout, _, Func<string?> done) = _steps[_step];
            string? detail = done();
            double elapsed = Now - _stepAt;
            if (detail is not null && detail.StartsWith("FAIL", StringComparison.Ordinal))
            {
                Shot(name + " FAILED");
                return SmokeResult.Fail($"step '{name}': {detail} | {Describe(session)} | done: {string.Join("; ", _results)}");
            }

            if (detail is null && elapsed > timeout)
            {
                Shot(name + " TIMEOUT");
                return SmokeResult.Fail($"step '{name}' had no result after {timeout:F1} s | {Describe(session)} | done: {string.Join("; ", _results)}");
            }

            if (detail is null)
            {
                return null;
            }

            Shot(name);
            _results.Add($"{name}: {detail}");
            GD.Print($"SVM_SMOKE {TestName} {_side} ok {name}: {detail}");
            if (_step + 1 >= _steps.Count)
            {
                return SmokeResult.Pass($"{_side}: " + string.Join("; ", _results));
            }

            Next(_step + 1);
            return null;
        }

        private void Next(int index)
        {
            _step = index;
            _stepAt = Now;
            _steps[index].Begin();
        }

        private double StepSeconds => Now - _stepAt;

        // ---- the steps ----------------------------------------------------------------------------------------------

        private void Build(PlaySession session)
        {
            if (_side == PlaySide.Spy)
            {
                Face(session, 1, "face a merc bot");
                _steps.Add(("the merc's glow is orange", 4.0, () => { }, () => Glow(session, typeof(SPAWNDEFENSE), Orange, 0)));
                _steps.Add(("the merc bot's motion tracking turns it red", 5.0, () => _command = 1, () => Glow(session, typeof(SPAWNDEFENSE), Motion, K.BeamModeMotion)));
                _steps.Add(("its EMF turns it blue at once", 5.0, () => _command = 2, () => Glow(session, typeof(SPAWNDEFENSE), Emf, K.BeamModeEmf)));
                _steps.Add(("goggles up: orange again", 5.0, () => _command = 2, () => Glow(session, typeof(SPAWNDEFENSE), Orange, K.BeamModeNormal)));
                Face(session, 2, "face the spy mate");
                _steps.Add(("the spy mate's glow is green", 4.0, () => { }, () => Glow(session, typeof(SPawnAttaque), null, 0)));
                _steps.Add(("turn the camera round to the own spy's face (mouse)", 8.0, () => _faceYaw = -1, () =>
                {
                    if (session.LocalController is not { } pc || session.LocalPawn is not { } own)
                    {
                        return null;
                    }

                    if (_faceYaw < 0)
                    {
                        _faceYaw = (own.Rotation.Yaw + 32768) & 0xFFFF;
                        _facePawnYaw = own.Rotation.Yaw & 0xFFFF;
                    }

                    // The view of the sandbox captures (local/goggle-glow/game): the spy's front, pitched down 1500.
                    int err = (((_faceYaw - pc.Rotation.Yaw) + 32768) & 0xFFFF) - 32768;
                    int errPitch = (((FacePitch - pc.Rotation.Pitch) + 32768) & 0xFFFF) - 32768;
                    GoggleGlowView? glow = _root!.Pawns?.ViewOf(own)?.GoggleGlow;
                    Camera3D? camera = _root.GetViewport().GetCamera3D();
                    if (Math.Abs(err) < 600 && Math.Abs(errPitch) < 300 && StepSeconds > 2.0 && glow is { Shown: true } && camera is not null &&
                        !camera.IsPositionBehind(glow.Model!.Root.GlobalPosition))
                    {
                        return string.Create(CultureInfo.InvariantCulture,
                            $"view yaw {pc.Rotation.Yaw & 0xFFFF} pitch {pc.Rotation.Pitch & 0xFFFF}, pawn yaw {own.Rotation.Yaw & 0xFFFF} (was {_facePawnYaw}), own glow {glow.DrawnSkin} at {camera.UnprojectPosition(glow.Model.Root.GlobalPosition):F0}");
                    }

                    if (NewTick(session))
                    {
                        _keys!.Look(Math.Clamp(LookGain * err / KeyPilot.UnitsPerCount, -60f, 60f), Math.Clamp(-LookGain * errPitch / KeyPilot.UnitsPerCount, -20f, 20f));
                    }

                    return null;
                }));
                _steps.Add(("own night vision (key SwitchHeadset 1) keeps the glows", 6.0, () => _keys!.Tap("SwitchHeadset 1"), () =>
                {
                    SPlayerController? pc = session.LocalController;
                    if (pc?.RendMap != SGoggle.REN_NightVision || _root!.View?.RendMap != SGoggle.REN_NightVision)
                    {
                        return null;
                    }

                    string? mate = Glow(session, typeof(SPawnAttaque), null, 0);
                    SBasePawn? own = session.LocalPawn;
                    GoggleGlowView? ownGlow = _root.Pawns?.ViewOf(own)?.GoggleGlow;
                    if (mate is null || mate.StartsWith("FAIL", StringComparison.Ordinal))
                    {
                        return mate;
                    }

                    if (own?.GoggleBeam is not { } beam || beam.CurrentMode != K.BeamModeNight || ownGlow is not { Shown: true })
                    {
                        return StepSeconds > 3.0 ? $"FAIL: own glow mode {own?.GoggleBeam?.CurrentMode} shown {ownGlow?.Shown}" : null;
                    }

                    return $"RendMap 34; mate {mate}; own glow mode {beam.CurrentMode} skin {Key(beam.Skin.Path)} drawn {ownGlow.DrawnSkin} (third person)";
                }));
                _steps.Add(("own thermal (key SwitchHeadset 2) leaves the glows out", 6.0, () => _keys!.Tap("SwitchHeadset 2"), () =>
                {
                    if (session.LocalController?.RendMap != SGoggle.REN_ThermalVision || _root!.View?.RendMap != SGoggle.REN_ThermalVision)
                    {
                        return null;
                    }

                    SBasePawn? mate = ClientPawn(session, typeof(SPawnAttaque));
                    SGoggleBeam? beam = mate?.GoggleBeam;
                    return beam is { HeatOpacity: 0f, EMFOpacity: 0f } && StepSeconds > 1.0
                        ? "RendMap 36; the glow's HeatOpacity 0 (not drawn in the heat pass)" : null;
                }));
                _steps.Add(("the key again: normal view", 6.0, () => _keys!.Tap("SwitchHeadset 2"), () =>
                    session.LocalController?.RendMap == SGoggle.REN_DynLight && StepSeconds > 1.0 ? "RendMap 5" : null));
                return;
            }

            Face(session, 2, "face the spy bot");
            _steps.Add(("no spy glow on a merc's machine", 4.0, () => { }, () =>
            {
                SBasePawn? spy = ClientPawn(session, typeof(SPawnAttaque));
                if (spy is null)
                {
                    return null;
                }

                GoggleGlowView? glow = _root!.Pawns?.ViewOf(spy)?.GoggleGlow;
                int beams = 0;
                foreach (SGoggleBeam_ATT b in session.Client.World.Level.AllActors<SGoggleBeam_ATT>())
                {
                    beams += b.bDeleteMe ? 0 : 1;
                }

                return spy.GoggleBeam is null && beams == 0 && glow is not { Shown: true }
                    ? "the spy has no beam here and none is drawn" : $"FAIL: spy beam {spy.GoggleBeam} ({beams} spy beams), drawn {glow?.Shown}";
            }));
            _steps.Add(("own motion tracking (key SwitchHeadset 1) turns the own glow red here and on the server", 6.0, () => _keys!.Tap("SwitchHeadset 1"), () =>
                Own(session, Motion, K.BeamModeMotion)));
            _steps.Add(("the key again: orange here and on the server", 6.0, () => _keys!.Tap("SwitchHeadset 1"), () =>
                Own(session, Orange, K.BeamModeNormal)));
        }

        /// <summary>Puts the local player in front of the bot of <paramref name="kind"/> and turns its view to it (mouse).</summary>
        private void Face(PlaySession session, int kind, string name)
        {
            _steps.Add((name, 12.0, () =>
            {
                _placed = false;
                _targetKind = kind;
            }, () =>
            {
                if (!_placed || session.LocalController is not { } pc)
                {
                    return null;
                }

                int err = (((_yawToTarget - pc.Rotation.Yaw) + 32768) & 0xFFFF) - 32768;
                if (Math.Abs(err) < 400 && StepSeconds > 2.5)
                {
                    return $"view yaw {pc.Rotation.Yaw & 0xFFFF}, the bot at yaw {_yawToTarget}";
                }

                if (NewTick(session))
                {
                    _keys!.Look(Math.Clamp(LookGain * err / KeyPilot.UnitsPerCount, -40f, 40f), 0f);
                }

                return null;
            }));
        }

        /// <summary>
        /// The target's glow on this machine: its beam's skin and mode (wanted <paramref name="skin"/>, null for the
        /// mesh's own) and the drawn model wearing it.
        /// </summary>
        private string? Glow(PlaySession session, Type type, string? skin, int mode)
        {
            SBasePawn? pawn = ClientPawn(session, type);
            if (pawn?.GoggleBeam is not { } beam)
            {
                return StepSeconds > 3.0 ? $"FAIL: the {type.Name} has no beam on this machine" : null;
            }

            SimPawnView? view = _root!.Pawns?.ViewOf(pawn);
            GoggleGlowView? glow = view?.GoggleGlow;
            string own = type == typeof(SPAWNDEFENSE) ? Orange : Green;
            string drawnWanted = skin ?? own;
            bool skinOk = skin is null ? beam.Skin.IsNone || Key(beam.Skin.Path) == Green : Key(beam.Skin.Path) == skin || (beam.Skin.IsNone && skin == own);
            if (beam.CurrentMode != mode || !skinOk || glow is not { Shown: true } || glow.DrawnSkin != drawnWanted || view is not { Visible: true })
            {
                return null;
            }

            Camera3D? camera = _root.GetViewport().GetCamera3D();
            Vector3 at = glow.Model!.Root.GlobalPosition;
            string onScreen = camera is null || camera.IsPositionBehind(at) ? "behind the camera" : $"at {camera.UnprojectPosition(at):F0}";
            return string.Create(CultureInfo.InvariantCulture,
                $"mode {beam.CurrentMode}, skin {Key(beam.Skin.Path)}, drawn {glow.DrawnMesh} as {glow.DrawnSkin} {onScreen}, {(pawn.Location - session.LocalPawn!.Location).Size():F0} uu away");
        }

        /// <summary>The local merc's own glow here and on the server's copy of its pawn.</summary>
        private string? Own(PlaySession session, string skin, int mode)
        {
            SBasePawn? pawn = session.LocalPawn;
            if (pawn?.GoggleBeam is not { } beam)
            {
                return StepSeconds > 3.0 ? "FAIL: the local merc has no beam" : null;
            }

            string serverSkin;
            int serverMode;
            lock (_gate)
            {
                serverSkin = _serverOwnSkin;
                serverMode = _serverOwnMode;
            }

            GoggleGlowView? glow = _root!.Pawns?.ViewOf(pawn)?.GoggleGlow;
            bool here = beam.CurrentMode == mode && Key(beam.Skin.Path) == skin;
            bool server = serverMode == mode && serverSkin == skin;
            if (!here || !server)
            {
                return null;
            }

            return glow is { Shown: true }
                ? "FAIL: the own glow is drawn in first person"
                : $"here mode {beam.CurrentMode} skin {Key(beam.Skin.Path)} (not drawn: first person), server mode {serverMode} skin {serverSkin}";
        }

        /// <summary>
        /// The client's copy of the target bot: the pawn of <paramref name="type"/> with the target's player name, else
        /// the one nearest to where the server last had the target (other than the local one).
        /// </summary>
        private SBasePawn? ClientPawn(PlaySession session, Type type)
        {
            SBasePawn? local = session.LocalPawn;
            FVector from;
            string? name;
            lock (_gate)
            {
                from = _serverTargetKnown ? _serverTargetAt : local?.Location ?? default;
                name = _serverTargetName;
            }

            SBasePawn? best = null;
            float bestDist = float.MaxValue;
            foreach (SBasePawn p in session.Client.World.Level.AllActors<SBasePawn>())
            {
                if (p.bDeleteMe || p.Health <= 0 || ReferenceEquals(p, local) || !type.IsInstanceOfType(p))
                {
                    continue;
                }

                if (name is not null && p.PlayerReplicationInfo?.PlayerName == name)
                {
                    return p;
                }

                float d = (p.Location - from).Size();
                if (d < bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }

            return best;
        }

        private string Describe(PlaySession session)
        {
            var parts = new List<string>();
            foreach (SBasePawn p in session.Client.World.Level.AllActors<SBasePawn>())
            {
                SGoggleBeam? b = p.GoggleBeam;
                GoggleGlowView? g = _root?.Pawns?.ViewOf(p)?.GoggleGlow;
                parts.Add($"{p.GetType().Name} {p.PlayerReplicationInfo?.PlayerName} at {p.Location} {(ReferenceEquals(p, session.LocalPawn) ? "(local) " : "")}beam {(b is null ? "none" : $"{b.GetType().Name} mode {b.CurrentMode} skin {Key(b.Skin.Path)} hidden {b.bHidden}")} drawn {g?.Shown} {g?.DrawnSkin}");
            }

            string why;
            lock (_gate)
            {
                why = _serverWhy;
            }

            return $"RendMap {session.LocalController?.RendMap}; {string.Join("; ", parts)}; server: {why}";
        }

        // ---- the server side --------------------------------------------------------------------------------------------

        /// <summary>
        /// On the server thread before each world tick: every bot is held; the target bot faces the local player, who is
        /// moved once in front of it, and presses its goggle key when the test asks.
        /// </summary>
        private void DriveBots(PlaySession session)
        {
            if (session.ServerController?.Pawn is SBasePawn own)
            {
                lock (_gate)
                {
                    _serverOwnSkin = Key(own.GoggleBeam?.Skin.Path);
                    _serverOwnMode = own.GoggleBeam?.CurrentMode ?? -1;
                }
            }

            if (session.Bots is not { } bots || session.ServerController?.Pawn is not SBasePawn local)
            {
                return;
            }

            // A bot gets its goggles in its own first thinking tick; it is held (idle input) from then on. The bots that
            // are not the target walk off sideways for two seconds first (two of them can stand on the same spawn spot,
            // and an overlapped glow would hide the target's).
            foreach (AiController b in bots.Bots)
            {
                b.Held = b.Controller.Goggle is not null;
            }

            int kind = _targetKind;
            if (kind == 0)
            {
                return;
            }

            AiController? bot = bots.Bots.FirstOrDefault(b => b.Pawn is SBasePawn { bDeleteMe: false, Health: > 0 } p &&
                (kind == 1 ? p is SPAWNDEFENSE : p is SPawnAttaque) && !ReferenceEquals(p, local));
            if (bot?.Pawn is not SBasePawn target || !bot.Held)
            {
                Why($"no bot of kind {kind}");
                return;
            }

            lock (_gate)
            {
                _serverTargetAt = target.Location;
                _serverTargetKnown = true;
                _serverTargetName = target.PlayerReplicationInfo?.PlayerName;
            }

            if (!_placed)
            {
                if (target.Physics != EPhysics.PHYS_Walking)
                {
                    return;
                }

                FVector start = local.Location;
                foreach ((FVector at, int yaw) in Spots(target, local))
                {
                    if (local.SetLocation(at))
                    {
                        _yawToTarget = yaw;
                        _placed = true;
                        Why($"moved to {local.Location} facing {target.GetType().Name} at {target.Location}");
                        break;
                    }
                }

                if (!_placed)
                {
                    local.SetLocation(start);
                    Why($"no spot round {target.Location}");
                    return;
                }
            }

            foreach (AiController other in bots.Bots)
            {
                if (ReferenceEquals(other, bot) || !other.Held || other.Pawn is null)
                {
                    continue;
                }

                _walkOff.TryGetValue(other, out int walked);
                if (walked < WalkOffTicks)
                {
                    _walkOff[other] = walked + 1;
                    new AiInput { BT_Up = 1, FastSpeed = true, View = new FRotator(0, (_yawToTarget + 16384) & 0xFFFF, 0) }.ApplyTo(other.Controller);
                }
            }

            var input = new AiInput { View = new FRotator(0, (_yawToTarget + 32768) & 0xFFFF, 0) };

            // A spy turns to where it walks, not to its view: the mate takes a few steps toward the local spy so its face
            // (and its glow) points at the camera, as the sandbox captures look at a spy's front.
            if (kind == 2 && _approachTicks < ApproachTicks)
            {
                _approachTicks++;
                input.BT_Up = 1;
            }

            if (target.bIsCrouched && target.Physics == EPhysics.PHYS_Walking && (session.TicksRun & 31) == 0)
            {
                input.bCrouch = 1;
            }

            int command = _command;
            if (command != 0)
            {
                _command = 0;
                input.Press(AiCommand.SwitchHeadset, command);
            }

            input.ApplyTo(bot.Controller);
            if (command != 0)
            {
                SGoggle? g = bot.Controller.Goggle;
                Why($"{target.GetType().Name} {target.PlayerReplicationInfo?.PlayerName} at {target.Location} pressed SwitchHeadset {command}: goggle {g?.CurrentMode} ({g?.GetStateName()}), beam {target.GoggleBeam?.CurrentMode}, pad {bot.Controller.iDontUpdatePadInput}, state {bot.Controller.GetStateName()}");
            }
        }

        private const int WalkOffTicks = 120;

        private const int ApproachTicks = 20;

        private int _approachTicks;

        private const int FacePitch = -1500 & 0xFFFF;

        /// <summary>
        /// The share of the remaining turn the mouse moves each tick. The view the loops read is the 60 Hz sim's, so a
        /// correction sent every drawn frame was sent again and again before the sim turned (at 480 fps the view swung
        /// 14000 units each side of the target): one correction per tick, half the error, converges at any frame rate.
        /// </summary>
        private const float LookGain = 0.5f;

        private long _lookTick = -1;

        private int _faceYaw = -1;
        private int _facePawnYaw;

        private readonly Dictionary<AiController, int> _walkOff = new(ReferenceEqualityComparer.Instance);

        private void Why(string why)
        {
            lock (_gate)
            {
                _serverWhy = why;
            }
        }

        /// <summary>Spots 260 to 480 uu in front of the target with a clear line at both heights and a floor.</summary>
        /// <summary>True once per client tick: the sim has turned the view by the last correction.</summary>
        private bool NewTick(PlaySession session)
        {
            long tick = session.View.Tick;
            if (tick == _lookTick)
            {
                return false;
            }

            _lookTick = tick;
            return true;
        }

        private static IEnumerable<(FVector At, int YawToTarget)> Spots(SBasePawn target, SBasePawn local)
        {
            FVector up = new(0f, 0f, 40f);
            for (float d = 300f; d <= 480f; d += 60f)
            {
                for (int k = 0; k < 32; k++)
                {
                    int y = (k * 2048) & 0xFFFF;
                    FVector dir = new FRotator(0, y, 0).Vector();
                    FVector at = target.Location + dir * d;
                    if (GadgetWorld.Line(target, at, target.Location, ETraceFlags.TRACE_World, out _) ||
                        GadgetWorld.Line(target, at + up, target.Location + up, ETraceFlags.TRACE_World, out _) ||
                        !GadgetWorld.Line(target, at - new FVector(0f, 0f, 400f), at, ETraceFlags.TRACE_World, out FCheckResult floor))
                    {
                        continue;
                    }

                    yield return (new FVector(at.X, at.Y, floor.Location.Z + local.CollisionHeight + 2f), (y + 32768) & 0xFFFF);
                }
            }
        }

        /// <summary>With a window and SVM_PLAY_SHOTS naming a folder (keep it under local/), saves the game's frame.</summary>
        private void Shot(string label)
        {
            string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
            if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            Image image = _root.GetViewport().GetTexture().GetImage();
            string safe = new(label.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_side}-{_shots++:D2}-{safe}.png")));
        }

        public void Dispose()
        {
            _keys?.ReleaseAll();
            _root?.QueueFree();
            _root = null;
        }
    }
}
