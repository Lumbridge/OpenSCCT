using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.Voice;

namespace OpenScctGame.Smoke;

/// <summary>Exercises shared physical binding ownership through Godot input dispatch without writing settings.</summary>
internal static class BindingRuntimeChecks
{
    internal static List<string> Run(SceneTree tree)
    {
        var problems = new List<string>();
        BindingInput? ownedService = null;
        BindingDispatchProbe? probe = null;
        PlayInput? input = null;
        KeyLayout? savedLayout = null;
        Control? previousFocus = tree.Root.GuiGetFocusOwner();
        var local = new LocalInput();
        var held = new HashSet<Key>();
        var heldMouse = new HashSet<MouseButton>();
        BindingInput service = BindingInput.Current ?? (ownedService = new BindingInput { Name = "BindingRuntimeSession" });
        if (ownedService is not null) tree.Root.AddChild(ownedService);
        savedLayout = service.Layout;
        void Expect(bool condition, string description)
        {
            if (!condition) problems.Add("binding runtime: " + description);
        }

        // WindowInput is the native-window observer boundary. Viewport.PushInput intentionally bypasses that
        // native signal, so emit it explicitly before dispatching these synthetic events to the scene tree.
        void Send(InputEvent ev, bool nativeBoundary = true)
        {
            if (ev is InputEventMouseButton mouse && mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
            {
                if (mouse.Pressed) heldMouse.Add(mouse.ButtonIndex); else heldMouse.Remove(mouse.ButtonIndex);
            }
            if (nativeBoundary)
            {
                probe?.ExpectEvent(ev.GetInstanceId());
                tree.Root.EmitSignal(Window.SignalName.WindowInput, ev);
            }
            tree.Root.PushInput(ev);
        }
        InputEventKey KeyEvent(Key key, bool pressed, bool ctrl = false, bool shift = false, bool alt = false) => new()
        {
            Keycode = key, PhysicalKeycode = key, Pressed = pressed,
            CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt,
        };
        void KeyInput(Key key, bool pressed, bool ctrl = false, bool shift = false, bool alt = false, bool nativeBoundary = true)
        {
            if (pressed) held.Add(key); else held.Remove(key);
            Send(KeyEvent(key, pressed, ctrl, shift, alt), nativeBoundary);
        }
        void Tick()
        {
            local.EndTick();
            input!.FillTick(local);
        }
        void ReleaseTick()
        {
            // A release remains visible for the tick containing a short press, then clears on the next tick.
            Tick();
            Tick();
        }
        void ReleaseAll()
        {
            foreach (Key key in held.ToArray()) KeyInput(key, false);
            foreach (MouseButton button in heldMouse.ToArray()) Send(new InputEventMouseButton { ButtonIndex = button, Pressed = false });
            input?.ClearHeld(local);
            service.ResetAll();
        }

        try
        {
            tree.Root.GuiReleaseFocus();
            var layout = KeyLayout.Parse("""
                [Engine.Input]
                IK_E=Button bFire | Function Fire
                IK_Ctrl+E=Button bReload
                IK_F20=Button bFire | Function Fire
                IK_F21=Button bFire | Function Fire
                IK_Shift=InvButton bUsing
                IK_Ctrl+MouseWheelUp=Button bCrouch
                IK_Ctrl+MouseX1=Button bReload
                IK_MouseWheelUp=NextGadget
                IK_Ctrl+F22=PushToTalk
                IK_Alt+F22=TeamPushToTalk
                IK_F23=PushToTalk
                IK_F24=ToggleVoiceMute
                """);
            input = new PlayInput
            {
                Name = "BindingRuntimeInput", Bindings = InputBindings.FromLayout(layout, PlaySide.Spy),
                CapturePointer = false, RequireCapture = false, Target = local,
            };
            tree.Root.AddChild(input);
            probe = new BindingDispatchProbe { Name = "BindingDispatchProbe", Session = service };
            tree.Root.AddChild(probe);

            KeyInput(Key.Ctrl, true, ctrl: true);
            KeyInput(Key.E, true, ctrl: true);
            Tick();
            Expect(local.Get("bReload") == 1 && local.Get("bFire") == 0 && !local.Commands.Any(c => c.Name == "Fire"),
                "exact chord overrides its plain input");
            KeyInput(Key.Ctrl, false);
            ReleaseTick();
            Expect(local.Get("bReload") == 0 && local.Get("bFire") == 0, "required modifier release ends chord without plain fallback");
            KeyInput(Key.E, true);
            Tick();
            Expect(local.Get("bFire") == 0, "held chord base cannot retrigger after early modifier release");
            KeyInput(Key.E, false);

            KeyInput(Key.Ctrl, true, ctrl: true);
            KeyInput(Key.Shift, true, ctrl: true, shift: true);
            KeyInput(Key.E, true, ctrl: true, shift: true);
            Tick();
            Expect(local.Get("bFire") == 1 && local.Get("bReload") == 0 && local.Get("bUsing") == 1,
                "extra-modifier fallback keeps the plain input and standalone modifier action");
            ReleaseAll();

            KeyInput(Key.F20, true);
            KeyInput(Key.F21, true);
            Tick();
            KeyInput(Key.F20, false);
            Tick();
            Expect(local.Get("bFire") == 1, "releasing one alternate preserves another held source");
            KeyInput(Key.F21, false);
            ReleaseTick();
            Expect(local.Get("bFire") == 0, "releasing the final alternate ends the held action");

            KeyInput(Key.F20, true);
            Tick();
            probe.SwallowReleases = true;
            KeyInput(Key.F20, false);
            ReleaseTick();
            Expect(probe.ConsumedReleases > 0 && local.Get("bFire") == 0,
                "UI consumption cannot swallow a previously accepted gameplay release");
            probe.SwallowReleases = false;

            input.Enabled = false;
            KeyInput(Key.Ctrl, true, ctrl: true);
            KeyInput(Key.F22, true, ctrl: true);
            KeyInput(Key.F23, true, ctrl: true);
            Expect(service.VoiceAllHeld && local.Get("bFire") == 0, "voice aggregates alternate sources while gameplay is disabled");
            KeyInput(Key.Ctrl, false);
            Expect(service.VoiceAllHeld, "plain voice alternate survives chord release");
            KeyInput(Key.F22, false);
            KeyInput(Key.F23, false);
            Expect(!service.VoiceAllHeld, "last voice source releases");
            KeyInput(Key.Alt, true, alt: true);
            KeyInput(Key.F22, true, alt: true);
            Expect(service.VoiceTeamHeld && !service.VoiceAllHeld, "team voice remains independent of all-channel voice");
            KeyInput(Key.F22, false, alt: true);
            KeyInput(Key.Alt, false);
            Expect(!service.VoiceTeamHeld, "team voice release clears its held source");
            var mute = KeyEvent(Key.F24, true);
            held.Add(Key.F24);
            Send(mute);
            service.Resolve(mute);
            service.Resolve(mute);
            Expect(service.TakeMuteEdges() == 1 && service.TakeMuteEdges() == 0, "event cache prevents duplicate mute edges");
            KeyInput(Key.F24, false);
            input.Enabled = true;

            using (BindingInput.SuspendCapture())
            {
                KeyInput(Key.Ctrl, true, ctrl: true);
                KeyInput(Key.F22, true, ctrl: true);
                KeyInput(Key.F20, true, ctrl: true);
                Tick();
                Expect(!service.VoiceAllHeld && local.Get("bFire") == 0, "capture suppresses voice and gameplay");
            }
            KeyInput(Key.F20, true, ctrl: true);
            KeyInput(Key.F22, true, ctrl: true);
            Tick();
            Expect(!service.VoiceAllHeld && local.Get("bFire") == 0, "inputs held through capture remain blocked");
            ReleaseAll();
            KeyInput(Key.Ctrl, true, ctrl: true);
            KeyInput(Key.F22, true, ctrl: true);
            Expect(service.VoiceAllHeld, "fresh chord works after captured inputs release");
            ReleaseAll();

            KeyInput(Key.Ctrl, true, ctrl: true);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, CtrlPressed = true });
            Tick();
            Expect(local.Get("bCrouch") == 1 && local.Wheel == 1 && !local.Commands.Any(c => c.Name == "NextGadget"),
                "modified wheel uses its chord as a one-tick pulse");
            Tick();
            Expect(local.Get("bCrouch") == 0 && local.Wheel == 0, "wheel button action cannot remain held");
            ReleaseAll();

            KeyInput(Key.Ctrl, true, ctrl: true);
            // The caller hides the Options GUI while these gameplay-only input checks run.
            Vector2 clickPosition = tree.Root.GetVisibleRect().Size / 2f;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = true, CtrlPressed = true,
                Position = clickPosition, GlobalPosition = clickPosition });
            Tick();
            Expect(local.Get("bReload") == 1, "modified mouse button survives native-to-viewport event transformation");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = false, CtrlPressed = true,
                Position = clickPosition, GlobalPosition = clickPosition });
            ReleaseTick();
            Expect(local.Get("bReload") == 0, "modified mouse button releases");
            ReleaseAll();

            Expect(probe.MissingObservedEvents == 0 && probe.ObservedEvents > 0,
                "WindowInput caches events before node input handlers");
            // Headless/direct-injection callers bypass WindowInput; the unhandled consumer resolves lazily once.
            probe.RequireObserver = false;
            KeyInput(Key.F20, true, nativeBoundary: false);
            Tick();
            Expect(local.Get("bFire") == 1, "direct Viewport input still resolves through the shared session");
            KeyInput(Key.F20, false, nativeBoundary: false);
            ReleaseTick();
            Expect(local.Get("bFire") == 0, "direct-injection release clears the logical input");

            KeyInput(Key.F20, true, nativeBoundary: false);
            Tick();
            probe.SwallowReleases = true;
            var syntheticRelease = KeyEvent(Key.F20, false);
            held.Remove(Key.F20);
            service.Resolve(syntheticRelease);
            Send(syntheticRelease, nativeBoundary: false);
            ReleaseTick();
            Expect(local.Get("bFire") == 0, "synthetic callers resolve before UI can consume their release");
            probe.SwallowReleases = false;

            held.Add(Key.E);
            GamepadInput.Send(tree.Root, "Ctrl+E", true);
            Tick();
            Expect(local.Get("bReload") == 1 && local.Get("bFire") == 0, "legacy keyboard-linked controller chord selects its exact binding");
            GamepadInput.Send(tree.Root, "Ctrl+E", false);
            held.Remove(Key.E);
            ReleaseTick();
            Expect(local.Get("bReload") == 0, "legacy keyboard-linked controller chord releases");
            input.Enabled = false;
            held.Add(Key.F22);
            GamepadInput.Send(tree.Root, "Ctrl+F22", true);
            Expect(service.VoiceAllHeld, "synthetic controller chord can hold voice while gameplay is disabled");
            GamepadInput.Send(tree.Root, "Ctrl+F22", false);
            held.Remove(Key.F22);
            Expect(!service.VoiceAllHeld, "synthetic controller voice chord releases");
            input.Enabled = true;

            ReleaseAll();
            layout.Bind("MouseWheelUp", "SetSpyFirstPerson 1");
            layout.Bind("MouseWheelDown", "SetSpyFirstPerson 0");
            layout.Bind("X", "SwitchSpyPerspective");
            input.Bindings = InputBindings.FromLayout(layout, PlaySide.Spy);
            foreach (MouseButton direction in new[] { MouseButton.WheelUp, MouseButton.WheelUp, MouseButton.WheelDown, MouseButton.WheelDown })
            {
                Send(new InputEventMouseButton { ButtonIndex = direction, Pressed = true });
                Tick();
                int expected = direction == MouseButton.WheelUp ? 1 : 0;
                Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == expected &&
                    local.Commands.Any(c => c.Name == "SetSpyFirstPerson") &&
                    !local.Commands.Any(c => c.Name is "NextGadget" or "PreviousGadget"),
                    "direct perspective wheel notches select a view without toggling or changing gadgets");
                Tick();
                Expect(local.Commands.All(c => c.Name != "SetSpyFirstPerson" || c.Arg == expected),
                    "direct perspective survives the wheel pulse release");
            }
            foreach (string id in new[] { "firstperson", "thirdperson" })
            {
                string key = UI.ControlActions.VirtualPrefix + "RB:" + id;
                input.Key(key, true);
                input.Key(key, false);
                Tick();
                Expect(local.Commands.Any(c => c.Name == "SetSpyFirstPerson") &&
                    local.Commands.Last(c => c.Name == "SetSpyFirstPerson").Arg == (id == "firstperson" ? 1 : 0),
                    "controller direct perspective selects its view independently of keyboard bindings");
            }
            ReleaseAll();
            input.Bindings = InputBindings.FromLayout(layout, PlaySide.Merc);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            input.Key(UI.ControlActions.VirtualPrefix + "RB:firstperson", true);
            input.Key(UI.ControlActions.VirtualPrefix + "RB:thirdperson", true);
            Tick();
            Expect(!local.Commands.Any(c => c.Name == "SetSpyFirstPerson"), "merc ignores direct spy perspective bindings");
        }
        catch (Exception error)
        {
            problems.Add("binding runtime exception: " + error);
        }
        finally
        {
            ReleaseAll();
            if (probe is not null) { tree.Root.RemoveChild(probe); probe.Free(); }
            if (input is not null) { tree.Root.RemoveChild(input); input.Free(); }
            if (savedLayout is not null) VoiceInput.BindLayout(savedLayout);
            if (ownedService is not null) { tree.Root.RemoveChild(ownedService); ownedService.Free(); }
            if (previousFocus is not null && GodotObject.IsInstanceValid(previousFocus) && !previousFocus.IsQueuedForDeletion())
                previousFocus.GrabFocus();
        }
        return problems;
    }
}

internal sealed partial class BindingDispatchProbe : Node
{
    private readonly HashSet<ulong> _expectedEvents = new();
    public BindingInput Session { get; set; } = null!;
    public bool RequireObserver { get; set; } = true;
    public bool SwallowReleases { get; set; }
    public int MissingObservedEvents { get; private set; }
    public int ObservedEvents { get; private set; }
    public int ConsumedReleases { get; private set; }
    public void ExpectEvent(ulong eventId) => _expectedEvents.Add(eventId);

    public override void _Input(InputEvent input)
    {
        if (input is not (InputEventKey or InputEventMouseButton)) return;
        if (RequireObserver && _expectedEvents.Remove(input.GetInstanceId()))
        {
            ObservedEvents++;
            if (!Session.HasObserved(input)) MissingObservedEvents++;
        }
        if (SwallowReleases && !input.IsPressed())
        {
            ConsumedReleases++;
            GetViewport().SetInputAsHandled();
        }
    }
}
