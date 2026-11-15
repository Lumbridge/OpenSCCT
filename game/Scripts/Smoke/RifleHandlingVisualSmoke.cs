using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Hud;

namespace OpenScctGame.Smoke;

/// <summary>Actual play view captures of individual rifles: hip, ADS, EMF and the full reload timeline.</summary>
public sealed class RifleHandlingVisualSmoke : SmokeTest
{
    public const string TestName = "rifle-handling-visual";
    public override string Name => TestName;
    private PlayRoot? _root;
    private int _gun = int.TryParse(System.Environment.GetEnvironmentVariable("SVM_RIFLE_START"), out int first) ? first : 0;
    private int _phase, _reloadFrame;
    private long _at;
    private readonly List<object> _evidence = [];
    private bool _laserReload;
    private string _expectedClass = "";

    public override SmokeResult Step(SmokeContext context)
    {
        if (_root is null)
        {
            if (System.Environment.GetEnvironmentVariable("SVM_RIFLE_SIZE") is { } dimensions)
            {
                string[] parts = dimensions.Split('x');
                context.Tree.Root.Size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
            }
            RenderK.HighQualityVision = Options.SettingsStore.Current.HighQualityVision;
            var launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with
            {
                Settings = launcher.Settings with { Map = "AquaE", Side = PlaySide.Merc,
                    Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0, Sidearms = true },
                Render = true, Hud = true, Audio = false, TicksPerFrame = 1, Pilot = null,
            });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session is not { LocalPawn: { } pawn, LocalController: { } pc,
            ServerController.Pawn: SBasePawn serverPawn } session || pc.IsInState("WaitingForPawn")) return SmokeResult.Running;
        if (_root.Controls is { } controls)
        {
            if (controls.IsCaptured) controls.ReleasePointer();
            controls.RequireCapture = false; controls.Enabled = true;
        }
        if (session.TicksRun < 100) return SmokeResult.Running;
        string id = MercRifles.All[_gun].Id.ToLowerInvariant();
        string classPath = System.Environment.GetEnvironmentVariable("SVM_RIFLE_CLASS") ?? MercRifles.All[_gun].ClassPath;
        long elapsed = session.TicksRun - _at;
        switch (_phase)
        {
            case 0:
                serverPawn.MyWeapon?.Destroy();
                var weapon = classPath.StartsWith("sidearm:", StringComparison.Ordinal)
                    ? new MercSidearm { Kind = int.Parse(classPath[8..]) }
                    : OpenScct.Game.Devices.DeviceClasses.Index.Find(classPath)!.Create() as SWeaponDefense
                    ?? throw new InvalidOperationException("Rifle class is missing");
                serverPawn.XLevel.SpawnActor(weapon, serverPawn.Location, default);
                weapon.GiveTo(serverPawn); weapon.bDrawWeapon = true; weapon.GotoState("s_Selected");
                serverPawn.MyWeapon = weapon;
                if (weapon is MercSidearm) serverPawn.ActiveWeaponSlot = 1;
                _expectedClass = weapon.GetUcClass().Path;
                serverPawn.WeaponLoaded = 2; serverPawn.bWeaponTransit = false;
                session.ServerController!.GotoState("s_PlayerWalkingSt2H");
                _root.FirstPerson!.Map = _root.Map;
                _at = session.TicksRun; _phase = 1;
                break;
            case 1:
                if (elapsed < 70 || pawn.MyWeapon?.GetUcClass().Path != _expectedClass) break;
                // A direct server fixture spawn bypasses the ordinary owner inventory grant.
                var active = (SWeaponDefense)pawn.MyWeapon;
                active.GiveTo(pawn);
                pawn.MyWeapon = active;
                active.bDrawWeapon = true;
                _at = session.TicksRun; _phase = 10;
                break;
            case 10:
                if (elapsed < 8) break;
                if (System.Environment.GetEnvironmentVariable("SVM_RIFLE_COSMETIC") is { } cosmetic)
                    _root.FirstPerson!.Model!.SetCosmetic(cosmetic);
                Shot(id + "-hip");
                _root.FirstPerson!.Map = null;
                _root.FirstPerson.Model!.Light(StudioLight());
                _at = session.TicksRun; _phase = 11;
                break;
            case 11:
                if (elapsed < 8) break;
                Shot(id + "-hip-studio");
                if (pawn.MyWeapon is not MercRifle) { _at = session.TicksRun; _phase = 3; break; }
                PlayerCommands.Execute(pc, new InputCommand("SetScope", 1));
                _at = session.TicksRun; _phase = 2;
                break;
            case 2:
                if (elapsed < 40) break;
                if (pawn.MyWeapon is not MercRifle { AimRequested: true } || serverPawn.MyWeapon is not MercRifle { AimRequested: true })
                    return SmokeResult.Fail(id + ": ADS differs between client and server: " +
                        $"client={pawn.MyWeapon?.GetStateName()}/{(pawn.MyWeapon as MercRifle)?.AimRequested}, controller={pc.GetStateName()}, loaded={pawn.WeaponLoaded}, transit={pawn.bWeaponTransit}, lock={pc.iDontUpdatePadInput}, inventory={pc.bIsInInventoryMode}; " +
                        $"server={serverPawn.MyWeapon?.GetStateName()}/{(serverPawn.MyWeapon as MercRifle)?.AimRequested}, controller={session.ServerController!.GetStateName()}, loaded={serverPawn.WeaponLoaded}, transit={serverPawn.bWeaponTransit}");
                Shot(id + "-ads");
                PlayerCommands.Execute(pc, new InputCommand("SetScope", 0));
                _at = session.TicksRun; _phase = 3;
                break;
            case 3:
                if (elapsed < 30) break;
                pc.RendMap = SGoggle.REN_EMFVision;
                if (pc.Goggle is { } goggle) goggle.CurrentMode = SGoggle.REN_EMFVision;
                PlayerCommands.Execute(pc, new InputCommand("SwitchLaser"));
                _at = session.TicksRun; _phase = 4;
                break;
            case 4:
                if (elapsed < 20) break;
                Shot(id + "-emf");
                _root.FirstPerson!.Model!.Root.Visible = false;
                _at = session.TicksRun; _phase = 41;
                break;
            case 41:
                if (elapsed < 8) break;
                Shot(id + "-emf-without-foreground");
                _root.FirstPerson!.Model!.Root.Visible = true;
                _root.Pawns!.Visible = false;
                _at = session.TicksRun; _phase = 42;
                break;
            case 42:
                if (elapsed < 8) break;
                Shot(id + "-emf-without-pawns");
                _root.Pawns!.Visible = true;
                pc.RendMap = SGoggle.REN_DynLight;
                if (pc.Goggle is { } normal) normal.CurrentMode = SGoggle.REN_DynLight;
                pawn.MyWeapon!.Fire(1f);
                _at = session.TicksRun; _phase = 5;
                break;
            case 5:
                if (elapsed < 50) break;
                _laserReload = pawn is SPAWNDEFENSE { TorcheLaser.bInUse: true };
                pawn.MyWeapon!.Reload();
                _at = session.TicksRun; _phase = 6; _reloadFrame = 0;
                break;
            case 6:
                if (_laserReload)
                {
                    var hud = new HudSnapshot(); HudSimReader.Read(pc, hud);
                    if (hud.ShowReticle) return SmokeResult.Fail(id + ": crosshair reappeared during a laser reload");
                }
                if (elapsed >= 3 && elapsed >= _reloadFrame * 12)
                {
                    Shot(id + "-reload-" + _reloadFrame.ToString("D2"));
                    _reloadFrame++;
                }
                if (elapsed < 210) break;
                if (System.Environment.GetEnvironmentVariable("SVM_RIFLE_ONCE") != "1" && ++_gun < MercRifles.All.Count) { _phase = 0; break; }
                string folder = Folder();
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "poses.json"), JsonSerializer.Serialize(_evidence, new JsonSerializerOptions { WriteIndented = true }));
                _root.QueueFree();
                return SmokeResult.Pass("Captured requested weapon cases in map/studio lighting, ADS, EMF, and full reloads; " + folder);
        }
        return SmokeResult.Running;
    }

    private static string Folder() => System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.GetFullPath("../local/rifle-handling");

    private static ActorLightSet StudioLight()
    {
        var light = new ActorLightSet { Ambient = new Vector3(.35f, .35f, .35f), Count = 2 };
        light.Lights[0] = new PackedLight(new Vector4(.4f, .8f, .447214f, -1), new Vector4(.8f, .8f, .8f, 1), new Vector4(0, 0, 0, -2));
        light.Lights[1] = new PackedLight(new Vector4(-.6f, .3f, -.74162f, -1), new Vector4(.3f, .3f, .3f, 1), new Vector4(0, 0, 0, -2));
        return light;
    }

    private void Shot(string name)
    {
        if (_root?.FirstPerson is not { Shown: true, Model: { } model })
            throw new InvalidOperationException("No visible foreground weapon: " + name);
        var bones = new Dictionary<string, float[]>();
        foreach (string bone in new[] { "L_wrist_03", "L_thumb4_07", "L_point4_011", "Main_j_049", "ADS" })
        {
            int index = model.Skeleton.FindBone(bone);
            if (index < 0) continue;
            Vector3 p = (model.Skeleton.GlobalTransform * model.CurrentBonePose(index)).Origin;
            bones[bone] = [p.X, p.Y, p.Z];
        }
        _evidence.Add(new { name, bones, tick = _root.Session!.TicksRun, vision = _root.View!.RendMap,
            reload = _root.FirstPerson.ChannelFrame(SWeaponDefense.ACTIONCHANNEL), aim = _root.FirstPerson.AimBlend,
            asset = _root.Session.LocalPawn?.MyWeapon?.Mesh.Path, model.Root.Visible,
            transform = model.Root.GlobalTransform.ToString() });
        Directory.CreateDirectory(Folder());
        if (DisplayServer.GetName() != "headless") _root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Folder(), name + ".png"));
    }
}
