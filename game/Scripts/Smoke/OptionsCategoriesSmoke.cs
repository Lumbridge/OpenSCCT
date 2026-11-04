using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Every options settings browser remains searchable, aligned and usable when resized.</summary>
public sealed class OptionsCategoriesSmoke : SmokeTest
{
    public const string TestName = "options-categories";
    public override string Name => TestName;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes = [new(1280, 720), new(800, 600), new(3840, 2160), new(800, 600)];
    private OptionsMenu? _options;
    private Control[] _browsers = [];
    private int _size, _browser, _case, _frames;
    private int _hudCase;
    private bool _hudPhase;
    private string _searchKey = "";

    public override SmokeResult Step(SmokeContext context)
    {
        SceneTree tree = context.Tree;
        if (_options is null)
        {
            if (!SettingsStore.Isolated) return SmokeResult.Fail("options-categories requires isolated smoke settings");
            _options = new OptionsMenu { Overlay = true };
            tree.Root.AddChild(_options);
            tree.CurrentScene = _options;
            CheckStagedControls(_options);
            CheckHudEditor(_options);
            foreach (CheckButton toggle in _options.FindChildren("ControllerAimSettings", nameof(CheckButton), true, false).OfType<CheckButton>())
                toggle.ButtonPressed = true;
            _browsers = _options.FindChildren("*", nameof(Control), true, false).OfType<Control>()
                .Where(c => c.HasMeta("settings_browser") && c.GetMeta("settings_browser").AsBool()).ToArray();
            foreach (string name in new[] { "Display", "Performance", "Audio", "Voice", "HUD", "Controller (Shared)", "Crosshair" })
                Expect(_browsers.Any(c => Ancestors(c).Any(n => n.Name == name)), name + " uses a settings browser");
            if (_browsers.Length == 0) return SmokeResult.Fail("no settings browsers were found");
            Resize(tree, _sizes[_size]);
        }
        if (_hudPhase) return StepHudLayout(tree);

        Control page = _browsers[_browser];
        if (page.FindChild("SettingsCategory", true, false) is not OptionButton category ||
            page.FindChild("SettingsSearch", true, false) is not LineEdit search)
            return SmokeResult.Fail(page.GetPath() + ": missing category or search control");
        int categories = category.ItemCount;
        if (categories == 0) return SmokeResult.Fail(page.GetPath() + ": no categories");
        if (++_frames == 1)
        {
            Show(page);
            search.Text = "";
            search.EmitSignal(LineEdit.SignalName.TextChanged, "");
            int selected = _case < categories ? _case : 0;
            category.Select(selected);
            category.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selected);
            _searchKey = AllRows(page).Select(KeyOf).LastOrDefault(k => k.Length > 0) ?? "";
            if (_case >= categories)
            {
                string query = _case == categories ? _searchKey : "no-such-option-9f21";
                search.Text = query;
                search.EmitSignal(LineEdit.SignalName.TextChanged, query);
            }
        }
        if (_frames < 8) return SmokeResult.Running;

        string label = _case < categories ? category.GetItemText(_case) : _case == categories ? "search " + _searchKey : "empty search result";
        string where = $"{_sizes[_size].X}x{_sizes[_size].Y} {page.GetPath()} {label}";
        CheckLayout(page, where);
        CheckDetails(page, where);
        Control[] shown = AllRows(page).Where(c => c.IsVisibleInTree()).ToArray();
        if (_case < categories) Expect(shown.Length > 0, where + ": category has settings");
        else if (_case == categories) Expect(shown.Any(c => KeyOf(c) == _searchKey), where + ": search finds a setting across categories");
        else Expect(shown.Length == 0, where + ": unmatched search hides all settings");
        MenuPage.Shot(tree.Root, $"options-categories-{_size}-{_sizes[_size].X}x{_sizes[_size].Y}-{_browser}-{_case}");

