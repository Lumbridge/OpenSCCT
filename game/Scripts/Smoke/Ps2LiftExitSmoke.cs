using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Real local input and authority/client checks on the installed DTK grouped lifts and one-way exits.</summary>
public sealed class Ps2LiftExitSmoke : SmokeTest
{
    public const string TestName = "ps2-lift-exit";
    public override string Name => TestName;
    private readonly string _case = System.Environment.GetEnvironmentVariable("SVM_PS2_WORLD_CASE") ?? "lift-b";
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SInterrupteur? _panel, _call, _using;
    private Mover? _lift, _door;
    private SPresenceTrigger? _detector;
    private int _phase, _frames, _teardown, _exit;
    private double _since;
    private float _startZ, _normal;
    private bool _travelShot;
    private readonly HashSet<string> _states = [];
    private string _diagnosticState = "";
    private bool _standingExitRequested;
    private int _standingFrames;
    private readonly bool _standAtExit = System.Environment.GetEnvironmentVariable("SVM_PS2_EXIT_STANCE") != "crouched";
    private bool _encroachDiagnostics;
    private int _retryRecovery;
    private double _retryRecoveryAt;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass(_case + ": normal input and both peers verified") : SmokeResult.Running;
        if (_root is null)
        {
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with
            {
                Settings = launch.Settings with { Map = "DTK02P", Mode = "Extraction", Side = PlaySide.Spy,
                    Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline, Bots = 0 },
                TicksPerFrame = 2, Audio = false, Render = DisplayServer.GetName() != "headless", Hud = true,
            });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session is not { LocalController.SBPawn: { } pawn,
            ServerController.SBPawn: { } host } session) return SmokeResult.Running;
        var pc = session.LocalController!;
        if (!_encroachDiagnostics && System.Environment.GetEnvironmentVariable("SVM_PS2_EXIT_ENCROACH") == "1")
        {
            _encroachDiagnostics = true;
            var level = session.Host.World.Level;
            var service = level.GetService<IActorNatives>()!;
            level.SetService<IActorNatives>(new ExitEncroachDiagnostics(service, () => _door));
        }
        _keys ??= KeyPilot.Attach(_root); _keys.Update(); ++_frames;
        _states.Add(pc.GetStateName().ToString());
        if (_panel is not null && _phase is 2 or 3)
        {
            string state = pc.GetStateName() + "/" + session.ServerController!.GetStateName() + "/" + _panel.TriggerInteraction?.GetStateName();
            if (state != _diagnosticState || _frames % 60 == 0)
            {
                _diagnosticState = state; DiagnoseHack(session.ServerController!, pc);
            }
        }
        if (_frames % 60 == 0) GD.Print($"PS2_WORLD case={_case} phase={_phase} client={pc.GetStateName()} at={pawn.Location} authority={host.Location} base={host.Base?.ActorId} lift={_lift?.KeyNum}/{_lift?.PrevKeyNum} detector={_detector?.IsPresenceActive}");
        if (!_keys.Ready || session.TicksRun < 360) return SmokeResult.Running;
        double elapsed = context.ElapsedSeconds - _since;
        if (_phase == 0)
        {
            if (_case.StartsWith("exit", StringComparison.Ordinal)) { BeginExit(context); return SmokeResult.Running; }
            bool a = _case == "lift-a";
            var level = session.Host.World.Level;
            _lift = level.Actors.OfType<Mover>().Single(m => m.Tag == new Name(a ? "UnknownGlobalPS2_867191" : "UnknownGlobalPS2_867192"));
            FVector panelAt = a ? new FVector(3231.2f, -2829.5f, 133) : new FVector(1301.9f, -6111, 134);
            _panel = level.Actors.OfType<SInterrupteur>().OrderBy(s => FVector.Dist(s.Location, panelAt)).First();
            _call = level.Actors.OfType<SInterrupteur>().Single(s => s.Event == _lift.Tag && s.sDescriptionText == "Go up");
            GD.Print($"PS2_WORLD_SETUP panel={_panel.Location} hack={_panel.iHackingTime} call={_call.Location} usable={_call.InteractionObj?.bUsable} team={_call.InteractionObj?.UsableBy}");
            BeginApproach(_panel, context); return SmokeResult.Running;
        }
        if (_phase == 1)
        {
            float distance = FlatDistance(pawn.Location, _using!.Location);
            if (elapsed < .5) return SmokeResult.Running;
            if (distance > _using.fInteractionRadius + 12)
            {
                _keys.Press("BT_Up");
                if (elapsed > 8) return SmokeResult.Fail($"Normal approach stalled at {pawn.Location}, target {_using.Location}, distance {distance}");
            }
            else { _keys.Release("BT_Up"); _keys.Press("bUsing"); Next(2, context); }
        }
        else if (_phase == 2)
        {
            bool selected = pc.IManager?.GetCurrentInteraction() is STriggerInteraction { Owner: SInterrupteur owner } &&
                FVector.Dist(owner.Location, _using!.Location) < 1;
            if (pc.IsInState("s_MenuInteraction") && selected)
            {
                _keys.Release("bUsing"); Save(context, ReferenceEquals(_using, _panel) ? "lift-hack-selected.png" : "lift-go-up-selected.png");
                Next(ReferenceEquals(_using, _panel) ? 3 : 4, context);
            }
            else if (pc.IsInState("s_MenuInteraction") && _frames % 15 == 0) _keys.Tap("BT_Down");
            else if (elapsed > 10) return SmokeResult.Fail("Normal Use did not select " + _using!.sDescriptionText + "; current=" + pc.IManager?.GetCurrentInteraction()?.ClassName);
        }
        else if (_phase == 3)
        {
            if (_call!.InteractionObj is { } callEntry && (byte)callEntry.UsableBy == 1)
            {
                GD.Print($"PS2_WORLD_HACK normal input completed; call permission={_call.InteractionObj.UsableBy}; states={string.Join(',',_states)}");
                Save(context, "lift-hack-completed.png"); _standingExitRequested = false; Next(31, context);
            }
            else if (elapsed > 25) return SmokeResult.Fail("Normal panel hack did not enable the source lift switches");
        }
        else if (_phase == 31)
        {
            if (EnsureStanding(pc, session.ServerController!)) BeginApproach(_call!, context);
            else if (elapsed > 12) return SmokeResult.Fail("Normal uncrouch after hacking did not settle on both peers");
        }
        else if (_phase == 4)
        {
            if (_lift!.bInterpolating || _lift.KeyNum > 0)
            {
                _startZ = _lift.BasePos.Z;
                GD.Print($"PS2_WORLD_TRAVEL start authorityBase={host.Base?.ActorId},clientBase={pawn.Base?.ActorId}");
                Next(5, context);
            }
            else if (elapsed > 10) return SmokeResult.Fail("Normal Go up use did not start the grouped lift");
        }
        else if (_phase == 5)
        {
            Camera(_lift!.Location + new FVector(0, 0, -80), _lift.Location + new FVector(-120, 200, 120));
            if (!_travelShot && _lift.Location.Z > _startZ + 100) { Save(context, "lift-rising.png"); _travelShot = true; }
            var copy = Replica(_lift);
            bool doorsReady = Group(_lift).All(m => m.KeyNum == 3 && m.PrevKeyNum == 3);
            if (_lift.KeyNum == 3 && _lift.PrevKeyNum == 3 && copy is { KeyNum: 3, PrevKeyNum: 3 } && doorsReady && elapsed > 3)
            {
                Require(MathF.Abs(host.Location.Z - (_lift.Location.Z - 240 + host.CollisionHeight)) < 12,
                    "Authority pawn did not ride the lift: " + host.Location);
                Require(MathF.Abs(host.Location.Z - pawn.Location.Z) < 8, "Client/authority rider heights differ");
                Save(context, "lift-upper-doors-open.png");
                GD.Print($"PS2_WORLD_PASS lift={_lift.Tag} key3; upper doors open; host={host.Location}; client={pawn.Location}; bases={host.Base?.ActorId}/{pawn.Base?.ActorId}");
                Next(6, context);
            }
            else if (elapsed > 20) return SmokeResult.Fail($"Lift reversed or upper doors stalled: {string.Join(';', Group(_lift).Select(m => $"{m.ActorId}:{m.KeyNum}/{m.PrevKeyNum}:{m.GetStateName()}:{m.Location}"))}");
        }
        else if (_phase == 6)
        {
            bool a = _case == "lift-a";
            int yaw = a ? 49152 : 0;
            int errorYaw = FRotator.NormalizeAxis(yaw - pc.Rotation.Yaw);
            if (Math.Abs(errorYaw) > 200) _keys.Look(Math.Clamp(errorYaw / KeyPilot.UnitsPerCount, -80f, 80f), 0);
            else _keys.Press("BT_Up");
            bool hostOut = a ? host.Location.Y < _lift!.BasePos.Y - 200 : host.Location.X > _lift!.BasePos.X + 208;
            bool ownerOut = a ? pawn.Location.Y < _lift!.BasePos.Y - 200 : pawn.Location.X > _lift!.BasePos.X + 208;
            if (hostOut && ownerOut)
            {
                _keys.Release("BT_Up"); Save(context, "lift-walked-out-upper-floor.png");
                GD.Print($"PS2_WORLD_PASS normal upper-floor walkout host={host.Location}, client={pawn.Location}, bases={host.Base?.ActorId}/{pawn.Base?.ActorId}"); Finish();
            }
            else if (elapsed > 12) return SmokeResult.Fail($"Upper-floor normal walkout blocked: host={host.Location}, client={pawn.Location}");
        }
        else if (_phase == 10)
        {
            if (_standAtExit && !EnsureStanding(pc, session.ServerController!))
            {
                if (elapsed > 6) return SmokeResult.Fail("Could not stand in the exit fixture corridor");
                return SmokeResult.Running;
            }
            if (elapsed > 1)
            {
                Save(context, $"exit-{_exit}-inside.png"); _keys.Press("BT_Up"); Next(11, context);
            }
        }
        else if (_phase == 11)
        {
            if (System.Environment.GetEnvironmentVariable("SVM_PS2_CROUCH_RETRY_RECOVERY") == "1")
            {
                if (_retryRecovery == 0 && elapsed > 3 && _door!.IsInState("s_WaitAndRetry"))
                {
                    _keys.ReleaseAll(); _keys.Press("BT_Down"); _retryRecovery = 1; _retryRecoveryAt = context.ElapsedSeconds;
                    GD.Print($"PS2_WORLD_RECOVERY normal backstep begins exit={_exit} at={host.Location}");
                }
                else if (_retryRecovery == 1 && context.ElapsedSeconds - _retryRecoveryAt > 1.2)
                {
                    _keys.Release("BT_Down"); _retryRecovery = 2; _retryRecoveryAt = context.ElapsedSeconds;
                    GD.Print($"PS2_WORLD_RECOVERY backstep released exit={_exit} host={host.Location} client={pawn.Location} buttons={pc.BT_Down}/{session.ServerController!.BT_Down} states={pc.GetStateName()}/{session.ServerController.GetStateName()} velocity={host.Velocity}");
                }
                else if (_retryRecovery == 2 && _door!.KeyNum == 1 && _door.PrevKeyNum == 1 && !_door.bInterpolating)
                {
                    _keys.Press("BT_Up"); _retryRecovery = 3;
                    GD.Print($"PS2_WORLD_RECOVERY door fully open exit={_exit} at={host.Location}");
                }
            }
            if (_normal * (host.Location.X - _door!.BasePos.X) < -80 && _normal * (pawn.Location.X - _door.BasePos.X) < -80)
            {
                _keys.Release("BT_Up"); Require(_door.KeyNum == 1, "Spy crossed without source automatic door opening");
                Save(context, $"exit-{_exit}-crossed.png"); GD.Print($"PS2_WORLD_EXIT_PASS detector={_detector!.Location} host={host.Location} client={pawn.Location} key={_door.KeyNum}");
                if (++_exit == 3) Finish(); else BeginExit(context);
            }
            else if (elapsed > 12)
            {
                var collision = MovementNatives.Driver(host)!.Collision;
                foreach (var target in session.Host.World.Level.Actors.OfType<SBombTargetObjectiveTrigger>())
                {
                    collision.GetCylinder(target, out float radius, out float height, out bool collide, out bool block, out bool players);
                    GD.Print($"PS2_WORLD_COLLISION target={target.ActorId} at={target.Location} r/h={radius}/{height} fields={target.bCollideActors}/{target.bBlockActors}/{target.bBlockPlayers} query={collide}/{block}/{players} explicit={target.CollisionFlagsOverridden}");
                }
                return SmokeResult.Fail($"Normal spy walk blocked at exit {_exit}: host={host.Location}, client={pawn.Location}, key={_door!.KeyNum}, state={_door.GetStateName()}, detector={_detector!.IsPresenceActive}, pawnCylinder={host.CollisionRadius}/{host.CollisionHeight}");
            }
        }
        return SmokeResult.Running;
    }

    private void BeginApproach(SInterrupteur target, SmokeContext context)
    {
        _keys!.ReleaseAll(); _using = target;
        FVector at = target.Location + target.Rotation.Vector() * 100;
        at.Z = _root!.Session!.LocalController!.SBPawn!.CollisionHeight + 1;
        PlaceBoth(at, new FRotator(0, target.Rotation.Yaw + 32768, 0));
        Camera(target.Location, target.Location + new FVector(200, 200, 150)); Next(1, context);
    }
    private void DiagnoseHack(SPlayerController authority, SPlayerController client)
    {
        var type = typeof(SInterrupteur).Assembly.GetType("OpenScct.Game.Objectives.InteractionAuthorization")!;
        object sessions = type.GetField("Players", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        object?[] values = [authority, null];
        bool found = (bool)sessions.GetType().GetMethod("TryGetValue")!.Invoke(sessions, values)!;
        string snapshot = "absent";
        if (found)
        {
            object session = values[1]!;
            object? Field(string name) => session.GetType().GetField(name)!.GetValue(session);
            var entry = (SInteractObject)Field("Entry")!;
            object? reach = type.GetMethod("CanReach", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [authority, entry]);
            object? current = type.GetMethod("IsCurrent", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [session]);
            snapshot = $"started={Field("Started")},earliest={Field("EarliestCompletion")},reachable={reach},current={current},entryState={entry.GetStateName()},usable={entry.bUsable},entryLoc={entry.Location},height={entry.ItHeight},radius={entry.ItRadius}";
        }
        GD.Print($"PS2_WORLD_HACK_DIAG hostTime={authority.XLevel.WorldTimeSeconds} hostState={authority.GetStateName()} hostPc={authority.StateFrame.Pc} ownerState={client.GetStateName()} ownerPc={client.StateFrame.Pc} used={client.UsedInterraction?.ActorId}/{authority.UsedInterraction?.ActorId} permission={_call!.InteractionObj?.UsableBy} panelUsable={_panel!.InteractionObj?.bUsable} authorization={snapshot}");
    }

    private void BeginExit(SmokeContext context)
    {
        _keys!.ReleaseAll();
        _retryRecovery = 0;
        _standingExitRequested = false;
        _standingFrames = 0;
        Name eventName = new(_exit switch { 0 => "UnknownGlobalPS2_867216", 1 => "UnknownGlobalPS2_867217", _ => "UnknownGlobalPS2_867209" });
        _detector = _root!.Session!.Host.World.Level.Actors.OfType<SPresenceTrigger>().Single(t => t.Event == eventName && t.DetectedClass.Path == "SBase.SBasePawn");
        _door = _root.Session.Host.World.Level.Actors.OfType<Mover>().Single(m => m.Tag == eventName);
        _normal = MathF.Sign(_detector.Location.X - _door.BasePos.X);
        var at = new FVector(_door.BasePos.X + _normal * 160, _detector.Location.Y, 512 + _root.Session.LocalController!.SBPawn!.CollisionHeight + 1);
        PlaceBoth(at, new FRotator(0, _normal > 0 ? 32768 : 0, 0));
        Camera(_door.BasePos, _door.BasePos + new FVector(_normal * 250, 0, 90));
        GD.Print($"PS2_WORLD_EXIT_SETUP index={_exit} detector={_detector.Location}, allowedClass={_detector.DetectedClass.Path}, pawn={at}");
        Next(10, context);
    }
    private Mover? Replica(Mover source) => _root!.Session!.Client.World.Level.Actors.OfType<Mover>().FirstOrDefault(m => m.Tag == source.Tag && m.bIsLeader);
    private bool EnsureStanding(SPlayerController owner, SPlayerController authority)
    {
        if (owner.IsInState("s_PlayerWalkingCr0H") && !_standingExitRequested)
        {
            _standingExitRequested = true; _keys!.Tap("bCrouch");
        }
        if (owner.IsInState("s_PlayerWalkingSt0H") && authority.IsInState("s_PlayerWalkingSt0H") &&
            !owner.SBPawn!.bIsCrouched && !authority.SBPawn!.bIsCrouched) return ++_standingFrames >= 10;
        _standingFrames = 0; return false;
    }
    private static IEnumerable<Mover> Group(Mover m) { for (Mover? a = m; a is not null; a = a.Follower) yield return a; }
    private static float FlatDistance(FVector a, FVector b) { a.Z = b.Z = 0; return FVector.Dist(a, b); }
    private void Next(int phase, SmokeContext context) { _phase = phase; _since = context.ElapsedSeconds; }
    private void Finish() { _keys!.ReleaseAll(); _root!.QueueFree(); _teardown = 1; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Camera(FVector target, FVector at) => _root!.View?.SetUeCamera(at, (target - at).Rotation(), 70, cut: true);
    private void PlaceBoth(FVector at, FRotator facing)
    {
        Require(Place(_root!.Session!.ServerController!, at, facing) && Place(_root.Session.LocalController!, at, facing), "Fixture pose blocked: " + at);
        _root.Session.Host.Movement.EndpointOf(_root.Session.ServerController!)?.Server?.ReinitPawn();
    }
    private static bool Place(SPlayerController pc, FVector at, FRotator facing)
    {
        var pawn = pc.SBPawn!; pawn.Velocity = pawn.Acceleration = default;
        bool moved = MovementNatives.Driver(pawn)?.FarMoveActor(pawn, at) ?? pawn.SetLocation(at);
        pawn.SetBase(null); pawn.bJustTeleported = true; pawn.Rotation = pc.Rotation = facing; pc.Location = pawn.Location;
        return moved;
    }
    private static void Save(SmokeContext context, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string? directory = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS"); if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory); using Image image = context.Tree.Root.GetTexture().GetImage(); image.SavePng(Path.Combine(directory, name));
    }

    // Optional private smoke observer. All native calls retain the original implementation and result.
    private sealed class ExitEncroachDiagnostics(IActorNatives inner, Func<Mover?> door) : IActorNatives
    {
        private int _reports;
        public void SetCollision(Actor actor, bool a, bool b, bool p) => inner.SetCollision(actor, a, b, p);
        public void SetStaticMesh(Actor actor, AssetRef mesh) => inner.SetStaticMesh(actor, mesh);
        public void SetPhysics(Actor actor, EPhysics physics) => inner.SetPhysics(actor, physics);
        public bool SetLocation(Actor actor, FVector location) => inner.SetLocation(actor, location);
        public bool FastTrace(FVector end, FVector start) => inner.FastTrace(end, start);
        public bool MoveEncroacher(Actor actor, FVector delta, FRotator rotation)
        {
            FVector before = actor.Location; FRotator oldRotation = actor.Rotation;
            bool accepted = inner.MoveEncroacher(actor, delta, rotation);
            if (accepted || !ReferenceEquals(actor, door()) || _reports++ >= 12) return accepted;
            var collision = MovementNatives.Driver(actor)!.Collision;
            actor.Location = before + delta; actor.Rotation = rotation;
            try
            {
                foreach (Actor other in actor.XLevel.Actors)
                {
                    collision.GetCylinder(other, out float r, out float h, out bool c, out bool ba, out bool bp);
                    if (ReferenceEquals(actor, other) || !c || (!ba && !bp) ||
                        !collision.OverlapsActorColliders(actor, other.Location, new FVector(r, r, h))) continue;
                    actor.Location = before; actor.Rotation = oldRotation;
                    bool oldOverlap = collision.OverlapsActorColliders(actor, other.Location, new FVector(r, r, h));
                    actor.Location = before + delta; actor.Rotation = rotation;
                    GD.Print($"PS2_WORLD_ENCROACH at={actor.Level.TimeSeconds} mover={actor.ActorId} old={before} delta={delta} test={actor.Location} rotation={rotation} oldOverlap={oldOverlap} mesh={actor.StaticMesh.Path} other={other.ActorId}/{other.ClassName} pos={other.Location} r/h={r}/{h} flags={c}/{ba}/{bp} base={other.Base?.ActorId} state={(actor as Mover)?.GetStateName()}");
                }
            }
            finally { actor.Location = before; actor.Rotation = oldRotation; }
            return accepted;
        }
    }
}
