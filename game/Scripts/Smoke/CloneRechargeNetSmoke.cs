using System;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Controls;

namespace OpenScctGame.Smoke;

/// <summary>Owner input, host deployment, replicated cooldown and the actual gadget HUD.</summary>
public sealed class CloneRechargeNetSmoke : SmokeTest
{
    public const string TestName = "clone-recharge-net";
    public override string Name => TestName;
    private PlayRoot? _root;
    private CloneGadget? _hostClone;
    private int _phase;
    private long _at, _deployed;
    private bool _middleShot;
    private readonly BindingInputState _keys = new();
    private readonly KeyLayout _layout = KeyLayout.Parse("""
        [Engine.Input]
        IK_LeftMouse=Button bFire | Function Fire | GadgetQuickChoice 0
        IK_RightMouse=Button bAltFire | Function AltFire | GadgetQuickChoice 1 | Button bHoldBreath
        IK_3=SelectGadget 2
        IK_Double+3=LaunchGadget 2
        """);

    public override SmokeResult Step(SmokeContext context)
    {
        if (_root is null)
        {
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with {
                Settings = launch.Settings with { Map = "AquaE", Side = PlaySide.Spy,
                    Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0 },
                Render = true, Hud = true, Audio = false, TicksPerFrame = 3, Pilot = null });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session is not { LocalPawn: SBasePawn pawn, LocalController: { } pc,
            ServerController.Pawn: SBasePawn hostPawn } session || pc.IsInState("WaitingForPawn")) return SmokeResult.Running;
        if (_root.Controls is { } controls) {
            if (controls.IsCaptured) controls.ReleasePointer();
            controls.RequireCapture = false; controls.Enabled = true;
        }
        if (session.TicksRun < 180) return SmokeResult.Running;
        long elapsed = session.TicksRun - _at;
        switch (_phase)
        {
            case 0:
                while (hostPawn.MyGadget is { } old) { hostPawn.MyGadget = old.MyGadget; old.Destroy(); }
                foreach (SGadget gadget in new SGadget[] { new SGadSmokeGrenade(), new SGadChaffeGrenade(), new CloneGadget() }) {
                    hostPawn.XLevel.SpawnActor(gadget, hostPawn.Location, default);
                    gadget.GiveTo(hostPawn);
                }
                _hostClone = hostPawn.MyGadget!.MyGadget!.MyGadget as CloneGadget
                    ?? throw new InvalidOperationException("Host clone fixture missing");
                hostPawn.SelectedGadget = _hostClone;
                _at = session.TicksRun; _phase = 1;
                break;
            case 1:
                if (elapsed < 90 || PawnInventory.GadgetInSlot(pawn, 2) is not CloneGadget client) break;
                pawn.SelectedGadget = client;
                _root.Controls!.Bindings = InputBindings.FromLayout(_layout, PlaySide.Spy);
                Tap("LeftMouse", 1000);
                _at = session.TicksRun; _phase = 2;
                break;
            case 2:
                if (elapsed < 150) break;
                if (_hostClone!.Charge != 1) return SmokeResult.Fail("Quick slot 1 also deployed the selected clone");
                Tap("RightMouse", 2000);
                _at = session.TicksRun; _phase = 3;
                break;
            case 3:
                if (elapsed < 150) break;
                if (_hostClone!.Charge != 1) return SmokeResult.Fail("Quick slot 2 also deployed the clone");
                Tap("3", 3000); Tap("3", 3200);
                _at = session.TicksRun; _phase = 4;
                break;
            case 4:
                if (_hostClone!.Charge != 0) {
                    if (elapsed > 180) return SmokeResult.Fail("Owner launch of clone slot 3 never reached the host");
                    break;
                }
                _deployed = session.TicksRun;
                _phase = 5;
                break;
            case 5:
                long since = session.TicksRun - _deployed;
                var hud = new HudSnapshot(); HudSimReader.Read(pc, hud);
                if (since > 60 && since < 3500) {
                    if (pawn.SelectedGadget is not CloneGadget ownerClone)
                        return SmokeResult.Fail("Launched clone was not selected in the owner HUD");
                    if (ownerClone.Charge != 0 || ownerClone.RechargeRemaining <= 0 || hud.Gadget?.RechargeRemaining <= 0)
                        return SmokeResult.Fail("Owner cooldown/HUD did not receive the host recharge state");
                }
                if (since > 1800 && !_middleShot) { Shot("clone-half-recharged"); _middleShot = true; }
                if (since < 3500 && _hostClone!.Charge != 0) return SmokeResult.Fail("Clone became ready before one minute");
                if (since < 3700) break;
                if (_hostClone!.Charge != 1 || PawnInventory.GadgetInSlot(pawn, 2) is not CloneGadget { Charge: 1, RechargeRemaining: 0 })
                    return SmokeResult.Fail("Host/owner clone did not recharge after one minute");
                Shot("clone-ready");
                _root.QueueFree();
                return SmokeResult.Pass("Real mouse aliases leave selected clone idle; double-tap 3 launches once on host; 60-second recharge and HUD replicate");
        }
        return SmokeResult.Running;
    }

    private void Tap(string key, long at)
    {
        foreach (var e in _keys.Update(_layout, key, true, nowMsec: at)) _root!.Controls!.Key(e.Key, e.Pressed);
        foreach (var e in _keys.Update(_layout, key, false, nowMsec: at+30)) _root!.Controls!.Key(e.Key, e.Pressed);
    }

    private void Shot(string name)
    {
        string folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.GetFullPath("../local/clone-recharge");
        Directory.CreateDirectory(folder);
        if (name == "clone-half-recharged" && _root?.Hud?.ActivePage is { } page)
        {
            var box = page.DrawList.Items.Where(i => i.Path == "Gadget/fond01").SelectMany(i => i.Points).ToArray();
            var fill = page.DrawList.Find("Gadget/RechargeFill")?.Points;
            if (box.Length == 0 || fill is not { Length: > 0 })
                throw new InvalidOperationException("Recharge box fill is missing");
            float bottom = box.Max(p => p.Y), height = bottom-box.Min(p => p.Y);
            float filled = fill.Max(p => p.Y)-fill.Min(p => p.Y);
            if (MathF.Abs(fill.Max(p => p.Y)-bottom) > 1 || filled/height is < .47f or > .53f)
                throw new InvalidOperationException($"Recharge bounds: box bottom={bottom}, height={height}, fill bottom={fill.Max(p => p.Y)}, height={filled}; ratio={filled/height}; must fill the lower half at 30 seconds");
        }
        _root!.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
    }
}
