using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Exercises the editor's actual capture and conflict controls without writing user settings.</summary>
internal static class BindingEditorChecks
{
    private sealed record ViewState(string Search, string Category, int Filter, string? Selected, int Scroll);

    public static List<string> Run(OptionsMenu options)
    {
        var problems = new List<string>();
        void Expect(bool condition, string message) { if (!condition) problems.Add("binding editor: " + message); }
        bool split = options.Editing.SplitKeyboard;
        PlaySide shownSide = options.Keys?.Side ?? PlaySide.Spy;
        int tab = options.Tabs!.CurrentTab;
        var sections = options.Tabs.GetChildren().OfType<Control>()
            .Select(role => role.GetNodeOrNull<TabContainer>("Sections")).Where(s => s is not null).Cast<TabContainer>()
            .ToDictionary(s => s, s => s.CurrentTab);
        var drafts = new Dictionary<PlaySide, KeyLayout>();
        var views = new Dictionary<Control, ViewState>();
        try
        {
            options.Editing.SplitKeyboard = true;
            foreach (PlaySide side in new[] { PlaySide.Spy, PlaySide.Merc })
            {
                options.LoadLayout(side);
                drafts[side] = options.Keys!.Draft!.Layout.Clone();
                Control page = options.Tabs.GetTabControl(side == PlaySide.Spy ? 0 : 1).GetNode<Control>("Sections/Bindings");
                views[page] = SnapshotView(page);
            }
            options.Tabs.CurrentTab = 0;
            options.Tabs.GetCurrentTabControl().GetNode<TabContainer>("Sections").CurrentTab = 0;
            options.LoadLayout(PlaySide.Spy);
            var draft = options.Keys!.Draft!;
            var forward = ControlActions.All.Single(a => a.Id == "forward");
            KeyLayout stock = DefaultKeyLayout.Create();
            Control bindings = options.Tabs.GetCurrentTabControl().GetNode<Control>("Sections/Bindings");
            SetView(bindings, "Movement", "", 0);
            draft.Reset(stock);
            options.LoadLayout(PlaySide.Spy);
            (bindings.FindChild("BindingSlot_forward_1", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            Key(options, Godot.Key.A);
            ConfirmationDialog? addConflict = options.GetChildren().OfType<ConfirmationDialog>()
                .FirstOrDefault(d => !d.IsQueuedForDeletion() && d.Title == "Key already assigned");
            Expect(addConflict is not null, "alternate A explains its existing movement assignment");
            addConflict?.EmitSignal(ConfirmationDialog.SignalName.CustomAction, "share");
            Expect((bindings.FindChild("BindingSlot_forward_0", true, false) as Button)?.Text == "W" &&
                (bindings.FindChild("BindingSlot_forward_1", true, false) as Button)?.Text == "A",
                "adding lexically earlier A to the Alternate cell leaves W in the Primary cell");
            (bindings.FindChild("BindingSlot_forward_1", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            Key(options, Godot.Key.F13);
            Expect((bindings.FindChild("BindingSlot_forward_0", true, false) as Button)?.Text == "W" &&
                (bindings.FindChild("BindingSlot_forward_1", true, false) as Button)?.Text == "F13",
                "replacing the Alternate cell preserves W as Primary and F13 as Alternate");
            (bindings.FindChild("BindingMore_forward", true, false) as MenuButton)?.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 10000L);
            Key(options, Godot.Key.B);
            ConfirmationDialog? extraConflict = options.GetChildren().OfType<ConfirmationDialog>()
                .FirstOrDefault(d => !d.IsQueuedForDeletion() && d.Title == "Key already assigned");
            Expect(extraConflict is not null, "additional B explains its existing assignment");
            extraConflict?.EmitSignal(ConfirmationDialog.SignalName.CustomAction, "share");
            Expect((bindings.FindChild("BindingSlot_forward_0", true, false) as Button)?.Text == "W" &&
                (bindings.FindChild("BindingSlot_forward_1", true, false) as Button)?.Text == "F13" && draft.Keys(forward).Count() == 3,
                "adding a third binding leaves both visible cells unchanged");

            draft.Reset(stock);
            draft.Add(forward, "F14");
            options.LoadLayout(PlaySide.Spy);

            var more = bindings.FindChild("BindingMore_forward", true, false) as MenuButton;
            Expect(more is not null, "action exposes an add-binding menu");
            more?.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 10000L);
            Expect(options.Listening == "forward", "add input opens capture for the selected action");
            Key(options, Godot.Key.F13, ctrl: true);
            Expect(draft.Keys(forward).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "W", "F14", "Ctrl+F13" }),
                "capturing Ctrl+F13 adds an input and preserves both existing alternatives");
            Expect(options.Listening is null, "successful keyboard capture closes the modal");

            PressKeyButton(options, "W");
            Key(options, Godot.Key.F15);
            Expect(draft.Keys(forward).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "F14", "Ctrl+F13", "F15" }),
                "replacing the selected input preserves other alternatives");
            PressKeyButton(options, "F14");
            var remove = options.FindChild("BindingCaptureRemove", true, false) as Button;
            Expect(remove is { Disabled: false }, "selected input can be removed from its capture dialog");
            remove?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(draft.Keys(forward).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "Ctrl+F13", "F15" }),
                "remove clears only the selected input");

            string beforeCancel = draft.Layout.ToIni();
            PressKeyButton(options, "Ctrl+F13");
            Key(options, Godot.Key.Escape);
            Expect(options.Listening is null && draft.Layout.ToIni() == beforeCancel, "Escape cancels capture without changing the draft");

            // Invalid mouse capture must not subsequently assign the modifier's release to a keyboard-only shortcut.
            SetView(bindings, "Interface", "", 0);
            var map = ControlActions.All.Single(a => a.Id == "map");
            var mapSlot = bindings.FindChild("BindingSlot_map_0", true, false) as Button;
            mapSlot?.EmitSignal(BaseButton.SignalName.Pressed);
            string beforeInvalid = draft.Layout.ToIni();
            Key(options, Godot.Key.Shift);
            options._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, ShiftPressed = true, Position = new Vector2(-100, -100) });
            Key(options, Godot.Key.Shift, pressed: false);
            Expect(options.Listening == map.Id && draft.Layout.ToIni() == beforeInvalid, "keyboard-only shortcut rejects modified mouse input without assigning the released modifier");
            Key(options, Godot.Key.Escape);

            SetView(bindings, "Movement", "", 0);
            PressKeyButton(options, "Ctrl+F13");
            string beforeModifiers = draft.Layout.ToIni();
            Key(options, Godot.Key.Ctrl);
            Key(options, Godot.Key.Shift, ctrl: true);
            Key(options, Godot.Key.Shift, pressed: false, ctrl: true);
            Key(options, Godot.Key.Ctrl, pressed: false);
            Expect(options.Listening == "forward" && draft.Layout.ToIni() == beforeModifiers,
                "releasing a modifier-only combination does not silently capture one modifier");
            Key(options, Godot.Key.Escape);

            // Right-click and Delete operate on the pointed-at input without adding persistent row buttons.
            draft.Reset(stock);
            draft.Layout.Bind("W", "Button BT_Up | Button bFire | CustomSource 7");
            draft.Add(forward, "F14");
            draft.Add(forward, "Ctrl+F13");
            options.LoadLayout(PlaySide.Spy);
            SetView(bindings, "Movement", "", 0);
            Expect(bindings.FindChild("SelectedBindingClear", true, false) is null, "bindings details do not retain the bottom-right Clear binding control");
            Button shared = options.KeyButtons["W"];
            shared.EmitSignal(Control.SignalName.MouseEntered);
            var search = (LineEdit)bindings.FindChild("BindingSearch", true, false);
            search.GrabFocus();
            string beforeTextDelete = draft.Layout.ToIni();
            Key(options, Godot.Key.Delete);
            Expect(draft.Layout.ToIni() == beforeTextDelete, "Delete in the search field cannot clear a hovered binding");
            search.ReleaseFocus();
            shared.GrabFocus();
            PopupMenu? contextMenu = RightClickBinding(options, shared);
            int clearIndex = contextMenu?.GetItemIndex(1) ?? -1;
            Expect(clearIndex >= 0 && contextMenu?.IsItemDisabled(clearIndex) == false, "right-click on an assigned input exposes Clear binding");
            contextMenu?.EmitSignal(PopupMenu.SignalName.IdPressed, 1L);
            Expect(options.Listening is null && !draft.Keys(forward).Contains("W") && draft.Keys(forward).Contains("F14") && draft.Keys(forward).Contains("Ctrl+F13"),
                "right-click clear removes one input without capture and preserves its alternatives");
            Expect(draft.CommandOf("W").Contains("bFire", StringComparison.Ordinal) && draft.CommandOf("W").Contains("CustomSource 7", StringComparison.Ordinal),
                "right-click clear retains other actions and custom commands sharing the key");

            Button alternate = options.KeyButtons["F14"];
            alternate.GrabFocus(); alternate.EmitSignal(Control.SignalName.MouseEntered);
            Key(options, Godot.Key.Delete);
            Expect(options.Listening is null && !draft.Keys(forward).Contains("F14") && draft.Keys(forward).Contains("Ctrl+F13"),
                "Delete while hovering an input clears only that input");
            Button remaining = options.KeyButtons["Ctrl+F13"];
            remaining.GrabFocus(); remaining.EmitSignal(Control.SignalName.MouseEntered);
            remaining.EmitSignal(Control.SignalName.MouseExited);
            string beforeOutsideDelete = draft.Layout.ToIni();
            Key(options, Godot.Key.Delete);
            Expect(draft.Layout.ToIni() == beforeOutsideDelete, "Delete after the pointer leaves the binding does not clear it");
            var empty = bindings.FindChild("BindingSlot_forward_1", true, false) as Button;
            string beforeUnbound = draft.Layout.ToIni();
            PopupMenu? emptyMenu = empty is null ? null : RightClickBinding(options, empty);
            int emptyClear = emptyMenu?.GetItemIndex(1) ?? -1;
            Expect(emptyClear < 0 || emptyMenu?.IsItemDisabled(emptyClear) == true, "unbound input has no enabled destructive context action");
            emptyMenu?.Hide();
            Expect(draft.Layout.ToIni() == beforeUnbound, "right-click on an unbound slot leaves bindings unchanged");

            // The capture stage must get Delete before the hovered-input shortcut can consume it.
            draft.Layout.Bind("Delete", "");
            options.LoadLayout(PlaySide.Spy);
            remaining = options.KeyButtons["Ctrl+F13"];
            remaining.EmitSignal(Control.SignalName.MouseEntered);
            remaining.EmitSignal(BaseButton.SignalName.Pressed);
            Key(options, Godot.Key.Delete);
            Expect(options.Listening is null && draft.Keys(forward).Contains("Delete"), "Delete remains bindable while capturing a replacement");

            Button deleteBinding = options.KeyButtons["Delete"];
            deleteBinding.EmitSignal(Control.SignalName.MouseEntered);
            string beforeEmptySearchDelete = draft.Layout.ToIni();
            SetView(bindings, "Movement", "__binding_editor_no_match__", 0);
            Key(options, Godot.Key.Delete);
            Expect(draft.Layout.ToIni() == beforeEmptySearchDelete, "empty search results cannot clear a stale hovered input");

            foreach (string choice in new[] { "cancel", "share", "replace", "swap" })
            {
                SetView(bindings, "Movement", "", 0);
                draft.Reset(stock);
                draft.Layout.Bind("W", "Button BT_Up | CustomSource 7");
                draft.Add(forward, "F14");
                draft.Layout.Bind("F16", "Button bReload | CustomDestination 12");
                options.LoadLayout(PlaySide.Spy);
                string before = draft.Layout.ToIni();
                PressKeyButton(options, "W");
                Key(options, Godot.Key.F16);
                ConfirmationDialog? dialog = options.GetChildren().OfType<ConfirmationDialog>()
                    .FirstOrDefault(d => !d.IsQueuedForDeletion() && d.Title == "Key already assigned");
                Expect(dialog is not null, choice + " opens a key conflict dialog");
                Expect(dialog?.DialogText.Contains("CustomDestination 12", StringComparison.Ordinal) == true, choice + " describes the complete occupied destination");
                if (dialog is null) { Key(options, Godot.Key.Escape); continue; }
                if (choice == "cancel") dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
                else if (choice == "replace") dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                else dialog.EmitSignal(ConfirmationDialog.SignalName.CustomAction, choice);
                if (choice == "cancel")
                {
                    Expect(draft.Layout.ToIni() == before, "conflict cancellation leaves every binding unchanged");
                    continue;
                }
                Expect(draft.Keys(forward).Contains("F14") && draft.Keys(forward).Contains("F16") && !draft.Keys(forward).Contains("W"),
                    choice + " edits the selected source and retains its alternate");
                Expect(draft.CommandOf("W").Contains("CustomSource 7", StringComparison.Ordinal), choice + " retains unrelated source commands");
                if (choice == "share")
                    Expect(draft.CommandOf("F16").Contains("CustomDestination 12", StringComparison.Ordinal) && draft.CommandOf("F16").Contains("bReload", StringComparison.Ordinal), "Share retains all destination commands");
                if (choice == "replace") Expect(draft.CommandOf("F16") == forward.Command, "Replace replaces the destination's complete binding");
                if (choice == "swap")
                    Expect(draft.CommandOf("W").Contains("CustomDestination 12", StringComparison.Ordinal) && draft.CommandOf("W").Contains("bReload", StringComparison.Ordinal) && draft.CommandOf("F16") == forward.Command,
                        "Swap transfers the destination chain while preserving source custom commands");
            }
        }
        catch (Exception exception)
        {
            problems.Add("binding editor checks: " + exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            foreach (PopupMenu menu in options.FindChildren("BindingContextMenu", nameof(PopupMenu), true, false).OfType<PopupMenu>()) menu.Hide();
            Key(options, Godot.Key.Escape);
            foreach (ConfirmationDialog dialog in options.GetChildren().OfType<ConfirmationDialog>().Where(d => !d.IsQueuedForDeletion()).ToArray())
                dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            options.Editing.SplitKeyboard = true;
            foreach ((PlaySide side, KeyLayout layout) in drafts)
            {
                options.LoadLayout(side);
                options.Keys!.Draft!.Reset(layout);
            }
            options.Editing.SplitKeyboard = split;
            options.LoadLayout(shownSide);
            foreach ((Control page, ViewState view) in views)
            {
                SetView(page, view.Category, view.Search, view.Filter);
                if (view.Selected is not null && page.FindChild(view.Selected, true, false) is Button selected)
                    selected.EmitSignal(BaseButton.SignalName.Pressed);
                if (page.FindChild("ActionList", true, false) is ScrollContainer scroll) scroll.ScrollVertical = view.Scroll;
            }
            foreach ((TabContainer nested, int index) in sections) nested.CurrentTab = index;
            options.Tabs.CurrentTab = tab;
        }
        problems.AddRange(ControllerBindingEditorChecks.Run(options));
        return problems;
    }

    private static void Key(OptionsMenu options, Godot.Key key, bool pressed = true, bool ctrl = false) =>
        options._Input(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = ctrl });

    private static PopupMenu? RightClickBinding(OptionsMenu options, Button button)
    {
        button.EmitSignal(Control.SignalName.GuiInput,
            new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = Vector2.Zero });
        return options.FindChildren("BindingContextMenu", nameof(PopupMenu), true, false).OfType<PopupMenu>()
            .LastOrDefault(menu => !menu.IsQueuedForDeletion() && menu.Visible);
    }

    private static void PressKeyButton(OptionsMenu options, string key)
    {
        if (options.KeyButtons.TryGetValue(key, out Button? button))
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
            return;
        }
        // The third and later inputs remain editable through the row's +N menu.
        ControlAction forward = ControlActions.All.Single(a => a.Id == "forward");
        int index = options.Keys!.Draft!.Keys(forward).ToList().FindIndex(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        var page = options.Tabs!.GetCurrentTabControl().GetNode<Control>("Sections/Bindings");
        if (index < 2 || page.FindChild("BindingMore_forward", true, false) is not MenuButton more)
            throw new InvalidOperationException("No binding slot for " + key);
        more.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, (long)index);
    }

    private static ViewState SnapshotView(Control page)
    {
        var search = (LineEdit)page.FindChild("BindingSearch", true, false);
        var category = (OptionButton)page.FindChild("BindingCategory", true, false);
        var filter = (OptionButton)page.FindChild("BindingFilter", true, false);
        var selected = page.FindChildren("BindingAction_*", nameof(Button), true, false).OfType<Button>().FirstOrDefault(b => b.ButtonPressed);
        var scroll = (ScrollContainer)page.FindChild("ActionList", true, false);
        return new ViewState(search.Text, category.GetItemText(category.Selected), filter.Selected, selected?.Name.ToString(), scroll.ScrollVertical);
    }

    private static void SetView(Control page, string categoryName, string query, int filterIndex)
    {
        var filter = (OptionButton)page.FindChild("BindingFilter", true, false);
        filter.Select(filterIndex); filter.EmitSignal(OptionButton.SignalName.ItemSelected, (long)filterIndex);
        var category = (OptionButton)page.FindChild("BindingCategory", true, false);
        int index = Enumerable.Range(0, category.ItemCount).FirstOrDefault(i => category.GetItemText(i) == categoryName);
        category.Select(index); category.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        var search = (LineEdit)page.FindChild("BindingSearch", true, false);
        search.Text = query; search.EmitSignal(LineEdit.SignalName.TextChanged, query);
    }
}
