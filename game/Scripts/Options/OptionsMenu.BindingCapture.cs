using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private const string PadTeamPing = ControlActions.PadPrefix + ControlActions.PingId;

    private sealed class BindingCaptureSession
    {
        public required BindingEditor Editor;
        public required BindingEntry Entry;
        public BindingSlot? Slot;
        public required string ReturnEntryId;
        public required Control Overlay;
        public required Control Buttons;
        public required Label Status;
        public IDisposable? Suppression;
        public string? Modifier;
        public readonly HashSet<string> ModifiersDown = new(StringComparer.OrdinalIgnoreCase);
        public bool ModifierCombination;
        public int Device = -1;
        public PadBindingCapture? Pad;
        public readonly HashSet<string> EventDown = new(StringComparer.Ordinal);
        public CheckBox? DoubleTap;
    }

    /// <summary>
    /// Double taps run through the play input. Interface, chat and Enhanced Reality shortcuts are read by their own
    /// screens from the plain key, so they do not offer one.
    /// </summary>
    private static bool SupportsDoubleTap(BindingEditor editor, BindingEntry entry) => !editor.Pad &&
        entry.Action is { } action && action.Category != "Interface" && action.Id is not ("chat" or "teamchat" or "quickchat" or "er");
    private BindingCaptureSession? _bindingCapture;

    private void BeginBindingCapture(BindingEditor editor, BindingEntry entry, BindingSlot? slot)
    {
        if (_dialogOpen) return;
        string returnEntryId = entry.Id;
        SelectBindingEntry(editor, entry);
        // A contextual binding shown on an atomic action still edits the complete named preset.
        if (editor.Pad && slot is { PadAction: { } source } && source != entry.PadAction)
            entry = editor.Entries.FirstOrDefault(e => e.PadAction == source) ?? entry;
        _listeningClutch = null;
        _bindingSide = editor.Side; _keys = ModelOf(editor.Side); _listening = entry.Id; _listeningAction = entry.Action;
        _dialogOpen = true;
        var overlay = new Control { Name = "BindingCapture", MouseFilter = MouseFilterEnum.Stop, ZIndex = 50 };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.AddChild(new ColorRect { Color = new Color(0, 0, 0, .88f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        overlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(MathF.Min(560, FrameWidth - 64), 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox()); center.AddChild(panel);
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 10); panel.AddChild(content);
        content.AddChild(new Label { Text = "Bind: " + entry.Label, Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        content.AddChild(new Label { Text = entry.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        content.AddChild(Help(editor.Pad
            ? "Release all controls, then press a button or trigger. Hold the first control and press the second for a supported combination. Hold B for its held assignment. Esc or Back + Start cancels."
            : "Press a key or mouse button. Hold Ctrl, Shift or Alt to make a combination. Press and release a modifier alone to bind it. Esc cancels."));
        if (!editor.Pad)
            content.AddChild(Help("A modifier's own action stays active alongside a combination, such as Shift + R."));
        var status = new Label { Name = "BindingCaptureStatus", Text = editor.Pad ? "Release all controller controls" : "Waiting for input", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(status);
        var controls = new VBoxContainer { Name = "BindingCaptureControls" };
        content.AddChild(controls);
        if (editor.Pad)
        {
            var choose = new OptionButton { Name = "BindingCaptureControl", FitToLongestItem = false, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            choose.AddItem("Or choose a supported control");
            foreach (string control in PadSettings.Controls) choose.AddItem(BindingDisplay(editor, new BindingSlot(control)));
            choose.ItemSelected += index => { if (index > 0) CompleteBindingCapture(PadSettings.Controls[(int)index - 1]); };
            controls.AddChild(choose);
        }
        CheckBox? doubleTap = null;
        if (SupportsDoubleTap(editor, entry))
        {
            doubleTap = new CheckBox
            {
                Name = "BindingCaptureDoubleTap", Text = "Double tap: trigger on two quick presses", FocusMode = FocusModeEnum.None,
                ButtonPressed = slot is not null && BindingKey.TryParse(slot.Input, out var current) && current.IsDoubleTap,
                TooltipText = "Tick this with the mouse, then press the key once. The key's own action still runs on each press.",
            };
            controls.AddChild(doubleTap);
        }
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var remove = new Button { Name = "BindingCaptureRemove", Text = "Clear binding", Disabled = slot is null, CustomMinimumSize = new Vector2(136, 36) };
        var cancel = new Button { Name = "BindingCaptureCancel", Text = "Cancel", CustomMinimumSize = new Vector2(120, 36) };
        remove.Pressed += RemoveCapturedBinding;
        cancel.Pressed += CancelBindingCapture;
        buttons.AddChild(remove); buttons.AddChild(cancel); controls.AddChild(buttons);
        var capture = new BindingCaptureSession { Editor = editor, Entry = entry, Slot = slot, ReturnEntryId = returnEntryId, Overlay = overlay, Buttons = controls, Status = status, Suppression = BindingInput.SuspendCapture(), DoubleTap = doubleTap };
        if (editor.Pad)
        {
            capture.Device = PadMenus.CurrentDevice();
            capture.Pad = new PadBindingCapture(capture.Device);
            capture.Pad.Update(CapturePadDown(capture), 0);
            if (capture.Pad.Armed) status.Text = "Press a controller control, or choose one below.";
        }
        _bindingCapture = capture;
        PadMenus.ListeningForBinding = true;
        AddChild(overlay);
        cancel.GrabFocus();
    }

    private void RemoveCapturedBinding()
    {
        if (_bindingCapture is not { Slot: { } slot } capture) return;
        ClearBindingInput(capture.Editor, capture.Entry, slot);
        CancelBindingCapture();
        RebuildKeys();
    }

    private void CancelBindingCapture()
    {
        if (_bindingCapture is not { } capture) return;
        _bindingCapture = null;
        PadMenus.ListeningForBinding = false;
        _listening = null; _listeningAction = null; _dialogOpen = false;
        capture.Suppression?.Dispose();
        if (IsInstanceValid(capture.Overlay)) { RemoveChild(capture.Overlay); capture.Overlay.QueueFree(); }
        RestoreBindingFocus(capture.Editor, capture.ReturnEntryId, capture.Slot?.Input);
    }

    private bool HandleBindingCapture(InputEvent @event)
    {
        if (_bindingCapture is not { } capture) return false;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled(); CancelBindingCapture(); return true;
        }
        // Keep the visible Cancel / Remove / control picker usable with a mouse during capture.
        if (@event is InputEventMouseButton mouse && capture.Buttons.GetGlobalRect().HasPoint(mouse.Position)) return false;
        if (@event is not (InputEventKey or InputEventMouseButton or InputEventJoypadButton or InputEventJoypadMotion)) return false;
        GetViewport().SetInputAsHandled();
        if (capture.Editor.Pad)
        {
            if (@event is InputEventJoypadButton or InputEventJoypadMotion)
            {
                if (capture.Device < 0) capture.Device = @event.Device;
                if (capture.Device != @event.Device) return true;
                string? physical = @event switch
                {
                    InputEventJoypadButton button => GamepadInput.Buttons.FirstOrDefault(p => p.Value == button.ButtonIndex).Key,
                    InputEventJoypadMotion { Axis: JoyAxis.TriggerLeft } => "LT",
                    InputEventJoypadMotion { Axis: JoyAxis.TriggerRight } => "RT",
                    _ => null,
                };
                if (physical is not null)
                {
                    string logical = _edit.LookOf(capture.Editor.Side == PlaySide.Merc, true).PhysicalControl(physical);
                    bool down = @event is InputEventJoypadButton { Pressed: true } || @event is InputEventJoypadMotion motion && motion.AxisValue > _edit.Pad.TriggerThreshold;
                    if (down) capture.EventDown.Add(logical); else capture.EventDown.Remove(logical);
                    AdvancePadCapture(capture, 0);
                }
            }
            return true;
        }
        if (@event is InputEventKey key)
        {
            if (key.Echo) return true;
            if (key.MetaPressed) { capture.Status.Text = "Use Ctrl, Shift or Alt for keyboard combinations."; return true; }
            if (GodotKeys.IsModifier(key))
            {
                string modifier = GodotKeys.ToIk(key)!;
                if (key.Pressed)
                {
                    capture.ModifiersDown.Add(modifier);
                    BindingModifiers held = GodotKeys.ModifiersOf(key) | BindingKey.ModifierOf(modifier);
                    bool multiple = held != BindingModifiers.None && ((int)held & ((int)held - 1)) != 0;
                    capture.ModifierCombination |= capture.ModifiersDown.Count > 1 || multiple;
                    capture.Modifier = capture.ModifierCombination ? null : modifier;
                    capture.Status.Text = capture.ModifierCombination ? "Press a key with these modifiers. A modifier-only combination cannot be bound." : "Hold the modifier and press a key, or release it to bind the modifier alone.";
                }
                else
                {
                    capture.ModifiersDown.Remove(modifier);
                    if (capture.ModifierCombination)
                    {
                        if (capture.ModifiersDown.Count == 0 && GodotKeys.ModifiersOf(key) == BindingModifiers.None)
                        {
                            capture.ModifierCombination = false;
                            capture.Status.Text = "Waiting for a key, mouse button or single modifier.";
                        }
                    }
                    else if (capture.Modifier == modifier) CompleteBindingCapture(modifier);
                }
                return true;
            }
            if (key.Pressed && GodotKeys.ToBinding(key) is { } binding) { capture.Modifier = null; CompleteBindingCapture(binding); }
        }
        else if (@event is InputEventMouseButton { Pressed: true } button && GodotKeys.ToBinding(button) is { } binding)
            { capture.Modifier = null; CompleteBindingCapture(binding); }
        return true;
    }

    private HashSet<string> CapturePadDown(BindingCaptureSession capture)
    {
        var down = new HashSet<string>(capture.EventDown, StringComparer.Ordinal);
        if (capture.Device >= 0)
        {
            LookSettings look = _edit.LookOf(capture.Editor.Side == PlaySide.Merc, true);
            foreach (string control in PadSettings.PhysicalControls)
                if (GamepadInput.IsDown(capture.Device, look.PhysicalControl(control), _edit.Pad.TriggerThreshold)) down.Add(control);
        }
        return down;
    }

    private void TickBindingCapture(double delta)
    {
        if (_bindingCapture is { Editor.Pad: true } capture) AdvancePadCapture(capture, delta);
    }

    private void AdvancePadCapture(BindingCaptureSession capture, double delta)
    {
        HashSet<string> down = CapturePadDown(capture);
        if (down.Contains("Back") && down.Contains("Start")) { CancelBindingCapture(); return; }
        string? binding = capture.Pad?.Update(down, delta);
        if (binding is not null) { CompleteBindingCapture(binding); return; }
        capture.Status.Text = capture.Pad?.Error ?? (capture.Pad?.Preview is { } preview ? "Release to bind " + BindingDisplay(capture.Editor, new BindingSlot(preview)) : capture.Pad?.Armed == true ? "Press a controller control, or choose one below." : "Release all controller controls");
    }

    private void CompleteBindingCapture(string input)
    {
        if (_bindingCapture is not { } capture) return;
        PlaySide side = capture.Editor.Side;
        if (capture.DoubleTap is { ButtonPressed: true } && BindingKey.TryParse(input, out var captured))
        {
            if (captured.IsPulse)
            {
                capture.Status.Text = "A double tap needs a key or mouse button, not the mouse wheel.";
                return;
            }
            input = (captured with { Modifiers = captured.Modifiers | BindingModifiers.Double }).Canonical;
        }
        if (!capture.Editor.Pad && capture.Entry.Action?.Id == ControlActions.PingId && GodotKeys.IsPulse(input))
        {
            capture.Status.Text = "Team ping needs a key or mouse button you can hold.";
            return;
        }
        if (capture.Editor.Pad)
        {
            var buttons = _edit.Pad.ButtonsOf(side == PlaySide.Merc);
            bool splitB = input == "B:Hold" || input == "B" && buttons.GetValueOrDefault("B:Hold", "").Length > 0;
            bool splitsAnExistingPing = input == "B:Hold" && buttons.GetValueOrDefault("B", "") == PadTeamPing;
            if (capture.Entry.PadAction == PadTeamPing && splitB || splitsAnExistingPing)
            {
                capture.Status.Text = "Team ping needs one button for both tap and hold. Use a different button or clear B's held action first.";
                return;
            }
        }
        if (!capture.Editor.Pad && capture.Entry.Action is { } action && (action.Category == "Interface" || action.Id is "chat" or "teamchat" or "er") && GodotKeys.ToEvent(input) is not InputEventKey)
        {
            capture.Status.Text = "This action requires a keyboard key. Choose a key or keyboard combination.";
            return;
        }
        CancelBindingCapture();
        if (capture.Editor.Pad) AssignPadBinding(capture.Editor, capture.Entry, capture.Slot?.Input, input);
        else if (capture.Entry.Action is { } selected) AssignKeyboardBinding(side, selected, capture.Slot?.Input, input);
        else AssignCustomBinding(capture.Editor, capture.Entry, capture.Slot?.Input, input);
    }

    private void AssignKeyboardBinding(PlaySide side, ControlAction action, string? oldKey, string key)
    {
        ActionBindingDraft draft = ModelOf(side).Draft!;
        if (string.Equals(oldKey, key, StringComparison.OrdinalIgnoreCase) || draft.Keys(action).Contains(key, StringComparer.OrdinalIgnoreCase)) { RebuildKeys(); return; }
        void Apply(bool replace)
        {
            if (oldKey is null) draft.Add(action, key, replace);
            else draft.Replace(action, oldKey, key, replace);
        }
        if (draft.CommandOf(key).Length == 0) { Apply(false); RebuildKeys(); return; }
        OpenBindingConflict("Key already assigned", GodotKeys.DisplayName(key) + " currently performs:\n" + DescribeBinding(draft, key, side),
            () => Apply(true), oldKey is not null && CanSwapKeyboardInput(draft, key, oldKey) ? () => draft.Swap(action, oldKey, key) : null,
            () => Apply(false), side, false, action.Id, key);
    }

    private void AssignCustomBinding(BindingEditor editor, BindingEntry entry, string? oldKey, string key)
    {
        ActionBindingDraft draft = ModelOf(editor.Side).Draft!;
        if (oldKey == key) { RebuildKeys(); return; }
        string command = draft.CommandOf(entry.CustomKey!);
        string destination = draft.CommandOf(key);
        void Apply(bool share, bool swap)
        {
            if (oldKey is not null) draft.Layout.Bind(oldKey, swap ? destination : "");
            draft.Layout.Bind(key, share && destination.Length > 0 ? destination + " | " + command : command);
        }
        if (destination.Length == 0) { Apply(false, false); RebuildKeys(); return; }
        OpenBindingConflict("Key already assigned", GodotKeys.DisplayName(key) + " currently performs:\n" + DescribeBinding(draft, key, editor.Side),
            () => Apply(false, false), oldKey is not null && CanSwapKeyboardInput(draft, key, oldKey) ? () => Apply(false, true) : null,
            () => Apply(true, false), editor.Side, false, entry.Id, key);
    }

    private static bool CanSwapKeyboardInput(ActionBindingDraft draft, string from, string to) => !GodotKeys.IsPulse(to) ||
        !draft.Layout.Resolve(from).Any(action => action.Kind == KeyActionKind.Command &&
            action.Name.Equals(ControlActions.PingCommand, StringComparison.OrdinalIgnoreCase));

    private void AssignPadBinding(BindingEditor editor, BindingEntry entry, string? oldControl, string control)
    {
        if (!PadSettings.Controls.Contains(control)) return;
        Dictionary<string, string> buttons = _edit.Pad.ButtonsOf(editor.Side == PlaySide.Merc);
        string action = entry.PadAction!;
        string destination = buttons.GetValueOrDefault(control, "");
        if (destination == action || oldControl == control) { RebuildKeys(); return; }
        void Apply(bool swap)
        {
            ApplyPadBindingChange(editor, () =>
            {
                if (oldControl is not null) buttons[oldControl] = swap ? destination : "";
                buttons[control] = action;
            }, oldControl, control, swap);
        }
        if (destination.Length == 0) { Apply(false); RebuildKeys(); return; }
        string label = editor.Entries.FirstOrDefault(e => e.PadAction == destination)?.Label ?? destination;
        bool canSwap = oldControl is not null && !(oldControl == "B:Hold" && destination == PadTeamPing) &&
            !(oldControl == "B" && destination == PadTeamPing && buttons.GetValueOrDefault("B:Hold", "").Length > 0);
        OpenBindingConflict("Controller input already assigned", BindingDisplay(editor, new BindingSlot(control)) + " currently performs:\n" + label,
            () => Apply(false), canSwap ? () => Apply(true) : null, null, editor.Side, true, editor.View.Selected ?? entry.Id, control);
    }

    private void OpenBindingConflict(string title, string existing, Action replace, Action? swap, Action? share, PlaySide side, bool pad, string actionId, string newInput)
    {
        _dialogOpen = true;
        IDisposable suppression = BindingInput.SuspendCapture();
        var dialog = new ConfirmationDialog { Name = "BindingConflict", Title = title, DialogText = existing + "\n\nReplace reassigns this input." + (share is null ? "" : " Share keeps its current actions.") + (swap is null ? "" : " Swap exchanges the selected binding."), OkButtonText = "Replace", CancelButtonText = "Cancel", Exclusive = true };
        if (share is not null) dialog.AddButton("Share key", false, "share");
        if (swap is not null) dialog.AddButton("Swap", false, "swap");
        bool finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            dialog.Exclusive = false;
            dialog.Hide();
            _dialogOpen = false;
            suppression.Dispose();
            RemoveChild(dialog);
            dialog.QueueFree();
            RebuildKeys();
            if ((pad ? _padBindingEditors : _bindingEditors).TryGetValue(side, out BindingEditor? editor)) RestoreBindingFocus(editor, actionId, newInput);
        }
        dialog.Confirmed += () => { replace(); Finish(); };
        dialog.Canceled += Finish;
        dialog.CustomAction += which => { if (which == "swap") swap?.Invoke(); else if (which == "share") share?.Invoke(); Finish(); };
        dialog.TreeExiting += () => { if (!finished) { finished = true; _dialogOpen = false; suppression.Dispose(); } };
        AddChild(dialog); dialog.PopupCentered(new Vector2I((int)MathF.Min(640, FrameWidth - 48), 230));
        dialog.GetCancelButton().GrabFocus();
    }

    private static void RestoreBindingFocus(BindingEditor editor, string entryId, string? input)
    {
        Callable.From(() =>
        {
            if (!IsInstanceValid(editor.Page) || !editor.Page.IsInsideTree()) return;
            if (editor.Rows.FindChild("BindingSlot_" + BindingNodeId(entryId) + "_0", true, false) is Button first && first.IsVisibleInTree()) first.GrabFocus();
        }).CallDeferred();
    }
}
