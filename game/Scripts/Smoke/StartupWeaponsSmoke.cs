using System;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Server.Online;
using OpenScctGame.Online;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>A fresh process through Create Session, lobby travel and cold first spawn.</summary>
public sealed class StartupWeaponsSmoke : SmokeTest
{
    public const string TestName = "startup-weapons";
    public override string Name => TestName;
    private PlayRoot? _root;
    private int _phase, _before;
    private long _at;
    private string _sound = "";
    private string? _oldGadget;
    private OnlineService? _online;
    private bool _launched;
    private OpenScct.Server.NetCombatBridge? _serverCombat;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_online is null)
        {
            _oldGadget = SettingsStore.Current.Loadout.MercSidearm;
            SettingsStore.Current.Loadout.MercSidearm = Sidearms.Get(SidearmKind.Unsuppressed).CarryId;
            _online = OnlineService.Get(context.Tree);
            if (!_online.Host(new HostRequest
            {
                Name = "Startup smoke", Port = 0, LanPort = 0, LoopbackOnly = true, Listed = false, Upnp = false,
                Setup = LobbySetup.ForMap("AquaE", OpenScct.Core.EGameMode.GM_Multi,
                    _online.Catalog.Maps.GetMapSettings("AquaE"), 8) with { Sidearms = true },
            })) return Done(false, _online.LastError ?? "hosting failed");
            _online.Hosting!.Server.HostStarted += host =>
            {
                _serverCombat = host.Combat;
                if (_serverCombat is not null) _serverCombat.Trace = [];
            };
            return SmokeResult.Running;
        }
        if (_online.LastError is { } onlineError) return Done(false, onlineError);
        if (!_launched && _online.Client is { State: OnlineClientState.Lobby } client && client.Lobby.Local is { } local)
        {
            if (local.Team != 1) client.SetTeam(1);
            else { client.Launch(); _launched = true; }
        }
        _root ??= context.Tree.CurrentScene as PlayRoot;
        if (_root is null) return SmokeResult.Running;
        if (_root.LoadError is { } error) return Done(false, error);
        if (_root.Controls is { } controls) controls.Enabled = false;
        if (_root.Session is not { } session || session.LocalPawn is not { } pawn) return SmokeResult.Running;
        if (_at == 0) _at = session.TicksRun;
        if (session.TicksRun - _at > 600)
            return Done(false, $"phase {_phase}: primary={pawn.PrimaryWeapon?.GetType().Name}, active={pawn.MyWeapon?.GetType().Name}, drawn={(pawn.MyWeapon as SWeaponDefense)?.bDrawWeapon}, grant={pawn.GearGrantWeapon}; combat={session.Client.Combat?.Received}/{session.Client.Combat?.Sent}, held={session.Client.Combat?.Held}; server={string.Join(" | ", _serverCombat?.Trace ?? [])}");
        switch (_phase)
        {
            case 0:
                if (pawn.PrimaryWeapon is not SWeaponDefense || pawn.SidearmWeapon is null || session.LocalController!.IsInState("WaitingForPawn")) break;
                _phase = 1; _at = session.TicksRun;
                break;
            case 1:
                if (session.TicksRun - _at < 60) break;
                if (_root.FirstPerson?.Shown != true) return Done(false, "first spawn has no visible primary gun");
                BeginFire(session, pawn);
                break;
            case 2:
            case 4:
                if (session.TicksRun - _at < 12) break;
                session.Input.Set("bFire", false);
                var calls = _root.Audio!.Sound!.Calls.Where(c => c.ActorId == pawn.ActorId && c.Sound == _sound).ToArray();
                if (calls.Count(c => !c.Heard) <= _before || calls.Any(c => c.Heard))
                    return Done(false, $"{_sound}: first shots did not start locally, or a delayed server copy played");
                if (_phase == 4) return Done(true, $"Cold first spawn shows rifle and pistol; both start their own shot audio before rendering. Load {_root.LoadSeconds:F1} s.");
                session.Input.Commands.Add(new InputCommand("SwapWeapon"));
                _phase = 3; _at = session.TicksRun;
                break;
            case 3:
                if (session.TicksRun - _at < 60) break;
                if (pawn.MyWeapon is not MercSidearm || _root.FirstPerson?.Shown != true)
                    return Done(false, "first pistol swap did not draw the pistol");
                BeginFire(session, pawn);
                break;
        }
        return SmokeResult.Running;
    }

    private void BeginFire(PlaySession session, SBasePawn pawn)
    {
        _sound = ((SWeaponDefense)pawn.MyWeapon!).FireSingleShotSound.Path!;
        _before = _root!.Audio!.Sound!.Calls.Count(c => c.ActorId == pawn.ActorId && c.Sound == _sound && !c.Heard);
        session.Input.Set("bFire", true);
        session.Input.Commands.Add(new InputCommand("Fire"));
        _phase++; _at = session.TicksRun;
    }

    private SmokeResult Done(bool ok, string detail)
    {
        if (_oldGadget is not null) SettingsStore.Current.Loadout.MercSidearm = _oldGadget;
        _root?.QueueFree();
        return ok ? SmokeResult.Pass(detail) : SmokeResult.Fail(detail);
    }
}
