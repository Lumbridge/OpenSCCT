using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScctGame.Play.Session;

/// <summary>TestRange lessons using the normal client/server simulation and player input. No alternate mechanics.</summary>
public sealed class BrowserLesson : IDisposable
{
    private readonly PlaySession _session;
    private readonly Queue<InputCommand> _commands = new();
    private bool _placed, _moving, _forward, _back, _hadForcedMove;
    private int _direction = 1;
    private long _landed = -1, _visionPressed = -1;
    private bool _wasFalling, _wasLanding;
    private int _lastVision;
    private bool _returnRequested;
    private long _returnedAt, _modeChanged = -1;
    private int _observedVision;
    private bool _keptMoving;
    private readonly List<VisionAttempt> _history = new();

    public sealed record VisionAttempt(int Number, string Result, double RecoveryMs, double? SwitchMs,
        double? VisionMs, double? SavedMs, bool Moving);
    public IReadOnlyList<VisionAttempt> History => _history;
    public double? BaselineMs { get; private set; }
    public double? BestMs { get; private set; }
    public int Returns { get; private set; }
    public static FVector ReturnPortal => new(7136, 4800, 90);
    public const float PortalRadius = 100;

    public BrowserLesson(PlaySession session, string kind)
    {
        if (session.IsRemote || session.Settings.HostMode != PlayHostMode.Inline ||
            session.Settings.Map != "TestRange" || session.Settings.Rules != PlayRules.PlayLevel)
            throw new ArgumentException("Browser lessons require local TestRange practice.", nameof(session));
        _session = session;
        Kind = kind;
        if (session.Bots is { } bots) bots.Stopped = true;
        session.Host.BeforeWorldTick += BeforeServerTick;
        session.BeforeClientTick += BeforeClientTick;
        session.AfterClientTick += AfterClientTick;
    }

    public string Kind { get; }
    public string Feedback { get; private set; } = "Preparing practice…";
    public bool Ready => _placed;
    public bool Moving => _moving;
    public bool Debug { get; set; } = true;
    public SBasePawn? Target { get; private set; }
    public float? Distance => Target is not null && _session.LocalPawn is { } pawn ? FVector.Dist(pawn.Location, Target.Location) : null;
    public int Attempts { get; private set; }
    public int Grabs { get; private set; }
    public double? RecoveryMs { get; private set; }
    public string State => _session.LocalController?.GetStateName().ToString() ?? "Loading";
    public int Vision => _session.LocalController?.RendMap ?? 0;

    public static (FVector Location, FRotator Rotation) Start(string kind) => kind == "vision-cancelling"
        ? (new FVector(6596, 4608, 686), default)
        : (new FVector(3840, 2560, 86), default);

    public void Action(string action)
    {
        if (!_placed) return;
        switch (action)
        {
            case "reset" when Kind == "vision-cancelling": _returnRequested = true; break;
            case "drop": _forward = true; _back = false; break;
            case "forward": _forward = true; _back = false; break;
            case "back": _back = true; _forward = false; break;
            case "stop": _forward = _back = false; break;
            case "night": _commands.Enqueue(new("SwitchHeadset", 1)); break;
            case "thermal": _commands.Enqueue(new("SwitchHeadset", 2)); break;
            case "grab": Attempts++; _commands.Enqueue(new("GrabOnly")); break;
            case "moving": _moving = !_moving; break;
            case "debug": Debug = !Debug; break;
        }
    }

    private void BeforeClientTick(PlaySession session, long tick)
    {
        while (_commands.TryDequeue(out var command)) session.Input.Commands.Add(command);
        foreach (var command in session.Input.Commands)
            if (command.Name.Equals("SwitchHeadset", StringComparison.OrdinalIgnoreCase))
            {
                if (RecoveryMs is not null)
                    Feedback = "That switch came after recovery ended. Walk into the green portal to try earlier.";
                else if (_visionPressed < 0) _visionPressed = tick;
            }
        if (_forward || _back || _hadForcedMove)
        {
            session.Input.Set("BT_Up", _forward);
            session.Input.Set("BT_Down", _back);
        }
        _hadForcedMove = _forward || _back;
    }

