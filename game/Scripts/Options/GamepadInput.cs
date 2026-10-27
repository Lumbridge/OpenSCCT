using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

/// <summary>
/// Controller basics (rl:input.pad): the first connected Godot joypad (SDL mapping database) drives the local player
/// through the side's key layout: the left stick holds the keys bound to BT_Up/Down/Left/Right (radial deadzone,
/// optional eight-way snap), the right stick adds look counts (deadzone, response curve, vertical scale, the side's pad
/// look multiplier), and the buttons press the keys bound to their actions. The spy toggles to running above the walk
/// threshold. Defaults are Reloaded's (deadzones 0.25 and 0.15, vertical 0.75, walk threshold 0.95).
/// </summary>
public partial class GamepadInput : Node
{
    /// <summary>The button layout (Reloaded's own pad layout is not decoded, FEATURES open question 4).</summary>
    [Provenance(ProvenanceKind.Estimated, "Default pad layout per team not decoded; stock action names on the usual pad positions")]
    public static readonly IReadOnlyDictionary<JoyButton, string> Buttons = new Dictionary<JoyButton, string>
    {
        [JoyButton.A] = "Jump",
        [JoyButton.B] = "bCrouch",
        [JoyButton.X] = "bUsing",
        [JoyButton.Y] = "SwitchHeadset",
        [JoyButton.LeftShoulder] = "bUseBackToWall",
        [JoyButton.RightShoulder] = "bRoll",
        [JoyButton.LeftStick] = "ChangeSpeed",
        [JoyButton.RightStick] = "SwitchBinocularVision",
        [JoyButton.DpadUp] = "WheelUp",
        [JoyButton.DpadDown] = "WheelDown",
        [JoyButton.DpadLeft] = "bFireMode",
        [JoyButton.DpadRight] = "bReload",
    };

    /// <summary>Trigger presses: right fires, left alternate fires.</summary>
    public const float TriggerPress = 0.5f;

    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _keyOf = new(StringComparer.OrdinalIgnoreCase);
    private InputBindings? _keysFor;
    private bool _wasFast;

    public GamepadInput()
    {
        Name = "GamepadInput";
    }

    public PlayInput? PlayInput { get; set; }

    public PlaySession? Session { get; set; }

    public GameSettings Settings { get; set; } = new();

    /// <summary>The pad in use, or -1.</summary>
    public int Device { get; private set; } = -1;

    public override void _Process(double delta)
    {
        if (!Settings.Pad.Enabled || PlayInput is not { Enabled: true, Bindings: { } bindings } input)
        {
            ReleaseAll();
            return;
        }

        Godot.Collections.Array<int> pads = Godot.Input.GetConnectedJoypads();
        Device = pads.Count > 0 ? pads[0] : -1;
        if (Device < 0)
        {
            ReleaseAll();
            return;
        }

        if (!ReferenceEquals(_keysFor, bindings))
        {
            ReleaseAll();
            _keyOf.Clear();
            _keysFor = bindings;
        }

        PadSettings pad = Settings.Pad;
        bool merc = Session?.LocalPawn is SPAWNDEFENSE;
        if (pad.Move)
        {
            Vector2 move = Stick(JoyAxis.LeftX, JoyAxis.LeftY, pad.DeadzoneLeft);
            bool snap = merc ? pad.MercMove8 : pad.SpyMove8;
            if (snap && move != Vector2.Zero)
            {
                float a = Mathf.Snapped(move.Angle(), Mathf.Pi / 4f);
                move = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * move.Length();
            }

            Hold(input, "BT_Up", move.Y < -0.38f);
            Hold(input, "BT_Down", move.Y > 0.38f);
            Hold(input, "BT_Left", move.X < -0.38f);
            Hold(input, "BT_Right", move.X > 0.38f);
            RunThreshold(input, merc, move.Length(), pad.RunThreshold);
        }

        if (pad.Aim && Session is { } session)
        {
            Vector2 look = Stick(JoyAxis.RightX, JoyAxis.RightY, pad.DeadzoneRight);
            LookSettings group = Settings.LookOf(merc, pad: true);
            float k = pad.LookSpeed * (float)delta;
            float multiplier = merc ? group.Look : (session.LocalPawn?.WeaponLoaded ?? 0) > 0 ? group.Look : group.FreeLook;
            session.Input.MouseX += Response(look.X, group.LookResponse) * k * multiplier;
            session.Input.MouseY -= Response(look.Y, group.LookResponse) * k * multiplier * pad.VerticalScale;
        }

        if (pad.Binds)
        {
            foreach ((JoyButton button, string action) in Buttons)
            {
                Hold(input, action, Godot.Input.IsJoyButtonPressed(Device, button));
            }

            Hold(input, "bFire", Godot.Input.GetJoyAxis(Device, JoyAxis.TriggerRight) > TriggerPress);
            Hold(input, "bAltFire", Godot.Input.GetJoyAxis(Device, JoyAxis.TriggerLeft) > TriggerPress);
        }
    }

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

    /// <summary>
    /// The spy's automatic run: above the walk threshold the spy runs, below it walks (ChangeSpeed toggles
    /// SPlayerController.bFastSpeed). Mercs have no speed toggle.
    /// </summary>
    private void RunThreshold(PlayInput input, bool merc, float deflection, float threshold)
    {
        if (merc || Session?.LocalController is not { } pc || deflection <= 0f)
        {
            return;
        }

        bool fast = deflection >= threshold;
        if (fast != pc.bFastSpeed && fast != _wasFast && KeyFor(input, "ChangeSpeed") is { } key)
        {
            input.Key(key, true);
            input.Key(key, false);
        }

        _wasFast = fast;
    }

    /// <summary>Holds or releases the first key the layout binds to <paramref name="action"/> (a button byte or a command).</summary>
    private void Hold(PlayInput input, string action, bool down)
    {
        if (KeyFor(input, action) is not { } key)
        {
            return;
        }

        if (down && _held.Add(action))
        {
            input.Key(key, true);
        }
        else if (!down && _held.Remove(action))
        {
            input.Key(key, false);
        }
    }

    private string? KeyFor(PlayInput input, string action)
    {
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

        _keyOf[action] = found;
        return found;
    }

    private void ReleaseAll()
    {
        if (PlayInput is { } input)
        {
            foreach (string action in _held)
            {
                if (_keyOf.TryGetValue(action, out string? key) && key is not null)
                {
                    input.Key(key, false);
                }
            }
        }

        _held.Clear();
    }
}
