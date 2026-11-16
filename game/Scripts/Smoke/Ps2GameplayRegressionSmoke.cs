using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Normal input against installed PS2 content, with real client/authority marker and outcome checks.</summary>
public sealed class Ps2GameplayRegressionSmoke : SmokeTest
{
    public const string TestName = "ps2-gameplay-regression";
    public override string Name => TestName;
    private readonly string _mode = System.Environment.GetEnvironmentVariable("SVM_PS2_REGRESSION_MODE") ?? "Extraction";
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SFlag? _flag;
    private SComputerObjectiveTrigger? _computer;
    private SBombTargetObjectiveTrigger? _bombTarget;
    private Ps2Modem? _modem;
    private SFlagDropZone? _delivery;
    private int _phase, _frames, _teardown, _settled;
    private double _since, _cycleAt;
    private bool _hackingSeen;
    private double _completionAt = -1;
    private int _completionWaitFrames;
    private readonly HashSet<string> _states = [];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass($"{_mode}: normal input, rendered markers and replicated objective outcome") : SmokeResult.Running;
        if (_root is null)
        {
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with
            {
                Settings = launch.Settings with { Map = "DTK02P", Mode = _mode, Side = PlaySide.Spy,
                    Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline, Bots = 0 },
                TicksPerFrame = 2, Audio = false, Render = true, Hud = true,
            });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session is not { LocalController.SBPawn: { } pawn,
            ServerController.SBPawn: { } hostPawn } session || _root.Hud is null) return SmokeResult.Running;
        var pc = session.LocalController!;
        var authority = session.ServerController!;
        _keys ??= KeyPilot.Attach(_root); _keys.Update();
        ++_frames;
        _states.Add(pc.GetStateName().ToString());
        if (_frames % 60 == 0) GD.Print($"PS2_REGRESSION mode={_mode} phase={_phase} client={pc.GetStateName()} at={pawn.Location} health={pawn.Health} carry={pawn.CarriedFlag?.ActorId} gadget={pawn.SelectedGadget?.GetType().Name} markers={_root.Hud.Snapshot.Objectives.Count} drawn={DrawnMarkers()}");
        if (!_keys.Ready || session.TicksRun < 360) return SmokeResult.Running;
        double elapsed = context.ElapsedSeconds - _since;
        if (_phase == 0)
        {
            if (_mode == "Sabotage" && (pawn.Physics != EPhysics.PHYS_Walking || hostPawn.Physics != EPhysics.PHYS_Walking)) return SmokeResult.Running;
            if (pc.myHUD is SHud { EnhancedRealityLevel: 0 }) _keys.Tap("SwitchEnhancedReality");
            if (_mode == "Extraction")
            {
                _flag = session.Host.World.Level.Actors.OfType<SFlag>().Where(f => f.bPS2Objective && !f.bHidden && f.Ps2Computer is not null)
                    .OrderBy(f => FVector.Dist(f.Ps2Computer!.Location, new FVector(-1749, -3168.1f, 606))).First();
                PlaceBoth(new FVector(-1749, -3058, 601), new FRotator(0, 49152, 0));
                _computer = _flag.Ps2Computer;
            }
            else if (_mode == "Neutralization")
            {
                _computer = session.Host.World.Level.Actors.OfType<SComputerObjectiveTrigger>()
                    .First(c => c.bPS2Objective && c.bActive && !c.bLocked && c.Objective is { ObjectiveTaken: false });
                PlaceBoth(_computer.Location + _computer.Rotation.Vector() * 130f,
                    new FRotator(0, _computer.Rotation.Yaw + 32768, 0));
            }
            else
            {
                _bombTarget = session.Host.World.Level.Actors.OfType<SBombTargetObjectiveTrigger>()
                    .First(t => t.bPS2Objective && t.bActive && !t.bLocked && t.Ps2Computer is not null);
                _computer = _bombTarget.Ps2Computer;
                if (!FindModemPose(authority, out FVector at, out FRotator facing))
                    return SmokeResult.Fail("No eligible original world surface found in an active PS2 target cylinder");
                PlaceBoth(at, facing);
            }
            Next(1, context); return SmokeResult.Running;
        }
        var distant = _root.Hud.Snapshot.Objectives.FirstOrDefault(o => !o.Taken && FVector.Dist(o.Location, pawn.Location) > 250);
        if (_phase is 1 or 2 && distant is not null) InspectCamera(distant.Location);
        else if (_phase < 5 && _computer is not null) InspectCamera(_computer.Location + new FVector(0, 0, 25));
        if (_phase == 5 && _delivery is not null) InspectCamera(_delivery.Location);
        if (_phase == 14 && _modem is not null) InspectCamera(_modem.Location);
        if (_phase == 1 && elapsed > 2)
        {
            if (_mode != "Sabotage" && pawn.bIsCrouched) _keys.Tap("bCrouch");
            Next(2, context);
        }
        else if (_phase == 2 && elapsed > 2)
        {
            Require(pawn.Health > 0 && hostPawn.Health > 0, "Fixture pawn died before interaction");
            Save(context, "initial-markers.png"); CheckHud("initial");
            if (_mode == "Sabotage") { Next(10, context); return SmokeResult.Running; }
            if (_mode == "Extraction") PlaceBoth(new FVector(-1749, -3058, 601), new FRotator(0, 49152, 0));
            else PlaceBoth(_computer!.Location + _computer.Rotation.Vector() * 130f, new FRotator(0, _computer.Rotation.Yaw + 32768, 0));
            Next(21, context);
        }
        else if (_phase == 21 && elapsed > .6)
        {
            _keys.Press("bUsing"); Next(3, context);
        }
        else if (_phase == 3)
        {
            var entry = pc.IManager?.GetCurrentInteraction();
            bool selected = _mode == "Extraction"
                ? entry is SPickupInteraction { Owner: SFlag { Ps2Computer: { } c } } && FVector.Dist(c.Location, _computer!.Location) < 1
                : entry is SComputerInteraction { Owner: SComputerObjectiveTrigger terminal } && FVector.Dist(terminal.Location, _computer!.Location) < 1;
            if (pc.IsInState("s_MenuInteraction") && selected) { _keys.Release("bUsing"); Next(4, context); }
            else if (pc.IsInState("s_MenuInteraction") && _frames % 15 == 0) _keys.Tap("BT_Down");
            else if (elapsed > 10) return SmokeResult.Fail("Normal Use failed to select the source objective");
        }
        else if (_phase == 4 && _mode == "Neutralization")
        {
            Require(pawn.CarriedFlag is null && hostPawn.CarriedFlag is null, "Neutralization created a vial carrier");
            if (!_hackingSeen && _computer is { bTriggering: true, DeltaCaptureTime: > 1f })
            {
                CheckHud("hacking", requireDraw: false);
                Require(_root.Hud.Snapshot.Objectives.Any(o => o.Triggering && o.Percent > 0), "Client marker did not show hacking progress");
                _hackingSeen = true; Save(context, "hacking-markers.png");
            }
            if (_computer!.Objective is { ObjectiveTaken: true })
            {
                var replica = ReplicaComputer();
                if (_completionAt < 0) _completionAt = context.ElapsedSeconds;
                if (replica?.Objective is not { ObjectiveTaken: true })
                {
                    Require(context.ElapsedSeconds - _completionAt < 10, "Completed source terminal state did not replicate");
                    return SmokeResult.Running;
                }
                Require(_hackingSeen && _states.Contains("s_Computer_Use"), "Capture bypassed normal computer use");
                var completed = _root.Hud.Snapshot.Objectives.Where(o => FVector.Dist(o.Location, _computer.Location) < 1).ToArray();
                // The smoke runs before this frame's HUD snapshot refresh. Start its deadline at observed
                // completion; CaptureTime is simulation time and may take longer than that in wall time.
                if (completed.Any(o => !o.Taken))
                {
                    ++_completionWaitFrames;
                    if (context.ElapsedSeconds - _completionAt >= 10) DumpCompletionGraph("hud-still-untaken");
                    Require(context.ElapsedSeconds - _completionAt < 10, "Completed source terminal retained an untaken marker");
                    return SmokeResult.Running;
                }
                GD.Print($"PS2_REGRESSION_COMPLETED authorityObjective={_computer.Objective.ActorId} clientObjective={replica.Objective.ActorId} snapshotWaitFrames={_completionWaitFrames} observedCompletionWaitSeconds={context.ElapsedSeconds - _completionAt:F3} matchingHudMarkers={completed.Length} untakenHudMarkers={completed.Count(o => !o.Taken)}");
                Save(context, "completed-markers.png"); Finish("normal Neutralization capture and marker update");
            }
            else if (elapsed > _computer.CaptureTime + 30) return SmokeResult.Fail("Normal hack timed out");
        }
        else if (_phase == 4 && _mode == "Extraction")
        {
            if (ReferenceEquals(hostPawn.CarriedFlag, _flag) && pawn.CarriedFlag is { bPS2Objective: true } vial &&
                vial.AttachmentBone == SFlag.Ps2CarryBone && _flag!.AttachmentBone == SFlag.Ps2CarryBone &&
                pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) &&
                authority.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal))
            {
                if (++_settled < 5) return SmokeResult.Running;
                var clientZones = vial.DropZone.Where(z => z is not null && !z.bDeleteMe).ToArray();
                Require(clientZones.Length > 0 && _root.Hud.Snapshot.Objectives.Any(o => o.Kind == HudObjectiveKind.DropZone &&
                    clientZones.Any(z => FVector.Dist(o.Location, z!.Location) < 1)), "Carried source vial has no valid destination marker");
                _delivery = _flag.DropZone.OfType<SFlagDropZone>().First(z => !z.bDeleteMe);
                InspectCamera(_delivery.Location); Save(context, "delivery-marker.png");
                Next(5, context);
            }
            else if (elapsed > 18) return SmokeResult.Fail("Normal pickup did not settle on both peers");
        }
        else if (_phase == 5 && elapsed > 1)
        {
            CheckHud("carrying");
            Require(_root.Hud.Markers.DrawList.Items.Any(i => i.Path.StartsWith("ER/DropZone/", StringComparison.Ordinal)), "Valid delivery marker was not actually drawn");
            Save(context, "delivery-marker.png");
            FVector approach = _delivery!.Location - new FVector(_delivery.CollisionRadius + 100f, 0, 0);
            approach.Z += pawn.CollisionHeight;
            PlaceBoth(approach, default); Next(6, context);
        }
        else if (_phase == 6 && elapsed > 1)
        {
            Require(ReferenceEquals(hostPawn.CarriedFlag, _flag), "Approach placement delivered without movement");
            _keys.Press("BT_Up"); Next(7, context);
        }
        else if (_phase == 7)
        {
            if (_flag!.Objective is { ObjectiveTaken: true } && hostPawn.CarriedFlag is null && pawn.CarriedFlag is null)
            {
                _keys.Release("BT_Up"); Save(context, "delivered.png"); Finish("normal pickup, rendered valid delivery marker and movement into source delivery volume");
            }
            else if (elapsed > 10) return SmokeResult.Fail($"Physical delivery failed: pawn={hostPawn.Location},zone={_delivery!.Location},radius={_delivery.CollisionRadius},height={_delivery.CollisionHeight}");
        }
        else if (_phase == 10)
        {
            if (pawn.SelectedGadget is SGadBomb && hostPawn.SelectedGadget is SGadBomb)
            {
                Save(context, "modem-selected.png"); Next(11, context);
            }
            else if (context.ElapsedSeconds - _cycleAt > .5)
            {
                _keys.Tap("BT_PrevItem"); _cycleAt = context.ElapsedSeconds;
                if (elapsed > 10) return SmokeResult.Fail("Normal gadget cycle failed to select modem on both peers");
            }
        }
        else if (_phase == 11 && elapsed > .5)
        {
            GD.Print($"PS2_REGRESSION_AIM client={pawn.Location}/{pc.Rotation},eye={pawn.EyeHeight},cr={pawn.bIsCrouched};authority={hostPawn.Location}/{authority.Rotation},eye={hostPawn.EyeHeight},cr={hostPawn.bIsCrouched}");
            Require(pawn.SelectedGadget is SGadBomb client && client.CanUseGadget(), $"Owner modem cannot use source surface: {(pawn.SelectedGadget as SGadBomb)?.WhyCantUse}");
            Require(hostPawn.SelectedGadget is SGadBomb server && server.CanUseGadget(), "Authority modem cannot use source surface");
            // Raw configured mouse input, including the composite quick-choice binding that used to replace the modem.
            _root.Controls!.Key("RightMouse", true); Next(12, context);
        }
        else if (_phase == 12 && elapsed > .2)
        {
            _root.Controls!.Key("RightMouse", false);
            Require(pawn.SelectedGadget is SGadBomb && hostPawn.SelectedGadget is SGadBomb, "Right mouse replaced selected modem");
            _modem = session.Host.World.Level.Actors.OfType<Ps2Modem>().FirstOrDefault();
            if (_modem is not null) Next(13, context);
            else if (elapsed > 5) return SmokeResult.Fail("Normal right mouse did not place a modem");
        }
        else if (_phase == 13)
        {
            var client = session.Client.World.Level.Actors.OfType<Ps2Modem>().FirstOrDefault(m => !m.bDeleteMe);
            if (client is null || elapsed < 1) return SmokeResult.Running;
            Require(client.StaticMesh == _modem!.StaticMesh && !client.StaticMesh.IsNone && FVector.Dist(client.Location, _modem.Location) < 2,
                "Source modem mesh/placement did not replicate");
            Require(_bombTarget!.bTriggering && session.Client.World.Level.Actors.OfType<SBombTargetObjectiveTrigger>()
                .Any(t => FVector.Dist(t.Location, _bombTarget.Location) < 1 && t.bTriggering), "Modem target did not trigger on both peers");
            Require(_root.Fx?.Effects?.MeshesShown.GetValueOrDefault(nameof(Ps2Modem)) > 0, "Placed modem was not rendered");
            Require(((SGadBomb)pawn.SelectedGadget!).Charge == 0 && ((SGadBomb)hostPawn.SelectedGadget!).Charge == 0, "Modem charge did not replicate");
            InspectCamera(_modem.Location); Next(14, context);
        }
        else if (_phase == 14 && elapsed > 1)
        {
            // This close downward inspection frame intentionally excludes the
            // distant objective icons. Their actual draw was checked initially.
            Save(context, "modem-placed.png"); CheckHud("modem-placed", requireDraw: false);
            Finish("normal gadget cycle and raw right mouse placed the original source modem; charge, mesh and triggering replicated");
        }
        return SmokeResult.Running;
    }

    private void CheckHud(string phase, bool requireDraw = true)
    {
        var host = new HudSnapshot(); HudSimReader.Read(_root!.Session!.ServerController!, host);
        GD.Print($"PS2_REGRESSION_DRAW phase={phase} enhanced={_root.Hud!.Snapshot.EnhancedReality} hidden={_root.Hud.Snapshot.HideHud} valid={_root.Hud.Snapshot.Valid} haspawn={_root.Hud.Snapshot.HasPawn} side={_root.Hud.Snapshot.Side} screen={_root.Hud.DrawnScreen} items={string.Join(';', _root.Hud.Markers.DrawList.Items.Select(i => i.Path))} projected={string.Join(';',_root.Hud.Snapshot.Objectives.Select(o => $"{o.Name}:taken={o.Taken},at={o.Location},screen={_root.Hud.Projector?.Invoke(o.Location)}"))}");
        Require(host.Objectives.Count > 0 && _root.Hud!.Snapshot.Objectives.Count > 0,
            $"{phase}: objective markers missing client={_root.Hud!.Snapshot.Objectives.Count},authority={host.Objectives.Count}");
        Require(!requireDraw || DrawnMarkers() > 0, phase + ": client produced no visible ER icon draw item");
        GD.Print($"PS2_REGRESSION_HUD phase={phase} client={string.Join(';',_root.Hud.Snapshot.Objectives.Select(o => $"{o.Role}/{o.Kind}/{o.Name}:taken={o.Taken},active={o.Triggering},percent={o.Percent}"))} authorityCount={host.Objectives.Count} drawn={DrawnMarkers()}");
    }

    private void DumpCompletionGraph(string phase)
    {
        var session = _root!.Session!;
        var fresh = new HudSnapshot(); HudSimReader.Read(session.LocalController!, fresh);
        GD.Print($"PS2_COMPLETION phase={phase} selected={_computer!.ActorId} at={_computer.Location} hostTime={session.Host.World.Level.Info.TimeSeconds} clientTime={session.Client.World.Level.Info.TimeSeconds} replica={ReplicaComputer()?.ActorId} cached={string.Join(';', _root.Hud!.Snapshot.Objectives.Select(o => $"{o.Role}/{o.Kind}/{o.Name}@{o.Location}:taken={o.Taken},trig={o.Triggering},pct={o.Percent}"))} fresh={string.Join(';', fresh.Objectives.Select(o => $"{o.Role}/{o.Kind}/{o.Name}@{o.Location}:taken={o.Taken},trig={o.Triggering},pct={o.Percent}"))}");
        foreach (var side in new[] { (Name: "authority", World: session.Host.World), (Name: "client", World: session.Client.World) })
        {
            foreach (var objective in side.World.Level.Actors.OfType<SObjective>())
            {
                var presentation = ObjectiveHud.PresentationTriggers(objective).OfType<SObjectiveTrigger>().ToArray();
                bool relevant = presentation.Any(t => FVector.Dist(t.Location, _computer.Location) < 1) ||
                    objective.Triggers.Any(t => t is not null && FVector.Dist(t.Location, _computer.Location) < 1) ||
                    ReferenceEquals(objective, _computer.Objective) || ReferenceEquals(objective, ReplicaComputer()?.Objective);
                if (!relevant) continue;
                GD.Print($"PS2_COMPLETION_OBJECTIVE side={side.Name} id={objective.ActorId} type={objective.GetType().Name} name={objective.ObjectiveName} at={objective.Location} taken={objective.ObjectiveTaken} deleted={objective.bDeleteMe} activated={objective.activated} parent={objective.ParentObjective?.ActorId} parentMode={(objective.ParentObjective as SMission)?.GameMode} ownMode={(objective as SMission)?.GameMode} authored={string.Join(';', objective.Triggers.Select(t => t is null ? "null" : $"{t.ActorId}/{t.GetType().Name}:deleted={t.bDeleteMe},ps2={t.bPS2Objective},obj={t.Objective?.ActorId},taken={t.Objective?.ObjectiveTaken},at={t.Location}"))} presentation={string.Join(';', presentation.Select(t => $"{t.ActorId}/{t.GetType().Name}:deleted={t.bDeleteMe},ps2={t.bPS2Objective},obj={t.Objective?.ActorId},taken={t.Objective?.ObjectiveTaken},active={t.bActive},triggering={t.bTriggering},at={t.Location}"))}");
            }
            foreach (var trigger in side.World.Level.Actors.OfType<SObjectiveTrigger>().Where(t => FVector.Dist(t.Location, _computer.Location) < 1))
                GD.Print($"PS2_COMPLETION_TRIGGER side={side.Name} id={trigger.ActorId} type={trigger.GetType().Name} deleted={trigger.bDeleteMe} active={trigger.bActive} ps2={trigger.bPS2Objective} obj={trigger.Objective?.ActorId} objDeleted={trigger.Objective?.bDeleteMe} objTaken={trigger.Objective?.ObjectiveTaken} parent={trigger.Objective?.ParentObjective?.ActorId} parentMode={(trigger.Objective?.ParentObjective as SMission)?.GameMode} trig={trigger.bTriggering} delta={(trigger as SComputerObjectiveTrigger)?.DeltaCaptureTime}");
        }
    }

    private int DrawnMarkers() => _root!.Hud!.Markers.DrawList.Items.Count(i => i.Path.EndsWith("/icon", StringComparison.Ordinal) && i.Colors.Any(c => c.A > .01f));
    private SComputerObjectiveTrigger? ReplicaComputer() => _root!.Session!.Client.World.Level.Actors.OfType<SComputerObjectiveTrigger>().FirstOrDefault(c => c.bPS2Objective && FVector.Dist(c.Location, _computer!.Location) < 1);
    private void InspectCamera(FVector target)
    {
        FVector camera = target + new FVector(200, 160, 110);
        _root!.View!.SetUeCamera(camera, (target - camera).Rotation(), 70, cut: true);
    }
    private void Next(int phase, SmokeContext context) { _phase = phase; _since = context.ElapsedSeconds; }
    private void Finish(string message) { GD.Print("PS2_REGRESSION PASS " + message); _keys!.ReleaseAll(); _root!.QueueFree(); _teardown = 1; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void PlaceBoth(FVector at, FRotator facing)
    {
        bool host = Place(_root!.Session!.ServerController!, at, facing);
        bool owner = Place(_root.Session.LocalController!, at, facing);
        Require(host && owner, $"Fixture pose blocked: {at}, authority={host},owner={owner}");
        _root.Session.Host.Movement.EndpointOf(_root.Session.ServerController!)?.Server?.ReinitPawn();
    }
    private static bool Place(SPlayerController pc, FVector at, FRotator facing)
    {
        var pawn = pc.SBPawn!;
        pawn.Velocity = pawn.Acceleration = default;
        bool moved = MovementNatives.Driver(pawn)?.FarMoveActor(pawn, at) ?? pawn.SetLocation(at);
        pawn.SetBase(null); pawn.bJustTeleported = true; pawn.Rotation = pc.Rotation = facing; pc.Location = pawn.Location;
        return moved;
    }
    private bool FindModemPose(SPlayerController pc, out FVector at, out FRotator facing)
    {
        at = default; facing = default;
        SGadBomb? gadget = null;
        for (var g = pc.SBPawn!.MyGadget; g is not null; g = g.MyGadget) if (g is SGadBomb bomb) gadget = bomb;
        Require(gadget is not null, "No original modem in normal inventory");
        var reasons = new Dictionary<int,int>(); int traces = 0, poses = 0;
        foreach (var target in pc.XLevel.Actors.OfType<SBombTargetObjectiveTrigger>().Where(t => t.bPS2Objective && t.bActive && !t.bLocked))
        for (int radius = 80; radius <= Math.Min(target.Ps2TargetRadius - 40, 400); radius += 80)
        for (int angle = 0; angle < 8; angle++)
        {
            float a = angle * MathF.PI / 4;
            FVector p = target.Location + new FVector(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
            Actor? floor = MovementNatives.Trace(pc.SBPawn, out FVector hit, out _, p - new FVector(0, 0, 300), p + new FVector(0, 0, 200), true);
            if (floor is null) continue;
            traces++;
            p = hit + new FVector(0, 0, pc.SBPawn.CollisionHeight + 1);
            if (!Place(pc, p, default)) continue;
            poses++;
            foreach (int pitch in new[] { -16384, -6000, 0, 6000 })
            for (int yaw = 0; yaw < 65536; yaw += 8192)
            {
                var rotation = new FRotator(pitch, yaw, 0); pc.Rotation = rotation;
                if (Ps2ModemRules.Probe(gadget!, out var placement))
                {
                    at = pc.SBPawn.Location; facing = rotation; _bombTarget = placement.Target; _computer = placement.Target.Ps2Computer;
                    GD.Print($"PS2_REGRESSION_SURFACE pawn={at} hit={placement.Location} facing={rotation} target={placement.Target.Location} cylinder={placement.Target.Ps2TargetRadius}/{placement.Target.Ps2TargetHeight}");
                    return true;
                }
                reasons[gadget!.WhyCantUse] = reasons.GetValueOrDefault(gadget.WhyCantUse) + 1;
            }
        }
        GD.Print($"PS2_REGRESSION_SURFACE_FAILED traces={traces} poses={poses} physics={pc.SBPawn.Physics} sAction={pc.SBPawn.sAction} charge={gadget!.Charge} refire={gadget.RefireRate} elapsed={gadget.Level.TimeSeconds-gadget.LastUsed} reasons={string.Join(';', reasons.Select(p=>$"{p.Key}:{p.Value}"))} targets={string.Join(';',pc.XLevel.Actors.OfType<SBombTargetObjectiveTrigger>().Select(t=>$"{t.Location}/{t.Ps2TargetRadius}/{t.Ps2TargetHeight},active={t.bActive},locked={t.bLocked}"))}");
        return false;
    }
    private static void Save(SmokeContext context, string name)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); using Image image = context.Tree.Root.GetTexture().GetImage(); image.SavePng(Path.Combine(folder, name));
    }
}
