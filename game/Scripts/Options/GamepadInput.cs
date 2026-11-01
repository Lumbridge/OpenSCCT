using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

/// <summary>
/// Controller play (rl:input.pad): the controller in use (the last one that sent input, else the first connected)
/// drives the local player through independent actions (or a saved legacy key layout). The left stick (radial deadzone, optional eight-way snap) is
/// the analog move (<see cref="LocalInput.PadForward"/>: full circle, a part push walks slower) and also holds the keys
/// bound to BT_Up/Down/Left/Right past <see cref="MovePress"/>; the right stick adds look counts (deadzone, response curve, vertical
/// scale, the side's pad look multiplier). PadBindingState resolves the side's buttons, held actions and combinations
/// (<see cref="PadSettings.SpyButtons"/>, <see cref="PadSettings.MercButtons"/>). Legacy assignments still send their
/// keyboard/mouse events; new actions and local menu commands work without any keyboard binding. The spy runs above the
/// walk threshold. Defaults are Reloaded's (deadzones 0.25 and 0.15, vertical 0.75, walk threshold 0.95).
/// </summary>
public partial class GamepadInput : Node
{
    /// <summary>The Godot buttons of the pad controls (the triggers are axes).</summary>
    public static readonly IReadOnlyDictionary<string, JoyButton> Buttons = new Dictionary<string, JoyButton>(StringComparer.Ordinal)
    {
        ["A"] = JoyButton.A,
        ["B"] = JoyButton.B,
        ["X"] = JoyButton.X,
        ["Y"] = JoyButton.Y,
        ["LB"] = JoyButton.LeftShoulder,
        ["RB"] = JoyButton.RightShoulder,
        ["LS"] = JoyButton.LeftStick,
        ["RS"] = JoyButton.RightStick,
        ["DpadUp"] = JoyButton.DpadUp,
        ["DpadDown"] = JoyButton.DpadDown,
        ["DpadLeft"] = JoyButton.DpadLeft,
        ["DpadRight"] = JoyButton.DpadRight,
        ["Start"] = JoyButton.Start,
        ["Back"] = JoyButton.Back,
    };

    /// <summary>How far a trigger goes down before it counts as pressed.</summary>
    public const float TriggerPress = 7680f / 32767f;

    /// <summary>How far the left stick leans before a direction key goes down.</summary>
    public const float MovePress = 0.38f;

    /// <summary>The key each held control (a pad control or a stick direction) pressed, so the release lifts that key.</summary>
    private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _keyOf = new(StringComparer.OrdinalIgnoreCase);
    private InputBindings? _keysFor;
    private bool _wasMerc;
    private bool _wasFast;
    private bool _stickSet;
    private readonly PadBindingState _buttonsState = new();
    private readonly PadActionState _actionState = new();
    private readonly HashSet<string> _clutchOne = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clutchTwo = new(StringComparer.Ordinal);
    private GameSettings? _settingsFor;
    private bool _wasFlying;

    private static ulong s_drivenFrame = ulong.MaxValue;

    public GamepadInput()
    {
        Name = "GamepadInput";
    }

    public PlayInput? PlayInput { get; set; }

    public PlaySession? Session { get; set; }

    public GameSettings Settings { get; set; } = new();

    /// <summary>The pad in use, or -1.</summary>
    public int Device { get; private set; } = -1;

    /// <summary>True while a controller drives a player (this frame or the last): the menus leave its buttons alone.</summary>
    public static bool Driving => s_drivenFrame != ulong.MaxValue && Engine.GetProcessFrames() - s_drivenFrame <= 1;

