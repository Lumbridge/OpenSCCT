using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>One real rendered host/client pass through every pistol, its fire/reload clips and a finish.</summary>
public sealed class PlaySidearmSmoke : SmokeTest
{
    public const string TestName = "play-sidearms";
    public override string Name => TestName;
    private PlayRoot? _root;
    private int _kind, _phase;
    private long _at;
    private bool _shot;
    private RenderMap? _worldLights;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_root is null)
        {
            _root=PlayRoot.Create(new PlayConfig
            {
                Settings=new PlaySettings { Map="SteeE", Side=PlaySide.Merc, Rules=PlayRules.PlayLevel,
                    HostMode=PlayHostMode.Inline, MapRoots=[Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),"..",".content","svmap")), ..context.App.Options.MapRoots] },
                Render=true, Hud=true, TicksPerFrame=1,
            });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session is not { } session || session.LocalPawn is not { } pawn ||
            session.ServerController?.Pawn is not SBasePawn serverPawn || session.LocalController is not { } pc)
            return SmokeResult.Running;
        if (_root.Controls is { } controls) controls.Enabled=false;
        if (pc.IsInState("WaitingForPawn")) return SmokeResult.Running;
        if (session.TicksRun-_at>600 && _phase!=0) return SmokeResult.Fail($"pistol {_kind} phase {_phase} timed out");
        var definition=Sidearms.All[_kind];
        switch (_phase)
        {
            case 0:
                SidearmEquipment.Give(serverPawn,definition.Kind);
                if (serverPawn.PlayerReplicationInfo is { } pri)
                {
                    var look=new CosmeticLook(pri.SvmLook,pri.SvmLookExtra).With((CosmeticSlot)(6+_kind),
                        CosmeticCatalog.Find("merc."+definition.Id+".cryo"));
                    (pri.SvmLookLow,pri.SvmLookHigh)=look.Split();
                    pri.SvmLookExtraLow=(int)look.Extra;pri.SvmLookExtraHigh=(int)(look.Extra>>32);
                }
                _phase=1;_at=session.TicksRun;
                break;
            case 1:
                if (pawn.SidearmWeapon?.Kind!=_kind) break;
                session.Input.Commands.Add(new InputCommand("SwapWeapon"));
                _phase=2;_at=session.TicksRun;
                break;
            case 2:
                if (session.TicksRun-_at<45) break;
                if (_root.FirstPerson is not { Shown:true, Model:{ } model }) return SmokeResult.Fail("pistol view model is missing");
                if (!model.Sampler.Has("FirstReloStFd2") || !model.Sampler.Has("FirstButtStFd2")) return SmokeResult.Fail("pistol contextual clips missing");
                var attachments = _root.Pawns?.ViewOf(pawn)?.Attachments;
                if (attachments is null || !attachments.TryGetWeaponBone(new Name("FX"), out _))
                    return SmokeResult.Fail("active third-person pistol socket is missing");
                Shot(definition.Id+"-ready");
                // Explicit studio diagnostic, separate from the map-lit capture above.
                _worldLights = _root.FirstPerson.Map;
                _root.FirstPerson.Map = null;
                model.Light(new ActorLightSet { Ambient = new Vector3(.8f, .8f, .8f) });
                _phase=6;_at=session.TicksRun;
                break;
            case 6:
                if (session.TicksRun-_at<3) break;
                Shot(definition.Id+"-pose-studio");
                _root.FirstPerson!.Map = _worldLights;
                session.Input.Set("bFire",true);session.Input.Commands.Add(new InputCommand("Fire"));
                _phase=3;_at=session.TicksRun;_shot=false;
                break;
            case 3:
                if (session.TicksRun-_at<10) break;
                session.Input.Set("bFire",false);
                if (session.TicksRun-_at<35) break;
                if (pawn.SidearmWeapon!.ClipAmmo>=definition.Magazine) return SmokeResult.Fail("pistol did not fire");
                session.Input.Set("bReload",true);
                _phase=4;_at=session.TicksRun;
                break;
            case 4:
                if (session.TicksRun-_at>30 && !_shot) { Shot(definition.Id+"-reload");_shot=true; }
                if (session.TicksRun-_at<(definition.ReloadSeconds+1)*Sim.Hz) break;
                session.Input.Set("bReload",false);
                if (pawn.SidearmWeapon!.ClipAmmo!=definition.Magazine || serverPawn.SidearmWeapon!.ClipAmmo!=definition.Magazine)
                    return SmokeResult.Fail("pistol reload disagrees across worlds");
                session.Input.Commands.Add(new InputCommand("SwapWeapon"));
                _phase=5;_at=session.TicksRun;
                break;
            case 5:
                if (session.TicksRun-_at<45) break;
                if (pawn.ActiveWeaponSlot!=0 || serverPawn.ActiveWeaponSlot!=0) return SmokeResult.Fail("return to primary failed");
                serverPawn.SidearmWeapon?.Dummy?.Destroy();serverPawn.SidearmWeapon?.Destroy();serverPawn.SidearmWeapon=null;
                if (++_kind==4) { _root.QueueFree();return SmokeResult.Pass("Four pistols: models, Cryo, contextual clips, firing, reload and switching in two worlds."); }
                _phase=0;
                break;
        }
        return SmokeResult.Running;
    }

    private void Shot(string name)
    {
        if (DisplayServer.GetName()=="headless" || System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is not { Length:>0 } folder) return;
        Directory.CreateDirectory(folder);
        _root!.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name+".png"));
    }
}
