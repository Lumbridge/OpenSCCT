using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private readonly Dictionary<PlaySide, (int Category, string Search)> _mouseAimBrowsing = new();

    private Control CreateMouseAimEditor(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        string prefix = merc ? "controls_merc_kbm" : "controls_spy_kbm";
        LookSettings Look() => _edit.LookOf(merc, false);
        string[] categories = ["Sensitivity", "Scope & cameras", "Modifiers", "View"];
        var state = _mouseAimBrowsing.GetValueOrDefault(side, (Category: -1, Search: ""));
        int selected = Math.Clamp(state.Category, -1, categories.Length - 1);
        var page = new VBoxContainer { Name = "Mouse & Aim", SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 8);
        var toolbar = new HBoxContainer();
        var search = new LineEdit
        {
            Name = "MouseAimSearch", PlaceholderText = "Find a setting…", ClearButtonEnabled = true,
            Text = state.Search, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(100, 32),
        };
        toolbar.AddChild(search);
        var reset = new MenuButton { Name = "ResetMouse", Text = "Reset…", CustomMinimumSize = new Vector2(90, 32) };
        reset.GetPopup().AddItem("Reset mouse & aim", 0);
        reset.GetPopup().IdPressed += _ => Confirm("Reset mouse & aim",
            "Restore this role's mouse sensitivity and camera settings? Changes take effect when you Apply or Save & Close.",
            () => ResetLook(side, false), "Reset mouse & aim");
        toolbar.AddChild(reset);
        page.AddChild(toolbar);

        var category = new OptionButton
        {
            Name = "MouseAimCategory", FitToLongestItem = false, ClipText = true,
            CustomMinimumSize = new Vector2(220, 32),
        };
        category.AddItem("All");
        foreach (string name in categories) category.AddItem(name);
        toolbar.AddChild(category);
        toolbar.MoveChild(category, 1);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        var sidebar = new VBoxContainer { Name = "MouseAimCategories", CustomMinimumSize = new Vector2(190, 0) };
        body.AddChild(sidebar);
        var columnDivider = SettingsColumnDivider("MouseAimColumnDivider");
        body.AddChild(columnDivider);
        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = new Label { Name = "MouseAimHeading" };
        title.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        main.AddChild(title);
        var list = new ScrollContainer
        {
            Name = "MouseAimList", FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        list.AddChild(rows);
        main.AddChild(list);
        body.AddChild(main);
        page.AddChild(body);

        page.AddChild(new HSeparator());
        var details = new VBoxContainer { Name = "MouseAimDetails" };
        var detailTitle = new Label { Name = "MouseAimDetailsTitle" };
        detailTitle.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        var detailText = new Label
        {
            Name = "MouseAimDetailsText", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44), MaxLinesVisible = 3,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        detailText.AddThemeColorOverride("font_color", UiStyle.Text);
        details.AddChild(detailTitle);
        details.AddChild(detailText);
        page.AddChild(details);

        var entries = new List<(int Category, string Key, string Label, string Description, PanelContainer Panel, BoxContainer Row)>();
        var sections = new Dictionary<int, MarginContainer>();
        var categoryButtons = new List<Button>();
        PanelContainer? highlighted = null;
        StyleBoxFlat RowStyle(bool active) => new()
        {
            BgColor = active ? new Color(.06f, .13f, .08f, .8f) : new Color(.025f, .045f, .03f, .55f),
            BorderColor = active ? UiStyle.MenuFocus : new Color(.19f, .3f, .22f, .45f),
            BorderWidthBottom = 1, BorderWidthLeft = active ? 2 : 0,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        void Explain(PanelContainer panel, string label, string description)
        {
            if (highlighted != panel)
            {
                highlighted?.AddThemeStyleboxOverride("panel", RowStyle(false));
                highlighted = panel;
                panel.AddThemeStyleboxOverride("panel", RowStyle(true));
            }
            detailTitle.Text = label;
            detailText.Text = description;
            detailText.TooltipText = description;
        }
        void Add(int group, string key, string label, string description, Action<GridContainer> build)
        {
            // Reuse the setting callbacks and typed-value synchronization, then own the complete row here.
            // Each editor keeps its own columns without affecting other settings.
            var source = new GridContainer();
            build(source);
            Node[] cells = source.GetChildren().ToArray();
            foreach (Node child in cells) source.RemoveChild(child);
            source.Free();
            var heading = (Label)cells[0];
            heading.Name = "SettingLabel";
            heading.Text = label;
            heading.CustomMinimumSize = new Vector2(0, 0);
            heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            heading.SizeFlagsStretchRatio = 1f;
            heading.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            heading.AddThemeColorOverride("font_color", UiStyle.Text);
            var input = new HBoxContainer
            {
                Name = "SettingInput", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.7f,
            };
            input.AddThemeConstantOverride("separation", 10);
            for (int i = 1; i < cells.Length; i++)
            {
                var control = (Control)cells[i];
                control.CustomMinimumSize = new Vector2(control is SpinBox ? 100 : control is NotchedSlider ? 100 : 0,
                    control.CustomMinimumSize.Y);
                input.AddChild(control);
                control.TooltipText = description;
                if (control is CheckBox) control.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            }
            var row = new BoxContainer { Name = "SettingRow" };
            row.AddThemeConstantOverride("separation", 16);
            row.AddChild(heading);
            row.AddChild(input);
            var panel = new PanelContainer { Name = "Row_" + Safe(key), MouseFilter = MouseFilterEnum.Pass };
            panel.SetMeta("mouse_aim_setting", key);
            panel.AddThemeStyleboxOverride("panel", RowStyle(false));
            panel.AddChild(row);
            if (!sections.ContainsKey(group))
            {
                var section = SettingsSectionHeading("MouseAimSection_" + group, categories[group], sections.Count == 0);
                rows.AddChild(section);
                sections.Add(group, section);
            }
            rows.AddChild(panel);
            void ShowDetails() => Explain(panel, label, description);
            panel.MouseEntered += ShowDetails;
            foreach (Control control in panel.FindChildren("*", nameof(Control), true, false).OfType<Control>())
            {
                control.FocusEntered += ShowDetails;
                control.MouseEntered += ShowDetails;
            }
            entries.Add((group, key, label, description, panel, row));
        }
        void Sensitivity(int group, string suffix, string label, string description, float value, Action<float> change, float max = 10f) =>
            Add(group, prefix + suffix, label, description,
                g => Slider(g, label, prefix + suffix, .05f, max, SensStep, value, change, SensFormat, typed: true));
        void Toggle(int group, string key, string label, string description, bool value, Action<bool> change) =>
            Add(group, key, label, description, g => Check(g, label, key, value, change));

        if (!merc) Sensitivity(0, ".free_look", "Free look sensitivity", "Mouse sensitivity with your weapon holstered. Higher values turn farther for the same mouse movement.",
            Look().FreeLook, v => Look().FreeLook = v, 30f);
        Sensitivity(0, ".look", merc ? "Look sensitivity" : "Gun look sensitivity",
            merc ? "Mouse sensitivity for normal aiming. Higher values turn farther for the same mouse movement. Type an exact value beside the slider."
                : "Mouse sensitivity with your weapon drawn. Free look sensitivity is configured separately.", Look().Look, v => Look().Look = v);
        Toggle(0, "invert_y." + side, "Invert vertical mouse movement", "Reverse vertical mouse movement. This setting is shared by Spy and Mercenary.", _edit.InvertY, v =>
        {
            _edit.InvertY = v;
            foreach (PlaySide role in new[] { PlaySide.Spy, PlaySide.Merc })
                if (_controls.TryGetValue("invert_y." + role, out Control? control) && control is CheckBox box)
                    box.SetPressedNoSignal(v);
        });
        if (!merc) Toggle(0, "invert_x_spy", "Invert horizontal free look", "Reverse horizontal mouse movement while your weapon is holstered.", _edit.InvertXSpy, v => _edit.InvertXSpy = v);

        Sensitivity(1, ".scope", merc ? "Scope sensitivity" : "Binocular sensitivity",
            "Multiplies your current mouse sensitivity while using " + (merc ? "the sniper scope." : "binoculars."), Look().Scope, v => Look().Scope = v);
        if (merc) Sensitivity(1, ".scope_vertical", "Scope vertical multiplier", "Vertical scope speed: 1 uses the original speed; 2.45 matches horizontal and vertical scope movement.",
            Look().ScopeVertical, v => Look().ScopeVertical = v);
        Sensitivity(1, ".cam", "Remote camera sensitivity", merc ? "Mouse sensitivity in remote camera views." : "Mouse sensitivity while using sticky cameras and Cam Net.",
            Look().Cam, v => Look().Cam = v);

        void Modifier(bool second)
        {
            string suffix = second ? ".clutch_two" : ".clutch_one";
            string name = second ? "Second modifier" : "Sensitivity modifier";
            Toggle(2, prefix + suffix + "_enabled", "Enable " + name.ToLowerInvariant(), "Enable a temporary sensitivity multiplier while its assigned key is held.",
                second ? Look().ClutchTwoEnabled : Look().ClutchOneEnabled,
                v => { if (second) Look().ClutchTwoEnabled = v; else Look().ClutchOneEnabled = v; });
            string key = second ? "clutch_two_key" : "clutch_one_key." + side;
            Add(2, key, name + " key", second ? "Hold this key to apply the second multiplier. Both Mercenary modifiers multiply together. Escape cancels key capture."
                    : "Hold this key to apply the multiplier. The key is shared by both roles; each role has its own strength. Escape cancels key capture.",
                g => ClutchKeyRow(g, name + " key", key, () => second ? _edit.ClutchTwoKey : _edit.ClutchOneKey,
                    v => { if (second) _edit.ClutchTwoKey = v; else _edit.ClutchOneKey = v; }));
            float multiplier = second ? Look().ClutchTwo : Look().ClutchOne;
            Add(2, prefix + suffix, name + " strength", "While held, 0.5× halves sensitivity, 1× keeps it unchanged and 2× doubles it. Release the key to restore normal speed.",
                g => Slider(g, name + " strength", prefix + suffix, .1f, 10f, .05f,
                    multiplier > 0f ? multiplier : 1f,
                    v => { if (second) Look().ClutchTwo = v; else Look().ClutchOne = v; }, typed: true));
        }
        Modifier(false);
        if (merc) Modifier(true);
        if (!merc) Toggle(3, "spy_roll_free_look", "Free camera while rolling", "Look around with the mouse or controller while rolling. The roll continues in its starting direction.",
            _edit.SpyRollFreeLook, v => _edit.SpyRollFreeLook = v);
        if (merc) Add(3, "ws_fov", "Field of view cap", "Maximum field of view for the Mercenary camera, in degrees.",
            g => Slider(g, "Field of view cap", "ws_fov", GameSettings.MinMercFov, GameSettings.MaxMercFov, 1,
                _edit.MercFovCap, v => _edit.MercFovCap = v, "0", typed: true));

        var empty = new Label { Name = "MouseAimEmpty", Text = "No settings match your search.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rows.AddChild(empty);
        void Filter()
        {
            string query = search.Text.Trim();
            _mouseAimBrowsing[side] = (selected, search.Text);
            int activeCategory = query.Length > 0 ? -1 : selected;
            title.Text = query.Length > 0 ? "Search results" : selected < 0 ? "All" : categories[selected];
            category.Select(activeCategory + 1);
            for (int i = 0; i < categoryButtons.Count; i++) categoryButtons[i].SetPressedNoSignal(activeCategory == i - 1);
            foreach (var entry in entries) entry.Panel.Visible = query.Length == 0 ? selected < 0 || entry.Category == selected :
                (entry.Label + " " + entry.Description + " " + entry.Key + " " + categories[entry.Category]).Contains(query, StringComparison.OrdinalIgnoreCase);
            bool firstSection = true;
            foreach (var (group, section) in sections)
            {
                section.Visible = activeCategory < 0 && entries.Any(e => e.Category == group && e.Panel.Visible);
                if (!section.Visible) continue;
                section.AddThemeConstantOverride("margin_top", firstSection ? 2 : 8);
                firstSection = false;
            }
            empty.Visible = !entries.Any(e => e.Panel.Visible);
            QueueScrollFocusLinks(rows);
            list.ScrollVertical = 0;
            var first = entries.FirstOrDefault(e => e.Panel.Visible);
            if (first.Panel is not null) Explain(first.Panel, first.Label, first.Description);
            else { detailTitle.Text = "Find a setting"; detailText.Text = "Search by a setting name or choose a category."; }
        }
        void SelectCategory(int index)
        {
            selected = index;
            search.Text = "";
            Filter();
        }
        for (int i = -1; i < categories.Length; i++)
        {
            int index = i;
            string label = i < 0 ? "All" : categories[i];
            var button = QuietBindingButton(label, "MouseAimCategory_" + (i < 0 ? "All" : i.ToString()));
            button.ToggleMode = true;
            button.CustomMinimumSize = new Vector2(0, 36);
            button.Pressed += () => SelectCategory(index);
            sidebar.AddChild(button);
            categoryButtons.Add(button);
        }
        category.ItemSelected += index => SelectCategory((int)index - 1);
        search.TextChanged += _ => Filter();
        void Fit()
        {
            sidebar.Visible = page.Size.X >= 900;
            columnDivider.Visible = sidebar.Visible;
            category.Visible = !sidebar.Visible;
            title.Visible = sidebar.Visible;
            foreach (var entry in entries) entry.Row.Vertical = main.Size.X < 620;
            bool compact = page.Size.Y < 320;
            detailTitle.Visible = !compact;
            detailText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detailText.ClipText = true;
            detailText.CustomMinimumSize = new Vector2(0, 44);
            detailText.MaxLinesVisible = compact ? 2 : 3;
        }
        page.Resized += Fit;
        main.Resized += Fit;
        page.VisibilityChanged += Fit;
        page.VisibilityChanged += () => QueueScrollFocusLinks(rows);
        Filter();
        Fit();
        return page;
    }
}
