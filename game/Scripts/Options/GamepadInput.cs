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
/// bound to BT_Up/Down/Left/Right past <see cref="MovePress"/>; the right stick adds look counts (deadzone, radial response curve, vertical
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
        ["P1"] = JoyButton.Paddle1,
        ["P2"] = JoyButton.Paddle2,
        ["P3"] = JoyButton.Paddle3,
        ["P4"] = JoyButton.Paddle4,
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

    /// <summary>The longest the look stick is ignored after the ping wheel closes while it has not recentred.</summary>
    public const double WheelRecentreSeconds = 0.5;
    private bool _wheelWas;
    private double _lookHold;

    private static ulong s_drivenFrame = ulong.MaxValue;
    private static readonly HashSet<GamepadInput> s_instances = new();
    private bool _suppressButtonsUntilRelease;

    // Event dispatch can precede the first process frame. Query the actual owner, not last frame's marker.
    public static bool GameplayActive => s_instances.Any(pad => pad.Settings.Pad.Enabled &&
        pad.PlayInput is { Enabled: true });

    public override void _EnterTree()
    {
        s_instances.Add(this);
        _suppressButtonsUntilRelease = true;
    }

    public static void SuppressHeldButtons()
    {
        foreach (GamepadInput pad in s_instances) pad._suppressButtonsUntilRelease = true;
    }

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

        if (!Settings.Pad.Enabled || PlayInput is not { Bindings: { } bindings } input || Typing() || !input.HasInputFocus ||
            (!input.Enabled && input.ModalInputActive?.Invoke() != true))
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
        if (!input.Enabled)
        {
            ClearStick();
            foreach (string key in _held.Values) SendControl(input, key, false);
            _held.Clear();
            if (pad.Binds) UpdateButtons(input, merc, down, delta);
            return;
        }
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

        bool wheelOpen = input.TeamPing.WheelOpen;
        Vector2 look = pad.Aim || wheelOpen ? Stick(JoyAxis.RightX, JoyAxis.RightY, pad.DeadzoneRight) : Vector2.Zero;
        if (wheelOpen) input.TeamPing.StickDirection(look.X, look.Y);

        // The stick that chose on the ping wheel is still pushed over when the wheel closes: it turns the view
        // again only once it is back in the middle (or after WheelRecentreSeconds, for a stick held on purpose).
        if (_wheelWas && !wheelOpen) _lookHold = WheelRecentreSeconds;
        _wheelWas = wheelOpen;
        if (_lookHold > 0)
        {
            if (look == Vector2.Zero) _lookHold = 0;
            else
            {
                _lookHold -= delta;
                look = Vector2.Zero;
            }
        }

        if (_suppressButtonsUntilRelease && down.Count == 0) _suppressButtonsUntilRelease = false;
        if (pad.Binds && !_suppressButtonsUntilRelease) UpdateButtons(input, merc, down, delta);
        else { ReleaseButtons(); _buttonsState.Reset(down); }

        if (pad.Aim && !wheelOpen && Session is { } session)
        {
            LookSettings group = Settings.LookOf(merc, pad: true);
            float k = StickCountsPerSecond * pad.LookScale * (float)delta;
            float clutch = group.ClutchOf(_clutchOne.Count > 0, _clutchTwo.Count > 0);
            (float h, float v) = PadMultipliers(session);
            string response = !merc && session.LocalPawn?.WeaponLoaded == 2 ? group.GunLookResponse : group.LookResponse;
            bool camera = session.LocalController is { } viewer && MouseLook.InCameraView(viewer);
            Vector2 shaped = Shape(look, response) * clutch * CurveGain(response, camera, merc);
            session.Input.MouseX += shaped.X * k * h;
            session.Input.MouseY -= shaped.Y * k * v * pad.VerticalScale;
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
        var context = new PadActionContext(InCamera: pc is not null && PlayerCommands.InCamera(pc), IsSpy: !merc,
            WeaponDrawn: pawn?.WeaponLoaded == 2, Scoped: pc?.bBinocularVision == true,
            CameraTransition: pc?.IsInState("s_UseObject_StickyCam_SmoothToPosition") == true);
        foreach (PadBindingEvent ev in _actionState.Update(_buttonsState.Update(Settings.Pad.ButtonsOf(merc), down, delta), delta, context))
        {
            if (ev.Pressed && !input.Enabled &&
                (input.ModalInputActive?.Invoke() != true ||
                 !(ev.Action.StartsWith("Action:hub_", StringComparison.Ordinal) || ev.Action == "Action:menu"))) continue;
            if (ev.Action is PadSettings.ClutchOneAction or PadSettings.ClutchTwoAction)
            {
                HashSet<string> held = ev.Action == PadSettings.ClutchOneAction ? _clutchOne : _clutchTwo;
                if (ev.Pressed) held.Add(ev.Control); else held.Remove(ev.Control);
                continue;
            }
            string action = ev.Action;
            if (action == "Pad:walllaser")
                action = (Session?.LocalPawn ?? input.Controller?.Invoke()?.SBPawn)?.WeaponLoaded == 2 ? "Action:weaponlaser" : "Action:wall";
            if (!ev.Pressed && down.Contains(ev.Control) && _held.TryGetValue(ev.Control, out string? key))
                input.TeamPing.CancelSource(key);
            Hold(input, ev.Control, action, ev.Pressed);
        }
    }

    private void ReleaseButtons()
    {
        _buttonsState.ReleaseAll();
        foreach (PadBindingEvent ev in _actionState.ReleaseAll())
        {
            if (PlayInput is not { } input) continue;
            if (_held.TryGetValue(ev.Control, out string? key)) input.TeamPing.CancelSource(key);
            Hold(input, ev.Control, ev.Action, false);
        }
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
    /// The look stick's rate at full deflection with a multiplier of 1, in mouse counts a second. Reloaded's pad code
    /// writes <c>aMouseX = frame seconds x 62 x stick x multiplier x clutch x 0.5</c> after the frame is presented, and
    /// the next frame's UInput::ReadInput scales it by 2 / DeltaTime like every input float, so the engine sees
    /// <c>aMouseX = 62 x stick x multiplier x clutch</c> at any frame rate. A mouse count gives
    /// <c>0.1 x multiplier / 10 x 2 / DeltaTime</c> (<see cref="MouseLook"/>), so the stick is worth
    /// 62 / (0.1 / 10 x 2) = 3100 counts a second: 9300 rotation units (51.1 degrees) a second per unit of multiplier.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b2840 (Enhanced 4.0 Beta 3): frame ms (0x1036e2bc, written at 0x100a6aae) x 62 / 1000 x 0.5 into PlayerController+0x4d4/+0x4d8 (aMouseX, aMouseY); called from the Present hook (0x100b63e0), before the next frame's ReadInput x 2 / DeltaTime (game 0x109fe9a0)")]
    public const float StickCountsPerSecond = 62f / (MouseLook.AxisEventScale / MouseLook.MultiplierDivisor * MouseLook.ReadInputScale);

    /// <summary>The look multiplier Reloaded's pad code uses in a camera view (sticky camera, camera network), before the side's cam setting.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b2840: 3.2 in place of the look multiplier while the camera-view flag 0x1036abbc is set; 0x10019b20 then scales by the pad's cam setting x 10 with fSensibiliteSouris 0.4")]
    public const float CameraStickMultiplier = 3.2f;

    /// <summary>
    /// What the shaped stick is multiplied by besides the side's look multiplier: the look multiplier is divided by
    /// the curve's inner scale (so the knee turns at 1x and the rim at 2x, 2.5x or 3x); in a camera view the
    /// multiplier is <see cref="CameraStickMultiplier"/>, divided by the scale for the merc only (the spy's camera
    /// views keep the raw curve: its rim turns at 1x and its knee at the scale).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b2840: spy free or gun look / table[curve].scale, camera view 3.2 with no division; merc (look or 3.2) / table[curve].scale")]
    public static float CurveGain(string curve, bool cameraView, bool merc)
    {
        float scale = Curve(curve).Scale;
        return cameraView ? CameraStickMultiplier / (merc ? scale : 1f) : 1f / scale;
    }

    private GameSettings? _padLookFor;
    private MouseLook _padLook = new();

    public override void _ExitTree()
    {
        s_instances.Remove(this);
        ReleaseAll();
    }

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
    /// Reloaded's response curves of the look stick (rl:input.curves), as its pad code shapes them: the curve acts on
    /// the stick's length (after the deadzone), never on each axis, so a diagonal push turns as fast as a straight one
    /// and the direction of the push is kept. Each curve is a knee and an inner scale (Linear 1 and 1; Soft 0.75 and
    /// 0.5; Medium 0.75 and 0.4; Aggressive 0.75 and 1/3): up to the knee the length rises in a straight line to the
    /// scale, past it by the square of the way to the rim up to 1. With the look multiplier divided by the scale
    /// (<see cref="CurveGain"/>) the inner 75 % is linear and reaches 1x at the knee, and the outer 25 % eases (no
    /// jump in speed at the knee) up to 2x, 2.5x or 3x at the rim. A sensitivity clutch multiplies the result.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100b2840 (Enhanced 4.0 Beta 3): radial deadzone with rescale, table 0x103411b0 {knee, scale} = {1, 1}, {0.75, 0.5}, {0.75, 0.4}, {0.75, 0.3333}; a > knee: ((a - knee) / (1 - knee))^2 x (1 - scale) + scale, else scale / knee x a; times direction, look multiplier / scale, clutch")]
    public static Vector2 Shape(Vector2 stick, string curve)
    {
        float len = stick.Length();
        if (len <= 0f)
        {
            return Vector2.Zero;
        }

        (float knee, float scale) = Curve(curve);
        float a = Math.Min(1f, len);
        float shaped;
        if (a > knee)
        {
            float t = (a - knee) / (1f - knee);
            shaped = t * t * (1f - scale) + scale;
        }
        else
        {
            shaped = scale / knee * a;
        }

        return stick / len * shaped;
    }

    /// <summary>A response curve's knee and inner scale (Reloaded.Core table 0x103411b0).</summary>
    private static (float Knee, float Scale) Curve(string curve) => curve switch
    {
        "CurveSoft" => (0.75f, 0.5f),
        "CurveMedium" => (0.75f, 0.4f),
        "CurveAggressive" => (0.75f, 1f / 3f),
        _ => (1f, 1f),
    };

    /// <summary>Sends a key or mouse button (IK name) as a real Godot event, as the keyboard or mouse would.</summary>
    public static void Send(Viewport? viewport, string ik, bool down)
    {
        switch (GodotKeys.ToEvent(ik))
        {
            case InputEventKey key:
                // Some handlers read Keycode (the hub), others PhysicalKeycode (the play input).
                key.Keycode = key.PhysicalKeycode;
                key.Pressed = down;
                BindingInput.Inject(key);
                break;
            case InputEventMouseButton button:
                // A captured pointer clicks at the middle of the window.
                Vector2 middle = viewport is null ? Vector2.Zero : viewport.GetVisibleRect().Size / 2f;
                button.Position = middle;
                button.GlobalPosition = middle;
                button.Pressed = down;
                BindingInput.Inject(button);
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
        if (down && action is "Action:players" or "Action:scoreboard_hold")
        {
            if (_held.ContainsKey(control)) return;
            string local = "scoreboard-local:" + control + ":" + (action == "Action:players" ? "ShowConnectedPlayers" : "HoldScoreboard");
            _held[control] = local;
            SendControl(input, local, true);
            return;
        }
        if (down)
        {
            if (action.StartsWith("Action:hub_", StringComparison.Ordinal))
            {
                var binding = OpenScctGame.Controls.HubBindings.All.FirstOrDefault(h => "Action:" + h.Id == action);
                if (binding.Command is not null) input.SendLocalCommand(binding.Command);
                return;
            }
            if (action is "Action:menu" or "Action:session")
            {
                input.SendLocalCommand(action == "Action:menu" ? "ShowMenu" : "DisplaySessionStatus");
                return;
            }
            if (action is "Action:quickchat" or "Action:chat" or "Action:teamchat")
            {
                if (_held.ContainsKey(control)) return;
                _held[control] = "chat-local:" + control;
                input.SendLocalCommand(action == "Action:quickchat" ? "QuickChat" : action == "Action:teamchat" ? "TeamTalk" : "Talk");
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
            if (GodotKeys.IsPulse(key))
            {
                // The wheel has no hold: one notch per press.
                SendControl(input, key, false);
            }

            _held[control] = key;
        }
        else if (_held.Remove(control, out string? key) && !GodotKeys.IsPulse(key))
        {
            SendControl(input, key, false);
        }
    }

    private void SendControl(PlayInput? input, string key, bool pressed)
    {
        if (key.StartsWith("chat-local:", StringComparison.Ordinal)) return;
        if (key.StartsWith("scoreboard-local:", StringComparison.Ordinal)) input?.SendLocalAction(key[(key.LastIndexOf(':') + 1)..], key, pressed);
        else if (key.StartsWith(UI.ControlActions.VirtualPrefix, StringComparison.Ordinal)) input?.Key(key, pressed);
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
            if (!GodotKeys.IsPulse(key))
            {
                PlayInput?.TeamPing.CancelSource(key);
                SendControl(PlayInput, key, false);
            }
        }

        _held.Clear();
    }
}
