using System;
using System.Linq;
using Godot;
using OpenScctGame.Controls;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private sealed record BindingTarget(BindingEditor Editor, BindingEntry Entry, BindingSlot Slot, Button Button);
    private BindingTarget? _hoveredBinding;
    private PopupMenu? _bindingContextMenu;
    private IDisposable? _bindingContextSuppression;

    private void AttachBindingActions(BindingEditor editor, BindingEntry entry, BindingSlot slot, Button button)
    {
        var target = new BindingTarget(editor, entry, slot, button);
        button.MouseEntered += () => _hoveredBinding = target;
        void Forget()
        {
            if (ReferenceEquals(_hoveredBinding, target)) _hoveredBinding = null;
        }
        button.MouseExited += Forget;
        button.TreeExiting += Forget;
        button.VisibilityChanged += () => { if (!button.IsVisibleInTree()) Forget(); };
        button.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouse ||
                _dialogOpen || _listening is not null || _listeningClutch is not null || _keep is not null ||
                !BindingTargetIsCurrent(target)) return;
            button.AcceptEvent();
            OpenBindingContext(target, mouse.Position);
        };
    }

    private bool BindingTargetIsCurrent(BindingTarget target) =>
        IsInstanceValid(target.Button) && !target.Button.IsQueuedForDeletion() && target.Button.IsInsideTree() &&
        target.Button.IsVisibleInTree() && BindingInputs(target.Editor, target.Entry)
            .Any(input => input.Input == target.Slot.Input && input.PadAction == target.Slot.PadAction);

    private bool HandleBindingActions(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: false,
                ShiftPressed: false, AltPressed: false, MetaPressed: false } key ||
            (key.Keycode != Key.Delete && key.PhysicalKeycode != Key.Delete) ||
            GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit ||
            _hoveredBinding is not { } target || !BindingTargetIsCurrent(target)) return false;
        GetViewport().SetInputAsHandled();
        ClearTargetBinding(target);
        return true;
    }

    private void OpenBindingContext(BindingTarget target, Vector2 localPosition)
    {
        CancelBindingActions();
        SelectBindingEntry(target.Editor, target.Entry);
        var menu = new PopupMenu { Name = "BindingContextMenu" };
        menu.AddSeparator(BindingDisplay(target.Editor, target.Slot) + " - " + target.Entry.Label);
        menu.AddItem("Change binding...", 0);
        menu.AddItem("Clear binding", 1);
        bool linked = target.Editor.Pad && target.Slot.PadAction != target.Entry.PadAction;
        string effects = BindingSlotDescription(target.Editor, target.Entry, target.Slot);
        menu.SetItemTooltip(2, effects + (linked ? ". Clears this input's complete contextual preset." : ". Other inputs and shared actions are kept."));
        _bindingContextMenu = menu;
        _bindingContextSuppression = BindingInput.SuspendCapture();
        _dialogOpen = true;
        menu.IdPressed += id =>
        {
            CancelBindingActions();
            if (!BindingTargetIsCurrent(target)) return;
            if (id == 0) BeginBindingCapture(target.Editor, target.Entry, target.Slot);
            else if (id == 1) ClearTargetBinding(target);
        };
        menu.PopupHide += CancelBindingActions;
        menu.TreeExiting += () =>
        {
            if (_bindingContextMenu != menu) return;
            // The parent is already removing this window; release state without removing it a second time.
            _bindingContextMenu = null;
            _hoveredBinding = null;
            _dialogOpen = false;
            IDisposable? suppression = _bindingContextSuppression;
            _bindingContextSuppression = null;
            suppression?.Dispose();
        };
        AddChild(menu);
        // Native and embedded popups use the viewport's screen transform, including an off-screen test window.
        menu.Position = (Vector2I)(GetViewport().GetScreenTransform() * target.Button.GetGlobalTransformWithCanvas() * localPosition);
        menu.Popup();
    }

    private void ClearTargetBinding(BindingTarget target)
    {
        SelectBindingEntry(target.Editor, target.Entry);
        ClearBindingInput(target.Editor, target.Entry, target.Slot);
        RebuildKeys();
        RestoreBindingFocus(target.Editor, target.Entry.Id, null);
    }

    private void CancelBindingActions()
    {
        _hoveredBinding = null;
        if (_bindingContextMenu is not { } menu) return;
        _bindingContextMenu = null;
        IDisposable? suppression = _bindingContextSuppression;
        _bindingContextSuppression = null;
        _dialogOpen = false;
        if (IsInstanceValid(menu))
        {
            menu.Exclusive = false;
            menu.Hide();
            if (menu.GetParent() == this) RemoveChild(menu);
            menu.QueueFree();
        }
        suppression?.Dispose();
    }
}
