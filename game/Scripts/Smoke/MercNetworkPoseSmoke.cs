using System;
using System.IO;
using Godot;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Draws remote sidearm and gas knockout poses through the snapshot and skeleton paths.</summary>
public sealed class MercNetworkPoseSmoke : SmokeTest
{
    public const string TestName = "merc-network-pose";
    public override string Name => TestName;
    private sealed class IdleBrain : BotBrain
    {
        public BotInput Input;
        public override BotInput Think(BotContext context) => Input;
    }
    private readonly IdleBrain _mercBrain = new();
    private readonly VirtualClock _clock = new();
    private BotMatch? _match;
    private SPlayerController _authority = null!, _owner = null!;
    private SBasePawn _body = null!, _seen = null!;
    private SimPawnView _view = null!;
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private long _tick;
    private int _frame;
    private string _folder = "", _pendingShot = "";
    private bool _fall, _sleep, _wake;
    private bool _throw;
    private float _idleHeadY;
    private ClientSession? _replay;
    private DemoPlaybackTransport? _playback;
    private uint _demoTick, _demoEnd;
    private int _pistolClipBefore;
    private int _demoFps = Sim.Hz;
    private double _demoFraction;
    private StreamWriter? _poseTrace;
    private bool _blast;
    private int _blastTicks, _movingDraws, _heldDraws, _maxHeldDraws;
    private FVector _lastDrawn;
    private bool _blastSeen, _landed;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_viewport is null) { Build(context); return SmokeResult.Running; }
        if (_pendingShot.Length != 0)
        {
            if (DisplayServer.GetName() != "headless")
            {
                using Image shot = _viewport.GetTexture().GetImage();
                shot.SavePng(Path.Combine(_folder, _pendingShot + ".png"));
            }
            _pendingShot = "";
        }
        if (_blast) return StepBlast();
        if (_replay is not null)
        {
            _demoFraction += Sim.Hz / (double)_demoFps;
            while (_demoFraction >= 1)
            {
                _playback!.ReleaseUpTo(++_demoTick);
                _replay.Tick(VirtualClock.TickToMicroseconds(++_tick, Sim.Hz));
                _view.OnTick();
                _demoFraction -= 1;
            }
            DrawFrame((float)_demoFraction, snapshot: false);
            ChannelPose pose = _view.DrawnChannels[0];
            _view.TryGetBone(new Name("B Head"), out Transform3D replayHead);
            FVector location = _view.DrawnLocation;
            _poseTrace?.WriteLine(FormattableString.Invariant($"{_frame},{_demoTick},{_demoFraction:F6},{_seen.Health},{pose.Sequence},{pose.Frame:F7},{pose.ClipFrame:F7},{location.X:F5},{location.Y:F5},{location.Z:F5},{replayHead.Origin.X:F6},{replayHead.Origin.Y:F6},{replayHead.Origin.Z:F6}"));
            if (++_frame % Math.Max(1, _demoFps / 3) == 0) _pendingShot = "tick-" + _demoTick;
            return _demoTick < _demoEnd ? SmokeResult.Running : Finish(SmokeResult.Pass($"Rendered recorded merc interval; {_folder}"));
        }
        if (_frame is >= 60 and < 240)
        {
            _owner.bAltFire = (byte)(_frame % 6 < 3 ? 1 : 0);
            if (_frame % 6 == 0) _owner.eventAltFire(1f);
        }
        else _owner.bAltFire = 0;
        if (_frame is 250 or 300 or 350 or 400) _owner.SwapWeapon();
        if (_frame == 420) _pistolClipBefore = ((MercSidearm)_owner.SBPawn!.MyWeapon!).ClipAmmo;
        _mercBrain.Input = _frame is >= 420 and < 465
            ? new BotInput { View = new FRotator((int)(MathF.Sin((_frame - 420) * .15f) * 9000), (_frame - 420) * 500, 0),
                FireHeld = _frame % 18 < 3, FirePressed = _frame % 18 == 0 }
            : BotInput.Idle;
        StepNetwork(1);
        DrawFrame();
        Vector3 target = UeSpace.ToGodot(_seen.Location);
        if (!_view.TryGetBone(new Name("B Head"), out Transform3D head))
            return Finish(SmokeResult.Fail("Merc head bone missing"));
        float headY = head.Origin.Y - target.Y;
        if (++_frame == 30)
        {
            _idleHeadY = headY;
            _pendingShot = "pistol-standing";
            if (!_view.Attachments!.TryGetWeapon(out _, out SkeletalModel? gun) || gun is null)
                return Finish(SmokeResult.Fail("Remote pistol attachment missing"));
        }
        if (_frame == 45) _pendingShot = "pistol-side";
        if (!_throw && _seen.PawnAnim[SBasePawn.WEAPONCHANNEL].BlendAlpha > .5f &&
            _seen.PawnAnim[SBasePawn.WEAPONCHANNEL].Sequence == new Name("ReloStSpFd1"))
        { _throw = true; _pendingShot = "pistol-grenade"; }
        if (_frame is 255 or 305 or 355 or 405) _pendingShot = "swap-" + _frame;
        if (_frame is 432 or 452) _pendingShot = "pistol-aim-fire-" + _frame;
        if (_frame == 470)
        {
            if (((MercSidearm)_owner.SBPawn!.MyWeapon!).ClipAmmo >= _pistolClipBefore)
                return Finish(SmokeResult.Fail("Pistol input did not fire after weapon swaps"));
            if (_seen.PawnAnim[SBasePawn.WEAPONCHANNEL].BlendAlpha > .01f)
                return Finish(SmokeResult.Fail("Grenade overlay stuck after rapid taps and swaps"));
            _pendingShot = "pistol-after-spam";
        }
        if (_frame == 480)
        {
            var gas = _authority.XLevel.SpawnActor(new SStickyCamEmitter(), _body.Location - new FVector(192, 0, 0), default)!;
            gas.Instigator = _match!.ServerController(_match.Bots[0])!.Pawn;
        }
        if (!_fall && _seen.bSleeping && _seen.PawnAnim[0].Frame is > .2f and < .8f)
        { _fall = true; _pendingShot = "pistol-falling-asleep"; }
        if (!_sleep && _seen.bSleeping && _owner.IsInState("s_LyingOnGround") && _seen.PawnAnim[0].Frame > .9f)
        {
            if (_idleHeadY - headY < .4f)
                return Finish(SmokeResult.Fail($"Sleeping rendered head did not fall: idle={_idleHeadY:F3}, sleep={headY:F3}"));
            for (int c = 1; c < PawnAnimation.ChannelCount; c++)
                if (_view.DrawnChannels[c].Alpha > .01f)
                    return Finish(SmokeResult.Fail($"Sleeping overlay {c} remains at {_view.DrawnChannels[c].Alpha}"));
            _sleep = true; _pendingShot = "pistol-asleep";
        }
        if (_sleep && !_wake && !_seen.bSleeping) { _wake = true; _pendingShot = "pistol-waking"; }
        if (_frame < 30 * Sim.Hz) return SmokeResult.Running;
        return Finish(_fall && _sleep && _wake && _throw
            ? SmokeResult.Pass($"Listen server, 35ms jitter/loss: remote pistol aim/fire, rapid grenade taps, swaps, rendered fall, sleep and wake; {_folder}")
            : SmokeResult.Fail($"Missing transitions: grenade={_throw}, fall={_fall}, sleep={_sleep}, wake={_wake}"));
    }

    private void Build(SmokeContext context)
    {
        _folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "local", "merc-network-pose"));
        Directory.CreateDirectory(_folder);
        _blast = System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_BLAST") == "1";
        if (System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_DEMO") is { Length: > 0 } path)
        {
            DemoRecording recording = DemoFile.Read(path);
            int start = int.Parse(System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_START") ?? "8");
            int seconds = int.Parse(System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_SECONDS") ?? "20");
            _demoFps = Math.Clamp(int.Parse(System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_FPS") ?? "60"), 20, 360);
            _folder = Path.Combine(_folder, Path.GetFileNameWithoutExtension(path) + "-" + start + "-" + _demoFps + "fps");
            Directory.CreateDirectory(_folder);
            _playback = new DemoPlaybackTransport(recording);
            MapCatalog maps = System.Environment.GetEnvironmentVariable("SVM_MERC_POSE_MAPS") is { Length: > 0 } mapRoot
                ? new MapCatalog(mapRoot) : MapCatalog.CreateDefault();
            _replay = new ClientSession(_playback, "demo", 0, new ClientSettings { PlayerName = "Demo", Gameplay = true, Maps = maps });
            _demoTick = recording.Index.FirstTick;
            uint target = _demoTick + (uint)(start * Sim.Hz);
            for (; _demoTick <= target; _demoTick++)
            {
                _playback.ReleaseUpTo(_demoTick);
                _replay.Tick(VirtualClock.TickToMicroseconds(++_tick, Sim.Hz));
            }
            _demoEnd = Math.Min(recording.Index.LastTick, _demoTick + (uint)(seconds * Sim.Hz));
            foreach (SBasePawn pawn in _replay.World.Level.AllActors<SBasePawn>())
                if (pawn is SPAWNDEFENSE { Health: > 0 }) { _seen = pawn; break; }
            if (_seen is null) throw new InvalidOperationException($"No living recorded merc at the requested time: {_replay.State}, {_replay.RejectReason}, protocol {_replay.Driver.ProtocolHash}/{recording.Header.ProtocolHash}");
            BuildViewport(context);
            _view.OnTick();
            _poseTrace = new StreamWriter(Path.Combine(_folder, "poses.csv"));
            _poseTrace.WriteLine("frame,tick,alpha,health,sequence,phase,clipPhase,x,y,z,headX,headY,headZ");
            return;
        }
        var network = new SimLinkNetwork(_clock, 507, new SimLinkProfile("wifi35", 35, 20, 1f, .5f, .5f, 25));
        _match = new BotMatch(new BotMatchSettings
        {
            Map = "TestRange", Maps = MapCatalog.CreateDefault(), NetMode = ENetMode.NM_ListenServer,
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => _mercBrain)],
        }, network.Bind, "server", 0);
        StepNetwork(180);
        _match.ServerController(_match.Bots[0])!.StartMatchOnServer();
        StepNetwork(600);
        _authority = _match.ServerController(_match.Bots[1])!;
        _owner = _match.Bots[1].Controller!;
        _body = (SBasePawn)_authority.Pawn!;
        _body.Health = 10000;
        SidearmEquipment.Give(_body, SidearmKind.Suppressed);
        var grenade = _authority.XLevel.SpawnActor(new SGadFragGrenade(), _body.Location, default, _body, _body)!;
        grenade.GiveTo(_body); grenade.Charge = 1;
        StepNetwork(120); _owner.SwapWeapon(); StepNetwork(120);
        _body.SelectedGadget = grenade;
        _owner.SBPawn!.SelectedGadget = (SGadFragGrenade)_match.Bots[1].Session.Driver.FindActorByNetGuid((uint)grenade.ActorId)!;
        _seen = (SBasePawn)_match.Bots[0].Session.Driver.FindActorByNetGuid((uint)_body.ActorId)!;
        if (_seen.ActiveWeaponSlot != 1) throw new InvalidOperationException("Remote pistol selection did not replicate");
        BuildViewport(context);
        if (_blast)
        {
            _demoFps = 144;
            _view.OnTick();
            _poseTrace = new StreamWriter(Path.Combine(_folder, "live-blast-poses.csv"));
            _poseTrace.WriteLine("frame,tick,health,physics,z,velocityZ,drawnX,drawnY,drawnZ,sequence,phase");
        }
    }

    private SmokeResult StepBlast()
    {
        _demoFraction += Sim.Hz / (double)_demoFps;
        while (_demoFraction >= 1)
        {
            if (++_blastTicks == 60)
            {
                _body.Health = 100;
                _authority.fSpawnProtectionEnd = 0;
                FVector at = _body.Location - new FVector(100, 0, 0);
                var grenade = _body.XLevel.SpawnActor(new SProjFrag(), at, default, _body, _body)!;
                grenade.Explode(at, FVector.Zero);
                if (_body.Health > 0) return Finish(SmokeResult.Fail("Fixture grenade did not kill the merc"));
            }
            StepNetwork(1);
            _view.OnTick();
            _demoFraction -= 1;
        }
        DrawFrame((float)_demoFraction, snapshot: false);
        FVector drawn = _view.DrawnLocation;
        bool flying = _seen.Health <= 0 && _seen.Physics == EPhysics.PHYS_Falling && _seen.Velocity.Size() > 50;
        if (flying)
        {
            _blastSeen = true;
            _movingDraws++;
            _heldDraws = (drawn - _lastDrawn).SizeSquared() < .0001f ? _heldDraws + 1 : 0;
            _maxHeldDraws = Math.Max(_maxHeldDraws, _heldDraws);
        }
        else
        {
            _heldDraws = 0;
            if (_blastSeen && _seen.Health <= 0 && _seen.Physics != EPhysics.PHYS_Falling) _landed = true;
        }
        ChannelPose pose = _view.DrawnChannels[0];
        _poseTrace!.WriteLine(FormattableString.Invariant($"{_frame},{_blastTicks},{_seen.Health},{_seen.Physics},{_seen.Location.Z:F6},{_seen.Velocity.Z:F6},{drawn.X:F6},{drawn.Y:F6},{drawn.Z:F6},{pose.Sequence},{pose.Frame:F7}"));
        _lastDrawn = drawn;
        if (++_frame % 48 == 0) _pendingShot = "live-blast-" + _frame;
        if (_blastTicks < 7 * Sim.Hz) return SmokeResult.Running;
        string detail = $"144fps fatal merc flight: moving draws={_movingDraws}, maximum held draws={_maxHeldDraws}, landed={_landed}";
        return Finish(_movingDraws > 20 && _maxHeldDraws <= 3 && _landed ? SmokeResult.Pass(detail) : SmokeResult.Fail(detail));
    }

    private void BuildViewport(SmokeContext context)
    {
        _viewport = new SubViewport { Size = new Vector2I(720, 720), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.1f,
            Near = .01f, Far = 10000f, CullMask = RenderLayers.MainView,
            Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() };
        _viewport.AddChild(_camera);
        _view = new SimPawnView(); _viewport.AddChild(_view);
        if (!_view.Bind(_seen, SkeletalModel.DefaultSharedRoot())) throw new InvalidOperationException("Converted merc model missing");
    }

    private void DrawFrame(float alpha = 1f, bool snapshot = true)
    {
        if (snapshot) _view.OnTick();
        _view.Draw(alpha, 1f / _demoFps);
        var light = new ActorLightSet { Ambient = new Vector3(.6f, .6f, .6f) };
        _view.Model!.Light(light);
        _view.Attachments!.Light(light);
        Vector3 target = UeSpace.ToGodot(_view.DrawnLocation);
        _seen.Rotation.GetAxes(out FVector forward, out FVector right, out _);
        Vector3 cameraOffset = _frame is >= 40 and < 60 ? UeSpace.DirectionToGodot(right * 3.5f)
            : UeSpace.DirectionToGodot(forward * 3f + right * 1.8f);
        _camera.Position = target + cameraOffset + new Vector3(0, .3f, 0);
        _camera.LookAt(target);
    }

    private void StepNetwork(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _clock.AdvanceTo(VirtualClock.TickToMicroseconds(++_tick, Sim.Hz));
            _match!.Step(_clock.NowMicroseconds);
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _poseTrace?.Dispose();
        _match?.Dispose(); _replay?.Dispose(); _playback?.Dispose(); _viewport.QueueFree(); return result;
    }
}
