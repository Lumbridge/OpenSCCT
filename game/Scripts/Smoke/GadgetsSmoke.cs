using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Gadgets;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=gadgets</c>: every default gadget used on SteeE through the player's keys (docs/GADGETS.md). The spy
/// walks to a wall and quick-throws the smoke and chaff grenades, draws the Shocker, scans with the heartbeat sensor,
/// fires a sticky cam, looks through it, releases its gas and leaves it. The merc then walks to a wall, switches the
/// gas mask on and off, throws a frag, goes through the camera network (next camera, exit) and places a proximity mine
/// through the mine menu. Each step checks the server's simulation, the client's state and, when the UI content is
/// there, the gadget HUD view.
/// </summary>
public sealed class GadgetsSmoke : SmokeTest
{
    public const string TestName = "gadgets";

    /// <summary>The DefaultProfile.ini kits (spec/equipment/loadouts.md), in slot order.</summary>
    public static readonly string[] SpyKit = ["SGadSmokeGrenade", "SGadChaffeGrenade", "SGadStickyCam", "SGadHeartBeatSensor"];

    public static readonly string[] MercKit = ["SGadFragGrenade", "SGadMultiMinesLauncher", "SGadCamNetwork", "SGadGasMask"];

    private const int TicksPerFrame = 8;
    private const int TeardownFrames = 4;
    private const int LegTimeoutTicks = 12000;

