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
    private const float BindingMoreWidth = 56f;
    private sealed record BindingEntry(string Id, string Category, string Label, string Description,
        ControlAction? Action = null, string? PadAction = null, string? CustomKey = null);
    private sealed record BindingSlot(string Input, string? PadAction = null);
    private sealed class BindingView
    {
        public string Category = "Movement";
        public string Search = "";
        public int Filter;
        public string? Selected;
    }
    private sealed class BindingEditor
    {
        public required PlaySide Side;
        public bool Pad;
        public required BindingView View;
        public required VBoxContainer Page;
        public required LineEdit Search;
        public required OptionButton Filter;
        public required VBoxContainer Categories;
        public required ScrollContainer Sidebar;
        public required MarginContainer CategoryMargin;
        public required VSeparator ColumnDivider;
        public required OptionButton CompactCategory;
        public required VBoxContainer Rows;
        public required ScrollContainer Scroll;
        public required Control HeadingTail;
        public required Label Description;
        public required Label Context;
        public required HBoxContainer Behavior;
        public CheckButton? TuningToggle;
        public readonly Dictionary<string, Button> CategoryButtons = new();
        public readonly Dictionary<string, Button> RowButtons = new();
        public readonly List<Control> SlotColumns = new();
        public List<BindingEntry> Entries = new();
        public string[] CategoryNames = Array.Empty<string>();
        public bool Building;
        public bool Tuning;
    }
    private readonly Dictionary<PlaySide, BindingEditor> _bindingEditors = new();
    private readonly Dictionary<PlaySide, BindingEditor> _padBindingEditors = new();
    // View state survives Apply and rebuilding the option pages; it never changes the saved layout.
    private readonly Dictionary<(PlaySide, bool), BindingView> _bindingViews = new();

    private Control CreateBindingEditor(PlaySide side, bool pad)
    {
        if (!_bindingViews.TryGetValue((side, pad), out BindingView? view))
            _bindingViews[(side, pad)] = view = new BindingView();
        var page = new VBoxContainer { Name = pad ? "ControllerBindings" : "Bindings", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 6);
        var toolbar = new HBoxContainer { Name = "BindingToolbar" };
        var search = new LineEdit { Name = "BindingSearch", PlaceholderText = "Search all actions or keys", TooltipText = "Search all actions, descriptions and assigned keys across every category.", Text = view.Search, ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var filter = new OptionButton { Name = "BindingFilter", CustomMinimumSize = new Vector2(126, 32), FitToLongestItem = false, ClipText = true };
        filter.AddItem("All inputs"); filter.AddItem("Unbound"); filter.AddItem("Changed");
        if (!pad) filter.AddItem("Shared keys");
        view.Filter = Math.Clamp(view.Filter, 0, filter.ItemCount - 1);
        filter.Selected = view.Filter;
        var more = new MenuButton { Name = "BindingOptions", Text = "More", CustomMinimumSize = new Vector2(64, 32) };
        more.GetPopup().AddItem("Reset all bindings for this role", 0);
        more.GetPopup().AddItem("Reset this role's settings...", 1);
        more.GetPopup().IdPressed += id =>
        {
            if (id == 1) { ConfirmResetRole(side); return; }
            Confirm("Reset bindings", pad ? "Restore this role's controller bindings?" : "Restore this role's keyboard and mouse bindings? Shared layouts affect both roles.", () =>
            {
                if (pad) _edit.Pad.ResetButtons(side == PlaySide.Merc); else ResetBindings(side);
                RebuildKeys();
            }, "Reset bindings");
        };
        var compact = new OptionButton { Name = "BindingCategory", CustomMinimumSize = new Vector2(176, 32), FitToLongestItem = false, ClipText = true };
        toolbar.AddChild(search); toolbar.AddChild(compact); toolbar.AddChild(filter); toolbar.AddChild(more);
        page.AddChild(toolbar);
        var body = new HBoxContainer { Name = "BindingBody", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        var categories = new VBoxContainer { Name = "BindingCategories", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        categories.AddThemeConstantOverride("separation", 2);
        var sidebar = new ScrollContainer { Name = "BindingCategoryList", CustomMinimumSize = new Vector2(144, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var categoryMargin = new MarginContainer { Name = "BindingCategoryMargin" };
        categoryMargin.AddThemeConstantOverride("margin_top", 6);
        sidebar.AddChild(categories); categoryMargin.AddChild(sidebar); body.AddChild(categoryMargin);
        var columnDivider = new VSeparator { Name = "BindingColumnDivider", MouseFilter = MouseFilterEnum.Ignore };
        columnDivider.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = UiStyle.MenuFocus with { A = .3f }, Thickness = 1, Vertical = true });
        body.AddChild(columnDivider);
        var table = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        heading.AddChild(new Label { Text = "Action", Modulate = UiStyle.Dim, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var first = new Label { Name = "BindingPrimaryHeading", Text = "Primary", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.Dim };
        var second = new Label { Name = "BindingAlternateHeading", Text = "Alternate", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.Dim };
        heading.AddChild(first); heading.AddChild(second);
        var headingTail = new Control { Name = "BindingHeadingTail", CustomMinimumSize = new Vector2(BindingMoreWidth, 0) };
        heading.AddChild(headingTail);
        table.AddChild(heading);
        var rows = new VBoxContainer { Name = "BindingRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        var scroll = new ScrollContainer { Name = "ActionList", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(rows); table.AddChild(scroll); body.AddChild(table); page.AddChild(body);
        page.AddChild(new HSeparator());
        var details = new VBoxContainer { Name = "BindingDetails", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 2);
        var description = new Label { Name = "BindingDescription", Text = "Select an action to see its controls.", AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var context = Help(""); context.Name = "BindingContext"; context.MaxLinesVisible = 1; context.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var behavior = new HBoxContainer { Name = "BindingBehavior" };
        details.AddChild(description); details.AddChild(context); details.AddChild(behavior); page.AddChild(details);
        var editor = new BindingEditor { Side = side, Pad = pad, View = view, Page = page, Search = search, Filter = filter, Categories = categories, Sidebar = sidebar, CategoryMargin = categoryMargin, ColumnDivider = columnDivider, CompactCategory = compact, Rows = rows, Scroll = scroll, HeadingTail = headingTail, Description = description, Context = context, Behavior = behavior };
        editor.SlotColumns.Add(first); editor.SlotColumns.Add(second);
        (pad ? _padBindingEditors : _bindingEditors)[side] = editor;
        if (!pad) _bindingHelp[side] = context;
        search.TextChanged += text => { if (editor.Building) return; view.Search = text; RebuildBindingEditor(side, pad, resetScroll: true); };
        filter.ItemSelected += index => { view.Filter = (int)index; RebuildBindingEditor(side, pad, resetScroll: true); };
        compact.ItemSelected += index => SelectBindingCategory(editor, editor.CategoryNames[(int)index]);
        scroll.GetVScrollBar().ValueChanged += _ => _hoveredBinding = null;
        return page;
    }

    private List<BindingEntry> BindingEntries(PlaySide side, bool pad)
    {
        var actions = ControlActions.ForRole(side == PlaySide.Merc, _edit.MouseWheel).ToArray();
        if (!pad)
        {
            var entries = actions.Select(a => new BindingEntry(a.Id, a.Category, a.Label, a.Description, Action: a)).ToList();
            var model = ModelOf(side);
            entries.AddRange(model.Rows().Where(r => model.Draft!.Layout.Expanded(r.Command).Any(p => !ControlActions.All.Any(a => a.Matches(p))))
                .Select(r => new BindingEntry("custom_" + r.Key, "Custom", "Custom: " + r.Command, "Saved custom command chain: " + r.Command, CustomKey: r.Key)));
            return entries;
        }
        var result = new List<BindingEntry>();
        foreach ((string assignment, string label) in ModelOf(side).PadChoices(forSide: side, wheelMode: _edit.MouseWheel).Where(c => c.Action.Length > 0))
        {
            ControlAction? action = actions.FirstOrDefault(a => assignment == ControlActions.PadPrefix + a.Id);
            string id = action?.Id ?? assignment.Replace(':', '_');
            string category = action?.Category ?? (assignment.StartsWith("Pad:", StringComparison.Ordinal) ? "Contextual" : assignment.StartsWith(PadSettings.KeyPrefix, StringComparison.Ordinal) ? "Hub" : "Controller");
            string description = action?.Description ?? PadPresetDescription(assignment, label);
            result.Add(new BindingEntry(id, category, label, description, Action: action, PadAction: assignment));
        }
        foreach (string assignment in _edit.Pad.ButtonsOf(side == PlaySide.Merc).Values.Where(a => a.Length > 0).Distinct())
            if (!result.Any(e => e.PadAction == assignment))
                result.Add(new BindingEntry("saved_" + assignment.Replace(':', '_'), "Custom", assignment + " (saved)", "This saved controller assignment is preserved unchanged.", PadAction: assignment));
        return result;
    }

    private static string PadPresetDescription(string assignment, string label) => assignment switch
    {
        PadSettings.SpyFireAction => "Contextual preset: fire immediately; a short tap quick-uses gadget slot 1 while unarmed. In a sticky camera it releases gas. Editing a preset changes all its linked behaviors.",
        PadSettings.SpyGadgetAction => "Contextual preset: use the selected gadget; a short tap quick-uses slot 2 while unarmed. Editing a preset changes all its linked behaviors.",
        PadSettings.MercGadgetAction => "Contextual preset: use the selected gadget and hold breath while scoped. Editing a preset changes both linked behaviors.",
        PadSettings.CrouchAction => "Contextual preset: crouch during play or leave the camera you are using. Editing a preset changes both linked behaviors.",
        PadSettings.LongRollAction => "Contextual preset: long roll or leave a camera. Hold B reaches its held assignment after 0.2 seconds.",
        PadSettings.WallLaserAction => "Contextual preset: back to wall while unarmed, or toggle the weapon laser with the weapon drawn.",
        PadSettings.ClutchOneAction or PadSettings.ClutchTwoAction => "Hold this control to use the sensitivity multiplier configured in Aim & movement settings.",
        _ => label + ". This assignment is independent of keyboard bindings unless explicitly saved as keyboard-linked.",
    };

    private static bool PresetIncludes(string preset, string id) => preset switch
    {
        PadSettings.SpyFireAction => id is "fire" or "quick1",
        PadSettings.SpyGadgetAction => id is "altfire" or "quick2",
        PadSettings.MercGadgetAction => id is "altfire" or "breath",
        PadSettings.CrouchAction => id == "crouch",
        PadSettings.WallLaserAction => id is "wall" or "weaponlaser",
        _ => false,
    };

    private List<BindingSlot> BindingInputs(BindingEditor editor, BindingEntry entry)
    {
        if (editor.Pad)
        {
            bool merc = editor.Side == PlaySide.Merc;
            List<BindingSlot> available = _edit.Pad.ButtonsOf(merc)
                .Where(b => b.Value == entry.PadAction || entry.Action is not null && PresetIncludes(b.Value, entry.Action.Id))
                .OrderBy(b => b.Value == entry.PadAction ? 0 : 1).ThenBy(b => Array.IndexOf(PadSettings.Controls, b.Key))
                .Select(b => new BindingSlot(b.Key, b.Value)).ToList();
            Dictionary<string, BindingSlot> byInput = available.ToDictionary(input => input.Input, StringComparer.Ordinal);
            return PadBindingOrder.Order(_edit.Pad, merc, entry.Id, available.Select(input => input.Input))
                .Select(input => byInput[input]).ToList();
        }
        if (entry.Action is { } action) return ModelOf(editor.Side).Draft!.Keys(action).Select(k => new BindingSlot(k)).ToList();
        return entry.CustomKey is { } key && ModelOf(editor.Side).Draft!.CommandOf(key).Length > 0 ? new List<BindingSlot> { new(key) } : new List<BindingSlot>();
    }

    private void SelectBindingCategory(BindingEditor editor, string category)
    {
        editor.View.Category = category;
        editor.View.Search = "";
        editor.Building = true; editor.Search.Text = ""; editor.Building = false;
        RebuildBindingEditor(editor.Side, editor.Pad, resetScroll: true);
    }

    private void RebuildBindingEditor(PlaySide side, bool pad, bool resetScroll = false)
    {
        if (!(pad ? _padBindingEditors : _bindingEditors).TryGetValue(side, out BindingEditor? editor)) return;
        Control? focused = GetViewport().GuiGetFocusOwner();
        string? focusName = focused is not null && editor.Page.IsAncestorOf(focused) ? focused.Name.ToString() : null;
        editor.Building = true;
        if (!pad && side == _bindingSide) _keyButtons.Clear();
        int scroll = resetScroll ? 0 : editor.Scroll.ScrollVertical;
        editor.Entries = BindingEntries(side, pad);
        editor.CategoryNames = new[] { "All" }.Concat(editor.Entries.Select(e => e.Category).Distinct().OrderBy(CategoryOrder)).ToArray();
        if (!editor.CategoryNames.Contains(editor.View.Category)) editor.View.Category = "All";
        foreach (Node child in editor.Categories.GetChildren()) { editor.Categories.RemoveChild(child); child.QueueFree(); }
        editor.CategoryButtons.Clear(); editor.CompactCategory.Clear();
        foreach (string category in editor.CategoryNames)
        {
            var button = QuietBindingButton(category, "BindingCategory_" + category);
            button.ToggleMode = true;
            button.Pressed += () => SelectBindingCategory(editor, category);
            editor.Categories.AddChild(button); editor.CategoryButtons[category] = button;
            editor.CompactCategory.AddItem(category);
        }
        string activeCategory = editor.View.Search.Length > 0 ? "All" : editor.View.Category;
        foreach ((string category, Button button) in editor.CategoryButtons) button.ButtonPressed = category == activeCategory;
        editor.CompactCategory.Select(Array.IndexOf(editor.CategoryNames, activeCategory));
        if (activeCategory == "All") editor.Sidebar.ScrollVertical = 0;
        foreach (Node child in editor.Rows.GetChildren()) { editor.Rows.RemoveChild(child); child.QueueFree(); }
        editor.RowButtons.Clear();
        editor.SlotColumns.RemoveRange(2, editor.SlotColumns.Count - 2);
        string query = editor.View.Search.Trim();
        var visible = editor.Entries.Where(e => (query.Length > 0 || editor.View.Category == "All" || e.Category == editor.View.Category) && MatchesBindingFilter(editor, e, query))
            .OrderBy(e => CategoryOrder(e.Category)).ToArray();
        string categoryHeading = "";
        foreach (BindingEntry entry in visible)
        {
            if ((query.Length > 0 || editor.View.Category == "All") && categoryHeading != entry.Category)
            {
                bool firstSection = categoryHeading.Length == 0;
                categoryHeading = entry.Category;
                var section = new MarginContainer { Name = "BindingSection_" + BindingNodeId(categoryHeading), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                section.AddThemeConstantOverride("margin_top", firstSection ? 2 : 8);
                section.AddThemeConstantOverride("margin_bottom", 4);
                var heading = new HBoxContainer();
                heading.AddThemeConstantOverride("separation", 10);
                heading.AddChild(new Label { Name = "BindingSectionLabel", Text = categoryHeading, Modulate = UiStyle.MenuFocus });
                var rule = new HSeparator { Name = "BindingSectionRule", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = UiStyle.MenuFocus with { A = .3f }, Thickness = 1 });
                heading.AddChild(rule); section.AddChild(heading); editor.Rows.AddChild(section);
            }
            List<BindingSlot> inputs = BindingInputs(editor, entry);
            var row = new HBoxContainer { Name = "BindingRow_" + BindingNodeId(entry.Id), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 8);
            var label = QuietBindingButton(entry.Label, "BindingAction_" + BindingNodeId(entry.Id));
            label.ToggleMode = true; label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.TooltipText = entry.Label + "\n" + entry.Description;
            label.Pressed += () => SelectBindingEntry(editor, entry);
            label.FocusEntered += () => SelectBindingEntry(editor, entry);
            row.AddChild(label); editor.RowButtons[entry.Id] = label;
            for (int slot = 0; slot < 2; slot++)
            {
                BindingSlot? binding = inputs.ElementAtOrDefault(slot);
                var button = BindingSlotButton(binding is null ? "-" : BindingDisplay(editor, binding), "BindingSlot_" + BindingNodeId(entry.Id) + "_" + slot);
                button.TooltipText = binding is null ? "Add a " + (slot == 0 ? "primary" : "alternate") + " binding for " + entry.Label : BindingSlotDescription(editor, entry, binding) + "\nClick to change. Right-click for actions, or hover and press Delete to clear this input.";
                button.Pressed += () => BeginBindingCapture(editor, entry, binding);
                button.FocusEntered += () => SelectBindingEntry(editor, entry);
                if (binding is not null) AttachBindingActions(editor, entry, binding, button);
                row.AddChild(button); editor.SlotColumns.Add(button);
                if (!pad && binding is not null && side == _bindingSide) _keyButtons.TryAdd(binding.Input, button);
            }
            var more = new MenuButton
            {
                Name = "BindingMore_" + BindingNodeId(entry.Id), Text = inputs.Count > 2 ? "+" + (inputs.Count - 2) : "+",
                TooltipText = inputs.Count > 2 ? (inputs.Count - 2) + " additional bindings. Click to view or add inputs." : "Add another binding",
                CustomMinimumSize = new Vector2(BindingMoreWidth, 32), Flat = true, ClipText = true,
            };
            StyleQuietBindingButton(more, 4f);
            PopupMenu popup = more.GetPopup();
            for (int index = 2; index < inputs.Count; index++) popup.AddItem(BindingDisplay(editor, inputs[index]), index);
            popup.AddItem("Add binding...", 10000);
            popup.IdPressed += id => BeginBindingCapture(editor, entry, id == 10000 ? null : inputs[(int)id]);
            row.AddChild(more); editor.Rows.AddChild(row);
        }
        if (visible.Length == 0) editor.Rows.AddChild(Help("No actions match. Clear the search or change the filter."));
        BindingEntry? selected = visible.FirstOrDefault(e => e.Id == editor.View.Selected) ?? visible.FirstOrDefault();
        if (selected is not null) SelectBindingEntry(editor, selected);
        else
        {
            editor.Description.Text = "No matching actions"; editor.Context.Text = "";
            ClearBindingBehavior(editor);
        }
        if (!pad && _sharingLabels.TryGetValue(side, out Label? sharing))
        {
            sharing.Text = _edit.SplitKeyboard ? "" : "Shared Spy / Mercenary bindings";
            sharing.Visible = !_edit.SplitKeyboard;
            sharing.TooltipText = "Edits here affect both roles using the Spy keyboard layout.";
        }
        editor.Scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scroll);
        editor.Building = false;
        UpdateBindingEditorLayout();
        if (focusName is not null && (focusName.StartsWith("BindingCategory_", StringComparison.Ordinal) || focusName.StartsWith("BindingSlot_", StringComparison.Ordinal) || focusName.StartsWith("BindingAction_", StringComparison.Ordinal)))
            Callable.From(() => { if (IsInstanceValid(editor.Page) && editor.Page.FindChild(focusName, true, false) is Control control && control.IsVisibleInTree()) control.GrabFocus(); }).CallDeferred();
    }

    private static int CategoryOrder(string category) => category switch
    {
        "Movement" => 0, "Combat" => 1, "Gadgets" => 2, "Vision" => 3, "Communication" => 4, "Interface" => 5,
        "Contextual" => 6, "Controller" => 7, "Hub" => 8, _ => 9,
    };

    private bool MatchesBindingFilter(BindingEditor editor, BindingEntry entry, string query)
    {
        List<BindingSlot> inputs = BindingInputs(editor, entry);
        if (query.Length > 0 && !(entry.Label + " " + entry.Description + " " + string.Join(" ", inputs.Select(i => BindingDisplay(editor, i)))).Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
        return editor.View.Filter switch
        {
            1 => inputs.Count == 0,
            2 => BindingChanged(editor, entry, inputs),
            3 => inputs.Any(i => IsSharedBinding(editor.Side, i.Input)),
            _ => true,
        };
    }

    private bool IsSharedBinding(PlaySide side, string input)
    {
        ActionBindingDraft draft = ModelOf(side).Draft!;
        return draft.Layout.Expanded(draft.CommandOf(input))
            .Select(part => ControlActions.All.FirstOrDefault(action => action.Matches(part))?.Id ?? "custom:" + part)
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
    }

    private static string BindingNodeId(string id) => new(id.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

    private bool BindingChanged(BindingEditor editor, BindingEntry entry, List<BindingSlot> inputs)
    {
        if (editor.Pad)
        {
            var before = GameSettings.FromJson(_baseline ?? _edit.ToJson()).Pad.ButtonsOf(editor.Side == PlaySide.Merc);
            bool Related(string? assignment) => assignment == entry.PadAction || entry.Action is not null && assignment is not null && PresetIncludes(assignment, entry.Action.Id);
            return PadSettings.Controls.Any(k => (Related(_edit.Pad.ButtonsOf(editor.Side == PlaySide.Merc).GetValueOrDefault(k)) || Related(before.GetValueOrDefault(k))) && _edit.Pad.ButtonsOf(editor.Side == PlaySide.Merc).GetValueOrDefault(k) != before.GetValueOrDefault(k));
        }
        var draft = ModelOf(editor.Side).Draft!;
        KeyLayout baseline = KeyLayout.Parse(draft.Baseline);
        if (entry.Action is { } action)
            return !inputs.Select(i => i.Input).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new ActionBindingDraft(baseline).Keys(action));
        return entry.CustomKey is { } key && draft.CommandOf(key) != baseline.Bindings.GetValueOrDefault(key, "");
    }

    private static Button QuietBindingButton(string text, string name)
    {
        var button = new Button { Name = name, Text = text, Flat = true, ClipText = true, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 32) };
        StyleQuietBindingButton(button);
        return button;
    }

    private static void StyleQuietBindingButton(Button button, float horizontalPadding = 8f)
    {
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = horizontalPadding, ContentMarginRight = horizontalPadding });
        var hover = new StyleBoxFlat { BgColor = UiStyle.MenuFocus with { A = .07f }, ContentMarginLeft = horizontalPadding, ContentMarginRight = horizontalPadding };
        button.AddThemeStyleboxOverride("hover", hover);
        var selected = (StyleBoxFlat)hover.Duplicate(); selected.BgColor = UiStyle.MenuFocus with { A = .15f };
        button.AddThemeStyleboxOverride("pressed", selected); button.AddThemeStyleboxOverride("hover_pressed", selected);
        // The shared button focus style grows outside the control and is clipped on the first scroll row.
        // Keep the SCCT corner treatment inside this quiet row, including its antialiased diagonal strokes.
        button.AddThemeStyleboxOverride("focus", new UiBox
        {
            Expand = -1f, Notch = 5f, Border = UiStyle.MenuFocus with { A = .8f },
            Ticks = UiStyle.MenuFocus, TickLength = 6f,
            ContentMarginLeft = horizontalPadding, ContentMarginRight = horizontalPadding, ContentMarginTop = 0, ContentMarginBottom = 0,
        });
    }

    private static Button BindingSlotButton(string text, string name)
    {
        var button = new Button { Name = name, Text = text, ClipText = true, CustomMinimumSize = new Vector2(108, 32) };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
        {
            bool active = state != "normal";
            button.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = UiStyle.Background with { A = active ? .8f : .35f }, BorderColor = UiStyle.MenuFocus with { A = active ? .8f : .22f }, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = 8, ContentMarginRight = 8 });
        }
        return button;
    }

    private string BindingDisplay(BindingEditor editor, BindingSlot slot)
    {
        if (!editor.Pad) return GodotKeys.DisplayName(slot.Input);
        LookSettings look = _edit.LookOf(editor.Side == PlaySide.Merc, true);
        return string.Join(" + ", slot.Input.Split('+').Select(part => part.EndsWith(":Hold", StringComparison.Ordinal) ? "Hold " + look.PhysicalControl(part[..^5]) : look.PhysicalControl(part)))
            .Replace("Dpad", "D-pad ", StringComparison.Ordinal);
    }

    private string BindingSlotDescription(BindingEditor editor, BindingEntry entry, BindingSlot slot)
    {
        if (!editor.Pad) return GodotKeys.DisplayName(slot.Input) + ": " + DescribeBinding(ModelOf(editor.Side).Draft!, slot.Input, editor.Side);
        BindingEntry? owner = editor.Entries.FirstOrDefault(e => e.PadAction == slot.PadAction);
        return BindingDisplay(editor, slot) + ": " + (owner?.Label ?? slot.PadAction) + (slot.PadAction != entry.PadAction ? " (linked contextual preset)" : "");
    }

    private void SelectBindingEntry(BindingEditor editor, BindingEntry entry)
    {
        editor.View.Selected = entry.Id;
        foreach ((string id, Button button) in editor.RowButtons) button.SetPressedNoSignal(id == entry.Id);
        var inputs = BindingInputs(editor, entry);
        string explanation = entry.Description;
        if (!editor.Pad && inputs.Any(i => BindingKey.TryParse(i.Input, out var key) && key.Modifiers != BindingModifiers.None))
            explanation += " The modifier's own action remains active alongside this combination.";
        if (editor.Pad && entry.Id is "forward" or "back" or "left" or "right")
            explanation = "The left stick moves this direction when controller movement is enabled. Button bindings below are optional extras.";
        editor.Description.Text = explanation; editor.Description.TooltipText = explanation;
        editor.Context.Text = inputs.Count == 0 ? "Unbound. Select an empty slot to add a binding." : string.Join(" | ", inputs.Select(i => BindingSlotDescription(editor, entry, i)));
        editor.Context.TooltipText = editor.Context.Text;
        ClearBindingBehavior(editor);
        string? behavior = entry.Action?.Id switch { "speed" => "walk", "scope" when editor.Side == PlaySide.Merc => "scope", "perspective" => "perspective", "cloak" => "cloak", _ => null };
        if (behavior is null) return;
        bool merc = editor.Side == PlaySide.Merc;
        LookSettings look = _edit.LookOf(merc, behavior == "walk" && editor.Pad);
        string value = behavior switch { "walk" => look.WalkBehavior, "scope" => _edit.MercKbm.ScopeBehavior, "perspective" => _edit.SpyKbm.PerspectiveBehavior, _ => _edit.SpyKbm.CloakBehavior };
        editor.Behavior.AddChild(new Label { Text = "Behavior" });
        var choice = new OptionButton { Name = "SelectedBindingBehavior", CustomMinimumSize = new Vector2(110, 30), TooltipText = "Hold stays active while pressed. Toggle switches on each press. Mouse wheel bindings always toggle." };
        choice.AddItem("Hold"); choice.AddItem("Toggle"); choice.Select(value == "Hold" ? 0 : 1);
        choice.ItemSelected += index =>
        {
            string next = index == 0 ? "Hold" : "Toggle";
            switch (behavior)
            {
                case "walk": _edit.LookOf(merc, false).WalkBehavior = next; _edit.LookOf(merc, true).WalkBehavior = next; break;
                case "scope": _edit.MercKbm.ScopeBehavior = next; break;
                case "perspective": _edit.SpyKbm.PerspectiveBehavior = next; break;
                case "cloak": _edit.SpyKbm.CloakBehavior = next; break;
            }
        };
        editor.Behavior.AddChild(choice);
    }

    private void ClearBindingInput(BindingEditor editor, BindingEntry entry, BindingSlot input)
    {
        if (editor.Pad)
        {
            Dictionary<string, string> buttons = _edit.Pad.ButtonsOf(editor.Side == PlaySide.Merc);
            if (buttons.GetValueOrDefault(input.Input) == input.PadAction)
                ApplyPadBindingChange(editor, () => buttons[input.Input] = "");
        }
        else if (entry.Action is { } action) ModelOf(editor.Side).Draft!.Remove(action, input.Input);
        else ModelOf(editor.Side).Draft!.Layout.Bind(input.Input, "");
    }

    private void ApplyPadBindingChange(BindingEditor editor, Action change, string? oldControl = null, string? newControl = null, bool swap = false)
    {
        // A contextual preset can appear on several action rows. Preserve the positions in every affected row,
        // including the original selected action when its binding actually belongs to a named preset.
        var before = editor.Entries.ToDictionary(entry => entry.Id,
            entry => BindingInputs(editor, entry).Select(input => input.Input).ToList(), StringComparer.Ordinal);
        change();
        foreach (BindingEntry entry in editor.Entries)
        {
            List<string> available = BindingInputs(editor, entry).Select(input => input.Input).ToList();
            var availableInputs = new HashSet<string>(available, StringComparer.Ordinal);
            var remaining = new HashSet<string>(available, StringComparer.Ordinal);
            var desired = new List<string>();
            foreach (string previous in before[entry.Id])
            {
                string current = previous;
                if (oldControl is not null && newControl is not null)
                {
                    if (previous == oldControl && availableInputs.Contains(newControl)) current = newControl;
                    else if (swap && previous == newControl && availableInputs.Contains(oldControl)) current = oldControl;
                }
                if (remaining.Remove(current)) desired.Add(current);
            }
            foreach (string input in available) if (remaining.Remove(input)) desired.Add(input);
            if (!before[entry.Id].SequenceEqual(available) || !desired.SequenceEqual(available))
                PadBindingOrder.RememberOrder(_edit.Pad, editor.Side == PlaySide.Merc, entry.Id, desired);
        }
    }

    private static void ClearBindingBehavior(BindingEditor editor)
    {
        foreach (Node child in editor.Behavior.GetChildren()) { editor.Behavior.RemoveChild(child); child.QueueFree(); }
    }

    private void UpdateBindingEditorLayout()
    {
        bool sidebar = FrameWidth >= 1000f;
        foreach (BindingEditor editor in _bindingEditors.Values.Concat(_padBindingEditors.Values))
        {
            editor.Search.PlaceholderText = sidebar ? "Search all actions or keys" : "Search";
            if (editor.TuningToggle is { } toggle) toggle.Text = sidebar ? "Aim & movement" : "Aim settings";
            editor.Sidebar.Visible = sidebar;
            editor.CategoryMargin.Visible = sidebar;
            editor.ColumnDivider.Visible = sidebar;
            editor.CompactCategory.Visible = !sidebar && !editor.Tuning;
            foreach (Control column in editor.SlotColumns) column.CustomMinimumSize = new Vector2(FrameWidth < 900f ? 90 : 108, column is Button ? 32 : 0);
        }
    }

    private void UpdateBindingHeadingGutters()
    {
        foreach (BindingEditor editor in _bindingEditors.Values.Concat(_padBindingEditors.Values))
        {
            if (!editor.Page.IsVisibleInTree() || editor.Rows.Size.X <= 0f) continue;
            // Rows lose the scrollbar gutter; their headers must reserve the same space to stay centered.
            float gutter = MathF.Max(0f, editor.Scroll.GetGlobalRect().End.X - editor.Rows.GetGlobalRect().End.X);
            var minimum = new Vector2(BindingMoreWidth + gutter, 0f);
            if (editor.HeadingTail.CustomMinimumSize != minimum) editor.HeadingTail.CustomMinimumSize = minimum;
        }
    }

    private void MergeControllerBindingToolbar(PlaySide side, HBoxContainer toolbar, CheckButton toggle, MenuButton reset)
    {
        BindingEditor editor = _padBindingEditors[side];
        editor.TuningToggle = toggle;
        HBoxContainer original = editor.Page.GetNode<HBoxContainer>("BindingToolbar");
        Control[] bindingControls = original.GetChildren().OfType<Control>().ToArray();
        foreach (Control spacer in toolbar.GetChildren().OfType<Control>().Where(c => c is not BaseButton).ToArray())
        {
            toolbar.RemoveChild(spacer); spacer.QueueFree();
        }
        int position = 0;
        foreach (Control control in bindingControls)
        {
            original.RemoveChild(control); toolbar.AddChild(control); toolbar.MoveChild(control, position++);
        }
        toolbar.MoveChild(toggle, position++);
        toolbar.MoveChild(reset, position);
        editor.Page.RemoveChild(original); original.QueueFree();
        void ShowTuning(bool tuning)
        {
            editor.Tuning = tuning;
            foreach (Control control in bindingControls) control.Visible = !tuning;
            reset.Visible = tuning;
            UpdateBindingEditorLayout();
        }
        toggle.Toggled += ShowTuning;
        ShowTuning(toggle.ButtonPressed);
    }
}
