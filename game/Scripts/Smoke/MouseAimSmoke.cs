using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Mouse and aim categories, search, aligned setting rows and staged edits at desktop and compact sizes.</summary>
public sealed class MouseAimSmoke : SmokeTest
{
    public const string TestName = "mouse-aim";
    public override string Name => TestName;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes = [new(1280, 720), new(800, 600), new(3840, 2160), new(800, 600)];
    private readonly string[] _categories = ["All", "Sensitivity", "Scope & cameras", "Modifiers", "View"];
    private readonly string[] _queries = ["fps", "fov", "sensitivity"];
    private OptionsMenu? _options;
    private int _size, _role, _case, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        SceneTree tree = context.Tree;
        if (_options is null)
        {
            // Apply is exercised only in the smoke runner's in-memory settings sandbox.
            if (!SettingsStore.Isolated) return SmokeResult.Fail("mouse-aim requires isolated smoke settings");
            var behaviorMenu = NewMenu(tree);
            CheckBehavior(behaviorMenu);
            behaviorMenu.QueueFree();
            _options = NewMenu(tree);
            Resize(tree, _sizes[_size]);
        }

        Control? page = MousePage(_options, _role);
        if (page is null) return SmokeResult.Fail("role Mouse & Aim page is missing");
        if (++_frames == 1)
        {
            _options.Tabs!.CurrentTab = _role;
            var sections = _options.Tabs.GetCurrentTabControl().GetNode<TabContainer>("Sections");
            sections.CurrentTab = page.GetIndex();
            var category = page.FindChild("MouseAimCategory", true, false) as OptionButton;
            var search = page.FindChild("MouseAimSearch", true, false) as LineEdit;
            if (category is null || search is null) return SmokeResult.Fail("Mouse & Aim category or search control is missing");
            Expect(category.ItemCount == _categories.Length, "Mouse & Aim has All and four setting categories");
            for (int i = 0; i < Math.Min(category.ItemCount, _categories.Length); i++)
                Expect(category.GetItemText(i) == _categories[i], $"category {i} has its expected label");
            search.Text = "";
            search.EmitSignal(LineEdit.SignalName.TextChanged, "");
            int selected = _case < _categories.Length ? _case : 0;
            category.Select(selected);
            category.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selected);
            if (_case >= _categories.Length)
            {
                string query = _queries[_case - _categories.Length];
                search.Text = query;
                search.EmitSignal(LineEdit.SignalName.TextChanged, query);
            }
        }
        if (_frames < 8) return SmokeResult.Running;

        string label = _case < _categories.Length ? _categories[_case] : "search " + _queries[_case - _categories.Length];
        string where = $"{_sizes[_size].X}x{_sizes[_size].Y} {(_role == 0 ? "Spy" : "Mercenary")} {label}";
        CheckLayout(page, where);
        CheckSearch(page, where);
        CheckExplanation(page, where);
        MenuPage.Shot(tree.Root, $"mouse-aim-{_size}-{_sizes[_size].X}x{_sizes[_size].Y}-{_role}-{_case}");

        _frames = 0;
        if (++_case < _categories.Length + _queries.Length) return SmokeResult.Running;
        _case = 0;
        if (++_role < 2) return SmokeResult.Running;
        _role = 0;
        if (++_size < _sizes.Length) { Resize(tree, _sizes[_size]); return SmokeResult.Running; }
        return _problems.Count == 0
            ? SmokeResult.Pass("both roles, all mouse/aim categories and search, contextual help, typed sensitivity, shared inversion and isolated apply/reset/discard at 800x600 through 4K and shrinking back")
            : SmokeResult.Fail(string.Join("; ", _problems.Distinct().Take(16)));
    }

    private static OptionsMenu NewMenu(SceneTree tree)
    {
        var menu = new OptionsMenu { Overlay = true };
        tree.Root.AddChild(menu);
        tree.CurrentScene = menu;
        return menu;
    }

    private static Control? MousePage(OptionsMenu options, int role) => options.Tabs?.GetTabControl(role)
        .GetNodeOrNull<TabContainer>("Sections")?.GetChildren().OfType<Control>().FirstOrDefault(c => c.Name == "Mouse & Aim");

    private static Control[] VisibleRows(Control page) => page.FindChildren("*", nameof(Control), true, false)
        .OfType<Control>().Where(c => c.HasMeta("mouse_aim_setting") && c.IsVisibleInTree()).ToArray();

    private void CheckLayout(Control page, string where)
    {
        _problems.AddRange(LayoutCheck.Problems(_options!, _sizes[_size].X).Select(p => where + ": " + p));
        _problems.AddRange(LayoutCheck.BoundsProblems(_options!, _sizes[_size]).Select(p => where + ": " + p));
        if (page.FindChild("MouseAimList", true, false) is not ScrollContainer list)
        {
            Expect(false, where + ": setting list is missing");
            return;
        }
        Expect(list.Size.Y >= 96f, where + $": setting list has only {list.Size.Y:0}px of visible height");
        Control[] rows = VisibleRows(page);
        if (_case < _categories.Length) Expect(rows.Length > 0, where + ": category contains no settings");
        float? labelLeft = null, inputLeft = null;
        foreach (Control row in rows)
        {
            string key = row.GetMeta("mouse_aim_setting").AsString();
            var label = row.FindChild("SettingLabel", true, false) as Label;
            var input = row.FindChild("SettingInput", true, false) as Control;
            Expect(label is not null && input is not null, where + ": " + key + " has a label and input");
            if (label is null || input is null) continue;
            Rect2 bounds = row.GetGlobalRect(), labelBounds = label.GetGlobalRect(), inputBounds = input.GetGlobalRect();
            Expect(bounds.Grow(1).Encloses(labelBounds) && bounds.Grow(1).Encloses(inputBounds), where + ": " + key + " contents fit their row");
            // Narrow pages may stack the input below its label; wide pages keep all input starts aligned.
            if (inputBounds.Position.Y < labelBounds.End.Y - 1f)
            {
                Expect(labelBounds.End.X <= inputBounds.Position.X + 1f, where + ": " + key + " label overlaps its input");
                if (inputLeft is { } priorInput) Expect(Math.Abs(priorInput - inputBounds.Position.X) <= 1f, where + ": input columns align");
                inputLeft = inputBounds.Position.X;
            }
            else Expect(labelBounds.End.Y <= inputBounds.Position.Y + 1f, where + ": " + key + " stacked input clears its label");
            if (labelLeft is { } priorLabel) Expect(Math.Abs(priorLabel - labelBounds.Position.X) <= 1f, where + ": setting labels align");
            labelLeft = labelBounds.Position.X;
        }
        if (rows.Length > 0 && list.ScrollVertical == 0)
            Expect(rows[0].GetGlobalRect().End.Y <= list.GetGlobalRect().End.Y + 1f, where + ": first setting is fully visible without scrolling");
    }

    private void CheckSearch(Control page, string where)
    {
        if (_case < _categories.Length) return;
        string[] keys = VisibleRows(page).Select(r => r.GetMeta("mouse_aim_setting").AsString()).ToArray();
        string prefix = _role == 0 ? "controls_spy_kbm" : "controls_merc_kbm";
        switch (_queries[_case - _categories.Length])
        {
            case "fps": Expect(keys.Length == 0, where + ": role frame rate belongs to Performance instead of Mouse & Aim"); break;
            case "fov": Expect(_role == 0 ? keys.Length == 0 : keys.Contains("ws_fov"), where + ": search respects role-specific field of view"); break;
            case "sensitivity": Expect(keys.Contains(prefix + ".look"), where + ": search finds look sensitivity"); break;
        }
    }

    private void CheckExplanation(Control page, string where)
    {
        var title = page.FindChild("MouseAimDetailsTitle", true, false) as Label;
        var text = page.FindChild("MouseAimDetailsText", true, false) as Label;
        Expect(title is not null && text is not null, where + ": contextual help exists");
        foreach (Control row in VisibleRows(page).Take(2))
        {
            string key = row.GetMeta("mouse_aim_setting").AsString();
            if (!_options!.Controls.TryGetValue(key, out Control? control)) continue;
            control.EmitSignal(Control.SignalName.FocusEntered);
            Expect(!string.IsNullOrWhiteSpace(title?.Text) && !string.IsNullOrWhiteSpace(text?.Text), where + ": focusing " + key + " explains the setting");
            if (row.FindChild("SettingLabel", true, false) is Label label)
                Expect(title?.Text == label.Text, where + ": focusing " + key + " selects its own explanation");
            control.EmitSignal(Control.SignalName.MouseEntered);
            Expect(!string.IsNullOrWhiteSpace(text?.Text), where + ": hovering " + key + " keeps contextual help");
        }
    }

    private void CheckBehavior(OptionsMenu options)
    {
        string original = SettingsStore.Current.ToJson();
        int writes = 0;
        void Saved(GameSettings _) => writes++;
        SettingsStore.Saved += Saved;
        try
        {
            Expect(!options.HasUnsavedChanges, "fresh mouse/aim draft is clean");
            bool inverted = !options.Editing.InvertY;
            ((CheckBox)options.Controls["invert_y.Spy"]).ButtonPressed = inverted;
            Expect(options.Editing.InvertY == inverted && ((CheckBox)options.Controls["invert_y.Merc"]).ButtonPressed == inverted,
                "vertical inversion synchronizes both role controls");
            foreach (string prefix in new[] { "controls_spy_kbm", "controls_merc_kbm" })
            {
                Control slider = options.Controls[prefix + ".look"];
                var row = AncestorSetting(slider);
                var number = row?.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>().FirstOrDefault();
                Expect(number is not null, prefix + " has an exact sensitivity field");
                if (number is null) continue;
                number.Value = 1.234;
                Expect(Math.Abs(((HSlider)slider).Value - 1.234) < 0.001, prefix + " exact field updates its slider");
            }
            Expect(Math.Abs(options.Editing.SpyKbm.Look - 1.234f) < 0.001f && Math.Abs(options.Editing.MercKbm.Look - 1.234f) < 0.001f,
                "exact sensitivity fields update both role drafts");
            Expect(options.HasUnsavedChanges && writes == 0 && SettingsStore.Current.ToJson() == original, "mouse/aim edits remain staged until Apply");
            Expect(options.Save() == Error.Ok, "isolated Apply succeeds");
            Expect(writes == 1 && !options.HasUnsavedChanges && Math.Abs(SettingsStore.Current.MercKbm.Look - 1.234f) < 0.001f,
                "Apply accepts mouse/aim edits and rebuilds a clean draft");
            string applied = SettingsStore.Current.ToJson();
            Control? mercPage = MousePage(options, 1);
            var reset = mercPage?.FindChild("ResetMouse", true, false) as BaseButton;
            Expect(reset is not null, "mouse/aim reset exists");
            if (reset is MenuButton menu) menu.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
            else reset?.EmitSignal(BaseButton.SignalName.Pressed);
            // Accept a reset confirmation if the page presents one.
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(Math.Abs(options.Editing.MercKbm.Look - new GameSettings().MercKbm.Look) < 0.001f && Math.Abs(options.Editing.SpyKbm.Look - 1.234f) < 0.001f,
                "mouse/aim reset restores only the selected role");
            Expect(options.HasUnsavedChanges && writes == 1 && SettingsStore.Current.ToJson() == applied, "mouse/aim reset remains staged");
            bool closed = false;
            options.Closed += () => closed = true;
            options.Back();
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(closed && writes == 1 && SettingsStore.Current.ToJson() == applied, "discard closes without publishing a staged reset");
        }
        finally
        {
            SettingsStore.Saved -= Saved;
            SettingsStore.Reset(GameSettings.FromJson(original));
        }
    }

    private static Control? AncestorSetting(Control control)
    {
        for (Node? parent = control; parent is not null; parent = parent.GetParent())
            if (parent is Control candidate && candidate.HasMeta("mouse_aim_setting")) return candidate;
        return null;
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