    private void BeforeServerTick(ServerHost host)
    {
        if (_session.Bots is not { } bots) return;
        // Held AI never perceives or attacks. The moving exercise supplies only walking input, after AI's idle tick.
        bots.Stopped = !_moving;
        foreach (var ai in bots.Bots)
        {
            ai.Held = true;
            var pc = ai.Controller;
            Protect(pc);
            if (!_moving || pc.Pawn is not SBasePawn { Health: > 0 } pawn || pawn.m_AttackTarget is not null ||
                !pc.GetStateName().ToString().Contains("Walking", StringComparison.Ordinal)) continue;
            if (pawn.Location.X > 4544) _direction = -1;
            if (pawn.Location.X < 4032) _direction = 1;
            var view = new FRotator(0, _direction > 0 ? 0 : 32768, 0);
            new AiInput { BT_Up = 1, View = view, Aim = view }.ApplyTo(pc);
        }
    }

    private void AfterClientTick(PlaySession session, long tick)
    {
        if (session.LocalPawn is not { } pawn || session.LocalController is not { } pc || session.ServerController is not { Pawn: SBasePawn serverPawn } authority) return;
        Protect(authority);
        Protect(pc);
        if (!_placed)
        {
            var start = Start(Kind);
            Place(authority, serverPawn, start.Location, start.Rotation);
            Place(pc, pawn, start.Location, start.Rotation);
            if (Kind == "spy-grab")
            {
                if (session.Bots?.Controllers is not { Count: > 0 } controllers || controllers[0] is not SPlayerController { Pawn: SBasePawn merc } bot) return;
                var at = new FVector(4096, 2560, merc.CollisionHeight + 2);
                Place(bot, merc, at, default);
                merc.iLastWakeTime = -100;
                // Network replicas have their own local actor ids; this lesson contains exactly one mercenary.
                foreach (var replica in session.Client.World.Level.AllActors<SPAWNDEFENSE>())
                    if (!replica.bDeleteMe && replica.Health > 0) { Target = replica; break; }
                if (Target is null) return;
            }
            _placed = true;
            _forward = _back = false;
            _wasFalling = _wasLanding = false;
            _landed = _visionPressed = -1;
            RecoveryMs = null;
            Feedback = Kind == "spy-grab" ? "Approach behind the mercenary and use Grab only or Interact."
                : "First, walk off without switching to measure your normal recovery. Then use the green portal and try switching vision at touchdown while moving.";
        }
        if (Kind == "spy-grab")
        {
            if (Target is { } target)
            {
                string next = pc.DescribeGrab(target);
                if (next == "Mercenary grabbed." && Feedback != next) Grabs++;
                Feedback = next;
            }
            return;
        }
        if (_returnRequested || tick - _returnedAt > Sim.Hz && pawn.Location.Z < 180 &&
            (pawn.Location - ReturnPortal).Size2D() < PortalRadius)
        {
            _returnRequested = false;
            Returns++;
            _returnedAt = tick;
            var start = Start(Kind);
            Place(authority, serverPawn, start.Location, start.Rotation);
            Place(pc, pawn, start.Location, start.Rotation);
            session.Host.Movement.EndpointOf(authority)?.Server?.ReinitPawn();
            serverPawn.DistantClientReinit(0);
            _forward = _back = false;
            _wasFalling = _wasLanding = _keptMoving = false;
            _landed = _visionPressed = _modeChanged = -1;
            _lastVision = _observedVision = pc.Goggle?.CurrentMode ?? 5;
            RecoveryMs = null;
            Feedback = "Back on the ledge. " + (BaselineMs is null
                ? "Try a drop without switching to measure your baseline." : "Keep moving and switch vision at touchdown. Your previous results are below.");
            return;
        }
        bool falling = pawn.Physics == EPhysics.PHYS_Falling;
        bool landing = pc.IsInState("PlayerLanding");
        int vision = pc.Goggle?.CurrentMode ?? 5;
        if (vision != _observedVision && _visionPressed >= 0) _modeChanged = tick;
        _observedVision = vision;
        if (_wasFalling && !falling && _landed < 0)
        {
            _landed = tick;
            Feedback = "Touchdown. Switch vision and keep moving.";
        }
        if (landing && _landed >= 0)
        {
            _wasLanding = true;
            _keptMoving |= session.Input.Get("BT_Up") != 0 || session.Input.Get("BT_Down") != 0 ||
                session.Input.Get("BT_Left") != 0 || session.Input.Get("BT_Right") != 0;
        }
        if (_wasLanding && !landing && _landed >= 0 && RecoveryMs is null)
        {
            Attempts++;
            RecoveryMs = (tick - _landed) * 1000.0 / Sim.Hz;
            _forward = false;
            bool switched = _visionPressed >= _landed - 1 && pc.Goggle?.CurrentMode != _lastVision;
            if (_visionPressed < 0) BaselineMs ??= RecoveryMs;
            double? timing = _visionPressed < 0 ? null : (_visionPressed - _landed) * 1000.0 / Sim.Hz;
            double? saved = BaselineMs is { } baseline ? baseline - RecoveryMs.Value : null;
            bool faster = saved > 1000.0 / Sim.Hz;
            string result = _visionPressed < 0 ? "Baseline" : timing < -1000.0 / Sim.Hz ? "Too early"
                : !_keptMoving ? "Keep moving" : switched && faster ? "Cancelled"
                : switched && BaselineMs is null ? "Vision switched" : "Try earlier";
            if (switched && _keptMoving) BestMs = Math.Min(BestMs ?? double.MaxValue, RecoveryMs.Value);
            var attempt = new VisionAttempt(Attempts, result, RecoveryMs.Value, timing,
                _modeChanged < 0 ? null : (_modeChanged - _landed) * 1000.0 / Sim.Hz, saved, _keptMoving);
            _history.Add(attempt);
            if (_history.Count > 6) _history.RemoveAt(0);
            string speed = faster ? $"Saved {saved:0} ms ({saved / BaselineMs * 100:0}%) against your baseline. "
                : $"Recovery {RecoveryMs:0} ms. ";
            string advice = result switch
            {
                "Baseline" => "Baseline recorded. Next time, switch at touchdown while holding movement.",
                "Too early" => $"Switched {-timing:0} ms before touchdown. Wait until your feet land.",
                "Keep moving" => "Hold a movement direction through the landing as you switch.",
                _ => timing is { } ms ? $"Switch input {ms:0} ms after touchdown. " +
                    (BaselineMs is null ? "Do one drop without switching to measure your saving." : "Try switching closer to touchdown for a faster recovery.") : "",
            };
            Feedback = result + ". " + speed + advice + " Walk into the green portal on your right to return to the ledge.";
        }
        // Ignore the few centimetres of settling at spawn on top of the tower.
        if (falling && pawn.Location.Z < 640) Feedback = "Falling. Wait for your feet to touch the floor.";
        _wasFalling = falling && pawn.Location.Z < 640;
        if (_landed < 0 && !falling) _lastVision = pc.Goggle?.CurrentMode ?? 0;
    }

