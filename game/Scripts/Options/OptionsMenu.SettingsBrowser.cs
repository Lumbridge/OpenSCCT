using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private readonly Dictionary<string, (int Category, string Search)> _settingsBrowsing = new();

    private static VSeparator SettingsColumnDivider(string name)
    {
        var divider = new VSeparator { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        divider.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = UiStyle.MenuFocus with { A = .3f }, Thickness = 1, Vertical = true });
        return divider;
    }

    private static MarginContainer SettingsSectionHeading(string name, string title, bool first)
    {
        var section = new MarginContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("margin_top", first ? 2 : 8);
        section.AddThemeConstantOverride("margin_bottom", 4);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 10);
        heading.AddChild(new Label { Text = title, Modulate = UiStyle.MenuFocus });
        var rule = new HSeparator { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = UiStyle.MenuFocus with { A = .3f }, Thickness = 1 });
        heading.AddChild(rule);
        section.AddChild(heading);
        return section;
    }

    /// <summary>Add an entire custom editor as one searchable entry in the current category.</summary>
    private static void SettingsPanel(GridContainer grid, string key, string label, string description, Control content)
    {
        content.SetMeta("options_panel", true);
        content.SetMeta("options_key", key);
        content.SetMeta("options_label", label);
        content.SetMeta("options_description", description);
        grid.AddChild(content);
    }

    /// <summary>Give existing controls a common browser while retaining their draft callbacks and live state.</summary>
    private Control CreateSettingsEditor(string name, GridContainer source, string? stateKey = null)
    {
        string identity = stateKey ?? name;
        var entries = new List<(int Category, string Key, string Label, string Description, Control[] Cells, bool Panel)>();
        var categories = new List<string>();
        string description = "";
        int group = -1;
        Node[] children = source.GetChildren().ToArray();
        for (int index = 0; index < children.Length;)
        {
            Node child = children[index];
            if (child.HasMeta("options_section"))
            {
                categories.Add(child.GetChild<Label>(0).Text);
                group = categories.Count - 1;
                description = child.GetChildren().OfType<Label>().Skip(1).FirstOrDefault()?.Text ?? "";
                index++;
                continue;
            }
            if (group < 0) { categories.Add("General"); group = 0; }
            if (child is Control panel && child.HasMeta("options_panel"))
            {
                source.RemoveChild(child);
                entries.Add((group, panel.GetMeta("options_key").AsString(), panel.GetMeta("options_label").AsString(),
                    panel.GetMeta("options_description").AsString(), [panel], true));
                index++;
                continue;
            }
            var cells = children.Skip(index).Take(3).Cast<Control>().ToArray();
            if (cells.Length != 3 || cells[0] is not Label heading)
                throw new InvalidOperationException("Incomplete settings row in " + identity);
            foreach (Control cell in cells) source.RemoveChild(cell);
            string key = _controls.FirstOrDefault(pair => pair.Value == cells[1]).Key ?? cells[1].Name.ToString();
            if (key.Length == 0 || key.StartsWith('@')) key = identity + "." + index;
            string help = cells[1].TooltipText;
            if (help.Length == 0) help = description.Length > 0 ? description : heading.Text + ". Changes take effect when you Apply or Save & Close.";
            entries.Add((group, key, heading.Text, help, cells, false));
            index += 3;
        }
        _settingsGrids.Remove(source);
        source.Free();

        var state = _settingsBrowsing.GetValueOrDefault(identity, (Category: -1, Search: ""));
        int selected = Math.Clamp(state.Category, -1, categories.Count - 1);
        var page = new VBoxContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        page.SetMeta("settings_browser", true);
        page.SetMeta("settings_page", identity);
        page.AddThemeConstantOverride("separation", 8);
        var toolbar = new HBoxContainer();
        var search = new LineEdit
        {
            Name = "SettingsSearch", PlaceholderText = "Find a setting…", ClearButtonEnabled = true, Text = state.Search,
            CustomMinimumSize = new Vector2(100, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        toolbar.AddChild(search);
        var category = new OptionButton { Name = "SettingsCategory", FitToLongestItem = false, ClipText = true, CustomMinimumSize = new Vector2(220, 32) };
        category.AddItem("All");
        foreach (string label in categories) category.AddItem(label);
        toolbar.AddChild(category);
        page.AddChild(toolbar);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        var sidebar = new ScrollContainer
        {
            Name = "SettingsCategoryList", CustomMinimumSize = new Vector2(190, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var navigation = new VBoxContainer { Name = "SettingsCategories", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        navigation.AddThemeConstantOverride("separation", 2);
        sidebar.AddChild(navigation);
        var categoryMargin = new MarginContainer { Name = "SettingsCategoryMargin" };
        categoryMargin.AddThemeConstantOverride("margin_top", 6);
        categoryMargin.AddChild(sidebar);
        body.AddChild(categoryMargin);
        var columnDivider = SettingsColumnDivider("SettingsColumnDivider");
        body.AddChild(columnDivider);
        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = new Label { Name = "SettingsHeading" };
        title.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        main.AddChild(title);
        var list = new ScrollContainer
        {
            Name = "SettingsList", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var rows = new VBoxContainer { Name = "SettingsRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        list.AddChild(rows);
        main.AddChild(list);
        body.AddChild(main);
        page.AddChild(body);
        page.AddChild(new HSeparator());
        var details = new VBoxContainer { Name = "SettingsDetails" };
        var detailTitle = new Label { Name = "SettingsDetailsTitle" };
        detailTitle.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        var detailText = new Label
        {
            Name = "SettingsDetailsText", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44), MaxLinesVisible = 3, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        detailText.AddThemeColorOverride("font_color", UiStyle.Text);
        details.AddChild(detailTitle);
        details.AddChild(detailText);
        page.AddChild(details);
        var panels = new List<(PanelContainer Panel, BoxContainer Row)>();
        var sectionHeadings = new Dictionary<int, MarginContainer>();
        PanelContainer? highlighted = null;
        StyleBoxFlat RowStyle(bool active) => new()
        {
            BgColor = active ? new Color(.06f, .13f, .08f, .8f) : new Color(.025f, .045f, .03f, .55f),
            BorderColor = active ? UiStyle.MenuFocus : new Color(.19f, .3f, .22f, .45f),
            BorderWidthBottom = 1, BorderWidthLeft = active ? 2 : 0,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        void Explain(int index)
        {
            PanelContainer panel = panels[index].Panel;
            if (highlighted != panel)
            {
                highlighted?.AddThemeStyleboxOverride("panel", RowStyle(false));
                highlighted = panel;
                panel.AddThemeStyleboxOverride("panel", RowStyle(true));
            }
            detailTitle.Text = entries[index].Label;
            detailText.Text = entries[index].Description;
            detailText.TooltipText = detailText.Text;
        }
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!sectionHeadings.ContainsKey(entry.Category))
            {
                var section = SettingsSectionHeading("SettingsSection_" + entry.Category, categories[entry.Category], sectionHeadings.Count == 0);
                sectionHeadings.Add(entry.Category, section);
                rows.AddChild(section);
            }
            var panel = new PanelContainer { Name = "Setting_" + Safe(entry.Key), MouseFilter = MouseFilterEnum.Pass };
            panel.SetMeta("options_setting", entry.Key);
            panel.AddThemeStyleboxOverride("panel", RowStyle(false));
            var row = new BoxContainer { Name = "SettingRow" };
            row.AddThemeConstantOverride("separation", 16);
            var label = entry.Panel ? new Label { Text = entry.Label } : (Label)entry.Cells[0];
            label.Name = "SettingLabel";
            label.CustomMinimumSize = Vector2.Zero;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.SizeFlagsStretchRatio = 1f;
            label.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            label.AddThemeColorOverride("font_color", UiStyle.Text);
            row.AddChild(label);
            var input = new HBoxContainer { Name = "SettingInput", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.7f };
            input.AddThemeConstantOverride("separation", 10);
            foreach (Control control in entry.Panel ? entry.Cells : entry.Cells.Skip(1))
            {
                float minimum = control is SpinBox ? 100 : control is NotchedSlider ? 100 : control is Label value && value.Text.Length > 0 ? 64 : 0;
                if (control.Name == "MicState") minimum = 104;
                control.CustomMinimumSize = new Vector2(minimum, control.CustomMinimumSize.Y);
                if (control is Label readout)
                {
                    readout.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    readout.SizeFlagsHorizontal = entry.Panel || control == entry.Cells[1] ? SizeFlags.ExpandFill : SizeFlags.ShrinkEnd;
                }
                if (entry.Panel || control == entry.Cells[1] && control is not Label) control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                if (control is CheckBox) control.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                if (control is Button button) button.ClipText = true;
                if (control is not Container && control.TooltipText.Length == 0) control.TooltipText = entry.Description;
                input.AddChild(control);
            }
            row.AddChild(input);
            panel.AddChild(row);
            rows.AddChild(panel);
            panels.Add((panel, row));
            int captured = index;
            panel.MouseEntered += () => Explain(captured);
            foreach (Control control in panel.FindChildren("*", nameof(Control), true, false).OfType<Control>())
            {
                control.FocusEntered += () => Explain(captured);
                control.MouseEntered += () => Explain(captured);
            }
        }
        var empty = new Label { Name = "SettingsEmpty", Text = "No settings match your search.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rows.AddChild(empty);
        var buttons = new List<Button>();
        void Filter()
        {
            string query = search.Text.Trim();
            _settingsBrowsing[identity] = (selected, search.Text);
            int activeCategory = query.Length > 0 ? -1 : selected;
            category.Select(activeCategory + 1);
            title.Text = query.Length > 0 ? "Search results" : selected < 0 ? "All" : categories[selected];
            for (int i = 0; i < buttons.Count; i++) buttons[i].SetPressedNoSignal(i == activeCategory + 1);
            foreach (MarginContainer section in sectionHeadings.Values) section.Hide();
            var visibleCategories = new HashSet<int>();
            int first = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool visible = query.Length == 0 ? selected < 0 || entry.Category == selected :
                    (entry.Label + " " + entry.Key + " " + entry.Description + " " + categories[entry.Category]).Contains(query, StringComparison.OrdinalIgnoreCase);
                panels[i].Panel.Visible = visible;
                if (visible && (selected < 0 || query.Length > 0) && visibleCategories.Add(entry.Category))
                {
                    MarginContainer section = sectionHeadings[entry.Category];
                    section.AddThemeConstantOverride("margin_top", visibleCategories.Count == 1 ? 2 : 8);
                    section.Show();
                }
                if (first < 0 && visible) first = i;
            }
            empty.Visible = first < 0;
            list.ScrollVertical = 0;
            if (first >= 0) Explain(first);
            else { detailTitle.Text = "Find a setting"; detailText.Text = "Search by a setting name or choose a category."; detailText.TooltipText = detailText.Text; }
        }
        void Choose(int index)
        {
            selected = index;
            search.Text = "";
            Filter();
        }
        for (int i = -1; i < categories.Count; i++)
        {
            int captured = i;
            string label = i < 0 ? "All" : categories[i];
            var button = QuietBindingButton(label, i < 0 ? "SettingsCategory_All" : "SettingsCategory_" + i);
            button.ToggleMode = true;
            button.CustomMinimumSize = new Vector2(0, 36);
            button.TooltipText = label;
            button.Pressed += () => Choose(captured);
            navigation.AddChild(button);
            buttons.Add(button);
        }
        category.ItemSelected += index => Choose((int)index - 1);
        search.TextChanged += _ => Filter();
        void Fit()
        {
            bool narrow = page.Size.X < 900;
            categoryMargin.Visible = !narrow;
            columnDivider.Visible = !narrow;
            category.Visible = narrow;
            title.Visible = !narrow;
            for (int i = 0; i < entries.Count; i++) panels[i].Row.Vertical = entries[i].Panel || main.Size.X < 620;
            bool compact = page.Size.Y < 320;
            detailTitle.Visible = !compact;
            detailText.MaxLinesVisible = compact ? 2 : 3;
        }
        page.Resized += Fit;
        main.Resized += Fit;
        page.VisibilityChanged += Fit;
        Filter();
        Fit();
        return page;
    }
}
