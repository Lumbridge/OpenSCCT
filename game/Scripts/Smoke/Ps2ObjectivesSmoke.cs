using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Objectives;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>Loads a recovered map through the real client/server and checks its original objective views.</summary>
public sealed class Ps2ObjectivesSmoke : SmokeTest
{
    public const string TestName = "ps2-objectives";
    public override string Name => TestName;
    private PlayRoot? _root;
    private SFlag? _flag;
    private SFlag? _serverFlag;
    private SComputerObjectiveTrigger? _computer;
    private SComputerObjectiveTrigger? _serverComputer;
    private AnimatedMeshView? _terminal;
    private Ps2Modem? _modem;
    private FVector _carryInspectAt;
    private KeyPilot? _keys;
    private int _frames;
    private int _teardown;
    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass("original vial, animated console, replicated alarm, liquid panner, modem mesh, destruction particles and carried vial") : SmokeResult.Running;
        if (_root is null)
        {
            var launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with
            {
                Settings = launcher.Settings with { Map = "DTK02P", Mode = "Extraction", Side = PlaySide.Spy,
                    Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0 },
                TicksPerFrame = 2, Audio = false,
            });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session?.LocalPawn is null || _root.Devices is null) return SmokeResult.Running;
        _keys ??= KeyPilot.Attach(_root);
        _keys.Update();
        ++_frames;
        if (_frames == 1)
        {
            var level = _root.Session.Host.World.Level;
            _carryInspectAt = level.Actors.OfType<PlayerStart>().First(s => s.TeamNumber == 1).Location;
            var source = level.Actors.OfType<SFlag>().Where(f => f.bPS2Objective && !f.bHidden && f.Ps2Computer is not null)
                .OrderBy(f => FVector.Dist(f.Ps2Computer!.Location,new FVector(-2812,-2811,604))).First();
            _serverFlag = source;
            _serverComputer = source.Ps2Computer;
            FVector at = source.PickupInteraction?.Location ?? source.Location + new FVector(110,150,0);
            Place(_root.Session.ServerController!, at);
            Place(_root.Session.LocalController!, at);
            _root.Session.Host.Movement.EndpointOf(_root.Session.ServerController!)?.Server?.ReinitPawn();
        }
        if (_frames < 60) return SmokeResult.Running;
        if (_flag is null)
        {
            _flag = _root.Session.Client.World.Level.Actors.OfType<SFlag>().FirstOrDefault(f => f.bPS2Objective && !f.bHidden &&
                f.Ps2Computer is { } proxy && _serverComputer is not null && FVector.Dist(proxy.Location,_serverComputer.Location)<1f);
            if (_flag is null) return SmokeResult.Fail("no active PS2 vial/proxy; client=" +
                string.Join(";",_root.Session.Client.World.Level.Actors.OfType<SFlag>().Select(f => $"{f.ActorId}:ps2={f.bPS2Objective},hidden={f.bHidden},proxy={f.Ps2Computer?.ActorId}")) +
                " server=" + string.Join(";",_root.Session.Host.World.Level.Actors.OfType<SFlag>().Select(f => $"{f.ActorId}:ps2={f.bPS2Objective},hidden={f.bHidden},proxy={f.Ps2Computer?.ActorId},remote={f.RemoteRole},role={f.Role},hz={f.NetUpdateFrequency},static={f.bStatic},always={f.bAlwaysRelevant},temp={f.bNetTemporary},class={_root.Session.Host.Driver.Classes.Find(f.GetType()) is not null}")));
            if (_flag.Server is not null || _flag.Ps2SourceStaticMesh.IsNone ||
                _flag.StaticMesh != _flag.Ps2SourceStaticMesh || _flag.InitialStaticMesh != _flag.Ps2SourceStaticMesh)
                return SmokeResult.Fail($"PC disk/rack replaced source vial: source={_flag.Ps2SourceStaticMesh},mesh={_flag.StaticMesh},initial={_flag.InitialStaticMesh}");
            if (!_root.Devices.Meshes.Any(m => ReferenceEquals(m.Actor,_flag) && m.Entry.Node.Mesh is not null))
                return SmokeResult.Fail("separate source vial mesh was not bound to its replicated actor");
            _computer = _flag.Ps2Computer!;
            _terminal = _root.Devices.Animated.FirstOrDefault(v => ReferenceEquals(v.Actor, _computer));
            if (_terminal is null) return SmokeResult.Fail("source animated console is not drawn");
            foreach (string sequence in new[] { "nd133_hack_ps2", "nd133_alarm_ps2", "nd133_ctf_seq_ps2", "nd133_keyboard_in_ps2" })
                if (!_terminal.Model.Sampler.Has(sequence)) return SmokeResult.Fail("missing original clip " + sequence);
            _serverComputer!.Ps2SetVisualState(5, 1f);
        }
        FVector target = _flag.Location;
        FVector camera = target + new FVector(110, 150, 65);
        if (_frames >= 175 && _flag.Base is SBasePawn carrier &&
            _root.Pawns?.TryGetBone(carrier, SFlag.Ps2CarryBone, out Transform3D carriedBone) == true)
        {
            // Inspection fill changes only this test's actor lighting, so the
            // source mesh/body intersection can be judged in the dark spawn.
            var inspectionLight = new ActorLightSet { Ambient = new Vector3(.7f, .7f, .7f) };
            _root.Pawns.ViewOf(carrier)?.Model?.Light(inspectionLight);
            foreach (var mesh in _root.Devices.Meshes.Where(m => ReferenceEquals(m.Actor, _flag)))
                ActorLighting.Apply(mesh.Entry.Node, inspectionLight);
            target = UeSpace.ToUe(carriedBone.Origin);
            carrier.Rotation.GetAxes(out FVector forward, out FVector right, out _);
            camera = target + (_frames < 210 ? right * 150f - forward * 80f :
                _frames < 240 ? forward * 160f + right * 40f : -forward * 160f + right * 40f) + new FVector(0, 0, 40);
        }
        _root.View!.SetUeCamera(camera, (target-camera).Rotation(), 60, cut:true);
        if (_frames == 80)
        {
            bool panner = false;
            foreach (Node node in _terminal!.Model.Root.FindChildren("*", "MeshInstance3D", true, false))
                if (node is MeshInstance3D { Mesh: { } mesh } instance)
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                        if ((instance.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is ShaderMaterial material &&
                            material.GetShaderParameter("uv_anim_type").AsInt32() == 1 &&
                            MathF.Abs(material.GetShaderParameter("uv_anim_a").AsVector4().Z - .05f) < .0001f) panner = true;
            if (!panner) return SmokeResult.Fail("source liquid panner missing from terminal shader");
            Save(context, "ps2-vial-and-console.png");
            _serverComputer!.Ps2SetVisualState(3, 1f);
        }
        if (_frames == 110)
        {
            if (_computer!.Ps2VisualState != 3 || _terminal!.Sequence != "nd133_alarm_ps2" || _terminal.Frame <= 0)
                return SmokeResult.Fail($"replicated source alarm animation did not advance: client state={_computer.Ps2VisualState},seq={_terminal?.Sequence},frame={_terminal?.Frame},at={_computer.Location}; server state={_serverComputer?.Ps2VisualState},at={_serverComputer?.Location}");
            Save(context, "ps2-console-alarm.png");
            var level = _root.Session.Host.World.Level;
            var source = level.Actors.OfType<SBombTargetObjectiveTrigger>().First(b => b.bPS2Objective);
            _modem = level.SpawnActor(new Ps2Modem {
                StaticMesh = source.Ps2ModemMesh, SmokeTexture = source.Ps2ModemSmokeTexture,
                FireTexture = source.Ps2ModemFireTexture,
            }, target + new FVector(25, 20, 0), default, null, null);
        }
        if (_frames == 150)
        {
            if (_root.Fx?.Effects?.MeshesShown.GetValueOrDefault(nameof(Ps2Modem)) != 1)
                return SmokeResult.Fail("replicated modem mesh was not drawn; clients="+
                    string.Join(";",_root.Session.Client.World.Level.Actors.OfType<Ps2Modem>().Select(m=>$"{m.StaticMesh.Path}:hidden={m.bHidden},static={m.bStatic},nodelete={m.bNoDelete}"))+
                    "; missing="+string.Join(";",_root.Fx?.Effects?.Missing ?? []));
            Save(context, "ps2-modem.png");
            _modem!.eventTakeDamage(1, null, _modem!.Location, FVector.Zero, SDamageType.SDamage_Explosion);
        }
        if (_frames == 165)
        {
            if (_root.Fx?.Effects?.Started.GetValueOrDefault(nameof(Ps2ModemEffect)) != 1 ||
                _root.Fx.Effects.PeakParticles.GetValueOrDefault(nameof(Ps2ModemEffect)) <= 0)
                return SmokeResult.Fail("replicated modem destruction particles were not drawn");
            Save(context, "ps2-modem-destruction.png");
            // Exercise the normal gameplay carry rule, then observe its replicated bone attachment.
            DiskCarryRules.PickupFlag(_root.Session.ServerController!, _serverFlag!);
            Place(_root.Session.ServerController!, _carryInspectAt);
            Place(_root.Session.LocalController!, _carryInspectAt);
            _root.Session.Host.Movement.EndpointOf(_root.Session.ServerController!)?.Server?.ReinitPawn();
        }
        if (_frames == 175 && _root.Session.LocalPawn.bIsCrouched) _keys.Tap("bCrouch");
        if (_frames == 200)
        {
            var vial = _root.Devices.Meshes.First(m => ReferenceEquals(m.Actor, _flag));
            if (_flag.Base is not SBasePawn || _flag.AttachmentBone != SFlag.Ps2CarryBone || !vial.Entry.Node.Visible || !vial.Moved ||
                _flag.StaticMesh != _flag.Ps2SourceStaticMesh || _flag.InitialStaticMesh != _flag.Ps2SourceStaticMesh)
                return SmokeResult.Fail("replicated original vial was not drawn on the carrier bone");
            if (_root.Session.LocalPawn.bIsCrouched || _root.Session.ServerController!.SBPawn!.bIsCrouched)
                return SmokeResult.Fail($"carry inspection did not stand at {_carryInspectAt}: client={_root.Session.LocalController!.GetStateName()},cr={_root.Session.LocalPawn.bIsCrouched};server={_root.Session.ServerController!.GetStateName()},cr={_root.Session.ServerController.SBPawn!.bIsCrouched}");
            Save(context, "ps2-vial-carried-right.png");
            GD.Print($"PS2_CARRY source={_flag.Ps2SourceStaticMesh} mesh={_flag.StaticMesh} initial={_flag.InitialStaticMesh} bone={_flag.AttachmentBone} relative={_flag.RelativeLocation} rotation={_flag.RelativeRotation}");
        }
        if (_frames == 230) Save(context, "ps2-vial-carried-front.png");
        if (_frames == 260)
        {
            Save(context, "ps2-vial-carried-back.png");
            _root.QueueFree(); _teardown=1;
        }
        return SmokeResult.Running;
    }
    private static void Save(SmokeContext context,string name)
    {
        string folder=System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.GetFullPath("../local/ps2-objectives");
        Directory.CreateDirectory(folder);
        using Image image=context.Tree.Root.GetTexture().GetImage();
        image.SavePng(Path.Combine(folder,name));
    }
    private static void Place(SPlayerController controller,FVector at)
    {
        var pawn=(SBasePawn)controller.Pawn!;
        pawn.Velocity=pawn.Acceleration=default;
        if (OpenScct.Game.Movement.MovementNatives.Driver(pawn) is { } driver)
        {
            driver.FarMoveActor(pawn,at);
            driver.GetState(pawn).TraversalBase=null;
        }
        else pawn.SetLocation(at);
        pawn.SetBase(null);pawn.bJustTeleported=true;
        controller.Location=pawn.Location;
    }
}