    private static void Place(SPlayerController pc, SBasePawn pawn, FVector at, FRotator facing)
    {
        pawn.m_AttackTarget = null;
        pawn.Health = 100;
        pawn.Velocity = pawn.Acceleration = default;
        pawn.Physics = EPhysics.PHYS_Walking;
        pawn.bWantsToCrouch = false;
        pc.bCrouchWithRoll = false;
        if (OpenScct.Game.Movement.MovementNatives.Driver(pawn) is { } driver)
        {
            driver.FarMoveActor(pawn, at);
            driver.GetState(pawn).TraversalBase = null;
        }
        else pawn.SetLocation(at);
        pawn.SetBase(null);
        pawn.bJustTeleported = true;
        pawn.Rotation = facing;
        pc.Location = pawn.Location;
        pc.SetViewRotation(facing);
        pc.MyDesiredRotation = pc.LocalMyDesiredRotation = facing;
        pc.GotoWaitingState();
    }

    private static void Protect(SPlayerController pc)
    {
        var rules = PracticeRules.Of(pc);
        if (rules is null)
        {
            rules = new PracticeRules();
            pc.XLevel.SetService(rules);
        }
        rules.On = true;
        rules.SetTank(pc, true);
    }

    public void Dispose()
    {
        _session.Host.BeforeWorldTick -= BeforeServerTick;
        _session.BeforeClientTick -= BeforeClientTick;
        _session.AfterClientTick -= AfterClientTick;
    }
}
