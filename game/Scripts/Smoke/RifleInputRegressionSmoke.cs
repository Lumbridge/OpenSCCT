using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using OpenScct.Game.Equipment;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Real held/released movement and scope keys through a connected owner's prediction.</summary>
public sealed class RifleInputRegressionSmoke : SmokeTest
{
    public const string TestName = "rifle-input-regression";
    public override string Name => TestName;
    private PlayRoot? _root;
    private int _phase;
    private long _at;
    private float _hip;
    private SGadBackPack? _pack;
    private readonly List<object> _samples = [];
    private static readonly KeyLayout Layout = KeyLayout.Parse("""
        [Engine.Input]
        IK_W=Button BT_Up
        IK_B=SwitchBinocularVision
        IK_RightMouse=Button bAltFire | Function AltFire | GadgetQuickChoice 1 | Button bHoldBreath
        """);

    public override SmokeResult Step(SmokeContext context)
    {
        if (_root is null)
        {
            var launch = PlayConfig.FromOptions(context.App.Options);
            Options.SettingsStore.Current.LookOf(true, false).ScopeBehavior = "Hold";
            _root = PlayRoot.Create(launch with {
                Settings = launch.Settings with { Map = "AquaE", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Inline, Bots = 0 }, Render = true, Hud = true, Audio = true,
                TicksPerFrame = 1, Pilot = null });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session is not { LocalPawn: { } pawn, LocalController: { } pc,
            ServerController.Pawn: SBasePawn hostPawn } session || pc.IsInState("WaitingForPawn")) return SmokeResult.Running;
        if (_root.Controls is not { } input) return SmokeResult.Running;
        if (input.IsCaptured) input.ReleasePointer();
        input.RequireCapture = false; input.Enabled = true;
        if (session.TicksRun < 120) return SmokeResult.Running;
        long elapsed = session.TicksRun - _at;
        if (_phase is >= 2 and <= 5)
        {
            var rifle = pawn.MyWeapon as MercRifle;
            var model = _root.FirstPerson?.Model;
            int sight = model?.Skeleton.FindBone("ADS") ?? -1;
            Vector3 ads = sight >= 0 ? model!.CurrentBonePose(sight).Origin : default;
            _samples.Add(new { phase = _phase, tick = session.TicksRun, localSpeed = pawn.Velocity.Size2D(),
                hostSpeed = hostPawn.Velocity.Size2D(), pawn.GroundSpeed, aim = rifle?.AimRequested,
                hostAim = (hostPawn.MyWeapon as MercRifle)?.AimRequested, blend = _root.FirstPerson?.AimBlend,
                state = pc.GetStateName().ToString(), weapon = rifle?.GetStateName().ToString(),
                ads = new[] {ads.X, ads.Y, ads.Z}, sequence = rifle?.ViewAnim.Sequence(0).ToString(), frame = _root.FirstPerson?.ChannelFrame(0) });
        }
        switch (_phase)
        {
            case 0:
                hostPawn.MyWeapon?.Destroy();
                var m4 = hostPawn.XLevel.SpawnActor(new M4A1S(), hostPawn.Location, default)!;
                m4.GiveTo(hostPawn); m4.bDrawWeapon = true; m4.GotoState("s_Selected");
                hostPawn.MyWeapon = m4; hostPawn.WeaponLoaded = 2; hostPawn.bWeaponTransit = false;
                session.ServerController!.GotoState("s_PlayerWalkingSt2H");
                _at = session.TicksRun; _phase = 1; break;
            case 1:
                if (elapsed < 70 || pawn.MyWeapon is not M4A1S owner) break;
                owner.GiveTo(pawn); pawn.MyWeapon = owner; owner.bDrawWeapon = true;
                input.Bindings = InputBindings.FromLayout(Layout, PlaySide.Merc);
                input.Key("W", true); _at = session.TicksRun; _phase = 2; break;
            case 2:
                if (elapsed < 40) break;
                _hip = pawn.Velocity.Size2D();
                input.Key("B", true); _at = session.TicksRun; _phase = 3; break;
            case 3:
                if (elapsed < 40) break;
                Require(pawn.MyWeapon is MercRifle { AimRequested: true } && hostPawn.MyWeapon is MercRifle { AimRequested: true }, "ADS owner/host mismatch");
                Require(MathF.Abs(pawn.Velocity.Size2D() - _hip*.25f) < 1f && MathF.Abs(hostPawn.Velocity.Size2D()-_hip*.25f) < 1f,
                    $"ADS speed mismatch: hip={_hip}, owner={pawn.Velocity.Size2D()}, host={hostPawn.Velocity.Size2D()}");
                Shot("ads-moving");
                input.Key("W", false); _at = session.TicksRun; _phase = 4; break;
            case 4:
                if (elapsed <= 14) Shot("ads-stop-" + elapsed.ToString("D2"));
                Require(pawn.MyWeapon is MercRifle { AimRequested: true } && _root.FirstPerson!.AimBlend > .99f, "Releasing movement lowered ADS");
                if (elapsed < 30) break;
                input.Key("B", false); input.Key("W", true); _at = session.TicksRun; _phase = 5; break;
            case 5:
                if (elapsed < 40) break;
                input.Key("W", false);
                var mask = hostPawn.XLevel.SpawnActor(new SGadGasMask(), hostPawn.Location, default)!;
                mask.GiveTo(hostPawn); hostPawn.SelectedGadget = mask;
                _at = session.TicksRun; _phase = 6; break;
            case 6:
                if (elapsed < 60) break;
                SGadGasMask? localMask = null;
                for (var g = pawn.MyGadget; g is not null; g = g.MyGadget) if (g is SGadGasMask found) localMask = found;
                if (localMask is null) return SmokeResult.Fail("Gas mask did not replicate");
                pawn.SelectedGadget = localMask;
                input.Key("RightMouse", true); _at = session.TicksRun; _phase = 7; break;
            case 7:
                if (elapsed < 12) break;
                input.Key("RightMouse", false);
                Require(pawn.bGasMask && hostPawn.bGasMask, "Regular alternate fire did not activate the gas mask on owner and host");
                Shot("regular-gadget-use");
                while (hostPawn.MyGadget is { } old) { hostPawn.MyGadget = old.MyGadget; old.Destroy(); }
                Give(hostPawn, [new SGadFragGrenade(), new SGadMultiMinesLauncher(), new SGadGasMask(), new SGadBackPack()]);
                _pack = (SGadBackPack)PawnInventory.GadgetInSlot(hostPawn, 3)!;
                hostPawn.SelectedGadget = _pack;
                Mate(hostPawn, "Alice", new FVector(200, 0, 0), [new SGadMultiMinesLauncher(), new SGadFragGrenade(), new SGadFlareLauncher(), new SGadGasMask()]);
                Mate(hostPawn, "Bob", new FVector(0, 200, 0), [new SGadGasMask(), new SGadMultiMinesLauncher(), new SGadFragGrenade(), new SGadBackPack()]);
                _at = session.TicksRun; _phase = 8; break;
            case 8:
                if (elapsed < 60) break;
                for (var g = pawn.MyGadget; g is not null; g = g.MyGadget) if (g is SGadBackPack found) pawn.SelectedGadget = found;
                _at = session.TicksRun; _phase = 9; break;
            case 9:
                if (elapsed < 10) break;
                Require(_root.Hud?.ActivePage?.Area("Backpack")?.Find("BackPackSelf")?.Visible == true, "Backpack self panel missing");
                CheckPack(pc, hostPawn);
                Shot("backpack");
                _pack!.aNbChargesGad_Self[0] = 1;
                hostPawn.aNbChargesGad_Mate_A0 = 0;
                hostPawn.aNbChargesGad_Mate_B0 = 2;
                _at = session.TicksRun; _phase = 12; break;
            case 12:
                if (elapsed < 30) break;
                var backpackArea = _root.Hud!.ActivePage!.Area("Backpack")!;
                Require(backpackArea.Find("BackPackFriend")!.Widget("friend1").Text == "0", "Teammate A depleted count did not update");
                Require(backpackArea.Find("BackPackFriend1")!.Widget("friend1").Text == "2", "Teammate B count did not update");
                Shot("backpack-counts-updated");
                PawnInventory.GadgetInSlot(pawn, 0)!.Charge = PawnInventory.GadgetInSlot(hostPawn, 0)!.Charge = 0;
                input.Key("RightMouse", true);
                _at = session.TicksRun; _phase = 13; break;
            case 13:
                if (elapsed < 15) break;
                input.Key("RightMouse", false);
                Require(PawnInventory.GadgetInSlot(pawn, 0)!.Charge > 0, "Backpack refill did not give own gadget charges");
                Require(_root.Hud!.ActivePage!.Area("Backpack")!.Find("BackPackSelf")!.Widget("self1").Text == "0", "Own backpack availability did not decrease after refill");
                Shot("backpack-after-refill");
                ((SWeaponDefense)pawn.MyWeapon!).ClipAmmo = ((SWeaponDefense)hostPawn.MyWeapon!).ClipAmmo = 0;
                pawn.MyWeapon!.Ammo = hostPawn.MyWeapon!.Ammo = 200;
                ((SWeaponDefense)hostPawn.MyWeapon!).PublishCarriedAmmo();
                pawn.MyWeapon!.eventReload();
                _at = session.TicksRun; _phase = 10; break;
            case 10:
                if (elapsed < 170) break;
                Require(_root.Audio!.Sound!.Starts.Any(x => x.Call == "OpenScctRifles.m4a1s_reload"), "Empty reload recording missing");
                ((SWeaponDefense)pawn.MyWeapon!).ClipAmmo = ((SWeaponDefense)hostPawn.MyWeapon!).ClipAmmo = 1;
                ((SWeaponDefense)hostPawn.MyWeapon!).PublishCarriedAmmo();
                pawn.MyWeapon!.eventReload();
                _at = session.TicksRun; _phase = 11; break;
            case 11:
                if (elapsed < 170) break;
                var reloads = _root.Audio!.Sound!.Starts.Where(x => x.Call.StartsWith("OpenScctRifles.m4a1s_reload", StringComparison.Ordinal)).Select(x => x.Call).ToArray();
                Require(reloads.Length == 2 && reloads[0] == "OpenScctRifles.m4a1s_reload" && reloads[1] == "OpenScctRifles.m4a1s_reload_tactical", "Empty-to-tactical reload audio mismatch: " + string.Join(",", reloads));
                string folder = Folder(); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.json"), JsonSerializer.Serialize(new { samples = _samples, reloads }, new JsonSerializerOptions { WriteIndented = true }));
                _root.QueueFree();
                return SmokeResult.Pass($"Recorded movement, owner/host ADS, key release and gadget panels; hip speed={_hip}; {folder}");
        }
        return SmokeResult.Running;
    }
    private static void Give(SBasePawn pawn, SGadget[] gadgets)
    {
        foreach (var gadget in gadgets) { pawn.XLevel.SpawnActor(gadget, pawn.Location, default); gadget.GiveTo(pawn); }
    }
    private static void Mate(SBasePawn owner, string name, FVector offset, SGadget[] gadgets)
    {
        var pawn = owner.XLevel.SpawnActor(new SPawnDefense_1Mesh(), owner.Location + offset, default)!;
        var pc = owner.XLevel.SpawnActor(new SPlayerController(), pawn.Location, default)!;
        pc.Possess(pawn);
        var pri = owner.XLevel.SpawnActor(new PlayerReplicationInfo(), pawn.Location, default)!;
        pri.SetOwner(pc); pri.PlayerName = name; pri.Team = owner.PlayerReplicationInfo!.Team;
        pc.PlayerReplicationInfo = pawn.PlayerReplicationInfo = pri;
        pawn.SetPhysics(EPhysics.PHYS_None);
        Give(pawn, gadgets);
    }
    private void CheckPack(SPlayerController pc, SBasePawn pawn)
    {
        var page = _root!.Hud!.ActivePage!;
        var area = page.Area("Backpack")!;
        var options = page.Assets.Package("Options")!;
        string[] names = ["BackPackSelf", "BackPackFriend", "BackPackFriend1"];
        SBasePawn[] people = [pawn, pawn.aBackPackOwner_A!, pawn.aBackPackOwner_B!];
        for (int panel = 0; panel < 3; panel++)
        {
            Require(people[panel] is not null, "Backpack teammate did not replicate");
            var node = area.Find(names[panel])!;
            Require(node.Visible, names[panel] + " panel missing");
            int row = 0;
            for (var gadget = people[panel].MyGadget; gadget is not null && row < 4; gadget = gadget.MyGadget)
            {
                if (!SBackPackInteraction.Refillable(gadget)) continue;
                int material = options.FindMaterial(gadget.MagmaIconName);
                Require(material >= 0 && node.Widget("Ico" + (++row)).Region == options.Materials[material].Region,
                    names[panel] + " wrong gadget icon: " + gadget.MagmaIconName);
            }
            Require(row == 4, names[panel] + " has an incomplete loadout");
        }
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static string Folder() => System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.GetFullPath("../local/rifle-input");
    private void Shot(string name)
    {
        Directory.CreateDirectory(Folder());
        if (DisplayServer.GetName() != "headless") _root!.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Folder(), name + ".png"));
    }
}
