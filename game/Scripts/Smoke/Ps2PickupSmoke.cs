using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>The real Use key, menu, reposition physics and pickup on the recovered DTK cabinet objective.</summary>
public sealed class Ps2PickupSmoke : SmokeTest
{
    public const string TestName = "ps2-pickup";
    public override string Name => TestName;
    private readonly bool _crouched = System.Environment.GetEnvironmentVariable("SVM_PS2_PICKUP_CROUCHED") == "1";
    private readonly bool _stationNeutralization = System.Environment.GetEnvironmentVariable("SVM_PS2_STATION_NEUTRALIZATION") == "1";
    private readonly bool _computerUse = System.Environment.GetEnvironmentVariable("SVM_PS2_USE_COMPUTER") == "1" ||
        System.Environment.GetEnvironmentVariable("SVM_PS2_STATION_NEUTRALIZATION") == "1";
    private readonly HashSet<string> _states = [];
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SFlag? _flag;
    private SComputerObjectiveTrigger? _computer;
    private int _phase;
    private int _frames;
    private int _teardown;
    private double _since;
    private string _target = "unseen";
    private bool _stationHackStarted;
    private int _stationCompleteFrames;
    private int _stationCaptureTime;
    private double _carrySettledSince = -1;
    private int _carrySettledFrames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass($"actual Use, computer={_computerUse}, crouched={_crouched}, target={_target}, states={string.Join(",", _states)}") : SmokeResult.Running;
        if (_root is null)
        {
            var config = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(config with
            {
                Settings = config.Settings with
                {
                    Map = _stationNeutralization ? "GAR09P" : _computerUse ? "MHO05P" : "DTK02P",
                    Mode = _computerUse ? "Neutralization" : "Extraction",
                    Side = PlaySide.Spy,
                    Rules = _stationNeutralization ? PlayRules.MapSettings : PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Inline,
                    Bots = 0
                },
                TicksPerFrame = 2,
                Audio = false
            });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session?.LocalController is not { SBPawn: { } pawn } pc ||
            _root.Session.ServerController is not { SBPawn: { } serverPawn } authority) return SmokeResult.Running;
        ++_frames;
        _keys ??= KeyPilot.Attach(_root);
        _keys.Update();
        _states.Add(pc.GetStateName().ToString());
        if (_stationNeutralization && _computer is { } terminal)
        {
            FVector at = terminal.Location + new FVector(0, 0, 20);
            FVector camera = at + terminal.Rotation.Vector() * 200f + new FVector(70, 70, 70);
            _root.View!.SetUeCamera(camera, (at - camera).Rotation(), 60f, cut: true);
        }
        if (_frames % 30 == 0) GD.Print($"PS2_PICKUP phase={_phase} " + Detail(pc, authority));
        if (!_keys.Ready) return SmokeResult.Running;
        if (_phase == 0)
        {
            if (_computerUse)
            {
                _computer = _stationNeutralization
                    ? _root.Session.Host.World.Level.Actors.OfType<SComputerObjectiveTrigger>()
                        .First(c => c.bPS2Objective && c.bActive && !c.bLocked && c.Objective is { ObjectiveTaken: false })
                    : _root.Session.Host.World.Level.Actors.OfType<SComputerObjectiveTrigger>().Where(c => c.bPS2Objective)
                    .OrderBy(c => FVector.Dist(c.Location, new FVector(2187.1211f, -178.8036f, 250.48088f))).First();
                if (!_stationNeutralization && FVector.Dist(_computer.Location, new FVector(2187.1211f, -178.8036f, 250.48088f)) > 2f)
                    return SmokeResult.Fail("MHO Room212 console not found");
                _stationCaptureTime = _computer.CaptureTime;
                FVector start = _computer.Location + _computer.Rotation.Vector() * 130f;
                if (!_stationNeutralization) start.Z = 249.005f;
                Place(authority, start); Place(pc, start);
                pc.Rotation = authority.Rotation = new FRotator(0, _computer.Rotation.Yaw + 32768, 0);
                pawn.Rotation = serverPawn.Rotation = pc.Rotation;
            }
            else
            {
                _flag = _root.Session.Host.World.Level.Actors.OfType<SFlag>().Where(f => f.bPS2Objective && !f.bHidden && f.Ps2Computer is not null)
                .OrderBy(f => FVector.Dist(f.Ps2Computer!.Location, new FVector(-1749, -3168.1f, 606))).First();
                if (FVector.Dist(_flag.Ps2Computer!.Location, new FVector(-1749, -3168.1f, 606)) > 5f)
                    return SmokeResult.Fail("DTK cabinet objective not found");
                Place(authority, new FVector(-1749, -3058, 601));
                Place(pc, new FVector(-1749, -3058, 601));
            }
            _root.Session.Host.Movement.EndpointOf(authority)?.Server?.ReinitPawn();
            _phase = 1; _since = context.ElapsedSeconds;
        }
        else if (_phase == 1 && context.ElapsedSeconds - _since > 2)
        {
            if (pawn.bIsCrouched != _crouched) _keys.Tap("bCrouch");
            _phase = 2; _since = context.ElapsedSeconds;
        }
        else if (_phase == 2 && context.ElapsedSeconds - _since > 2)
        {
            if (pawn.bIsCrouched != _crouched || serverPawn.bIsCrouched != _crouched)
                return SmokeResult.Fail("stance input failed " + Detail(pc, authority));
            if (_stationNeutralization) Save(context, "station-neutralization-fresh.png");
            _keys.Press("bUsing");
            _phase = 3; _since = context.ElapsedSeconds;
        }
        else if (_phase == 3)
        {
            SInteractObject? entry = pc.IManager?.GetCurrentInteraction();
            bool chosen = _computerUse ? entry is SComputerInteraction { Owner: SComputerObjectiveTrigger c } && FVector.Dist(c.Location, _computer!.Location) < 1 :
                entry is SPickupInteraction { Owner: SFlag { Ps2Computer: { } proxy } } && FVector.Dist(proxy.Location, _flag!.Ps2Computer!.Location) < 1;
            if (pc.IsInState("s_MenuInteraction") && chosen)
            {
                FVector stand;
                if (_computerUse) ((SComputerObjectiveTrigger)entry!.Owner!).GetUseSpot(pawn, out stand, out _);
                else SPlayerController.GetPickupSpot(entry!, pawn, out stand, out _);
                FVector horizontal = stand; horizontal.Z = pawn.Location.Z;
                Actor? obstacle = MovementNatives.Trace(pawn, out FVector hit, out FVector normal, horizontal, pawn.Location, true,
                    new FVector(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight));
                GD.Print($"PS2_PICKUP sweep obstacle={obstacle?.GetType().Name} id={obstacle?.ActorId} at={obstacle?.Location} mesh={obstacle?.StaticMesh.Path} hit={hit} normal={normal} target={stand}");
                _keys.Release("bUsing");
                _phase = 4; _since = context.ElapsedSeconds;
            }
            else if (pc.IsInState("s_MenuInteraction") && _frames % 15 == 0) _keys.Tap("BT_Down");
            else if (context.ElapsedSeconds - _since > 8) return SmokeResult.Fail("Use menu did not select original vial " + Detail(pc, authority));
        }
        else if (_phase == 4)
        {
            if (pc.IsInState(_computerUse ? "s_Computer_GoTo" : "s_Pickup_GoTo")) _target = pawn.vVandammePos.ToString();
            if (_stationNeutralization)
            {
                if (pawn.CarriedFlag is not null || serverPawn.CarriedFlag is not null)
                    return SmokeResult.Fail("Neutralization created an Extraction carrier");
                if (!_stationHackStarted && pc.IsInState("s_Computer_Use") && _computer is { bTriggering: true, DeltaCaptureTime: > 0.2f })
                {
                    _stationHackStarted = true;
                    Save(context, "station-neutralization-hacking.png");
                }
                if (_computer?.Objective is { ObjectiveTaken: true })
                {
                    if (!_stationHackStarted || !_states.Contains("s_Computer_GoTo") || !_states.Contains("s_Computer_Enter"))
                        return SmokeResult.Fail("Station objective skipped source computer path");
                    if (++_stationCompleteFrames >= 60)
                    {
                        Save(context, "station-neutralization-complete.png");
                        GD.Print($"PS2_STATION_NEUTRALIZATION PASS normal map rules, fresh case -> actual Use -> {_stationCaptureTime}s capture -> taken, no carried vial; " + Detail(pc, authority));
                        _keys.ReleaseAll(); _root.QueueFree(); _teardown = 1;
                    }
                }
                else if (context.ElapsedSeconds - _since > _stationCaptureTime + 25)
                    return SmokeResult.Fail("Station normal Neutralization did not complete " + Detail(pc, authority));
            }
            else if (_computerUse && pc.IsInState("s_Computer_Use") && _computer is { bTriggering: true, DeltaCaptureTime: > 0.2f })
            {
                if (!_states.Contains("s_Computer_GoTo") || !_states.Contains("s_Computer_Enter")) return SmokeResult.Fail("computer skipped use state path " + Detail(pc, authority));
                GD.Print("PS2_COMPUTER_USE PASS " + Detail(pc, authority));
                _keys.ReleaseAll(); _root.QueueFree(); _teardown = 1;
            }
            else if (!_computerUse && ReferenceEquals(serverPawn.CarriedFlag, _flag) && pawn.CarriedFlag is { bPS2Objective: true } vial &&
                ReferenceEquals(vial.Base, pawn) && !vial.AttachmentBone.IsNone &&
                pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) &&
                authority.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal))
            {
                // The owning client can finish its clip before authority. The
                // source hand -> thigh transition runs in authority EndState,
                // then its attachment still has to cross the network.
                if (_carrySettledSince < 0) _carrySettledSince = context.ElapsedSeconds;
                if (_flag!.AttachmentBone != SFlag.Ps2CarryBone || vial.AttachmentBone != SFlag.Ps2CarryBone ||
                    vial.StaticMesh != vial.Ps2SourceStaticMesh || vial.InitialStaticMesh != vial.Ps2SourceStaticMesh ||
                    _flag.StaticMesh != _flag.Ps2SourceStaticMesh || _flag.InitialStaticMesh != _flag.Ps2SourceStaticMesh)
                {
                    _carrySettledFrames = 0;
                    if (context.ElapsedSeconds - _carrySettledSince < 2) return SmokeResult.Running;
                    return SmokeResult.Fail("actual Use left a PC mesh or wrong carry bone; client " + VialDetail(vial) +
                        "; authority " + VialDetail(serverPawn.CarriedFlag) + "; " + Detail(pc, authority));
                }
                if (++_carrySettledFrames < 3) return SmokeResult.Running;
                if (!_states.Contains("s_Pickup_GoTo") || !_states.Contains("s_Pickup"))
                    return SmokeResult.Fail("pickup skipped source state path " + Detail(pc, authority));
                GD.Print("PS2_PICKUP PASS client " + VialDetail(vial) + "; authority " + VialDetail(_flag) + "; " + Detail(pc, authority));
                _keys.ReleaseAll(); _root.QueueFree(); _teardown = 1;
            }
            else if (context.ElapsedSeconds - _since > 12) return SmokeResult.Fail("actual reposition/pickup did not complete " + Detail(pc, authority));
        }
        return SmokeResult.Running;
    }

    private static string VialDetail(SFlag? vial) => vial is null ? "none" :
        $"id={vial.ActorId} ps2={vial.bPS2Objective} mesh={vial.StaticMesh.Path} initial={vial.InitialStaticMesh.Path} " +
        $"source={vial.Ps2SourceStaticMesh.Path} bone={vial.AttachmentBone} base={vial.Base?.ActorId} " +
        $"relative={vial.RelativeLocation}/{vial.RelativeRotation}";

    private string Detail(SPlayerController pc, SPlayerController authority) =>
        $"client={pc.GetStateName()} at={pc.SBPawn?.Location} target={pc.SBPawn?.vVandammePos} phys={pc.SBPawn?.Physics} " +
        $"crouch={pc.SBPawn?.bIsCrouched} cylinder={pc.SBPawn?.CollisionRadius}/{pc.SBPawn?.CollisionHeight} carry={pc.SBPawn?.CarriedFlag?.ActorId} " +
        $"entry={pc.IManager?.GetCurrentInteraction()?.GetType().Name} owner={pc.IManager?.GetCurrentInteraction()?.Owner?.GetType().Name} " +
        $"server={authority.GetStateName()} at={authority.SBPawn?.Location} carry={authority.SBPawn?.CarriedFlag?.ActorId} " +
        $"computer={_computer?.TriggerName} active={_computer?.bActive} locked={_computer?.bLocked} progress={_computer?.DeltaCaptureTime} distance={_computer?.UseRelativeLocation} " +
        $"flag={_flag?.Location} pickup={_flag?.PickupInteraction?.Location} states={string.Join(",", _states)} keys={string.Join(",", _keys?.Missing ?? [])}";

    private static void Place(SPlayerController controller, FVector at)
    {
        var pawn = controller.SBPawn!;
        pawn.Velocity = pawn.Acceleration = default;
        if (MovementNatives.Driver(pawn) is { } driver) { driver.FarMoveActor(pawn, at); driver.GetState(pawn).TraversalBase = null; }
        else pawn.SetLocation(at);
        pawn.SetBase(null); pawn.bJustTeleported = true;
        pawn.Rotation = new FRotator(0, 49152, 0); controller.Rotation = pawn.Rotation; controller.Location = pawn.Location;
    }

    private static void Save(SmokeContext context, string name)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using Image image = context.Tree.Root.GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, name));
    }
}
