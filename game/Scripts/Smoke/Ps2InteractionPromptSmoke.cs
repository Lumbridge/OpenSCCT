using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Crosses the original console's offset touch-cylinder center, bumps the case and uses normal input.</summary>
public sealed class Ps2InteractionPromptSmoke : SmokeTest
{
    public const string TestName = "ps2-interaction-prompt";
    public override string Name => TestName;
    private readonly bool _expectBug = System.Environment.GetEnvironmentVariable("SVM_PS2_CLOSE_PROMPT_EXPECT_BUG") == "1";
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SComputerObjectiveTrigger? _console;
    private FVector _far, _near;
    private FRotator _facing;
    private int _phase, _teardown;
    private double _since;
    private bool _hackingSeen;
    private readonly HashSet<string> _states = [];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass(_expectBug ? "old offset-origin prompt loss reproduced" : "far, near, cabinet bump and normal Use passed on both peers") : SmokeResult.Running;
        if (_root is null)
        {
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with { Settings = launch.Settings with { Map = "DTK02P", Mode = "Neutralization",
                Side = PlaySide.Spy, Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline, Bots = 0 },
                Audio = false, Render = true, Hud = true, TicksPerFrame = 2 });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session is not { LocalController.SBPawn: { } pawn,
            ServerController.SBPawn: { } hostPawn } session || _root.Hud is null) return SmokeResult.Running;
        var pc = session.LocalController!;
        _keys ??= KeyPilot.Attach(_root); _keys.Update();
        _states.Add(pc.GetStateName().ToString());
        if (!_keys.Ready || session.TicksRun < 360) return SmokeResult.Running;
        double elapsed = context.ElapsedSeconds - _since;
        if (_phase == 0)
        {
            Require(FindPose(), "No unmodified source console has clear far110 and near75 poses");
            PlaceBoth(_far);
            if (pawn.bIsCrouched) _keys.Tap("bCrouch");
            Next(1, context);
        }
        else if (_phase == 1 && elapsed > 1)
        {
            Require(!pawn.bIsCrouched && !hostPawn.bIsCrouched, "Standing fixture failed to uncrouch");
            Require(FindPose(), "No unmodified source console has clear far110 and near75 standing poses");
            PlaceBoth(_far); Next(2, context);
        }
        else if (_phase == 2 && elapsed > 1)
        {
            CheckPrompt(context, "far-before", true);
            PlaceBoth(_near); Next(3, context);
        }
        else if (_phase == 3 && elapsed > 1)
        {
            CheckPrompt(context, "near75", !_expectBug);
            PlaceBoth(_far); Next(4, context);
        }
        else if (_phase == 4 && elapsed > 1)
        {
            CheckPrompt(context, "far-restored", true);
            _keys.Press("BT_Up"); Next(5, context);
        }
        else if (_phase == 5 && elapsed > 1.5)
        {
            _keys.Release("BT_Up"); Next(6, context);
        }
        else if (_phase == 6 && elapsed > .6)
        {
            float distance = HorizontalDistance(hostPawn.Location, _console!.Location);
            Require(distance < 80 && distance > 20, $"Normal walk did not reach the console: {hostPawn.Location},distance={distance}");
            CheckPrompt(context, "walked-to-case", !_expectBug);
            if (_expectBug) { Finish(); return SmokeResult.Running; }
            _keys.Press("bUsing"); Next(7, context);
        }
        else if (_phase == 7)
        {
            bool selected = pc.IManager?.GetCurrentInteraction() is SComputerInteraction { ObjectiveTrigger: { } trigger } &&
                FVector.Dist(trigger.Location, _console!.Location) < 1;
            if (pc.IsInState("s_MenuInteraction") && selected) { _keys.Release("bUsing"); Next(8, context); }
            else if (pc.IsInState("s_MenuInteraction") && elapsed > .5) _keys.Tap("BT_Down");
            else if (elapsed > 10) return SmokeResult.Fail("Normal Use did not select the close console");
        }
        else if (_phase == 8)
        {
            var replica = Replica();
            if (_console is { bTriggering: true, DeltaCaptureTime: > .5f } && replica is { bTriggering: true, DeltaCaptureTime: > .25f })
            {
                Require(_states.Contains("s_Computer_Use"), "Hack bypassed normal computer states");
                if (!_hackingSeen) Save(context, "normal-use-hacking.png");
                _hackingSeen = true;
            }
            if (_console!.Objective is { ObjectiveTaken: true } && replica?.Objective is { ObjectiveTaken: true })
            {
                Require(_hackingSeen, "Capture bypassed replicated normal hacking");
                Save(context, "normal-use-complete.png");
                GD.Print($"PS2_PROMPT_USE PASS states={string.Join(',', _states)} completeAuthority=True completeClient=True");
                Finish();
            }
            else if (elapsed > _console.CaptureTime + 30) return SmokeResult.Fail($"Normal close Use did not complete replicated hacking: client={pc.GetStateName()},server={session.ServerController!.GetStateName()}");
        }
        if (_console is not null && _root.View is not null)
        {
            FVector target = _console.Location + new FVector(0, 0, 25);
            FVector camera = target + _console.Rotation.Vector() * 210 + new FVector(70, 65, 90);
            _root.View.SetUeCamera(camera, (target - camera).Rotation(), 70, cut: true);
        }
        return SmokeResult.Running;
    }

    private bool FindPose()
    {
        var pawn = _root!.Session!.ServerController!.SBPawn!;
        foreach (var c in pawn.XLevel.Actors.OfType<SComputerObjectiveTrigger>().Where(c => c.bPS2Objective && c.bActive && !c.bLocked && c.Objective is { ObjectiveTaken: false }))
        {
            FVector front = c.Rotation.Vector(); front.Z = 0; front = front.SafeNormal();
            if (!FloorPose(c.Location + front * 110, out FVector far) || !FloorPose(c.Location + front * 75, out FVector near)) continue;
            _console = c; _far = far; _near = near; _facing = new FRotator(0, c.Rotation.Yaw + 32768, 0);
            GD.Print($"PS2_PROMPT_FIXTURE case={c.ActorId},at={c.Location},rotation={c.Rotation},entry={c.InteractionObj?.Location},far={far},near={near}");
            return true;
        }
        return false;
    }
    private bool FloorPose(FVector at, out FVector result)
    {
        var pawn = _root!.Session!.ServerController!.SBPawn!;
        var hit = MovementNatives.Trace(pawn, out FVector floor, out _, at - new FVector(0, 0, 200), at + new FVector(0, 0, 80), false);
        result = floor + new FVector(0, 0, pawn.CollisionHeight + 1);
        return hit is not null && MovementNatives.FitsAt(pawn, result, new FVector(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight));
    }
    private SComputerObjectiveTrigger? Replica() => _root!.Session!.Client.World.Level.Actors.OfType<SComputerObjectiveTrigger>()
        .FirstOrDefault(c => !c.bDeleteMe && c.bPS2Objective && FVector.Dist(c.Location, _console!.Location) < 1);
    private void CheckPrompt(SmokeContext context, string label, bool expected)
    {
        var session = _root!.Session!;
        var sourceEntry = _console!.InteractionObj as SComputerInteraction;
        var clientEntry = Replica()?.InteractionObj as SComputerInteraction;
        if (clientEntry?.bDeleteMe == true) clientEntry = null;
        // Initial map references can point at the replaced client actor; select the live replicated backlink.
        clientEntry ??= session.Client.World.Level.Actors.OfType<SComputerInteraction>().FirstOrDefault(i => !i.bDeleteMe && i.ObjectiveTrigger is { } o && FVector.Dist(o.Location, _console.Location) < 1);
        bool source = sourceEntry?.CanUse(session.ServerController) == true;
        bool client = clientEntry?.CanUse(session.LocalController) == true;
        bool drawn = _root.Hud!.Interaction is { IsOpen: true } menu && menu.DrawList.Items.Count > 0;
        bool listed = clientEntry is not null && session.LocalController!.IManager!.IndexOf(clientEntry) >= 0;
        GD.Print($"PS2_PROMPT label={label},expected={expected},client={client},authority={source},drawn={drawn},listed={listed},show={_root.Hud.Snapshot.ShowInteractions},at={session.LocalPawn!.Location},case={_console.Location},entry={clientEntry?.Location},owner={clientEntry?.Owner?.ActorId},objective={clientEntry?.ObjectiveTrigger?.ActorId}");
        Save(context, label + ".png");
        Require(source == expected && client == expected, $"{label}: CanUse authority={source},client={client},expected={expected}");
        Require(listed, label + ": original touch entry left the client menu list");
        Require(drawn == expected, $"{label}: rendered prompt={drawn},expected={expected}");
    }
    private void PlaceBoth(FVector at)
    {
        var session = _root!.Session!;
        foreach (var pc in new[] { session.ServerController!, session.LocalController! })
        {
            var pawn = pc.SBPawn!;
            pawn.Velocity = pawn.Acceleration = default;
            bool moved = MovementNatives.Driver(pawn)?.FarMoveActor(pawn, at) ?? pawn.SetLocation(at);
            Require(moved && FVector.Dist(pawn.Location, at) < 2, $"Fixture pose blocked: requested={at},actual={pawn.Location}");
            pawn.SetBase(null); pawn.bJustTeleported = true; pawn.Rotation = pc.Rotation = _facing; pc.Location = pawn.Location;
        }
        session.Host.Movement.EndpointOf(session.ServerController!)?.Server?.ReinitPawn();
    }
    private static float HorizontalDistance(FVector a, FVector b) { FVector delta = a - b; delta.Z = 0; return delta.Size(); }
    private void Next(int phase, SmokeContext context) { _phase = phase; _since = context.ElapsedSeconds; }
    private void Finish() { _keys!.ReleaseAll(); _root!.QueueFree(); _teardown = 1; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Save(SmokeContext context, string name)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); using Image image = context.Tree.Root.GetTexture().GetImage(); image.SavePng(Path.Combine(folder, name));
    }
}