    public override void _Process(double delta)
    {
        int previousDevice = Device;
        Device = PadMenus.CurrentDevice();
        bool merc = Session?.LocalPawn is SPAWNDEFENSE || Session?.LocalPawn is null && PlayInput?.Bindings?.Side == PlaySide.Merc;
        LookSettings swaps = Settings.LookOf(merc, pad: true);
        var down = new HashSet<string>(StringComparer.Ordinal);
        if (Device >= 0)
            foreach (string control in PadSettings.PhysicalControls)
                if (IsDown(Device, swaps.PhysicalControl(control), Settings.Pad.TriggerThreshold)) down.Add(control);

        if (!Settings.Pad.Enabled || PlayInput is not { Enabled: true, Bindings: { } bindings } input || Typing() || !input.HasInputFocus)
        {
            ReleaseAll();
            _buttonsState.Reset(down);
            return;
        }

        if (Device < 0)
        {
            ReleaseAll();
            return;
        }

        if (!ReferenceEquals(_keysFor, bindings) || merc != _wasMerc || !ReferenceEquals(_settingsFor, Settings) || previousDevice != Device)
        {
            bool hadLayout = _keysFor is not null;
            ReleaseAll();
            if (hadLayout) _buttonsState.Reset(down);
            _keyOf.Clear();
            _keysFor = bindings;
            _settingsFor = Settings;
            _wasMerc = merc;
        }

        s_drivenFrame = Engine.GetProcessFrames();
        PadSettings pad = Settings.Pad;
        if (pad.Move)
        {
            Vector2 move = Stick(JoyAxis.LeftX, JoyAxis.LeftY, pad.DeadzoneLeft);
            bool snap = merc ? pad.MercMove8 : pad.SpyMove8;
            if (snap && move != Vector2.Zero)
            {
                float a = Mathf.Snapped(move.Angle(), Mathf.Pi / 4f);
                move = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * move.Length();
            }

            // The direction keys still go down past MovePress (the menus and every reader of the key bytes see them),
            // but the move itself is the analog stick: any direction, and a part push walks slower unless slow walk is
            // off, when only the direction counts.
            bool direct = pad.LayoutVersionOf(merc) > 0;
            Hold(input, "MoveUp", direct ? "Action:forward" : "BT_Up", move.Y < -MovePress);
            Hold(input, "MoveDown", direct ? "Action:back" : "BT_Down", move.Y > MovePress);
            Hold(input, "MoveLeft", direct ? "Action:left" : "BT_Left", move.X < -MovePress);
            Hold(input, "MoveRight", direct ? "Action:right" : "BT_Right", move.X > MovePress);
            Vector2 analog = pad.SlowWalk || move == Vector2.Zero ? move : move.Normalized();
            Session?.Input.SetPadStick(-analog.Y, analog.X);
            _stickSet = true;
            if (direct) input.SetPadWalk(!merc && pad.SlowWalk && move.Length() > 0f ? move.Length() < pad.RunThreshold : null);
            else RunThreshold(input, merc, move.Length(), pad.RunThreshold);
        }
        else
        {
            ClearStick();
        }

        bool flying = FreeCamera();
        if (flying != _wasFlying)
        {
            ReleaseButtons();
            _buttonsState.Reset(down);
            _wasFlying = flying;
        }
        if (flying) down.RemoveWhere(c => IsFreeCamControl(swaps.PhysicalControl(c)));
        if (pad.Binds) UpdateButtons(input, merc, down, delta);
        else { ReleaseButtons(); _buttonsState.Reset(down); }

        if (pad.Aim && Session is { } session)
        {
            Vector2 look = Stick(JoyAxis.RightX, JoyAxis.RightY, pad.DeadzoneRight);
            LookSettings group = Settings.LookOf(merc, pad: true);
            float k = pad.LookSpeed * (float)delta * LegacyUnitsPerCount / MouseLook.UnitsPerCount;
            float clutch = group.ClutchOf(_clutchOne.Count > 0, _clutchTwo.Count > 0);
            (float h, float v) = PadMultipliers(session);
            string response = !merc && session.LocalPawn?.WeaponLoaded == 2 ? group.GunLookResponse : group.LookResponse;
            session.Input.MouseX += Response(look.X * clutch, response) * k * h;
            session.Input.MouseY -= Response(look.Y * clutch, response) * k * v * pad.VerticalScale;
            if (look != Vector2.Zero)
            {
                Capture(input);
            }
        }
    }

