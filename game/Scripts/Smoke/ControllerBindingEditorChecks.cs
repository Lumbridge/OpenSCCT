using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Uses the actual controller capture controls while keeping every edit in the options draft.</summary>
internal static class ControllerBindingEditorChecks
{
    public static List<string> Run(OptionsMenu options)
    {
        var problems = new List<string>();
        void Expect(bool condition, string message) { if (!condition) problems.Add("controller binding editor: " + message); }
        int tab = options.Tabs!.CurrentTab;
        PlaySide shownSide = options.Keys?.Side ?? PlaySide.Spy;
        var sections = options.Tabs.GetChildren().OfType<Control>()
            .Select(role => role.GetNodeOrNull<TabContainer>("Sections")).Where(s => s is not null).Cast<TabContainer>()
            .ToDictionary(s => s, s => s.CurrentTab);
        var spy = new Dictionary<string, string>(options.Editing.Pad.SpyButtons, StringComparer.Ordinal);
        var merc = new Dictionary<string, string>(options.Editing.Pad.MercButtons, StringComparer.Ordinal);
        var spyOrder = options.Editing.Pad.SpyBindingOrder.ToDictionary(p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal);
        var mercOrder = options.Editing.Pad.MercBindingOrder.ToDictionary(p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal);
        bool swapLs = options.Editing.SpyPad.SwapLsLb, swapRs = options.Editing.SpyPad.SwapRsRb;
        Control controller = options.Tabs.GetTabControl(0).GetNode<Control>("Sections/Controller");
        var search = (LineEdit)controller.FindChild("BindingSearch", true, false);
        var category = (OptionButton)controller.FindChild("BindingCategory", true, false);
        var filter = (OptionButton)controller.FindChild("BindingFilter", true, false);
        string oldSearch = search.Text;
        string oldCategory = category.Selected >= 0 ? category.GetItemText(category.Selected) : "All";
        int oldFilter = filter.Selected;
        var scroll = (ScrollContainer)controller.FindChild("ActionList", true, false);
        int oldScroll = scroll.ScrollVertical;
        int device = Math.Max(0, PadMenus.CurrentDevice());
        Dictionary<string, string> Buttons() => options.Editing.Pad.SpyButtons;
        void KeyEscape() => options._Input(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        void Button(JoyButton button, bool pressed) => options._Input(new InputEventJoypadButton { Device = device, ButtonIndex = button, Pressed = pressed });
        void Axis(float value) => options._Input(new InputEventJoypadMotion { Device = device, Axis = JoyAxis.TriggerLeft, AxisValue = value });
        void View(string categoryName, string query, int filterIndex)
        {
            filter.Select(filterIndex); filter.EmitSignal(OptionButton.SignalName.ItemSelected, (long)filterIndex);
            int index = Enumerable.Range(0, category.ItemCount).FirstOrDefault(i => category.GetItemText(i) == categoryName);
            category.Select(index); category.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
            search.Text = query; search.EmitSignal(LineEdit.SignalName.TextChanged, query);
        }
        void Reload()
        {
            options.LoadLayout(PlaySide.Spy);
            View("Movement", "", 0);
        }
        void Begin(string action = "forward", int slot = 0)
        {
            if (controller.FindChild($"BindingSlot_{action}_{slot}", true, false) is not Button button)
                throw new InvalidOperationException("Missing controller slot " + action + "/" + slot);
            button.EmitSignal(BaseButton.SignalName.Pressed);
            options._Process(0);
        }
        void Reset()
        {
            options.Editing.Pad.SpyButtons = PadSettings.DefaultButtons();
            options.Editing.Pad.SpyBindingOrder.Clear();
            Reload();
        }
        ConfirmationDialog? Conflict() => options.GetChildren().OfType<ConfirmationDialog>()
            .FirstOrDefault(d => !d.IsQueuedForDeletion() && d.Title == "Controller input already assigned");

        try
        {
            options.Editing.SpyPad.SwapLsLb = false;
            options.Editing.SpyPad.SwapRsRb = false;
            options.Tabs.CurrentTab = 0;
            options.Tabs.GetCurrentTabControl().GetNode<TabContainer>("Sections").CurrentTab = 2;

            foreach (string gesture in new[] { "B", "Start", "LT", "LS+A", "B:Hold" })
            {
                Reset();
                Buttons()[gesture] = "";
                Reload();
                Begin();
                Expect(options.Listening == "forward", gesture + " opens controller capture");
                switch (gesture)
                {
                    case "B": Button(JoyButton.B, true); Button(JoyButton.B, false); break;
                    case "Start": Button(JoyButton.Start, true); Button(JoyButton.Start, false); break;
                    case "LT": Axis(1); Axis(0); break;
                    case "LS+A":
                        Button(JoyButton.LeftStick, true); Button(JoyButton.A, true);
                        Button(JoyButton.A, false); Button(JoyButton.LeftStick, false); break;
                    case "B:Hold":
                        Button(JoyButton.B, true); options._Process(PadBindingState.HoldSeconds + .01);
                        Button(JoyButton.B, false); break;
                }
                Expect(options.Listening is null && Buttons().GetValueOrDefault(gesture) == "Action:forward", gesture + " captures on release into the selected action");
                Expect(Buttons()["RT"] == PadSettings.SpyFireAction, gesture + " preserves the fire contextual preset");
                Expect(options.Editing.Pad.MercButtons.SequenceEqual(merc), gesture + " leaves the other role untouched");
                KeyEscape();
            }

            Reset();
            Buttons()["A"] = ""; Buttons()["Start"] = "";
            Reload(); View("Combat", "", 0);
            Begin("fire", slot: 1); Button(JoyButton.A, true); Button(JoyButton.A, false);
            Expect((controller.FindChild("BindingSlot_fire_0", true, false) as Button)?.Text == "RT" &&
                (controller.FindChild("BindingSlot_fire_1", true, false) as Button)?.Text == "A" && Buttons()["RT"] == PadSettings.SpyFireAction,
                "adding direct Fire on A leaves the inherited RT preset in the Primary cell");
            Begin("fire", slot: 1); Button(JoyButton.Start, true); Button(JoyButton.Start, false);
            Expect((controller.FindChild("BindingSlot_fire_0", true, false) as Button)?.Text == "RT" &&
                (controller.FindChild("BindingSlot_fire_1", true, false) as Button)?.Text == "Start" && Buttons()["A"] == "" && Buttons()["RT"] == PadSettings.SpyFireAction,
                "replacing direct Fire alternate with Start preserves both visible cells and the complete RT preset");

            Reset();
            string beforeConflict = string.Join("\n", Buttons().OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
            Begin(); Button(JoyButton.B, true); Button(JoyButton.B, false);
            ConfirmationDialog? canceled = Conflict();
            Expect(canceled is not null && canceled.DialogText.Contains("Crouch", StringComparison.OrdinalIgnoreCase), "occupied controller input explains the full contextual assignment");
            canceled?.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            Expect(string.Join("\n", Buttons().OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)) == beforeConflict,
                "conflict cancellation preserves every controller assignment");

            Reset();
            Buttons()["Start"] = "Action:forward"; Buttons()["A"] = "";
            Reload(); Begin(slot: 1);
            Button(JoyButton.A, true); Button(JoyButton.A, false);
            Expect(Buttons()["A"] == "Action:forward" && Buttons()["Start"] == "Action:forward", "adding a controller alternate preserves the first assignment");
            Expect((controller.FindChild("BindingSlot_forward_0", true, false) as Button)?.Text == "Start" &&
                (controller.FindChild("BindingSlot_forward_1", true, false) as Button)?.Text == "A",
                "adding a physically earlier controller input keeps the original primary slot");
            Buttons()["B"] = "";
            Reload(); Begin(slot: 1);
            Button(JoyButton.B, true); Button(JoyButton.B, false);
            Expect(Buttons()["A"] == "" && Buttons()["B"] == "Action:forward" &&
                (controller.FindChild("BindingSlot_forward_0", true, false) as Button)?.Text == "Start" &&
                (controller.FindChild("BindingSlot_forward_1", true, false) as Button)?.Text == "B",
                "replacing alternate controller input preserves both slot positions");
            Begin(slot: 1);
            (options.FindChild("BindingCaptureRemove", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(Buttons()["Start"] == "Action:forward" && Buttons()["B"] == "", "removing a controller alternate clears only its gesture");

            Reset();
            Buttons()["A"] = "Action:forward"; Buttons()["X"] = "Action:forward";
            Reload();
            var alternate = controller.FindChild("BindingSlot_forward_1", true, false) as Button;
            PopupMenu? contextMenu = alternate is null ? null : RightClickBinding(options, alternate);
            int clearIndex = contextMenu?.GetItemIndex(1) ?? -1;
            Expect(clearIndex >= 0 && contextMenu?.IsItemDisabled(clearIndex) == false, "right-click on a controller assignment exposes Clear binding");
            contextMenu?.EmitSignal(PopupMenu.SignalName.IdPressed, 1L);
            Expect(options.Listening is null && Buttons()["A"] == "Action:forward" && Buttons()["X"] == "", "right-click controller clear preserves the other alternate without entering capture");
            var primary = controller.FindChild("BindingSlot_forward_0", true, false) as Button;
            primary?.GrabFocus(); primary?.EmitSignal(Control.SignalName.MouseEntered);
            options._Input(new InputEventKey { Keycode = Key.Delete, PhysicalKeycode = Key.Delete, Pressed = true });
            Expect(Buttons()["A"] == "", "Delete while hovering a controller assignment clears that gesture");

            Reset();
            Buttons()["A"] = "Action:crouch"; Buttons()["Start"] = PadSettings.CrouchAction;
            Reload();
            // The direct A assignment is first; B and Start are the two inherited contextual assignments.
            var linkedPreset = controller.FindChild("BindingSlot_crouch_1", true, false) as Button;
            contextMenu = linkedPreset is null ? null : RightClickBinding(options, linkedPreset);
            contextMenu?.EmitSignal(PopupMenu.SignalName.IdPressed, 1L);
            Expect(Buttons()["B"] == "" && Buttons()["A"] == "Action:crouch" && Buttons()["Start"] == PadSettings.CrouchAction,
                "clearing an inherited preset removes only its selected gesture and retains direct and preset alternatives");
            Expect(Buttons()["RT"] == PadSettings.SpyFireAction && Buttons()["LT"] == PadSettings.SpyGadgetAction,
                "clearing a contextual assignment leaves other contextual presets intact");
            linkedPreset = controller.FindChild("BindingSlot_crouch_1", true, false) as Button;
            linkedPreset?.GrabFocus(); linkedPreset?.EmitSignal(Control.SignalName.MouseEntered);
            options._Input(new InputEventKey { Keycode = Key.Delete, PhysicalKeycode = Key.Delete, Pressed = true });
            Expect(Buttons()["Start"] == "" && Buttons()["A"] == "Action:crouch" && Buttons()["RT"] == PadSettings.SpyFireAction,
                "Delete on an inherited preset clears its complete selected assignment while preserving other inputs");

            foreach (string choice in new[] { "replace", "swap" })
            {
                Reset();
                Buttons()["A"] = "Action:forward"; Buttons()["X"] = "Action:forward";
                Reload(); Begin(); Button(JoyButton.Y, true); Button(JoyButton.Y, false);
                ConfirmationDialog? conflict = Conflict();
                Expect(conflict is not null, choice + " opens controller conflict");
                if (choice == "replace") conflict?.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                else conflict?.EmitSignal(ConfirmationDialog.SignalName.CustomAction, "swap");
                Expect(Buttons()["Y"] == "Action:forward" && Buttons()["X"] == "Action:forward", choice + " preserves another alternate");
                Expect(Buttons()["A"] == (choice == "swap" ? "Action:jump" : ""), choice + " handles only the selected source gesture");
            }

            Reset();
            Buttons()["Start"] = "";
            Reload(); Begin("crouch");
            Expect(options.Listening == "Pad_crouch", "editing a linked crouch slot selects its complete contextual preset");
            Button(JoyButton.Start, true); Button(JoyButton.Start, false);
            Expect(Buttons()["B"] == "" && Buttons()["Start"] == PadSettings.CrouchAction,
                "moving a contextual preset keeps all its behavior instead of flattening to a direct crouch action");
        }
        catch (Exception error)
        {
            problems.Add("controller binding editor exception: " + error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            foreach (PopupMenu menu in options.FindChildren("BindingContextMenu", nameof(PopupMenu), true, false).OfType<PopupMenu>()) menu.Hide();
            KeyEscape();
            foreach (ConfirmationDialog dialog in options.GetChildren().OfType<ConfirmationDialog>().Where(d => !d.IsQueuedForDeletion()).ToArray())
                dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            options.Editing.Pad.SpyButtons = spy;
            options.Editing.Pad.MercButtons = merc;
            options.Editing.Pad.SpyBindingOrder = spyOrder;
            options.Editing.Pad.MercBindingOrder = mercOrder;
            options.Editing.SpyPad.SwapLsLb = swapLs;
            options.Editing.SpyPad.SwapRsRb = swapRs;
            options.LoadLayout(shownSide);
            View(oldCategory, oldSearch, oldFilter);
            scroll.ScrollVertical = oldScroll;
            foreach ((TabContainer nested, int index) in sections) nested.CurrentTab = index;
            options.Tabs.CurrentTab = tab;
        }
        return problems;
    }

    private static PopupMenu? RightClickBinding(OptionsMenu options, Button button)
    {
        button.EmitSignal(Control.SignalName.GuiInput,
            new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = Vector2.Zero });
        return options.FindChildren("BindingContextMenu", nameof(PopupMenu), true, false).OfType<PopupMenu>()
            .LastOrDefault(menu => !menu.IsQueuedForDeletion() && menu.Visible);
    }
}
