using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Controller defaults through the actual pad dispatcher and gameplay input, without keyboard dependencies.</summary>
public sealed class ControllerDefaultsSmoke : SmokeTest
{
    public const string TestName = "controller-defaults";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        GameSettings previous = SettingsStore.Current;
        bool isolated = SettingsStore.Isolated;
        try
        {
            var settings = new GameSettings();
            settings.Pad.Enabled = true;
            settings.Clamp();
            SettingsStore.Isolated = true;
            SettingsStore.Reset(settings);
            foreach (bool merc in new[] { false, true })
                foreach (bool empty in new[] { false, true })
                {
                    using var rig = new Rig(merc, empty, settings);
                    CheckButtons(rig);
                    CheckCombinations(rig);
                    CheckInterfaceAndRelease(rig);
                    CheckWalk(rig);
                }
            return SmokeResult.Pass("spy/merc defaults with stock and empty keyboards: actions, modifier chords, hold/release, vision, interface isolation, disconnect and analog walk");
        }
        catch (InvalidOperationException e)
        {
            return SmokeResult.Fail(e.Message);
        }
        finally
        {
            SettingsStore.Reset(previous);
            SettingsStore.Isolated = isolated;
        }
    }

    private static void CheckButtons(Rig r)
    {
        r.Controller.SBPawn!.WeaponLoaded = 0;
        r.Tick("A");
        r.Button("bUsing");
        r.NoCommand("Jump");
        r.Release();

        r.Tick("Y");
        r.Command("Jump");
        r.Require(r.Local.Up == 280f, "jump did not provide its held axis");
        r.Release();

        r.Tick("X");
        r.Button(r.Merc ? "bReload" : "bFireMode");
        r.NoCommand("GadTorcheSwitch");
        r.Release();

        r.Tick("RT");
        r.Button("bFire");
        r.Command("Fire");
        r.NoCommand("GadgetQuickChoice");
        r.Tick();
        if (r.Merc) r.NoCommand("GadgetQuickChoice");
        else r.Command("GadgetQuickChoice", 0);
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Require(r.Local.Get("bFire") == 0, "released fire trigger remained held");

        r.Tick("RT");
        r.TickFor(PadBindingState.HoldSeconds + 0.01, "RT");
        r.NoCommand("GadgetQuickChoice");
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Release();

        r.Tick("LT");
        r.Button("bAltFire");
        r.Command("AltFire");
        r.Require(r.Local.Get("bHoldBreath") == 0, "gadget trigger also held breath");
        r.NoCommand("GadgetQuickChoice");
        r.Tick();
        if (r.Merc) r.NoCommand("GadgetQuickChoice");
        else r.Command("GadgetQuickChoice", 1);
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Require(r.Local.Get("bAltFire") == 0, "released gadget trigger remained held");

        r.Tick("LT");
        r.TickFor(PadBindingState.HoldSeconds + 0.01, "LT");
        r.NoCommand("GadgetQuickChoice");
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Release();

        if (r.Merc)
        {
            r.Controller.bBinocularVision = true;
            r.Tick("LT");
            r.Button("bAltFire");
            r.Button("bHoldBreath");
            r.Controller.bBinocularVision = false;
            r.Tick("LT");
            r.Button("bHoldBreath");
            r.Tick();
            r.NoCommand("GadgetQuickChoice");
            r.Tick();
            r.Require(r.Local.Get("bHoldBreath") == 0 && r.Local.Get("bAltFire") == 0,
                "scope change left gadget or breath held after trigger release");
        }

        r.Tick("B");
        if (r.Merc) r.Button("bCrouch");
        else r.Require(r.Local.Get("bCrouch") == 0, "spy crouch fired before tap/hold was resolved");
        r.Tick();
        if (!r.Merc) r.Button("bCrouch");
        r.Release();
        r.Require(r.Local.Get("bCrouch") == 0, "released crouch remained held");
        if (!r.Merc)
        {
            r.Tick("B");
            r.TickFor(PadBindingState.HoldSeconds + 0.01, "B");
            r.Command("LongRoll");
            r.Button("bRoll");
            r.Require(r.Local.Get("bCrouch") == 0, "held B also crouched");
            r.Release();
        }

        r.Tick("RB");
        if (r.Merc) r.Command("SwitchBinocularVision");
        else r.Button("bRoll");
        r.Release();

        r.Tick("DpadLeft");
        r.Command("SwitchHeadset", 1);
        r.Release();
        r.Tick("DpadRight");
        r.Command("SwitchHeadset", 2);
        r.Release();

        r.Tick("DpadUp");
        r.Command(r.Merc ? "GadTorcheSwitch" : "PrevStickyCam");
        r.Release();
        r.Controller.SBPawn!.WeaponLoaded = 0;
        r.Tick("DpadDown");
        if (r.Merc) r.Command("GadLaserSwitch");
        else { r.Button("bUseBackToWall"); r.NoCommand("SwitchLaser"); }
        r.Release();
        if (!r.Merc)
        {
            r.Controller.SBPawn.WeaponLoaded = 2;
            r.Tick("DpadDown");
            r.Command("SwitchLaser");
            r.Require(r.Local.Get("bUseBackToWall") == 0, "weapon laser also entered back-to-wall");
            r.Release();
            r.Controller.SBPawn.WeaponLoaded = 0;
        }
    }

    private static void CheckCombinations(Rig r)
    {
        string[] face = ["Y", "X", "B", "A"];
        for (int slot = 0; slot < face.Length; slot++)
        {
            r.Tick("LS");
            r.Tick("LS", face[slot]);
            r.Command("SelectGadget", slot);
            r.NoFaceAction();
            r.ReleaseCombination();

            if (r.Merc) continue;
            r.Tick("LB");
            r.NoCommand("PerformCoopMvt");
            r.Tick("LB", face[slot]);
            r.Command("LaunchGadget", slot);
            r.NoCommand("GadgetQuickChoice");
            r.NoFaceAction();
            r.NoCommand("PerformCoopMvt");
            r.ReleaseCombination();
        }

        r.Tick("LB");
        r.Tick("LB", "DpadDown");
        if (r.Merc) r.Button("bROF");
        else r.Command("SwitchBinocularVision");
        r.Require(r.Local.Get("bUseBackToWall") == 0, "LB+DpadDown also entered back-to-wall");
        r.NoCommand("GadLaserSwitch");
        r.ReleaseCombination();

        if (!r.Merc)
        {
            r.Tick("LB");
            r.NoCommand("PerformCoopMvt");
            r.Tick();
            r.Command("PerformCoopMvt");
            r.Release();
        }
    }

    private static void CheckInterfaceAndRelease(Rig r)
    {
        r.Tick("Back");
        r.Button("bSpecialsChat");
        r.Tick("Back");
        r.Button("bSpecialsChat");
        r.Require(r.InterfaceCommands.Count == 0, "Back alone opened a page");
        r.Release();

        r.Tick("Start");
        r.Require(r.InterfaceCommands.SequenceEqual(new[] { "ShowMenu" }), "Start did not dispatch only the menu");
        r.NoCommand("ShowMenu");
        r.Tick("Start");
        r.Require(r.InterfaceCommands.Count == 0, "held Start repeated its command");
        r.Release();

        r.Tick("LB");
        r.Tick("LB", "Back");
        r.Require(r.InterfaceCommands.SequenceEqual(new[] { "DisplaySessionStatus" }), "LB+Back did not dispatch only session status");
        r.Require(r.Local.Get("bSpecialsChat") == 0, "session shortcut also hacked communications");
        r.NoCommand("ToggleVoiceMute");
        r.NoCommand("DisplaySessionStatus");
        r.ReleaseCombination();

        r.Tick("RT", "LT", "Back");
        r.Button("bFire");
        r.Button("bAltFire");
        r.Button("bSpecialsChat");
        r.Pad._ExitTree(); // The real disconnect/teardown release path.
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Tick();
        r.NoCommand("GadgetQuickChoice");
        r.Require(LocalInput.ButtonNames.All(name => r.Local.Get(name) == 0), "disconnect left a gameplay button held");
        r.Tick("RT");
        r.Command("Fire");
        r.Release();
    }

    private static void CheckWalk(Rig r)
    {
        // The pawn reset emits SetWalk 0 first; the analog decision must win in the same tick.
        r.Controller.SBPawn = r.Merc ? new SPawnDefense_1Mesh() : new SPawnAttaque_1Mesh();
        r.Input.SetPadWalk(true);
        r.Tick();
        r.Require(r.Local.Commands.Last(c => c.Name == "SetWalk").Arg == 1, "pawn reset overrode analog walk");
        r.Input.SetPadWalk(false);
        r.Tick();
        r.Require(r.Local.Commands.Last(c => c.Name == "SetWalk").Arg == 0, "analog run was not delivered");
        r.Pad._ExitTree();
        r.Tick();
        r.NoCommand("SetWalk");
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _label;
        public readonly bool Merc;
        public readonly LocalInput Local = new();
        public readonly SPlayerController Controller;
        public readonly PlayInput Input;
        public readonly GamepadInput Pad;
        public readonly List<string> InterfaceCommands = new();

        public Rig(bool merc, bool empty, GameSettings settings)
        {
            Merc = merc;
            _label = $"{(merc ? "merc" : "spy")}/{(empty ? "empty keys" : "fallback keys")}";
            Controller = new SPlayerController { SBPawn = merc ? new SPawnDefense_1Mesh() : new SPawnAttaque_1Mesh() };
            KeyLayout layout = empty ? KeyLayout.Parse("[Engine.Input]") : DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, settings.MouseWheel);
            Input = new PlayInput
            {
                Target = Local, Controller = () => Controller, RequireCapture = false, CapturePointer = false,
                Bindings = InputBindings.FromLayout(layout, merc ? PlaySide.Merc : PlaySide.Spy),
            };
            Input.LocalCommand += InterfaceCommands.Add;
            Pad = new GamepadInput { PlayInput = Input, Settings = settings };
            Tick();
        }

        public void Tick(params string[] down) => TickFor(0.016, down);
        public void TickFor(double seconds, params string[] down)
        {
            Local.EndTick();
            InterfaceCommands.Clear();
            Pad.UpdateButtons(Input, Merc, new HashSet<string>(down, StringComparer.Ordinal), seconds);
            Input.FillTick(Local);
        }

        public void Release() { Tick(); Tick(); }
        public void ReleaseCombination()
        {
            for (int tick = 0; tick < 2; tick++)
            {
                Tick();
                NoFaceAction();
                NoCommand("PerformCoopMvt");
            }
        }
        public void Command(string name, int arg = 0) => Require(Local.Commands.Count(c => c.Name == name && c.Arg == arg) == 1, $"expected exactly one {name} {arg}; got {string.Join(", ", Local.Commands)}");
        public void NoCommand(string name) => Require(Local.Commands.All(c => c.Name != name), $"unexpected {name}");
        public void Button(string name) => Require(Local.Get(name) == 1, $"{name} was not held");
        public void NoFaceAction()
        {
            NoCommand("Jump");
            Require(new[] { "bUsing", "bCrouch", "bRoll", "bFireMode", "bReload" }.All(name => Local.Get(name) == 0), "modifier combination also fired a face-button action");
        }
        public void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(_label + ": " + message);
        }
        public void Dispose()
        {
            Pad._ExitTree();
            Input.LocalCommand -= InterfaceCommands.Add;
            Pad.Free();
            Input.Free();
        }
    }
}