    /// <summary>Logical buttons after stick/bumper swaps. Shared by polling and the controller smoke.</summary>
    internal void UpdateButtons(PlayInput input, bool merc, IReadOnlySet<string> down, double delta)
    {
        SPlayerController? pc = Session?.LocalController ?? input.Controller?.Invoke();
        SBasePawn? pawn = pc?.SBPawn;
        bool slot2Cloak = false;
        int slot = 0;
        for (SGadget? gadget = pawn?.MyGadget; gadget is not null; gadget = gadget.MyGadget)
        {
            if (!OpenScct.Game.Equipment.PawnInventory.IsSelectable(gadget)) continue;
            if (slot++ == 1) { slot2Cloak = gadget is SGadCloak; break; }
        }
        var context = new PadActionContext(InCamera: pc is not null && PlayerCommands.InCamera(pc), IsSpy: !merc,
            WeaponDrawn: pawn?.WeaponLoaded == 2, CloakSelected: pawn?.SelectedGadget is SGadCloak,
            CloakActive: pawn?.DesiredGlassEffect == 255, Scoped: pc?.bBinocularVision == true,
            Slot2IsCloak: slot2Cloak,
            CameraTransition: pc?.IsInState("s_UseObject_StickyCam_SmoothToPosition") == true);
        foreach (PadBindingEvent ev in _actionState.Update(_buttonsState.Update(Settings.Pad.ButtonsOf(merc), down, delta), delta, context))
        {
            if (ev.Pressed && !input.Enabled) break; // A menu command can stop gameplay during this batch.
            if (ev.Action is PadSettings.ClutchOneAction or PadSettings.ClutchTwoAction)
            {
                HashSet<string> held = ev.Action == PadSettings.ClutchOneAction ? _clutchOne : _clutchTwo;
                if (ev.Pressed) held.Add(ev.Control); else held.Remove(ev.Control);
                continue;
            }
            string action = ev.Action;
            if (action == "Pad:walllaser")
                action = (Session?.LocalPawn ?? input.Controller?.Invoke()?.SBPawn)?.WeaponLoaded == 2 ? "Action:weaponlaser" : "Action:wall";
            Hold(input, ev.Control, action, ev.Pressed);
        }
    }

    private void ReleaseButtons()
    {
        _buttonsState.ReleaseAll();
        foreach (PadBindingEvent ev in _actionState.ReleaseAll())
            if (PlayInput is { } input) Hold(input, ev.Control, ev.Action, false);
        _clutchOne.Clear();
        _clutchTwo.Clear();
    }

    /// <summary>
    /// The controller groups' look multipliers for the view the player is in (free or gun look, scope or binoculars,
    /// camera views, the vertical scales: rl:input.sens), divided by the mouse's: the session's mouse look scales every
    /// count by the mouse groups, so the stick's counts end up with the controller groups' multipliers alone.
    /// </summary>
    private (float Horizontal, float Vertical) PadMultipliers(PlaySession session)
    {
        if (session.LocalController is not { } pc)
        {
            return (1f, 1f);
        }

        if (!ReferenceEquals(_padLookFor, Settings))
        {
            _padLookFor = Settings;
            _padLook = SettingsApplier.MouseLookOf(Settings, pad: true);
        }

        (float ph, float pv) = _padLook.Multipliers(pc);
        (float mh, float mv) = session.Mouse.Multipliers(pc);
        return (ph / Math.Max(1e-3f, mh), pv / Math.Max(1e-3f, mv));
    }

    /// <summary>
    /// Rotation units a count turned (multiplier 1, sensitivity 40, the 1280-wide reference window) when the stick
    /// speeds were tuned: Speed 6 x 1280 / 640 x 4 x 50 x the tick. The mouse now turns
    /// <see cref="MouseLook.UnitsPerCount"/> at any window size, so the stick's counts are scaled by the ratio and the
    /// controller turns as it did in a 1280-wide window.
    /// </summary>
    private const float LegacyUnitsPerCount = 6f * (1280f / 640f) * 4f * 50f * OpenScct.Core.Sim.DeltaTime;

    private GameSettings? _padLookFor;
    private MouseLook _padLook = new();

    public override void _ExitTree() => ReleaseAll();

    private bool _lbWas;
    private bool _rbWas;

    /// <summary>The triggers and shoulder buttons the free camera takes while it flies.</summary>
    private static bool IsFreeCamControl(string control) => control is "LT" or "RT" or "LB" or "RB";

