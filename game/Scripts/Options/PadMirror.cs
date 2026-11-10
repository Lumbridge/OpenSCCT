using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Options;

/// <summary>
/// Steam Input's keyboard and mouse layouts (the Steam Deck's desktop layout, used whenever the game is not started
/// from Steam with a gamepad layout) send a key, a click or mouse movement together with the controller's own
/// input: B also presses Escape, the triggers also click, the right stick also moves the mouse. The game reads the
/// controller itself, so in play every such copy is a second, wrong command.
/// <para>
/// This spots the copies (the same controller control arriving with the same key or mouse button
/// <see cref="PairsNeeded"/> times running, each within <see cref="PairMs"/>) and from then on drops keyboard and
/// mouse presses and mouse movement while a controller drives the player and was used in the last
/// <see cref="RecentMs"/>. Releases, text fields, the menus and the input the game sends itself are never dropped.
/// </para>
/// The node is the gate: it stays the last child of the root so its <see cref="_Input"/> runs before every other
/// node's and a dropped event reaches nothing.
/// </summary>
public partial class PadMirror : Node
{
    /// <summary>How close a controller press and a key or click must be to count as one press sent twice.</summary>
    public const ulong PairMs = 60;

    /// <summary>How many times running the same control must come with the same key before the copies are dropped.</summary>
    public const int PairsNeeded = 3;

    /// <summary>How long after the last controller input the keyboard and mouse stay ignored in play.</summary>
    public const ulong RecentMs = 3000;

    /// <summary>Set on every screened event: true when it is a copy to drop.</summary>
    private const string Meta = "_svm_pad_mirror";

    private sealed class Pairing
    {
        public string Input = string.Empty;
        public int Count;
        public bool Paired = true;
        public ulong At;
    }

    private static readonly Dictionary<string, Pairing> s_pairs = new(StringComparer.Ordinal);
    private static readonly HashSet<(int Device, JoyAxis Axis)> s_triggersDown = new();
    private static string? s_control;
    private static ulong s_controlAt;
    private static string? s_input;
    private static ulong s_inputAt;
    private static ulong s_padAt;
    private static bool s_padSeen;

    /// <summary>Steam is sending keyboard and mouse input with the controller (found in this run).</summary>
    public static bool Active { get; private set; }

    /// <summary>Raised once, when the copies are first found.</summary>
    public static event Action? Detected;

    public PadMirror()
    {
        Name = "PadMirror";
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>The menus saw doubled presses themselves. Smoke runs never switch the filter on.</summary>
    public static void Declare()
    {
        if (Active || SettingsStore.Isolated) return;
        Active = true;
        GD.Print("SVM_PAD Steam Input is sending keyboard and mouse input together with the controller; ignoring it while the controller is in use in play");
        Detected?.Invoke();
    }

    /// <summary>Input the game sends itself (a controller button's key, a menu step) is never a copy.</summary>
    public static void MarkSynthetic(InputEvent input) => input.SetMeta(Meta, false);

    /// <summary>
    /// True when <paramref name="input"/> is a copy to drop. The first call for an event decides and records the
    /// answer on it, so every later reader (and Godot's localized copies of mouse events) gets the same one.
    /// </summary>
    public static bool Screen(InputEvent input)
    {
        if (input.HasMeta(Meta)) return input.GetMeta(Meta).AsBool();
        bool drop = Classify(input, Time.GetTicksMsec());
        input.SetMeta(Meta, drop);
        return drop;
    }

    private static bool Classify(InputEvent input, ulong now)
    {
        switch (input)
        {
            case InputEventJoypadButton button:
                if (button.Pressed)
                {
                    PadUsed(now);
                    PadPress("button " + (int)button.ButtonIndex, now);
                }
                return false;
            case InputEventJoypadMotion motion:
                if (Mathf.Abs(motion.AxisValue) > 0.5f) PadUsed(now);
                if (motion.Axis is JoyAxis.TriggerLeft or JoyAxis.TriggerRight)
                {
                    (int, JoyAxis) trigger = (motion.Device, motion.Axis);
                    if (motion.AxisValue > 0.5f)
                    {
                        if (s_triggersDown.Add(trigger)) PadPress("trigger " + (int)motion.Axis, now);
                    }
                    else if (motion.AxisValue < 0.3f) s_triggersDown.Remove(trigger);
                }
                return false;
            case InputEventKey { Pressed: true } key:
                if (!key.Echo) InputPress("key " + (long)(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode), now);
                return Dropping(now);
            case InputEventMouseButton { Pressed: true } click:
                InputPress("mouse " + (int)click.ButtonIndex, now);
                return Dropping(now);
            case InputEventMouseMotion:
                return Dropping(now);
            default:
                return false;
        }
    }

    private static void PadUsed(ulong now)
    {
        s_padAt = now;
        s_padSeen = true;
    }

    private static bool Dropping(ulong now) =>
        Active && s_padSeen && now - s_padAt <= RecentMs && SettingsStore.Current.Pad.Enabled &&
        GamepadInput.GameplayActive && !Typing();

    /// <summary>A text field has the keys: the on-screen keyboard types into it.</summary>
    private static bool Typing() =>
        (Engine.GetMainLoop() as SceneTree)?.Root.GuiGetFocusOwner() is LineEdit or TextEdit;

    private static void PadPress(string control, ulong now)
    {
        if (!s_pairs.TryGetValue(control, out Pairing? pairing)) s_pairs[control] = pairing = new Pairing();
        // One controller shown as two devices sends each press twice: the second is the same press.
        else if (now - pairing.At <= PairMs) return;
        // The last press of this control came alone, so its key is not a copy of it.
        else if (!pairing.Paired) pairing.Count = 0;
        pairing.At = now;
        pairing.Paired = false;
        if (s_input is { } input && now - s_inputAt <= PairMs)
        {
            s_input = null;
            Pair(pairing, input);
        }
        else
        {
            s_control = control;
            s_controlAt = now;
        }
    }

    private static void InputPress(string input, ulong now)
    {
        if (s_control is { } control && now - s_controlAt <= PairMs && s_pairs.TryGetValue(control, out Pairing? pairing))
        {
            s_control = null;
            Pair(pairing, input);
        }
        else
        {
            s_input = input;
            s_inputAt = now;
        }
    }

    private static void Pair(Pairing pairing, string input)
    {
        pairing.Count = pairing.Input == input ? pairing.Count + 1 : 1;
        pairing.Input = input;
        pairing.Paired = true;
        if (pairing.Count >= PairsNeeded) Declare();
    }

    private Node? _parent;

    public override void _Ready()
    {
        _parent = GetParent();
        _parent.ChildEnteredTree += OnSibling;
    }

    public override void _ExitTree()
    {
        if (_parent is not null && IsInstanceValid(_parent)) _parent.ChildEnteredTree -= OnSibling;
        _parent = null;
    }

    private void OnSibling(Node node)
    {
        if (node != this) Callable.From(StayLast).CallDeferred();
    }

    private void StayLast()
    {
        if (IsInsideTree() && GetParent() is { } parent && GetIndex() != parent.GetChildCount() - 1) parent.MoveChild(this, -1);
    }

    public override void _Input(InputEvent @event)
    {
        if (Screen(@event)) GetViewport().SetInputAsHandled();
    }
}
