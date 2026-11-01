using System;
using System.IO;
using System.Linq;
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
    private int _readsBeforeSwap;
    private long _lastFrame;
    private double _maxSwapFrameMs;
    private RenderMap? _worldLights;
    private bool _captureChecked;
    private int _reloadShots;
    private bool _jumpDuringReload;

    public override SmokeResult Step(SmokeContext context)
    {
        long frame = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_phase == 2 && _lastFrame != 0)
            _maxSwapFrameMs = Math.Max(_maxSwapFrameMs, (frame - _lastFrame) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        _lastFrame = frame;
        if (_root is null)
        {
            _root=PlayRoot.Create(new PlayConfig
            {
                Settings=new PlaySettings { Map="SteeE", Side=PlaySide.Merc, Rules=PlayRules.PlayLevel, Sidearms=true,
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
        if (_root.Controls is { } controls)
        {
            if (!_captureChecked)
            {
                if (!controls.IsCaptured) return SmokeResult.Fail("joining play did not capture input without a click");
                controls.ReleasePointer();
                controls._Process(0);
                if (controls.IsCaptured) return SmokeResult.Fail("released pointer was captured again without a click");
                _captureChecked = true;
            }
            controls.Enabled=false;
        }
        if (pc.IsInState("WaitingForPawn")) return SmokeResult.Running;
        if (session.TicksRun-_at>600 && _phase!=0) return SmokeResult.Fail($"pistol {_kind} phase {_phase} timed out");
        var definition=Sidearms.All[Math.Min(_kind, Sidearms.All.Count - 1)];
        switch (_phase)
        {
            case 0:
                SidearmEquipment.Give(serverPawn,definition.Kind);
                if (definition.Kind != SidearmKind.Magnum44 && serverPawn.PlayerReplicationInfo is { } pri)
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
                _readsBeforeSwap = OpenScctGame.Render.Characters.SkeletalModel.FilesRead;
                session.Input.Commands.Add(new InputCommand("SwapWeapon"));
                _phase=2;_at=session.TicksRun;
                break;
            case 2:
                if (session.TicksRun-_at<45) break;
                if (_root.FirstPerson is not { Shown:true, Model:{ } model }) return SmokeResult.Fail("pistol view model is missing");
                if (OpenScctGame.Render.Characters.SkeletalModel.FilesRead != _readsBeforeSwap)
                    return SmokeResult.Fail("switching to the pistol loaded a cold model during gameplay");
                if (!model.Sampler.Has("FirstReloStFd2") || !model.Sampler.Has("FirstButtStFd2")) return SmokeResult.Fail("pistol contextual clips missing");
                var attachments = _root.Pawns?.ViewOf(pawn)?.Attachments;
                if (attachments is null || !attachments.TryGetWeaponBone(new Name("FX"), out _))
                    return SmokeResult.Fail("active third-person pistol socket is missing");
                Shot(definition.Id+"-ready");
                // Explicit studio diagnostic, separate from the map-lit capture above.
                _worldLights = _root.FirstPerson.Map;
                _root.FirstPerson.Map = null;
                model.Light(StudioLight());
                _phase=6;_at=session.TicksRun;
                break;
            case 6:
                if (session.TicksRun-_at<3) break;
                Shot(definition.Id+"-pose-studio");
                _root.FirstPerson!.Map = _worldLights;
                if (definition.Automatic)
                {
                    session.Input.Commands.Add(new InputCommand("SwitchROF"));
                    _phase=9;_at=session.TicksRun;
                    break;
                }
                session.Input.Set("bFire",true);session.Input.Commands.Add(new InputCommand("Fire"));
                _phase=3;_at=session.TicksRun;_shot=false;
                break;
            case 9:
                if (session.TicksRun-_at<30) break;
                if (pawn.SidearmWeapon?.ROFMode != SRateOfFireMode.ROF_Single ||
                    serverPawn.SidearmWeapon?.ROFMode != SRateOfFireMode.ROF_Single || _root.Hud?.Snapshot.FireMode != 0)
                    return SmokeResult.Fail("G18 single-shot mode disagrees between client, server and HUD");
                Shot("g18-single");
                session.Input.Commands.Add(new InputCommand("SwitchROF"));
                _phase=10;_at=session.TicksRun;
                break;
            case 10:
                if (session.TicksRun-_at<30) break;
                if (pawn.SidearmWeapon?.ROFMode != SRateOfFireMode.ROF_Auto ||
                    serverPawn.SidearmWeapon?.ROFMode != SRateOfFireMode.ROF_Auto || _root.Hud?.Snapshot.FireMode != 2)
                    return SmokeResult.Fail("G18 automatic mode disagrees between client, server and HUD");
                Shot("g18-auto");
                session.Input.Set("bFire",true);session.Input.Commands.Add(new InputCommand("Fire"));
                _phase=3;_at=session.TicksRun;_shot=false;
                break;
            case 3:
                if (session.TicksRun-_at<10) break;
                session.Input.Set("bFire",false);
                if (session.TicksRun-_at<35) break;
                if (pawn.SidearmWeapon!.ClipAmmo>=definition.Magazine) return SmokeResult.Fail("pistol did not fire");
                if (definition.Kind == SidearmKind.Magnum44)
                {
                    if (_root.Audio!.Sound!.Calls.Any(c => c.Sound == pawn.SidearmWeapon.ReloadSound.Path))
                        return SmokeResult.Fail("revolver played reload audio before starting a reload");
                    if (pawn.SidearmWeapon.ClipAmmo > 0)
                    {
                        if (session.TicksRun-_at < (definition.Interval + .1f) * Sim.Hz) break;
                        session.Input.Set("bFire",true);session.Input.Commands.Add(new InputCommand("Fire"));
                        _at=session.TicksRun;
                        break;
                    }
                    Shot("magnum44-empty-before-reload");
                }
                session.Input.Set("bReload",true);
                _root.FirstPerson!.Map = null;
                _root.FirstPerson.Model!.Light(StudioLight());
                _reloadShots=0;
                _jumpDuringReload=false;
                _phase=4;_at=session.TicksRun;
                break;
            case 4:
                if (!_jumpDuringReload && session.TicksRun-_at>=6)
                {
                    pawn.SidearmWeapon!.AnimateJump();
                    _jumpDuringReload=true;
                }
                if (definition.Kind == SidearmKind.Magnum44 && session.TicksRun-_at >= 2 && _reloadShots == 0)
                {
                    var reloadCalls = _root.Audio!.Sound!.Calls.Where(c => c.Sound == pawn.SidearmWeapon!.ReloadSound.Path).ToArray();
                    if (reloadCalls.Length != 1 || reloadCalls[0].Heard ||
                        reloadCalls[0].Outcome != OpenScctGame.Audio.SoundCallOutcome.Started ||
                        pawn.SidearmWeapon!.ReloadUntil <= 0 ||
                        pawn.SidearmWeapon.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL) != pawn.SidearmWeapon.AFirstReloSt ||
                        _root.FirstPerson!.ChannelFrame(SWeaponDefense.ACTIONCHANNEL) < 0)
                        return SmokeResult.Fail("empty revolver reload audio did not start with its visible reload");
                }
                if (definition.Kind == SidearmKind.Magnum44 && _reloadShots < 8 &&
                    session.TicksRun-_at > definition.ReloadSeconds * new[] { .22, .32, .40, .46, .54, .60, .68, .84 }[_reloadShots] * Sim.Hz)
                {
                    Shot("magnum44-reload-stage-"+_reloadShots);
                    _reloadShots++;
                }
                if (session.TicksRun-_at>30 && !_shot) { Shot(definition.Id+"-reload");_shot=true; }
                if (session.TicksRun-_at<(definition.ReloadSeconds+1)*Sim.Hz) break;
                session.Input.Set("bReload",false);
                pawn.SidearmWeapon!.AnimateLand();
                if (pawn.SidearmWeapon!.ClipAmmo!=definition.Magazine || serverPawn.SidearmWeapon!.ClipAmmo!=definition.Magazine)
                    return SmokeResult.Fail("pistol reload disagrees across worlds");
                pawn.SidearmWeapon.GotoState(new Name("s_ElbowShocking"));
                _phase=11;_at=session.TicksRun;
                break;
            case 11:
                if (session.TicksRun-_at<20) break;
                Shot(definition.Id+"-melee");
                _phase=12;
                break;
            case 12:
                if (session.TicksRun-_at<100) break;
                _root.FirstPerson!.Map = _worldLights;
                session.Input.Commands.Add(new InputCommand("SwapWeapon"));
                _phase=5;_at=session.TicksRun;
                break;
            case 5:
                if (session.TicksRun-_at<45) break;
                if (pawn.ActiveWeaponSlot!=0 || serverPawn.ActiveWeaponSlot!=0) return SmokeResult.Fail("return to primary failed");
                serverPawn.SidearmWeapon?.Dummy?.Destroy();serverPawn.SidearmWeapon?.Destroy();serverPawn.SidearmWeapon=null;
                if (++_kind==Sidearms.All.Count)
                {
                    session.Input.Commands.Add(new InputCommand("SetScope", 1));
                    _phase=7;_at=session.TicksRun;
                    break;
                }
                _phase=0;
                break;
            case 7:
                if (session.TicksRun-_at<90) break;
                if (!pc.IsInState("s_Binoculars_Def") || _root.FirstPerson?.Shown != false)
                    return SmokeResult.Fail("the rifle view model must be hidden in the merc scope");
                Shot("rifle-scoped");
                session.Input.Commands.Add(new InputCommand("SetScope", 0));
                _phase=8;_at=session.TicksRun;
                break;
            case 8:
                if (session.TicksRun-_at<90) break;
                if (_root.FirstPerson?.Shown != true) return SmokeResult.Fail("the rifle did not return after leaving the scope");
                _root.QueueFree();
                return SmokeResult.Pass($"All pistols: models, Cryo, firing, reload and switching; rifle hidden in scope and restored afterward. Worst swap frame {_maxSwapFrameMs:F1} ms; no cold model reads.");
        }
        return SmokeResult.Running;
    }

    private static ActorLightSet StudioLight()
    {
        // Directional shading makes empty chambers and cartridge rims readable in captures.
        var lights = new ActorLightSet { Ambient = new Vector3(.25f, .25f, .25f), Count = 2 };
        lights.Lights[0] = new PackedLight(new Vector4(.4f, .8f, .447214f, -1f),
            new Vector4(1.1f, 1.05f, 1f, 1f), new Vector4(0f, 0f, 0f, -2f));
        lights.Lights[1] = new PackedLight(new Vector4(-.6f, .3f, -.74162f, -1f),
            new Vector4(.35f, .4f, .5f, 1f), new Vector4(0f, 0f, 0f, -2f));
        return lights;
    }

    private void Shot(string name)
    {
        if (DisplayServer.GetName()=="headless" || System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is not { Length:>0 } folder) return;
        Directory.CreateDirectory(folder);
        _root!.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name+".png"));
    }
}
