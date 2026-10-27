using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Controls;
using OpenScctGame.Fx;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-fx</c>: the gameplay effects a player sees, from the real keys (Godot input events read by
/// <see cref="PlayInput"/>, over loopback to the listen server on its thread, as the launchers run). Each step presses
/// the keys and then checks what is drawn: the thrown gadget's mesh in the camera's view, the effect class's particles
/// (converted sub-emitters of shared/fx), the casing, muzzle and spark emitters of a shot, the impact decal. Two legs
/// (SVM_FX_LEGS picks some): the spy (quick smoke, quick chaff, an aimed smoke with its trail, a Shocker shot, the sticky
/// cam and its gas) and the merc (a frag with its trail, explosion and flash, rifle shots at a wall, a proximity mine).
/// The walk to a wall before the steps is setup (the scripted pilot, as play-combat). With a window and SVM_PLAY_SHOTS
/// naming a folder (under local/), each step and timed moments of each effect are saved there.
/// </summary>
public sealed class PlayFxSmoke : SmokeTest
{
    public const string TestName = "play-fx";

    private const int TeardownFrames = 4;

    /// <summary>
    /// A quick-launched smoke or chaff grenade pops at its first hit, the floor under the spy's hand
    /// (spec/equipment/grenade-throw.md rule 10a): client ticks from the grenade showing up to its effect, with room
    /// for the replication delay. Its 3 s fuse would be 180.
    /// </summary>
    private const int QuickPopTicks = 45;

    private readonly List<string> _report = new();
    private readonly string[] _legs;
    private int _leg;
    private FxLeg? _current;
    private int _teardown = -1;
    private SmokeResult? _final;

    public PlayFxSmoke()
    {
        string? only = System.Environment.GetEnvironmentVariable("SVM_FX_LEGS");
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

        if (_current is null)
        {
            _current = _legs[_leg] == "merc" ? MercLeg(context) : SpyLeg(context);
            return SmokeResult.Running;
        }

        SmokeResult? result = _current.Step(context);
        if (result is null)
        {
            return SmokeResult.Running;
        }

        _current.Dispose();
        _current = null;
        _teardown = 0;
        if (result.Value.State == SmokeState.Failed)
        {
            _final = SmokeResult.Fail($"{_legs[_leg]}: {result.Value.Detail} | done: {string.Join("; ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        if (++_leg >= _legs.Length)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }

    // ---- the spy ----------------------------------------------------------------------------------------------

    private static FxLeg SpyLeg(SmokeContext context)
    {
        var leg = new FxLeg(context, PlaySide.Spy, standoff: 240f);
        leg.Add("quick smoke grenade (left mouse, holstered)", 900, s =>
        {
            s.FaceAway();
            s.Tap("LeftMouse", 30, 3);
            s.ShotsAt(s.SinceFirst<SProjSmoke>(), "grenade", 2, 6);
            s.ShotsAt(s.SinceFirst<SSmokeEmitter>(), "cloud", 3, 30, 90, 240, 480, 560);
        }, s => s.MeshSeen("SProjSmoke") && s.ParticlesSeen("SSmokeEmitter", 10) && s.SinceFirst<SSmokeEmitter>() > 570
                && s.SinceFirst<SProjSmoke>() - s.SinceFirst<SSmokeEmitter>() <= QuickPopTicks && s.ClientCount<SProjSmoke>() == 0
            ? s.Detail($"grenade mesh {s.Fx!.MeshesShown.GetValueOrDefault("SProjSmoke")}, popped {s.SinceFirst<SProjSmoke>() - s.SinceFirst<SSmokeEmitter>()} ticks after it showed, cloud peak {s.Fx.PeakParticles.GetValueOrDefault("SSmokeEmitter")} particles")
            : null);
        leg.Add("quick chaff grenade (right mouse, holstered)", 900, s =>
        {
            s.FaceAway();
            s.Tap("RightMouse", 30, 3);
            s.ShotsAt(s.SinceFirst<SProjChaffe>(), "grenade", 2, 6);
            s.ShotsAt(s.SinceFirst<SChaffeEmitter>(), "flakes", 2, 10, 30, 90, 300);
        }, s => s.MeshSeen("SProjChaffe") && s.ParticlesSeen("SChaffeEmitter", 100) && s.SinceFirst<SChaffeEmitter>() > 310
                && s.SinceFirst<SProjChaffe>() - s.SinceFirst<SChaffeEmitter>() <= QuickPopTicks
            ? s.Detail($"grenade mesh {s.Fx!.MeshesShown.GetValueOrDefault("SProjChaffe")}, burst {s.SinceFirst<SProjChaffe>() - s.SinceFirst<SChaffeEmitter>()} ticks after it showed, flakes peak {s.Fx.PeakParticles.GetValueOrDefault("SChaffeEmitter")}, smoke grenades left on the client {s.ClientCount<SProjSmoke>()}, drawn {s.LiveMesh("SProjSmoke")}")
            : null);
        leg.Add("draw the Shocker (tap E)", 300, s => s.Tap("E", 0, 4), s =>
            s.Pawn is { WeaponLoaded: 2 } && s.Aiming ? s.Detail("drawn") : null);
        leg.Add("Shocker shot at the wall (left mouse)", 300, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = (s.Combat?.Marks ?? 0, s.Combat?.MuzzleMeshes ?? 0);
            }

            if (s.Frame < 30)
            {
                s.FaceWall();
            }

            s.Tap("LeftMouse", 30, 3);
            s.ShotsAt(s.FramesSinceStarted("SSparkShocker"), "sparks", 1, 6, 20);
        }, s =>
        {
            (int marks, int muzzle) = ((int, int))s.Remember!;
            return s.Combat is { } fx && fx.Marks > marks && fx.MuzzleMeshes > muzzle && s.ParticlesSeen("SSparkShocker", 1) && fx.LastMarkDecal
                ? s.Detail($"mark with its decal, muzzle meshes {fx.MuzzleMeshes - muzzle}, sparks peak {s.Fx!.PeakParticles.GetValueOrDefault("SSparkShocker")}")
                : null;
        });
        leg.Add("select the smoke grenade (1)", 120, s => s.Tap("1", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadSmokeGrenade ? s.Detail("selected") : null);
        leg.Add("aimed smoke grenade with its trail (hold and release right mouse)", 900, s =>
        {
            s.FaceAway();
            s.Tap("RightMouse", 20, 40);
            s.ShotsAt(s.SinceFirst<SProjSmoke>(), "trail", 6, 15, 30);
        }, s => s.ParticlesSeen("SSmokeLaunchEmitter", 1) && s.ClientCount<SSmokeEmitter>() > 0
            ? s.Detail($"trail peak {s.Fx!.PeakParticles.GetValueOrDefault("SSmokeLaunchEmitter")} particles")
            : null);
        leg.Add("select the sticky cam (3)", 120, s => s.Tap("3", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadStickyCam ? s.Detail("selected") : null);
        leg.Add("fire the sticky cam at the wall (right mouse)", 600, s =>
        {
            if (s.Pawn is not { WeaponLoaded: 2 } && s.Frame == 5)
            {
                s.Send("E", true);
            }
            else if (s.Frame == 9)
            {
                s.Send("E", false);
            }

            s.FaceWall();
            s.Tap("RightMouse", 40, 4);
        }, s => s.MeshSeen("SStickyCam") && s.PC is { } pc && GadgetStates.InStickyCamView(pc)
            ? s.Detail($"cam mesh shown {s.Fx!.MeshesShown.GetValueOrDefault("SStickyCam")}, in {pc.GetStateName()}")
            : null);
        leg.Add("sticky cam gas (Shift)", 400, s =>
        {
            s.Tap("Shift", 10, 6);
            s.ShotsAt(s.SinceFirst<SStickyCamEmitter>(), "gas", 10, 60, 180);
        }, s => s.ParticlesSeen("SStickyCamEmitter", 5) && s.SinceFirst<SStickyCamEmitter>() > 190
            ? s.Detail($"gas peak {s.Fx!.PeakParticles.GetValueOrDefault("SStickyCamEmitter")} particles")
            : null);
        leg.Add("leave the cam (right mouse)", 300, s => s.Tap("RightMouse", 20, 4), s =>
            s.PC is { } pc && !GadgetStates.InStickyCamView(pc) ? s.Detail($"back in {pc.GetStateName()}, cam still drawn {s.LiveMesh("SStickyCam")}") : null);
        return leg;
    }

    // ---- the merc ---------------------------------------------------------------------------------------------

    private static FxLeg MercLeg(SmokeContext context)
    {
        var leg = new FxLeg(context, PlaySide.Merc, standoff: 40f);
        leg.Add("rifle shots at the wall (left mouse)", 300, s =>
        {
            if (s.Frame == 0)
            {
                s.Remember = (s.Combat?.Marks ?? 0, s.Combat?.MuzzleMeshes ?? 0, s.Combat?.Casings ?? 0);
            }

            s.FaceWall();
            s.Tap("LeftMouse", 20, 3);
            s.Tap("LeftMouse", 60, 3);
            s.ShotsAt(s.FramesSinceStarted("SDouilleEmitter"), "shot", 1, 3, 12);
        }, s =>
        {
            (int marks, int muzzle, int casings) = ((int, int, int))s.Remember!;
            string? spark = s.SparkClassSeen();
            return s.Combat is { } fx && fx.Marks > marks && fx.MuzzleMeshes > muzzle && fx.Casings > casings && spark is not null
                && s.ParticlesSeen("SDouilleEmitter", 1) && fx.LastMarkDecal && s.Fx is { PeakWorldLit: > 0 }
                ? s.Detail($"marks {fx.Marks - marks} with decals, muzzle meshes {fx.MuzzleMeshes - muzzle}, casings {fx.Casings - casings}, sparks {spark}, muzzle lights on the world {s.Fx.PeakWorldLit}")
                : null;
        });
        leg.Add("walk up to the wall (W)", 600, s =>
        {
            s.FaceWall();
            if (s.Frame == 10)
            {
                s.Send("W", true);
            }
        }, s => s.EyeToWall() is float d and < 42f ? s.Detail($"wall {d:F0} uu from the eyes") : null);
        leg.Add("place a proximity mine (2, hold right mouse for the menu, release)", 900, s =>
        {
            s.Tap("2", 0, 3);
            s.FaceWall();
            s.Tap("RightMouse", 60, 60);
        }, s => s.MeshSeen("SMineProx") ? s.Detail($"mine mesh {s.Fx!.MeshesShown.GetValueOrDefault("SMineProx")}") : null);
        leg.Add("select the frag grenade (1)", 120, s => s.Tap("1", 0, 3), s =>
            s.Pawn?.SelectedGadget is SGadFragGrenade ? s.Detail("selected") : null);
        leg.Add("throw a frag (hold and release right mouse)", 900, s =>
        {
            // The merc stands with his back to the wall he just put the mine on. The release only throws when the
            // muzzle check (CheckIfCanFire, merc-rifle.md rule 3) finds nothing 38 to 56 uu behind the rifle's FX bone,
            // which is 15 uu behind his eye (grenade-throw.md rule 4a): he walks clear of the wall first.
            s.FaceAway();
            if (s.PressedAt < 0 && !s.ClearBehind(100f))
            {
                s.Hold("W", s.Facing && s.Walking);
                return;
            }

            s.Hold("W", false);
            s.HoldWhenWalking("RightMouse", 50);
            s.ShotsAt(s.SinceFirst<SProjFrag>(), "frag", 6, 30, 90);
            s.ShotsAt(s.SinceFirst<SGrenadeExplosion>(), "explosion", 1, 4, 10, 30, 90, 300);
        }, s => s.MeshSeen("SProjFrag") && s.ParticlesSeen("SFragLaunchEmitter", 1) && s.ParticlesSeen("SGrenadeExplosion", 20)
            && s.Fx!.Started.GetValueOrDefault("SFlashLight_FragGrenade") > 0 && s.Fx.PeakWorldLit > 0 && s.SinceFirst<SGrenadeExplosion>() > 310
            ? s.Detail($"frag mesh, trail peak {s.Fx.PeakParticles.GetValueOrDefault("SFragLaunchEmitter")}, explosion peak {s.Fx.PeakParticles.GetValueOrDefault("SGrenadeExplosion")}, flash light lighting the world ({s.Fx.PeakWorldLit} at once)")
            : null);
        return leg;
    }

    // ---- the leg ----------------------------------------------------------------------------------------------

    private sealed class FxLeg : IDisposable
    {
        private readonly List<(string Name, int TimeoutTicks, Action<FxState> Act, Func<FxState, string?> Done)> _steps = new();
        private readonly List<string> _results = new();
        private readonly float _standoff;
        private readonly PlaySide _side;
        private PlayRoot? _root;
        private ScriptedPilot? _pilot;
        private FxState? _state;
        private (FVector Spot, FVector Face)? _wall;
        private int _index;
        private bool _hooked;
        private long _stepStartTick;
        private long _lastActTick = -1;

        public FxLeg(SmokeContext context, PlaySide side, float standoff)
        {
            _side = side;
            _standoff = standoff;
            _pilot = new ScriptedPilot(Wall) { UseHoldTicks = 1 };
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
                    Bots = 0,
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

        public void Add(string name, int timeoutTicks, Action<FxState> act, Func<FxState, string?> done)
        {
            if (_steps.Count == 0)
            {
                _steps.Add(("settle", 600, _ => { }, s => s.Walking && s.Ticks > 30 ? s.Detail($"in {s.PC!.GetStateName()}") : null));
            }

            _steps.Add((name, timeoutTicks, act, done));
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

            if (!_hooked)
            {
                Action<PlaySession, long>? before = session.BeforeClientTick;
                session.BeforeClientTick = (s, t) =>
                {
                    before?.Invoke(s, t);
                    if (_pilot is { Phase: not PilotPhase.Done and not PilotPhase.Failed })
                    {
                        _pilot.Tick(s);
                    }
                };
                controls.Enabled = true;
                controls.CapturePointer = false;
                controls.RequireCapture = DisplayServer.GetName() != "headless";
                if (controls.RequireCapture)
                {
                    OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
                }

                _hooked = true;
            }

            if (_root.Fx is null)
            {
                return SmokeResult.Fail("the play scene has no combat effects view (render off?)");
            }

            if (_pilot is { Phase: PilotPhase.Failed })
            {
                return SmokeResult.Fail($"pilot failed: {_pilot.Failure}");
            }

            if (_pilot is { Phase: not PilotPhase.Done })
            {
                if (session.TicksRun <= 60 * 60)
                {
                    return null;
                }

                GD.Print($"SVM_SMOKE {TestName} {_side}: the walk to the wall stopped at {session.LocalPawn?.Location}; the steps start here");
                _pilot = null;
                session.Input.Clear();
            }

            _state ??= new FxState(session, _root, _wall);
            if (_state.Frame == 0 && _index == 0 && _stepStartTick == 0)
            {
                _stepStartTick = session.TicksRun;
            }

            _state.Tick(session.TicksRun);
            (string name, int timeout, Action<FxState> act, Func<FxState, string?> done) = _steps[_index];
            _state.Ticks = (int)(session.TicksRun - _stepStartTick);
            _state.StepName = name;
            string? detail = _state.Frame == 0 ? null : done(_state);
            if (detail is not null)
            {
                _results.Add($"{name}: {detail}");
                GD.Print($"SVM_SMOKE {TestName} {_side} ok {name}: {detail}");
                _state.Shot(name);
                _state.ReleaseAll();
                _index++;
                _state.NextStep();
                _stepStartTick = session.TicksRun;
                return _index >= _steps.Count ? SmokeResult.Pass($"{_side}: " + string.Join("; ", _results)) : null;
            }

            if (_state.Ticks > timeout)
            {
                _state.Shot(name + " FAILED");
                return SmokeResult.Fail($"step '{name}' had no result after {timeout} ticks ({_state.Describe()}) | done: {string.Join("; ", _results)}");
            }

            // One step action a simulation tick: the steps count their key holds in frames, and at a few hundred frames a
            // second a three-frame tap could press and release between two ticks, unseen by the game.
            if (session.TicksRun == _lastActTick)
            {
                return null;
            }

            _lastActTick = session.TicksRun;
            act(_state);
            _state.Frame++;
            return null;
        }

        private (FVector Spot, FVector Face)? Wall(SPlayerController pc)
        {
            if (_wall is { } known)
            {
                return known;
            }

            if (pc.Pawn is not SBasePawn pawn)
            {
                return null;
            }

            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                FVector dir = new FRotator(0, k * 4096, 0).Vector();
                FVector start = pawn.Location;
                if (!GadgetWorld.Line(pawn, start + dir * 1500f, start, ETraceFlags.TRACE_World, out FCheckResult hit) || MathF.Abs(hit.Normal.Z) > 0.3f)
                {
                    continue;
                }

                FVector eye = start + new FVector(0f, 0f, pawn.EyeHeight);
                if (!GadgetWorld.Line(pawn, eye + dir * 1500f, eye, SGadMultiMinesLauncher.PlaceTraceFlags, out FCheckResult high) ||
                    MathF.Abs(high.Normal.Z) > 0.3f || MathF.Abs(FVector.Dist(high.Location, eye) - FVector.Dist(hit.Location, start)) > 8f)
                {
                    continue;
                }

                float d = FVector.Dist(hit.Location, start);
                if (d > _standoff + 60f && d < best)
                {
                    best = d;
                    FVector spot = hit.Location - dir * _standoff;
                    spot.Z = start.Z;
                    _wall = (spot, hit.Location);
                }
            }

            return _wall;
        }

        public void Dispose()
        {
            _state?.ReleaseAll();
            if (DisplayServer.GetName() != "headless")
            {
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
            }

            _root?.QueueFree();
            _root = null;
        }
    }

    /// <summary>What a step sees, and the keys it presses as Godot input events.</summary>
    private sealed class FxState
    {
        private static int s_shots;
        private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenMesh = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, long> _firstSeen = new();
        private readonly Dictionary<string, long> _startedAt = new(StringComparer.Ordinal);
        private readonly HashSet<string> _shotTaken = new(StringComparer.Ordinal);
        private readonly PlayRoot _root;
        private readonly (FVector Spot, FVector Face)? _wall;
        private readonly Dictionary<string, int> _startedBefore = new(StringComparer.Ordinal);
        private long _now;

        public FxState(PlaySession session, PlayRoot root, (FVector Spot, FVector Face)? wall)
        {
            Session = session;
            _root = root;
            _wall = wall;
        }

        public PlaySession Session { get; }

        public int Frame { get; set; }

        public int Ticks { get; set; }

        public string StepName { get; set; } = "";

        public object? Remember { get; set; }

        public SPlayerController? PC => Session.LocalController;

        public SBasePawn? Pawn => Session.LocalPawn;

        public CombatFxView? Combat => _root.Fx;

        public EffectViews? Fx => _root.Fx?.Effects;

        public bool Aiming => PC is { } pc && (pc.IsInState("s_PlayerWalkingSt2H") || pc.IsInState("s_PlayerWalkingCr2H"));

        public bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

        public void NextStep()
        {
            Frame = 0;
            Remember = null;
            PressedAt = -1;
            _firstSeen.Clear();
            _startedAt.Clear();
            _seenMesh.Clear();
            _shotTaken.Clear();
            _startedBefore.Clear();
            if (Fx is { } fx)
            {
                foreach ((string k, int v) in fx.Started)
                {
                    _startedBefore[k] = v;
                }

                fx.PeakParticles.Clear();
            }
        }

        /// <summary>Each frame before the step: notes the first tick each kind of actor appears and each effect starts.</summary>
        public void Tick(long now)
        {
            _now = now;
            Level level = Session.Client.World.Level;
            foreach (Actor a in level.AllActors<Actor>())
            {
                if (!a.bDeleteMe)
                {
                    _firstSeen.TryAdd(a.GetType(), now);
                }
            }

            if (Fx is not { } fx)
            {
                return;
            }

            foreach ((string k, int v) in fx.Started)
            {
                if (v > _startedBefore.GetValueOrDefault(k))
                {
                    _startedAt.TryAdd(k, now);
                }
            }

            Camera3D? camera = _root.GetViewport().GetCamera3D();
            foreach (Actor a in level.AllActors<Actor>())
            {
                // In view, or the camera itself (the view through a stuck sticky cam).
                if (fx.MeshOf(a) is { Visible: true } node && camera is not null
                    && (camera.IsPositionInFrustum(node.GlobalPosition) || camera.GlobalPosition.DistanceTo(node.GlobalPosition) < UeSpace.LengthToGodot(60f)))
                {
                    _seenMesh.Add(a.GetUcClass().Name);
                }
            }
        }

        /// <summary>Client ticks since the first actor of type T appeared this step, or -1.</summary>
        public int SinceFirst<T>()
            where T : Actor => _firstSeen.TryGetValue(typeof(T), out long t) ? (int)(_now - t) : -1;

        /// <summary>Client ticks since an effect class first started this step, or -1.</summary>
        public int FramesSinceStarted(string cls) => _startedAt.TryGetValue(cls, out long t) ? (int)(_now - t) : -1;

        /// <summary>A mesh of the class was drawn inside the camera's view this step.</summary>
        public bool MeshSeen(string cls) => _seenMesh.Contains(cls);

        public bool LiveMesh(string cls)
        {
            foreach (Actor a in Session.Client.World.Level.AllActors<Actor>())
            {
                if (a.GetUcClass().Name == cls && Fx?.MeshOf(a) is { Visible: true })
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The wall straight ahead of the eyes along the view (the mine trace), in uu, or null.</summary>
        public float? EyeToWall()
        {
            if (Pawn is not { } pawn || PC is not { } pc)
            {
                return null;
            }

            pc.Rotation.GetAxes(out FVector x, out _, out _);
            FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
            return GadgetWorld.Line(pawn, eye + (x * 500f), eye, SGadMultiMinesLauncher.PlaceTraceFlags, out FCheckResult h)
                ? FVector.Dist(h.Location, eye) : null;
        }

        /// <summary>At least <paramref name="count"/> particles of the class were drawn at once this step.</summary>
        public bool ParticlesSeen(string cls, int count) => Fx is { } fx && fx.PeakParticles.GetValueOrDefault(cls) >= count;

        /// <summary>A spark class of the impact table drawn this step.</summary>
        public string? SparkClassSeen()
        {
            if (Fx is not { } fx)
            {
                return null;
            }

            foreach ((string k, int v) in fx.PeakParticles)
            {
                if (v > 0 && (k.StartsWith("SSpark", StringComparison.Ordinal) || k == "SMetalSpark"))
                {
                    return k;
                }
            }

            return null;
        }

        public int ClientCount<T>()
            where T : Actor
        {
            int n = 0;
            foreach (T a in Session.Client.World.Level.AllActors<T>())
            {
                if (!a.bDeleteMe)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Saves the window at the listed ticks after <paramref name="since"/> (when a window and SVM_PLAY_SHOTS are set).</summary>
        public void ShotsAt(int since, string what, params int[] ticks)
        {
            if (since < 0)
            {
                return;
            }

            foreach (int t in ticks)
            {
                string key = $"{what}{t}";
                if (since >= t && _shotTaken.Add(key))
                {
                    Shot($"{StepName} {what} +{t}");
                }
            }
        }

        public void Shot(string step)
        {
            string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
            if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless")
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            Image image = _root.GetViewport().GetTexture().GetImage();
            string safe = string.Concat(step.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            if (safe.Length > 90)
            {
                safe = safe[..90];
            }

            image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{s_shots++:D3}-{safe}.png")));
        }

        /// <summary>
        /// Presses <paramref name="ik"/> once the pawn walks again and faces where it turns to (a large mouse turn starts
        /// the merc's fast turn, which takes no gadget keys), and releases it <paramref name="frames"/> frames later.
        /// </summary>
        public void HoldWhenWalking(string ik, int frames)
        {
            if (PressedAt < 0 && Frame >= 40 && Walking && PC is { } pc && !pc.IsInState("s_Berserk360"))
            {
                PressedAt = Frame;
                Send(ik, true);
            }
            else if (PressedAt >= 0 && Frame == PressedAt + frames)
            {
                Send(ik, false);
            }
        }

        public int PressedAt { get; set; } = -1;

        public void Tap(string ik, int at, int frames)
        {
            if (Frame == at)
            {
                Send(ik, true);
            }
            else if (Frame == at + frames)
            {
                Send(ik, false);
            }
        }

        public void MouseMove(float dx, float dy)
        {
            float width = _root.GetViewport().GetVisibleRect().Size.X;
            if (width > 1f && width < 640f)
            {
                dx *= 1280f / width;
                dy *= 1280f / width;
            }

            var ev = new InputEventMouseMotion { Relative = new Vector2(dx, dy), ScreenRelative = new Vector2(dx, dy), Position = Center };
            Input.ParseInputEvent(ev);
            Input.FlushBufferedEvents();
        }

        public void FaceWall() => Face(_wall is { } w ? w.Face - (Pawn?.Location ?? default) : null);

        public void FaceAway() => Face(_wall is { } w ? (Pawn?.Location ?? default) - w.Face : null);

        /// <summary>The view is within the turn tolerance of <see cref="Face"/> (yaw and pitch).</summary>
        public bool Facing { get; private set; }

        /// <summary>Nothing (world or actor) within <paramref name="distance"/> uu behind the eyes, against the view.</summary>
        public bool ClearBehind(float distance)
        {
            if (Pawn is not { } pawn || PC is not { } pc)
            {
                return false;
            }

            pc.Rotation.GetAxes(out FVector x, out _, out _);
            FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
            return !GadgetWorld.Line(pawn, eye - (x * distance), eye, ETraceFlags.TRACE_AllColliding, out _);
        }

        /// <summary>Holds or releases a key, sending only the changes.</summary>
        public void Hold(string ik, bool down)
        {
            if (down != _held.Contains(ik))
            {
                Send(ik, down);
            }
        }

        private void Face(FVector? direction)
        {
            if (PC is not { } pc || Pawn is null || direction is not { } d)
            {
                return;
            }

            int want = new FVector(d.X, d.Y, 0f).Rotation().Yaw;
            int err = (short)(want - pc.Rotation.Yaw);
            int pitchErr = (short)(0 - pc.Rotation.Pitch);
            Facing = Math.Abs(err) <= 150 && Math.Abs(pitchErr) <= 600;
            if (!Facing)
            {
                MouseMove(Math.Clamp(err / 60f, -40f, 40f), Math.Clamp(-pitchErr / 60f, -20f, 20f));
            }
        }

        private Vector2 Center => _root.GetViewport().GetVisibleRect().Size * 0.5f;

        public void Send(string ik, bool pressed)
        {
            InputEvent? ev = GodotKeys.ToEvent(ik);
            switch (ev)
            {
                case InputEventKey key:
                    key.Keycode = key.PhysicalKeycode;
                    key.Pressed = pressed;
                    break;
                case InputEventMouseButton button:
                    button.Pressed = pressed;
                    button.Position = Center;
                    button.GlobalPosition = Center;
                    break;
                default:
                    throw new InvalidOperationException("no Godot event for " + ik);
            }

            if (pressed)
            {
                _held.Add(ik);
            }
            else
            {
                _held.Remove(ik);
            }

            Input.ParseInputEvent(ev);
        }

        public void ReleaseAll()
        {
            foreach (string ik in new List<string>(_held))
            {
                Send(ik, false);
            }
        }

        public string Detail(string text) => string.Create(CultureInfo.InvariantCulture, $"{text} (tick {Ticks})");

        public string Describe()
        {
            string started = Fx is { } fx ? string.Join(",", fx.Started) : "no effects view";
            string peaks = Fx is { } f2 ? string.Join(",", f2.PeakParticles) : "";
            string meshes = Fx is { } f3 ? string.Join(",", f3.MeshesShown) : "";
            string missing = Fx is { } f4 ? string.Join(",", f4.Missing) : "";
            string wall = "-";
            if (Pawn is { } pawn && PC is { } pc0)
            {
                pc0.Rotation.GetAxes(out FVector x, out _, out _);
                FVector eye = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
                wall = GadgetWorld.Line(pawn, eye + (x * 500f), eye, SGadMultiMinesLauncher.PlaceTraceFlags, out FCheckResult h)
                    ? string.Create(CultureInfo.InvariantCulture, $"{FVector.Dist(h.Location, eye):F0} n={h.Normal}") : "none";
            }

            return string.Create(CultureInfo.InvariantCulture,
                $"state={PC?.GetStateName()} weapon={Pawn?.WeaponLoaded} gadget={Pawn?.SelectedGadget?.GetType().Name} charge={Pawn?.SelectedGadget?.Charge} " +
                $"started=[{started}] peaks=[{peaks}] meshes=[{meshes}] seenInView=[{string.Join(",", _seenMesh)}] missing=[{missing}] " +
                $"eyeWall={wall} rot={PC?.Rotation} mines={ClientCount<SMines>()} marks={Combat?.Marks} decal={Combat?.LastMarkDecal} muzzleMeshes={Combat?.MuzzleMeshes} casings={Combat?.Casings} pawnAt={Pawn?.Location}");
        }
    }
}
