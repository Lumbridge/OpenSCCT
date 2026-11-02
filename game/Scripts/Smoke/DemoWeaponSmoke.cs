using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Play;
using OpenScctGame.Play.Demo;
using OpenScctGame.UI;
using OpenScctGame.Views;
using DemoPlayer = OpenScctGame.Play.Demo.DemoPlayer;

namespace OpenScctGame.Smoke;

/// <summary>Loads and poses observed merc view models with no owning controller or first-person inventory.</summary>
public sealed class DemoWeaponSmoke : SmokeTest
{
    public const string TestName = "demo-weapon";
    public override string Name => TestName;
    private readonly string? _file = Environment.GetEnvironmentVariable("SVM_DEMO_WEAPON_FILE");
    private DemoPlayer? _player;
    private int _frames, _settle;
    private bool _selected, _restarted;
    private uint _seekTick;
    private string? _shot;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_file is not { Length: > 0 }) return CheckModels(context);
        if (_player is null)
        {
            SmokeResult models = CheckModels(context);
            if (models.State != SmokeState.Passed) return models;
            _player = DemoPlayer.Open(_file);
            _player.Clock.Paused = true;
            uint start = _player.FirstTick;
            foreach (DemoMarker marker in _player.Recording.Index.Markers)
                if (marker.Kind == DemoMarkerKind.RoundStart) { start = marker.Tick; break; }
            _player.SeekTarget = _seekTick = Math.Min(_player.LastTick, start + 2 * (uint)Sim.Hz);
            if (!DemoLibrary.Enter(context.Tree, _player)) return SmokeResult.Fail("Could not enter the requested demo");
            return SmokeResult.Running;
        }
        if (++_frames > 1800) return SmokeResult.Fail("Timed out waiting for an observed merc in the demo");
        if (context.Tree.CurrentScene is not PlayRoot play || play.Session is null) return SmokeResult.Running;
        if (_player.FastForward(600)) return SmokeResult.Running;
        if (_restarted)
        {
            if (!_player.Clock.Paused || _player.CurrentTick != _player.FirstTick || _player.Targets.Count == 0 || _player.Target is null)
                return SmokeResult.Fail("Paused backward seek to the first tick did not restore the player roster");
            return SmokeResult.Pass("Actual replay merc first-person weapon rendered and hid on camera switch; exact paused forward and backward seeks retained players" +
                (_shot is null ? string.Empty : "; " + _shot));
        }
        if (!_selected)
        {
            if (!_player.Clock.Paused || _player.CurrentTick != _seekTick)
                return SmokeResult.Fail($"Paused forward seek stopped at {_player.CurrentTick}, expected {_seekTick}");
            for (int i = 0; i < _player.Targets.Count; i++)
            {
                if (_player.Targets[i] is not SPAWNDEFENSE { Health: > 0 }) continue;
                _player.WatchIndex(i);
                _player.Mode = DemoCameraMode.FirstPerson;
                _player.SeekTarget = Math.Min(_player.LastTick, _player.CurrentTick + 2);
                _player.FastForward(2);
                _selected = true;
                break;
            }
            if (!_selected) return SmokeResult.Fail("The requested recording has no merc at round start");
            return SmokeResult.Running;
        }
        if (++_settle < 16) return SmokeResult.Running;
        if (_settle == 16)
        {
            if (play.FirstPerson is not { Shown: true, Model: not null } firstPerson || firstPerson.ChannelFrame(0) < 0)
                return SmokeResult.Fail("Actual demo first-person camera did not draw and pose the merc weapon");
            _shot = MenuPage.Shot(play, "demo-merc-first-person");
            _player.Mode = DemoCameraMode.ThirdPerson;
            return SmokeResult.Running;
        }
        if (play.FirstPerson?.Shown == true) return SmokeResult.Fail("Actual demo third-person camera retained the weapon overlay");
        _player = _player.Restart(_player.FirstTick);
        _restarted = true;
        if (!DemoLibrary.Enter(context.Tree, _player)) return SmokeResult.Fail("Could not restart the demo for a paused backward seek");
        return SmokeResult.Running;
    }

    private static SmokeResult CheckModels(SmokeContext context)
    {
        var view = new FirstPersonView();
        context.Tree.Root.AddChild(view);
        try
        {
            var pawn = new SPAWNDEFENSE
            {
                Role = ENetRole.ROLE_SimulatedProxy, Health = 100, WeaponLoaded = 2,
                NetworkAttachments = new NetworkPawnAttachments(0, true),
            };
            foreach (SWeaponDefenseDummy dummy in new SWeaponDefenseDummy[]
                { new(), new SWeaponDefenseDummy_Uzi(), new SWeaponDefenseDummy_Shotgun(), new MercSidearmDummy() })
            {
                pawn.WeaponDummy = dummy;
                view.OnObservedTick(pawn);
                view.DrawObserved(pawn, default, default, Sim.DeltaTime);
                if (!view.Shown || view.Model is null || view.ChannelFrame(0) < 0)
                    return SmokeResult.Fail($"{dummy.ClassName}: observed weapon or idle pose missing");
                dummy.MuzzleCount++;
                view.OnObservedTick(pawn);
                view.DrawObserved(pawn, default, default, Sim.DeltaTime);
                if (view.ChannelFrame(SWeaponDefense.ACTIONCHANNEL) < 0)
                    return SmokeResult.Fail($"{dummy.ClassName}: recorded shot did not pose the observed weapon");
            }
            view.DrawObserved(null, default, default, Sim.DeltaTime);
            if (view.Shown) return SmokeResult.Fail("Changing to third-person/free camera left the weapon visible");
            view.Draw(null, default, default, Sim.DeltaTime);
            if (view.Shown) return SmokeResult.Fail("Spectator controller unexpectedly drew a local weapon");
            return SmokeResult.Pass("Recorded attachments draw rifle, SMG, shotgun and sidearm with idle/fire poses; camera changes hide them");
        }
        finally { view.Free(); }
    }
}