    private readonly List<string> _report = new();
    private int _leg;
    private Leg? _current;
    private int _teardown = -1;
    private SmokeResult? _final;

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
            _current = _leg == 0 ? SpyLeg(context) : MercLeg(context);
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
            _final = SmokeResult.Fail($"{(_leg == 0 ? "spy" : "merc")}: {result.Value.Detail} | done: {string.Join("; ", _report)}");
            return SmokeResult.Running;
        }

        _report.Add(result.Value.Detail);
        if (++_leg >= 2)
        {
            _final = SmokeResult.Pass(string.Join(" || ", _report));
        }

        return SmokeResult.Running;
    }

    // ---- the spy ----------------------------------------------------------------------------------------------

    private static Leg SpyLeg(SmokeContext context)
    {
        var leg = new Leg(context, PlaySide.Spy, standoff: 260f);
        leg.Add("kit", 600, null, s => s.Kit(SpyKit));
        leg.Add("smoke grenade (quick throw)", 900, s => s.QuickThrow("SGadSmokeGrenade"), s =>
            s.Server(h => Count<SSmokeEmitter>(h) > 0 || Exploded<SProjSmoke>(h)) ? s.Detail($"smoke burst, client saw {s.ClientSeen<SProjSmoke>()} grenade(s)") : null);
        leg.Add("chaff grenade (quick throw)", 900, s => s.QuickThrow("SGadChaffeGrenade", wait: 140), s =>
            s.Server(h => All<SProjChaffe>(h.World.Level).Any(c => c.IsInState(GadgetStates.s_Brouillage)))
                ? s.Detail($"chaff field up, client saw {s.ClientSeen<SProjChaffe>()} grenade(s)") : null);
        leg.Add("draw the Shocker", 600, s => s.Input.Set("bFireMode", s.Tick < 50), s =>
            s.Pawn is { WeaponLoaded: 2 } && s.Server(h => h.ServerPawn() is { WeaponLoaded: not 0 }) ? s.Detail("drawn") : null);
        leg.Add("heartbeat sensor", 300, s => s.Select("SGadHeartBeatSensor"), s =>
        {
            if (s.Pawn?.SelectedGadget is not SGadHeartBeatSensor { StateOfDetect: not 0 } sensor)
            {
                return null;
            }

            if (s.Hud?.GadgetViews?.Heart is { } heart && heart.Cone == HeartCone.Hidden)
            {
                return null;
            }

            return s.Detail($"scanning StateOfDetect={sensor.StateOfDetect} cone={s.Hud?.GadgetViews?.Heart.Cone.ToString() ?? "no HUD"}");
        });
        leg.Add("sticky cam (fire and look)", 900, s =>
        {
            s.Select("SGadStickyCam");
            s.FaceWall();
            if (s.Tick == 30)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s =>
        {
            if (s.PC is not { } pc || !GadgetStates.InStickyCamViewState(pc) || !s.Server(h => Count<SStickyCam>(h) > 0))
            {
                return null;
            }

            bool hudState = (pc.myHUD as SHud)?.IsInSticky() == true;
            if (!hudState || (s.Hud is { } hud && hud.ActivePage is not StickyCamPage))
            {
                return null;
            }

            return s.Detail($"cam stuck, view {pc.GetStateName()}, HUD {(s.Hud is null ? "none" : "P_StickyCam")}");
        });
        leg.Add("sticky cam gas", 600, s => s.Input.Set("bUsing", s.Tick is >= 10 and < 20), s =>
            s.Server(h => All<SStickyCam>(h.World.Level).Any(c => c.Smoke_NbDoses == 0) && Count<SStickyCamEmitter>(h) > 0)
                ? s.Detail("gas released") : null);
        leg.Add("leave the sticky cam", 600, s =>
        {
            if (s.Tick == 10)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s => s.PC is { } pc && !GadgetStates.InStickyCamView(pc) && (pc.myHUD as SHud)?.IsInSticky() != true &&
            (s.Hud is null || s.Hud.ActivePage is SpyHudPage) ? s.Detail($"back in {pc.GetStateName()}") : null);
        return leg;
    }

    // ---- the merc ---------------------------------------------------------------------------------------------

    private static Leg MercLeg(SmokeContext context)
    {
        var leg = new Leg(context, PlaySide.Merc, standoff: 34f);
        leg.Add("kit", 600, null, s => s.Kit(MercKit));

        // A half turn sets off the merc's fast turn (s_Berserk360), which takes no gadget keys: wait for the walk.
        leg.Add("turn away from the wall", 300, s => s.FaceAway(), s => s.Tick > 30 && s.Walking ? s.Detail($"in {s.PC!.GetStateName()}") : null);
        leg.Add("gas mask on", 400, s =>
        {
            s.Select("SGadGasMask");
            s.FaceAway();
            if (s.Tick == 20)
            {
                s.Note = $"altfire at {s.Tick}: canFire={s.PC?.CheckIfCanFire()} state={s.PC?.GetStateName()} slots={(s.Pawn is null ? -1 : PawnInventory.TeamSlots(s.Pawn))} sel={s.Pawn?.SelectedGadget?.GetType().Name}";
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s =>
        {
            if (!s.Server(h => h.ServerPawn() is { bGasMask: true }))
            {
                return null;
            }

            if (s.Hud?.GadgetViews?.GasMask is { } mask && !(mask.View.Shown && mask.View.Worn))
            {
                return null;
            }

            float air = s.Pawn?.SelectedGadget is SGadGasMask m ? m.MaskDuration : -1f;
            return s.Detail($"worn, air {air:F1} s");
        });
        leg.Add("gas mask off", 400, s =>
        {
            if (s.Tick == 20)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s => s.Server(h => h.ServerPawn() is { bGasMask: false }) ? s.Detail("off") : null);
        leg.Add("camera network", 900, s =>
        {
            s.Select("SGadCamNetwork");
            s.FaceAway();
            if (s.Tick == 30)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s =>
        {
            if (s.PC is not { } pc || pc.GetStateName() != GadgetStates.s_UseObject_CamNetwork || s.Pawn?.UsedObject is not SCamNetwork cam)
            {
                return null;
            }

            if ((pc.myHUD as SHud)?.IsInCamNetwork() != true)
            {
                return null;
            }

            if (s.Hud is { } hud && (hud.ActivePage is not CamNetworkPage page || page.CameraName.Length == 0))
            {
                return null;
            }

            s.Remember = cam;
            string name = (s.Hud?.ActivePage as CamNetworkPage)?.CameraName ?? cam.CamName;
            return s.Detail($"looking through '{name}'");
        });
        leg.Add("next camera", 600, s => s.Input.Set("bUsing", s.Tick is >= 10 and < 16), s =>
            s.Pawn?.UsedObject is SCamNetwork cam && !ReferenceEquals(cam, s.Remember) && s.Server(h => h.ServerPawn()?.UsedObject is SCamNetwork)
                ? s.Detail($"switched to '{cam.CamName}'") : null);
        leg.Add("leave the camera network", 600, s =>
        {
            if (s.Tick == 10)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }
        }, s => s.PC is { } pc && !GadgetStates.InStickyCamView(pc) && s.Pawn?.UsedObject is null &&
            (s.Hud is null || s.Hud.ActivePage is MercHudPage) ? s.Detail($"back in {pc.GetStateName()}") : null);
        leg.Add("walk up to the wall", 200, s =>
        {
            s.Select("SGadMultiMinesLauncher");
            s.FaceWall();
            s.Input.Set("BT_Up", s.Tick < 40);
        }, s => s.Tick > 70 && s.Walking ? s.Detail("at the wall") : null);
        leg.Add("mine menu", 600, s =>
        {
            s.FaceWall();
            if (s.Tick == 10)
            {
                if (s.Pawn?.SelectedGadget is SGadMultiMinesLauncher l)
                {
                    l.UseMine();
                    s.Note = $"wall {s.WallDistance():F0} uu, hit={l.HitActor?.GetType().Name} normal={l.HitNormal} rot={s.PC?.Rotation} walking={s.Pawn.Physics}";
                }

                s.Input.Commands.Add(new InputCommand("AltFire"));
            }

            s.Input.Set("bAltFire", s.Tick >= 10);
        }, s =>
        {
            if (s.PC is not { } pc || pc.GetStateName() != GadgetStates.s_PutMineOnWall_InteractionMenu)
            {
                return null;
            }

            if (s.Hud?.GadgetViews?.MineMenu is { } menu && menu.Menu is null)
            {
                return null;
            }

            return s.Detail($"menu up{(s.Hud is null ? "" : " on the HUD")}");
        });
        leg.Add("proximity mine", 900, s => s.Input.Set("bAltFire", false), s =>
            s.Server(h => All<SMineProx>(h.World.Level).Any(m => !m.bDeleteMe))
                ? s.Detail($"mine placed ({s.Server(h => All<SMineProx>(h.World.Level).First().GetStateName().ToString())})") : null);
        leg.Add("turn away again", 300, s => s.FaceAway(), s => s.Tick > 30 && s.Walking ? s.Detail($"in {s.PC!.GetStateName()}") : null);

        // Last: a short throw can catch the thrower in the blast.
        leg.Add("frag grenade", 900, s =>
        {
            s.Select("SGadFragGrenade");
            s.FaceAway();
            if (s.Tick == 30)
            {
                s.Input.Commands.Add(new InputCommand("AltFire"));
            }

            s.Input.Set("bAltFire", s.Tick is >= 30 and < 130);
        }, s => s.Server(h => Exploded<SProjFrag>(h)) ? s.Detail($"frag thrown and exploded, client saw {s.ClientSeen<SProjFrag>()}") : null);
        return leg;
    }

    /// <summary>The live actors of a class in a level, in actor order.</summary>
    private static List<T> All<T>(Level level)
        where T : Actor
    {
        var list = new List<T>();
        foreach (T a in level.AllActors<T>())
        {
            if (!a.bDeleteMe)
            {
                list.Add(a);
            }
        }

        return list;
    }

    private static int Count<T>(ServerHost host)
        where T : Actor
    {
        int n = 0;
        foreach (T a in host.World.Level.AllActors<T>())
        {
            if (!a.bDeleteMe)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>A projectile of the class burst on the server (it has exploded, or a burst left it hidden).</summary>
    private static bool Exploded<T>(ServerHost host)
        where T : Projectile
    {
        foreach (T p in host.World.Level.AllActors<T>())
        {
            if (p.bHidden || p.IsInState(GadgetStates.s_Brouillage) || p is SProjSmoke { Exploded: true } || p is SProjChaffe { Exploded: true })
            {
                return true;
            }
        }

        return Seen.TryGetValue(typeof(T), out bool seen) && seen && !All<T>(host.World.Level).Any();
    }

    private static readonly Dictionary<Type, bool> Seen = new();

    /// <summary>One play of SteeE on one side, with its scripted steps.</summary>
    private sealed class Leg : IDisposable
    {
        private readonly List<(string Name, int Timeout, Action<StepState>? Act, Func<StepState, string?> Done)> _steps = new();
        private readonly List<string> _results = new();
        private readonly PlaySide _side;
        private readonly float _standoff;
        private PlayRoot? _root;
        private ScriptedPilot? _pilot;
        private (FVector Spot, FVector Face)? _wall;
        private StepState? _state;
        private int _index;
        private bool _hooked;

        public Leg(SmokeContext context, PlaySide side, float standoff)
        {
            _side = side;
            _standoff = standoff;
            Seen.Clear();
            _pilot = new ScriptedPilot(Wall, PilotArrival.FaceOnly);
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "SteeE",
                    Side = side,
                    Mode = "Story",
                    HostMode = PlayHostMode.Inline,
                    MapRoots = [.. context.App.Options.MapRoots],
                },
                Render = true,
                Hud = false,
                TicksPerFrame = TicksPerFrame,
                Pilot = _pilot,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
            Hud = HudLayer.Attach(_root, () => _root?.Session?.LocalController, out string? error);
            if (Hud is not null)
            {
                Hud.ScreenSize = new Vector2(1280f, 720f);
            }
            else
            {
                GD.Print($"SVM_SMOKE {TestName} no HUD views checked: {error}");
            }

        }

        public HudLayer? Hud { get; }

        public void Add(string name, int timeoutTicks, Action<StepState>? act, Func<StepState, string?> done) =>
            _steps.Add((name, timeoutTicks, act, done));

        public SmokeResult? Step(SmokeContext context)
        {
            if (_root is null || _pilot is null)
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

            if (session.LoginError is { } login)
            {
                return SmokeResult.Fail("login refused: " + login);
            }

            if (_pilot.Phase == PilotPhase.Failed)
            {
                return SmokeResult.Fail($"pilot failed: {_pilot.Failure}");
            }

            if (!_hooked)
            {
                // The steps press keys after the pilot each client tick.
                Action<PlaySession, long>? before = session.BeforeClientTick;
                session.BeforeClientTick = (s, t) =>
                {
                    before?.Invoke(s, t);
                    BeforeTick(s);
                };
                _hooked = true;
            }

            if (_root.Stats.Ticks > LegTimeoutTicks)
            {
                return SmokeResult.Fail($"no result after {LegTimeoutTicks} ticks at step {Current} ({_results.Count} done)");
            }

            if (_pilot.Phase != PilotPhase.Done)
            {
                return null;
            }

            Track(session);
            if (_state is null)
            {
                _state = new StepState(session, Hud, _wall);
            }

            (string name, int timeout, _, Func<StepState, string?> done) = _steps[_index];
            _state.Refresh();
            string? detail;
            try
            {
                detail = done(_state);
            }
            catch (InvalidOperationException ex)
            {
                return SmokeResult.Fail($"step '{name}' check threw: {ex.Message}");
            }

            if (detail is null)
            {
                if (_state.Tick > timeout)
                {
                    return SmokeResult.Fail($"step '{name}' did not happen in {timeout} ticks ({_state.Describe()})");
                }

                if (context.Frame % 60 == 0)
                {
                    GD.Print($"SVM_SMOKE {TestName} {_side} step '{name}' tick {_state.Tick}: {_state.Describe()}");
                }

                return null;
            }

            _results.Add($"{name}: {detail}");
            GD.Print($"SVM_SMOKE {TestName} {_side} {name}: {detail}");
            _index++;
            _state.NextStep();
            if (_index < _steps.Count)
            {
                return null;
            }

            return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
                $"{_side} ({_root.Stats.Ticks} ticks): {string.Join("; ", _results)}"));
        }

        private string Current => _index < _steps.Count ? _steps[_index].Name : "end";

        private void BeforeTick(PlaySession session)
        {
            if (_pilot?.Phase != PilotPhase.Done || _state is null || _index >= _steps.Count)
            {
                return;
            }

            _state.Refresh();
            _steps[_index].Act?.Invoke(_state);
            _state.Tick++;
        }

        /// <summary>Notes the gadget projectiles the server and the client have seen (a burst one may be gone at once).</summary>
        private static void Track(PlaySession session)
        {
            session.ServerSnapshot(h =>
            {
                Mark<SProjSmoke>(h.World.Level);
                Mark<SProjChaffe>(h.World.Level);
                Mark<SProjFrag>(h.World.Level);
                return 0;
            });
        }

        private static void Mark<T>(Level level)
            where T : Actor
        {
            if (All<T>(level).Any())
            {
                Seen[typeof(T)] = true;
            }
        }

        /// <summary>
        /// The pilot's target: the nearest wall in one of 16 directions from where the pawn stands, the spot
        /// <see cref="_standoff"/> uu in front of it and the wall to face. Found once.
        /// </summary>
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
                if (!GadgetWorld.Line(pawn, start + dir * 1500f, start, ETraceFlags.TRACE_World, out FCheckResult hit) ||
                    MathF.Abs(hit.Normal.Z) > 0.3f)
                {
                    continue;
                }

                // The same wall at eye height (the mine and sticky cam traces start there), facing the pawn.
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
            _root?.QueueFree();
            _root = null;
        }
    }

    /// <summary>What a step sees and presses: the session, the client's controller and pawn, the keys, the HUD.</summary>
    private sealed class StepState
    {
        private readonly (FVector Spot, FVector Face)? _wall;
        private readonly Dictionary<Type, int> _clientSeen = new();

        public StepState(PlaySession session, HudLayer? hud, (FVector Spot, FVector Face)? wall)
        {
            Session = session;
            Hud = hud;
            _wall = wall;
        }

        public PlaySession Session { get; }

        public HudLayer? Hud { get; }

        public int Tick { get; set; }

        public SPlayerController? PC { get; private set; }

        public SBasePawn? Pawn { get; private set; }

        public LocalInput Input => Session.Input;

        /// <summary>A step's own note for the failure line.</summary>
        public string Note { get; set; } = "";

        /// <summary>Something a step keeps for the next one (the camera looked through).</summary>
        public object? Remember { get; set; }

        public void Refresh()
        {
            PC = Session.LocalController;
            Pawn = PC?.Pawn as SBasePawn;
            if (Session.Client.World.Level is { } level)
            {
                Count<SProjSmoke>(level);
                Count<SProjChaffe>(level);
                Count<SProjFrag>(level);
            }
        }

        private void Count<T>(Level level)
            where T : Actor
        {
            int n = All<T>(level).Count();
            if (n > _clientSeen.GetValueOrDefault(typeof(T)))
            {
                _clientSeen[typeof(T)] = n;
            }
        }

        /// <summary>The most of <typeparamref name="T"/> the client's world held at once.</summary>
        public int ClientSeen<T>()
            where T : Actor => _clientSeen.GetValueOrDefault(typeof(T));

        public void NextStep() => Tick = 0;

        public T Server<T>(Func<ServerHost, T> read) => Session.ServerSnapshot(read);

        public string Detail(string text) => string.Create(CultureInfo.InvariantCulture, $"{text} (tick {Tick})");

        /// <summary>The kit check: the client's and the server's gadgets in slot order are the profile's.</summary>
        public string? Kit(string[] expected)
        {
            // Also past the first two seconds of play, when no quick throw is allowed yet.
            if (Pawn is null || Pawn.Level.TimeSeconds < K.QuickLaunchCooldown + 0.5f)
            {
                return null;
            }

            string client = string.Join(",", Gadgets(Pawn));
            string server = Server(h => h.ServerPawn() is { } p ? string.Join(",", Gadgets(p)) : "");
            string want = string.Join(",", expected);
            if (client != want || server != want)
            {
                string more = Server(h => h.ServerPawn() is { } p
                    ? $"server pawn {p.GetType().Name} weapon={p.MyWeapon?.GetType().Name} first={p.MyGadget?.GetType().Name} gadgets={All<SGadget>(h.World.Level).Count} " +
                      $"profile={h.World.Level.GetService<OpenScct.Game.Devices.IDeviceHooks>()?.GetPlayerProfile() is not null} ports={h.World.Level.GetService<OpenScct.Game.GameFlow.IGameFlowPorts>()?.GetType().Name}"
                    : "no server pawn");
                more += Server(h => h.World.Level.GetService<NetCombatBridge>() is { } b ? $" server sent={b.Sent} recv={b.Received} dropped={b.Dropped} waiting={b.Waiting}" : " no server bridge");
                more += Session.Client.World.Level.GetService<NetCombatBridge>() is { } c ? $" client sent={c.Sent} recv={c.Received} held={c.Held}" : " no client bridge";
                more += $"; client weapon={Pawn.MyWeapon?.GetType().Name} first={Pawn.MyGadget?.GetType().Name} gadgets={All<SGadget>(Session.Client.World.Level).Count}";
                return Tick > 500 ? throw new InvalidOperationException($"kit client=[{client}] server=[{server}], expected [{want}]; {more}") : null;
            }

            int charge = Pawn.SelectedGadget?.Charge ?? 0;
            return Detail($"[{want}] selected {Pawn.SelectedGadget?.GetType().Name} charge {charge}");
        }

        /// <summary>The selectable gadgets of a pawn in slot order (the MyGadget chain).</summary>
        public static IEnumerable<string> Gadgets(SBasePawn pawn)
        {
            int guard = 0;
            for (SGadget? g = pawn.MyGadget; g is not null && guard < 10; g = g.MyGadget, guard++)
            {
                if (PawnInventory.IsSelectable(g))
                {
                    yield return g.GetType().Name;
                }
            }
        }

        /// <summary>The slot of a gadget class in the client pawn's kit, or -1.</summary>
        public int SlotOf(string gadget) => Pawn is null ? -1 : Gadgets(Pawn).ToList().IndexOf(gadget);

        /// <summary>Selects the gadget with its slot key (once, on the step's first tick).</summary>
        public void Select(string gadget)
        {
            if (Tick == 0 && SlotOf(gadget) is var slot and >= 0)
            {
                Input.Commands.Add(new InputCommand("SelectGadget", slot));
            }
        }

        /// <summary>Selects then quick-throws a grenade (the quick choice key of its slot) after <paramref name="wait"/> ticks.</summary>
        public void QuickThrow(string gadget, int wait = 10)
        {
            Select(gadget);
            if (Tick == wait && SlotOf(gadget) is var slot and >= 0)
            {
                Input.Commands.Add(new InputCommand("GadgetQuickChoice", slot));
            }
        }

        /// <summary>The controller walks (not in a turn, a transition or a gadget state).</summary>
        public bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

        public void FaceWall() => Face(0);

        /// <summary>The horizontal distance from the pawn to the wall point the pilot faced.</summary>
        public float WallDistance() => Pawn is { } p && _wall is { } w
            ? MathF.Sqrt((w.Face.X - p.Location.X) * (w.Face.X - p.Location.X) + (w.Face.Y - p.Location.Y) * (w.Face.Y - p.Location.Y))
            : -1f;

        public void FaceAway() => Face(32768);

        private void Face(int extraYaw)
        {
            if (PC is { } pc && Pawn is { } pawn && _wall is { } w)
            {
                FVector d = w.Face - pawn.Location;
                pc.SetViewRotation(new FRotator(0, new FVector(d.X, d.Y, 0f).Rotation().Yaw + extraYaw, 0));
            }
        }

        public string Describe() => string.Create(CultureInfo.InvariantCulture,
            $"note=[{Note}] client={PC?.GetStateName()} server={Session.ServerController?.GetStateName()} gadget={Pawn?.SelectedGadget?.GetType().Name} " +
            $"weapon={Pawn?.WeaponLoaded} mask={Pawn?.bGasMask} gadLast={Pawn?.SelectedGadget?.LastUsed:F2} canFire={PC?.CheckIfCanFire()} own={Pawn?.SelectedGadget?.Owner?.GetType().Name} sb={PC?.SBPawn is not null} tick={Tick} transit={Pawn?.bWeaponTransit} pad={PC?.iDontUpdatePadInput} inv={PC?.bIsInInventoryMode} shock={Pawn?.bStickyShocking} log={string.Join("|", Session.Log)} unc={PC?.bUnCrouchAndUnDraw} query={PC?.bQuickGadgetQuery} last={PC?.fDateLaunchedLastQuickGadget:F2} now={PC?.Level.TimeSeconds:F2} fdm={PC?.fDateFireMode:F2} " +
            $"slots={(Pawn is null ? -1 : PawnInventory.TeamSlots(Pawn))} team={(Pawn?.PlayerReplicationInfo?.Team as STeamInfo)?.iNBGadgets} crouched={Pawn?.bIsCrouched} canUncrouch={(Pawn as SPawnAttaque)?.CanUnCrouch()} cwr={PC?.bCrouchWithRoll} used={Pawn?.UsedObject?.GetType().Name} hud={(PC?.myHUD as SHud)?.GetStateName()} page={Hud?.ActivePage?.GetType().Name}");
    }
}

/// <summary>Server-side helpers of the gadget smoke.</summary>
internal static class GadgetsSmokeServer
{
    /// <summary>The local player's pawn on the server.</summary>
    public static SBasePawn? ServerPawn(this ServerHost host)
    {
        foreach (SPlayerController pc in host.World.Level.AllActors<SPlayerController>())
        {
            if (pc.Pawn is SBasePawn p)
            {
                return p;
            }
        }

        return null;
    }
}