        _frames = 0;
        if (++_case < categories + 2) return SmokeResult.Running;
        // Clearing a search restores the selected category instead of retaining the empty state.
        search.Text = "";
        search.EmitSignal(LineEdit.SignalName.TextChanged, "");
        Expect(AllRows(page).Any(c => c.Visible), where + ": clearing search restores category settings");
        _case = 0;
        if (++_browser < _browsers.Length) return SmokeResult.Running;
        _browser = 0;
        _hudPhase = true;
        _hudCase = 0;
        return SmokeResult.Running;
    }

    private SmokeResult StepHudLayout(SceneTree tree)
    {
        var panel = _options!.FindChildren("*", nameof(Control), true, false).OfType<HudEditorPanel>().FirstOrDefault();
        if (panel is null) return SmokeResult.Fail("HUD editor browser is missing during layout review");
        int role = _hudCase / 3, aspect = _hudCase % 3;
        if (++_frames == 1)
        {
            Show(panel);
            foreach (var pick in new[] { (Name: "HudEditorRole", Value: role), (Name: "HudEditorAspect", Value: aspect) })
            {
                if (panel.FindChild(pick.Name, true, false) is not OptionButton picker)
                    return SmokeResult.Fail("HUD editor is missing " + pick.Name);
                picker.Select(pick.Value);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)pick.Value);
            }
        }
        if (_frames < 8) return SmokeResult.Running;
        string where = $"{_sizes[_size].X}x{_sizes[_size].Y} HUD editor role={role} aspect={aspect}";
        if (_size == 0 && _hudCase == 0) CheckHudMultiSelection(panel);
        _problems.AddRange(LayoutCheck.Problems(_options, _sizes[_size].X).Select(p => where + ": " + p));
        _problems.AddRange(LayoutCheck.BoundsProblems(_options, _sizes[_size]).Select(p => where + ": " + p));
        Rect2 panelBounds = panel.GetGlobalRect().Grow(1f);
        var preview = panel.FindChildren("*", nameof(Control), true, false).OfType<HudEditorPreview>().FirstOrDefault();
        var inspector = panel.FindChild("HudEditorInspector", true, false) as Control;
        Expect(preview is not null && inspector is not null, where + ": preview and inspector exist");
        if (preview is not null)
        {
            Expect(preview.IsVisibleInTree() && preview.Size.X >= 280f && preview.Size.Y >= 140f, where + ": preview has a usable visible size");
            Expect(panelBounds.Encloses(preview.GetGlobalRect()), where + ": preview fits the editor");
            foreach (ScrollContainer scroll in Ancestors(preview).OfType<ScrollContainer>())
                Expect(scroll.GetGlobalRect().Grow(1f).Encloses(preview.GetGlobalRect()), where + ": scroll area preserves the complete preview");
            foreach (string id in new[] { "Radar", "Location", "Activity", "Fps", "Network" })
            {
                bool exists = preview.ElementBounds.TryGetValue(id, out Rect2 bounds) && bounds.Size.X > 0f && bounds.Size.Y > 0f;
                Expect(exists, where + ": selectable preview bounds exist for " + id);
                if (exists) Expect(new Rect2(Vector2.Zero, preview.Size).Grow(1f).Encloses(bounds), where + ": " + id + " outline fits the preview");
            }
        }
        if (inspector is not null)
        {
            Expect(inspector.IsVisibleInTree() && panelBounds.Encloses(inspector.GetGlobalRect()), where + ": inspector fits the editor");
            if (preview is not null) Expect(preview.GetGlobalRect().End.X <= inspector.GetGlobalRect().Position.X + 1f, where + ": inspector clears the preview");
            if (inspector.FindChild("HudEditorProperties", true, false) is ScrollContainer properties)
                Expect(properties.Size.Y >= 96f && inspector.GetGlobalRect().Grow(1f).Encloses(properties.GetGlobalRect()), where + ": inspector properties have usable space within the inspector");
            foreach (SpinBox field in inspector.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>())
                Expect(field.Size.X >= 96f && field.GetGlobalRect().Position.X >= inspector.GetGlobalRect().Position.X - 1f &&
                    field.GetGlobalRect().End.X <= inspector.GetGlobalRect().End.X + 1f, where + ": numeric field fits the inspector");
        }
        foreach (string text in new[] { "Undo", "Redo" })
        {
            Button? button = panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
            Expect(button is not null && button.IsVisibleInTree() && button.Size.X >= 60f && panelBounds.Encloses(button.GetGlobalRect()),
                where + ": " + text + " remains visible and at least 60px wide");
        }
        MenuPage.Shot(tree.Root, $"options-categories-hud-{_sizes[_size].X}x{_sizes[_size].Y}-{role}-{aspect}-{_size}");
        _frames = 0;
        if (++_hudCase < 6) return SmokeResult.Running;
        _hudPhase = false;
        if (++_size < _sizes.Length) { Resize(tree, _sizes[_size]); return SmokeResult.Running; }
        return _problems.Count == 0
            ? SmokeResult.Pass("all options categories, cross-category search, context, staged controls and six HUD role/aspect layouts at 800x600, 720p and 4K, including shrinking back")
            : SmokeResult.Fail(string.Join("; ", _problems.Distinct().Take(16)));
    }

    private static IEnumerable<Node> Ancestors(Node node)
    {
        for (Node? current = node; current is not null; current = current.GetParent()) yield return current;
    }

    private static Control[] AllRows(Control page) => page.FindChildren("*", nameof(Control), true, false)
        .OfType<Control>().Where(c => c.HasMeta("options_setting")).ToArray();

    private static string KeyOf(Control row) => row.GetMeta("options_setting").AsString();

    private static void Show(Control page)
    {
        foreach (Node node in Ancestors(page).Reverse())
        {
            if (node.GetParent() is not TabContainer tabs) continue;
            for (int i = 0; i < tabs.GetTabCount(); i++)
                if (tabs.GetTabControl(i) == node) { tabs.CurrentTab = i; break; }
        }
    }

    private void CheckLayout(Control page, string where)
    {
        Expect(page.IsVisibleInTree(), where + ": browser can be opened");
        _problems.AddRange(LayoutCheck.Problems(_options!, _sizes[_size].X).Select(p => where + ": " + p));
        _problems.AddRange(LayoutCheck.BoundsProblems(_options!, _sizes[_size]).Select(p => where + ": " + p));
        if (page.FindChild("SettingsList", true, false) is not ScrollContainer list)
        {
            Expect(false, where + ": settings list exists");
            return;
        }
        Expect(list.Size.Y >= 96f, where + $": settings list has only {list.Size.Y:0}px of usable height");
        float? labelLeft = null, inputLeft = null;
        Control[] rows = AllRows(page).Where(c => c.IsVisibleInTree()).ToArray();
        foreach (Control row in rows)
        {
            string key = KeyOf(row);
            var label = row.FindChild("SettingLabel", true, false) as Label;
            var input = row.FindChild("SettingInput", true, false) as Control;
            Expect(label is not null && input is not null, where + ": " + key + " has a setting label and input");
            if (label is null || input is null) continue;
            Rect2 bounds = row.GetGlobalRect(), labelBounds = label.GetGlobalRect(), inputBounds = input.GetGlobalRect();
            Expect(bounds.Grow(1).Encloses(labelBounds) && bounds.Grow(1).Encloses(inputBounds), where + ": " + key + " fits its row");
            if (inputBounds.Position.Y < labelBounds.End.Y - 1f)
            {
                Expect(labelBounds.End.X <= inputBounds.Position.X + 1f, where + ": " + key + " label clears its input");
                if (inputLeft is { } previous) Expect(Math.Abs(inputBounds.Position.X - previous) <= 1f, where + ": input columns align");
                inputLeft = inputBounds.Position.X;
            }
            else Expect(labelBounds.End.Y <= inputBounds.Position.Y + 1f, where + ": " + key + " stacked label clears its input");
            if (labelLeft is { } prior) Expect(Math.Abs(labelBounds.Position.X - prior) <= 1f, where + ": setting labels align");
            labelLeft = labelBounds.Position.X;
        }
        if (rows.Length > 0 && list.ScrollVertical == 0)
        {
            Control first = rows[0];
            if (first.Size.Y <= list.Size.Y)
                Expect(first.GetGlobalRect().End.Y <= list.GetGlobalRect().End.Y + 1f, where + ": first setting is fully visible");
            else if (first.FindChild("SettingLabel", true, false) is Control heading)
                Expect(heading.GetGlobalRect().End.Y <= list.GetGlobalRect().End.Y + 1f, where + ": first compound editor has a visible heading");
        }
    }

    private void CheckDetails(Control page, string where)
    {
        var title = page.FindChild("SettingsDetailsTitle", true, false) as Label;
        var text = page.FindChild("SettingsDetailsText", true, false) as Label;
        Expect(title is not null && text is not null, where + ": contextual help exists");
        foreach (Control row in AllRows(page).Where(c => c.IsVisibleInTree()).Take(2))
        {
            if (row.FindChild("SettingLabel", true, false) is not Label label ||
                row.FindChild("SettingInput", true, false) is not Control input) continue;
            Control target = input.FindChildren("*", nameof(Control), true, false).OfType<Control>()
                .FirstOrDefault(c => c.FocusMode != Control.FocusModeEnum.None) ?? input;
            target.EmitSignal(Control.SignalName.FocusEntered);
            Expect(title?.Text == label.Text && !string.IsNullOrWhiteSpace(text?.Text), where + ": focusing " + KeyOf(row) + " explains that setting");
            row.EmitSignal(Control.SignalName.MouseEntered);
            Expect(title?.Text == label.Text && !string.IsNullOrWhiteSpace(text?.Text), where + ": hovering " + KeyOf(row) + " explains that setting");
        }
    }

    private void CheckStagedControls(OptionsMenu options)
    {
        string original = SettingsStore.Current.ToJson();
        bool voice = options.Editing.VoiceMuted, rumble = options.Editing.Pad.Rumble, crosshair = options.Editing.Crosshair.Custom;
        double sound = ((HSlider)options.Controls["sound_volume"]).Value;
        ((HSlider)options.Controls["sound_volume"]).Value = 37;
        ((CheckBox)options.Controls["voice_muted"]).ButtonPressed = !voice;
        ((CheckBox)options.Controls["controller.rumble"]).ButtonPressed = !rumble;
        ((CheckBox)options.Controls["crosshair.custom"]).ButtonPressed = !crosshair;
        Expect(Math.Abs(options.Editing.SoundVolume - .37f) < .001f && options.Editing.VoiceMuted == !voice &&
            options.Editing.Pad.Rumble == !rumble && options.Editing.Crosshair.Custom == !crosshair,
            "existing audio, voice, controller and crosshair setting IDs keep their callbacks");
        Expect(options.HasUnsavedChanges && SettingsStore.Current.ToJson() == original, "settings-browser edits stay in the draft");
        ((HSlider)options.Controls["sound_volume"]).Value = sound;
        ((CheckBox)options.Controls["voice_muted"]).ButtonPressed = voice;
        ((CheckBox)options.Controls["controller.rumble"]).ButtonPressed = rumble;
        ((CheckBox)options.Controls["crosshair.custom"]).ButtonPressed = crosshair;
        Expect(!options.HasUnsavedChanges && SettingsStore.Current.ToJson() == original, "restoring controls leaves a clean draft without writing settings");
    }

    private void CheckHudEditor(OptionsMenu options)
    {
        var panel = options.FindChildren("*", nameof(Control), true, false).OfType<HudEditorPanel>().FirstOrDefault();
        Expect(panel is not null, "HUD editor browser exists");
        if (panel is null) return;
        HudEditorSettings original = options.Editing.HudLayout.Clone();
        string applied = SettingsStore.Current.ToJson();
        string Snapshot(object value) => System.Text.Json.JsonSerializer.Serialize(value);
        try
        {
            Show(panel);
            var search = (LineEdit)panel.FindChild("HudEditorSearch", true, false)!;
            var elements = (OptionButton)panel.FindChild("HudEditorElement", true, false)!;
            var role = panel.FindChild("HudEditorRole", true, false) as OptionButton;
            var aspect = panel.FindChild("HudEditorAspect", true, false) as OptionButton;
            var preview = panel.FindChild("HudEditorPreview", true, false) as HudEditorPreview;
            Expect(role is not null && aspect is not null && preview is not null, "HUD editor keeps role and aspect controls and its preview");
            if (role is null || aspect is null || preview is null) return;
            void Pick(OptionButton picker, int index)
            {
                picker.Select(index);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
            }
            void Search(string query)
            {
                search.Text = query;
                search.EmitSignal(LineEdit.SignalName.TextChanged, query);
            }
            Pick(role, 0);
            Search("Radar");
            Expect(elements.ItemCount == 1 && elements.GetItemMetadata(0).AsString() == "Radar", "HUD search finds the radar by its stable metadata");
            Expect(elements.Selected == 0, "HUD search selects its matching element");
            Search("no-such-hud-element-9f21");
            Expect(elements.ItemCount == 0 && elements.Disabled, "HUD unmatched search disables the empty element selector");
            Search("");
            Expect(elements.ItemCount == HudEditorSettings.Elements.Count && !elements.Disabled, "clearing HUD search restores all elements");
            int selected = Enumerable.Range(0, elements.ItemCount).First(i => elements.GetItemMetadata(i).AsString() == "Location");
            Pick(elements, selected);
            Expect(elements.GetItemMetadata(elements.Selected).AsString() == "Location" &&
                panel.FindChild("HudEditorSelectedElement", true, false) is Label heading && heading.Text == HudEditorSettings.Elements["Location"],
                "HUD dropdown metadata selects the matching inspector element");
            SpinBox Field(string title) => panel.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>()
                .First(input => input.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == title));
            SpinBox x = Field("X offset %"), scale = Field("Size %");
            var before = options.Editing.HudLayout.Clone();
            float previousScale = before.Spy.GetValueOrDefault("Location")?.Scale ?? 1f;
            x.Value = 12.3;
            scale.Value = 125;
            HudElementSettings location = options.Editing.HudLayout.Spy["Location"];
            Expect(Math.Abs(location.X - .123f) < .001f && Math.Abs(location.Scale - 1.25f) < .001f, "HUD position and size fields edit the selected element");
            Expect(Snapshot(options.Editing.HudLayout.Merc) == Snapshot(before.Merc) &&
                Snapshot(options.Editing.HudLayout.Spy.Where(p => p.Key != "Location").ToDictionary()) ==
                Snapshot(before.Spy.Where(p => p.Key != "Location").ToDictionary()), "HUD inspector changes leave other elements and the other role unchanged");
            Button History(string text) => panel.FindChildren("*", nameof(Button), true, false).OfType<Button>()
                .Single(button => button.Text == text && button.Visible);
            History("Undo").EmitSignal(BaseButton.SignalName.Pressed);
            Expect(Math.Abs(options.Editing.HudLayout.Spy["Location"].Scale - previousScale) < .001f &&
                Math.Abs(options.Editing.HudLayout.Spy["Location"].X - .123f) < .001f, "HUD Undo restores only the last inspector edit");
            History("Redo").EmitSignal(BaseButton.SignalName.Pressed);
            Expect(Math.Abs(options.Editing.HudLayout.Spy["Location"].Scale - 1.25f) < .001f, "HUD Redo restores the inspector size edit");
            string edited = Snapshot(options.Editing.HudLayout);
            Pick(role, 1);
            Pick(aspect, 2);
            Expect(Math.Abs(x.Value - (before.Merc.GetValueOrDefault("Location")?.X ?? 0f) * 100) < .001,
                "HUD role selection loads the other role's element values");
            Pick(role, 0);
            Pick(aspect, 1);
            Expect(elements.GetItemMetadata(elements.Selected).AsString() == "Location" && Math.Abs(preview.Aspect - 4f / 3f) < .001f &&
                Math.Abs(x.Value - 12.3) < .001 && Snapshot(options.Editing.HudLayout) == edited,
                "HUD role and aspect changes preserve selected element and both layouts");
            x.EmitSignal(Control.SignalName.FocusEntered);
            Expect(panel.FindChild("HudEditorHelp", true, false) is Label help && help.Text.Contains("horizontally", StringComparison.Ordinal),
                "HUD inspector focus explains the selected property");
            Expect(options.HasUnsavedChanges && SettingsStore.Current.ToJson() == applied, "HUD browser edits remain staged");
        }
        finally
        {
            options.Editing.HudLayout = original;
            if (panel.FindChild("HudEditorRole", true, false) is OptionButton role)
            {
                role.Select(0);
                role.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            }
            Expect(!options.HasUnsavedChanges && SettingsStore.Current.ToJson() == applied, "HUD smoke restores both layouts without applying them");
        }
    }

    private void CheckHudMultiSelection(HudEditorPanel panel)
    {
        var original = _options!.Editing.HudLayout.Clone();
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(_options.Editing.HudLayout);
        var preview = (HudEditorPreview)panel.FindChild("HudEditorPreview", true, false)!;
        var rows = (Control)panel.FindChild("HudEditorCategoryRows", true, false)!;
        var search = (LineEdit)panel.FindChild("HudEditorSearch", true, false)!;
        search.Text = "";
        search.EmitSignal(LineEdit.SignalName.TextChanged, "");
        Button Row(string id) => rows.GetChildren().OfType<Button>().Single(b => b.Text == HudEditorSettings.Elements[id]);
        void Click(string id, bool ctrl = false)
        {
            Button row = Row(id);
            row.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, CtrlPressed = ctrl });
            row.EmitSignal(BaseButton.SignalName.Pressed);
        }
        void History(string text) => panel.FindChildren("*", nameof(Button), true, false).OfType<Button>()
            .Single(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
        try
        {
            var layout = _options.Editing.HudLayout;
            layout.Spy["Vie"] = new HudElementSettings { X = .02f, Y = .03f, Scale = .9f, Opacity = .7f };
            layout.Spy["Radar"] = new HudElementSettings { X = .15f, Y = .12f, Scale = 1.1f, Opacity = .8f };
            Click("Vie"); Click("Radar", ctrl: true);
            Expect(Row("Vie").ButtonPressed && Row("Radar").ButtonPressed && rows.GetChildren().OfType<Button>().Count(b => b.ButtonPressed) == 2,
                "Ctrl-click in the HUD list keeps both elements selected");
            Expect(((Label)panel.FindChild("HudEditorSelectedElement", true, false)!).Text.Contains("2 elements selected", StringComparison.Ordinal),
                "HUD inspector confirms a multiple selection");
            string beforeMove = Snapshot();
            preview._GuiInput(new InputEventKey { Pressed = true, Keycode = Key.Right });
            Expect(Math.Abs(layout.Spy["Vie"].X - .021f) < .00001f && Math.Abs(layout.Spy["Radar"].X - .151f) < .00001f,
                "arrow nudges move all selected HUD elements by the same amount");
            History("Undo"); Expect(Snapshot() == beforeMove, "one Undo restores the complete group nudge");
            History("Redo");
            string beforeReset = Snapshot();
            Row("Radar").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
            var menu = (PopupMenu)panel.FindChild("HudEditorElementMenu", true, false)!;
            Expect(menu.Visible && menu.GetItemText(0) == "2 elements selected", "right-click preserves the HUD group and confirms the reset target");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, 0L); menu.Hide();
            Expect(!_options.Editing.HudLayout.Spy.ContainsKey("Vie") && !_options.Editing.HudLayout.Spy.ContainsKey("Radar"),
                "context Reset restores every selected HUD element");
            History("Undo"); Expect(Snapshot() == beforeReset, "one Undo restores the complete group reset and keeps other layouts intact");

            // Use a real drawn hit area, including the smallest-area rule for overlapping elements.
            var target = preview.ElementBounds.Values.Where(r => r.Size.X > 0 && r.Size.Y > 0)
                .Select(r => r.GetCenter()).Where(p => preview.ScreenRect.HasPoint(p))
                .Select(p => (Point: p, Id: preview.ElementBounds.Where(e => e.Value.Grow(4).HasPoint(p)).OrderBy(e => e.Value.Area).First().Key))
                .First(t => t.Id is not "Vie" and not "Radar");
            preview._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = target.Point, CtrlPressed = true });
            Expect(Row(target.Id).ButtonPressed && Row("Vie").ButtonPressed && Row("Radar").ButtonPressed,
                "Ctrl-click in the HUD preview adds the hit element without replacing the group");
            string beforeDrag = Snapshot();
            var dragLayout = _options.Editing.HudLayout.Clone();
            preview._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = target.Point });
            preview._GuiInput(new InputEventMouseMotion { Relative = new Vector2(12, 9) });
            preview._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left, Position = target.Point + new Vector2(12, 9) });
            Vector2 Offset(HudEditorSettings settings, string id)
            {
                var e = settings.Spy.GetValueOrDefault(id) ?? new HudElementSettings();
                return new Vector2(e.X, e.Y);
            }
            Vector2 moved = Offset(_options.Editing.HudLayout, target.Id) - Offset(dragLayout, target.Id);
            Expect(moved.Length() > 0 && new[] { "Vie", "Radar" }.All(id => Row(id).ButtonPressed &&
                (Offset(_options.Editing.HudLayout, id) - Offset(dragLayout, id)).IsEqualApprox(moved)),
                "dragging a selected HUD element moves the group and grid snapping preserves relative offsets");
            History("Undo"); Expect(Snapshot() == beforeDrag, "one Undo restores the complete group drag");
            string beforeDeselect = Snapshot();
            preview._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = target.Point, CtrlPressed = true });
            preview._GuiInput(new InputEventMouseMotion { Relative = new Vector2(20, 20) });
            Expect(!Row(target.Id).ButtonPressed && Row("Vie").ButtonPressed && Row("Radar").ButtonPressed && Snapshot() == beforeDeselect,
                "Ctrl-click toggles a preview element off without dragging or snapping the remaining group");
            Click("Radar", ctrl: true); Click("Vie", ctrl: true);
            Expect(!rows.GetChildren().OfType<Button>().Any(b => b.ButtonPressed) &&
                ((CheckBox)panel.FindChild("HudEditorVisible", true, false)!).Disabled, "Ctrl-click can clear the HUD selection and disables its inspector");
            Click("Location");
            Expect(Row("Location").ButtonPressed && rows.GetChildren().OfType<Button>().Count(b => b.ButtonPressed) == 1,
                "an ordinary click replaces the HUD selection with one element");
        }
        finally
        {
            _options.Editing.HudLayout = original;
            Click("Location");
        }
    }

    private void Expect(bool condition, string message) { if (!condition) _problems.Add(message); }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        tree.Root.Size = size;
    }
}