    /// <summary>
    /// The cinematic free camera's pad controls (Enhanced 4.0, spec/hud/cinematic-camera.md rule 8): while it flies the
    /// triggers fly it up (right) and down (left) and the shoulder buttons step its speed (right faster, left slower,
    /// one step per press). The left stick moves it and the right stick turns it as they always do. True while it flies.
    /// </summary>
    private bool FreeCamera()
    {
        if (Session is not { LocalController: { FreeCameraFlying: not 0 } } session)
        {
            if (Session is { } idle)
            {
                idle.Input.PadUp = 0f;
            }

            _lbWas = _rbWas = false;
            return false;
        }

        session.Input.PadUp = Godot.Input.GetJoyAxis(Device, JoyAxis.TriggerRight) - Godot.Input.GetJoyAxis(Device, JoyAxis.TriggerLeft);
        bool lb = IsDown(Device, "LB");
        bool rb = IsDown(Device, "RB");
        if (rb && !_rbWas)
        {
            session.Input.Wheel++;
        }

        if (lb && !_lbWas)
        {
            session.Input.Wheel--;
        }

        _lbWas = lb;
        _rbWas = rb;
        return true;
    }

    /// <summary>A pad control is down: a button, or a trigger past <see cref="TriggerPress"/>.</summary>
    public static bool IsDown(int device, string control, float threshold = TriggerPress) => control switch
    {
        "LT" => Godot.Input.GetJoyAxis(device, JoyAxis.TriggerLeft) > threshold,
        "RT" => Godot.Input.GetJoyAxis(device, JoyAxis.TriggerRight) > threshold,
        _ => Buttons.TryGetValue(control, out JoyButton b) && Godot.Input.IsJoyButtonPressed(device, b),
    };

    /// <summary>A stick with a radial deadzone, rescaled so the edge of the deadzone is 0.</summary>
    public Vector2 Stick(JoyAxis x, JoyAxis y, float deadzone)
    {
        var v = new Vector2(Godot.Input.GetJoyAxis(Device, x), Godot.Input.GetJoyAxis(Device, y));
        return Radial(v, deadzone);
    }

    /// <summary>The radial deadzone with rescale (inputs inside the deadzone are 0, the rim stays 1).</summary>
    public static Vector2 Radial(Vector2 v, float deadzone)
    {
        float len = v.Length();
        if (len <= deadzone || len <= 0f)
        {
            return Vector2.Zero;
        }

        float scaled = Math.Min(1f, (len - deadzone) / Math.Max(1e-4f, 1f - deadzone));
        return v / len * scaled;
    }

    /// <summary>
    /// Reloaded's response curves (rl:input.curves): Linear; Soft keeps the inner 75 % linear and ramps the outer 25 %
    /// to 2x; Medium and Aggressive ramp to 2.5x and 3x.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.curves: Soft inner 75 % linear, outer 25 % to 2x; Medium 2.5x; Aggressive 3x")]
    public static float Response(float v, string curve)
    {
        float top = curve switch
        {
            "CurveSoft" => 2f,
            "CurveMedium" => 2.5f,
            "CurveAggressive" => 3f,
            _ => 1f,
        };
        float a = Math.Abs(v);
        if (top == 1f || a <= 0.75f)
        {
            return v;
        }

        float t = (a - 0.75f) / 0.25f;
        return Math.Sign(v) * (0.75f + t * (top - 0.75f));
    }

    /// <summary>Sends a key or mouse button (IK name) as a real Godot event, as the keyboard or mouse would.</summary>
    public static void Send(Viewport? viewport, string ik, bool down)
    {
        switch (GodotKeys.ToEvent(ik))
        {
            case InputEventKey key:
                // Some handlers read Keycode (the hub), others PhysicalKeycode (the play input).
                key.Keycode = key.PhysicalKeycode;
                key.Pressed = down;
                Godot.Input.ParseInputEvent(key);
                break;
            case InputEventMouseButton button:
                // A captured pointer clicks at the middle of the window.
                Vector2 middle = viewport is null ? Vector2.Zero : viewport.GetVisibleRect().Size / 2f;
                button.Position = middle;
                button.GlobalPosition = middle;
                button.Pressed = down;
                Godot.Input.ParseInputEvent(button);
                break;
        }
    }

    /// <summary>A text field has the keys (the chat line): the pad must not type into it.</summary>
    private bool Typing() => GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit;

    /// <summary>
    /// The spy's automatic run: above the walk threshold the spy runs, below it walks (ChangeSpeed toggles
    /// SPlayerController.bFastSpeed). Mercs have no speed toggle.
    /// </summary>
    private void RunThreshold(PlayInput input, bool merc, float deflection, float threshold)
    {
        if (merc || (input.Bindings?.Layout.Bindings.Values.Any(c => c.Contains("Walk", StringComparison.OrdinalIgnoreCase)) ?? false) || Session?.LocalController is not { } pc || deflection <= 0f)
        {
            return;
        }

        bool fast = deflection >= threshold;
        if (fast != pc.bFastSpeed && fast != _wasFast && KeyFor(input, "ChangeSpeed") is { } key)
        {
            Capture(input);
            Send(GetViewport(), key, true);
            Send(GetViewport(), key, false);
        }

        _wasFast = fast;
    }

    /// <summary>Holds or releases the key the layout binds to <paramref name="action"/> for one control.</summary>
    private void Hold(PlayInput input, string control, string action, bool down)
    {
        if (down)
        {
            if (action is "Action:menu" or "Action:session")
            {
                input.SendLocalCommand(action == "Action:menu" ? "ShowMenu" : "DisplaySessionStatus");
                return;
            }
            string? resolved = action.StartsWith(UI.ControlActions.PadPrefix, StringComparison.Ordinal)
                ? UI.ControlActions.VirtualPrefix + control + ":" + action[UI.ControlActions.PadPrefix.Length..]
                : KeyFor(input, action);
            if (_held.ContainsKey(control) || resolved is not { } key)
            {
                return;
            }

            if (!string.Equals(action, PadSettings.MenuAction, StringComparison.OrdinalIgnoreCase))
            {
                Capture(input);
            }

            SendControl(input, key, true);
            if (key.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase))
            {
                // The wheel has no hold: one notch per press.
                SendControl(input, key, false);
            }

            _held[control] = key;
        }
        else if (_held.Remove(control, out string? key) && !key.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase))
        {
            SendControl(input, key, false);
        }
    }

    private void SendControl(PlayInput? input, string key, bool pressed)
    {
        if (key.StartsWith(UI.ControlActions.VirtualPrefix, StringComparison.Ordinal)) input?.Key(key, pressed);
        else Send(IsInsideTree() ? GetViewport() : null, key, pressed);
    }

    /// <summary>
    /// The play input reads keys only while the pointer is captured (the mouse's first click takes it): pad play
    /// takes it the same way.
    /// </summary>
    private static void Capture(PlayInput input)
    {
        if (input.RequireCapture && input.CapturePointer && !input.IsCaptured && DisplayServer.GetName() != "headless")
        {
            Pointer.Capture();
        }
    }

    /// <summary>
    /// The key that runs <paramref name="action"/> in the layout: the first key bound to that command or button, the
    /// key itself for a <see cref="PadSettings.KeyPrefix"/> binding, Escape for the menu when no key runs ShowMenu.
    /// </summary>
    private string? KeyFor(PlayInput input, string action)
    {
        if (action.Length == 0)
        {
            return null;
        }

        if (action.StartsWith(PadSettings.KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return action[PadSettings.KeyPrefix.Length..];
        }

        if (_keyOf.TryGetValue(action, out string? cached))
        {
            return cached;
        }

        string? found = null;
        if (input.Bindings is { } b)
        {
            foreach (string key in b.Layout.Bindings.Keys)
            {
                foreach (KeyAction a in b.Actions(key))
                {
                    if (string.Equals(a.Name, action, StringComparison.OrdinalIgnoreCase) && a.Kind != KeyActionKind.Axis)
                    {
                        found = key;
                        break;
                    }
                }

                if (found is not null)
                {
                    break;
                }
            }
        }

        if (found is null && string.Equals(action, PadSettings.MenuAction, StringComparison.OrdinalIgnoreCase))
        {
            found = "Escape";
        }

        _keyOf[action] = found;
        return found;
    }

    /// <summary>Lets go of the analog stick this node set.</summary>
    private void ClearStick()
    {
        PlayInput?.SetPadWalk(null);
        if (_stickSet)
        {
            _stickSet = false;
            Session?.Input.SetPadStick(0f, 0f);
        }
    }

    private void ReleaseAll()
    {
        ReleaseButtons();
        ClearStick();
        if (Session is { } session)
        {
            session.Input.PadUp = 0f;
        }

        if (_held.Count == 0)
        {
            return;
        }

        Viewport? viewport = IsInsideTree() ? GetViewport() : null;
        foreach (string key in _held.Values)
        {
            if (!key.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase))
            {
                SendControl(PlayInput, key, false);
            }
        }

        _held.Clear();
    }
}
