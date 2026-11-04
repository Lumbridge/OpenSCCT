using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Draft behavior and every role subsection at multiple viewport sizes, including the in-game overlay.</summary>
public sealed class OptionsSmoke : SmokeTest
{
    public const string TestName = "options";
    public override string Name => TestName;
    private OptionsMenu? _options;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes =
    [
        new(1280, 720), new(800, 600), new(1024, 768), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160), new(1280, 720),
    ];
    private int _size, _tab, _section, _frames;
    private bool _advanced;
    private bool _allBindings;
    private string? _previousBindingCategory;
    private readonly Dictionary<MenuButton, string> _bindingCountTexts = new();

    public override SmokeResult Step(SmokeContext context)
    {
        SceneTree tree = context.Tree;
        if (_options is null)
        {
            _options = (OptionsMenu)FrontEnd.ShowOptions(tree);
            _options.Overlay = true;
            CheckBehavior(_options);
            _options.QueueFree();
            _options = new OptionsMenu { Overlay = true };
            tree.Root.AddChild(_options);
            tree.CurrentScene = _options;
            Resize(tree, _sizes[_size]);
        }
        if (++_frames == 1)
        {
            _options.Tabs!.CurrentTab = _tab;
            if (_tab == 6)
            {
                var role = _options.FindChild("HudEditorRole", true, false) as OptionButton;
                var aspect = _options.FindChild("HudEditorAspect", true, false) as OptionButton;
                role?.Select(_section / 3); role?.EmitSignal(OptionButton.SignalName.ItemSelected, _section / 3);
                aspect?.Select(_section % 3); aspect?.EmitSignal(OptionButton.SignalName.ItemSelected, _section % 3);
            }
            if (_options.Tabs.GetCurrentTabControl().GetNodeOrNull<TabContainer>("Sections") is { } sections)
            {
                sections.CurrentTab = _section;
                if (sections.GetCurrentTabControl().FindChild("ControllerAimSettings", true, false) is CheckButton tuning)
                    tuning.ButtonPressed = _advanced;
                foreach (Node node in sections.GetCurrentTabControl().FindChildren("*", nameof(CheckButton), true, false))
                    if (node is CheckButton button && button.Text == "Advanced settings") button.ButtonPressed = _advanced;
                if (_allBindings && sections.GetCurrentTabControl().FindChild("BindingCategory", true, false) is OptionButton category)
                {
                    _previousBindingCategory = category.Selected >= 0 ? category.GetItemText(category.Selected) : "Movement";
                    PickBindingCategory(category, "All");
                }
            }
        }
        if (_allBindings && _frames == 4 && _tab < 2 && _section is 0 or 2 &&
            _options.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetCurrentTabControl()
                .FindChild("BindingCategory_All", true, false) is Button focusedAll && focusedAll.IsVisibleInTree())
            focusedAll.GrabFocus();
        if (_allBindings && _frames == 4) ApplyBindingCountFixture();
        if (_frames < 8) return SmokeResult.Running;
        string where = $"{_sizes[_size].X}x{_sizes[_size].Y} options {_tab}/{_section} advanced={_advanced} all={_allBindings}";
        var problems = LayoutCheck.Problems(_options, _sizes[_size].X);
        if (_options.Size.X > _sizes[_size].X + 1f)
            problems.Add($"options expanded to {_options.Size.X:0}px in a {_sizes[_size].X}px viewport");
        problems.AddRange(LayoutCheck.BoundsProblems(_options, _sizes[_size]));
        if (_tab == 6 && _options.FindChildren("*", "", true, false).OfType<HudEditorPreview>().FirstOrDefault() is { } preview)
        {
            for (Node? parent = preview.GetParent(); parent is not null; parent = parent.GetParent())
                if (parent is ScrollContainer scroll && !scroll.GetGlobalRect().Grow(2f).Encloses(preview.GetGlobalRect()))
                    problems.Add("HUD editor preview is cropped by its scroll area");
            foreach (string id in new[] { "Radar", "Location", "Activity", "Fps", "Network" })
            {
                if (!preview.ElementBounds.TryGetValue(id, out Rect2 bounds) || bounds.Size.X <= 0f || bounds.Size.Y <= 0f)
                    problems.Add("HUD editor has no selectable " + id + " preview");
                else if (!new Rect2(Vector2.Zero, preview.Size).Grow(1f).Encloses(bounds))
                    problems.Add("HUD editor " + id + " outline leaves the preview");
            }
        }
        if (_tab == 6) problems.AddRange(HudEditorSidebarChecks.Run(_options));
        foreach (Node node in _options.FindChildren("ActionList", nameof(ScrollContainer), true, false))
        {
            if (node is not ScrollContainer list || !list.IsVisibleInTree()) continue;
            if (list.Size.Y < 96f) problems.Add($"bindings list has only {list.Size.Y:0}px of visible height");
            if (list.ScrollVertical == 0 && list.FindChild("BindingRows", true, false) is Control actions &&
                actions.FindChildren("BindingSlot_*", nameof(Button), true, false).OfType<Button>().FirstOrDefault() is { } first &&
                first.GetGlobalRect().End.Y > list.GetGlobalRect().End.Y + 1f)
                problems.Add("initial bindings view hides the first action below its instructions");
        }
        if (_tab < 2 && _section is 0 or 2 && !_advanced)
            CheckBindingBrowseLayout(_options.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetCurrentTabControl(), problems);
        _problems.AddRange(problems.Select(p => where + ": " + p));
        MenuPage.Shot(tree.Root, $"options-{_sizes[_size].X}x{_sizes[_size].Y}-{_tab}-{_section}-{_advanced}" + (_allBindings ? "-all" : ""));
        _frames = 0;
        if (_tab < 2)
        {
            if (!_advanced && _section is 0 or 2)
            {
                if (!_allBindings) { _allBindings = true; return SmokeResult.Running; }
                RestoreBindingCountTexts();
                if (_options.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetCurrentTabControl()
                    .FindChild("BindingCategory", true, false) is OptionButton category)
                    PickBindingCategory(category, _previousBindingCategory ?? "Movement");
                _allBindings = false;
                _previousBindingCategory = null;
            }
            if (!_advanced && _section is 1 or 2) { _advanced = true; return SmokeResult.Running; }
            _advanced = false;
            if (++_section < _options.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetTabCount()) return SmokeResult.Running;
            _section = 0;
        }
        if (_tab == 6) { if (++_section < 6) return SmokeResult.Running; _section = 0; }
        if (++_tab < _options.Tabs!.GetTabCount()) return SmokeResult.Running;
        _tab = 0;
        if (++_size < _sizes.Length) { Resize(tree, _sizes[_size]); return SmokeResult.Running; }
        return _problems.Count == 0 ? SmokeResult.Pass("draft/apply/discard, role bindings including All categories, all subsections and advanced settings from 800x600 to 4K, including shrinking after 4K")
            : SmokeResult.Fail(string.Join("; ", _problems.Take(12)));
    }

    private void Expect(bool value, string message) { if (!value) _problems.Add(message); }

    private static void PickBindingCategory(OptionButton category, string name)
    {
        int index = Enumerable.Range(0, category.ItemCount).FirstOrDefault(i => category.GetItemText(i) == name);
        category.Select(index);
        category.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    private void ApplyBindingCountFixture()
    {
        RestoreBindingCountTexts();
        Control section = _options!.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetCurrentTabControl();
        MenuButton[] counts = section.FindChildren("BindingMore_*", nameof(MenuButton), true, false).OfType<MenuButton>()
            .Where(button => button.IsVisibleInTree()).Take(3).ToArray();
        string[] labels = { "+", "+1", "+11" };
        for (int index = 0; index < counts.Length; index++)
        {
            _bindingCountTexts[counts[index]] = counts[index].Text;
            counts[index].Text = labels[index];
        }
    }

    private void RestoreBindingCountTexts()
    {
        foreach ((MenuButton button, string text) in _bindingCountTexts)
            if (GodotObject.IsInstanceValid(button) && !button.IsQueuedForDeletion()) button.Text = text;
        _bindingCountTexts.Clear();
    }

    private static Rect2 FocusDrawBounds(StyleBox style, Rect2 rect) => style switch
    {
        // UiBox expands in its custom draw callback; native style boxes expose each expanded edge.
        UiBox box => rect.Grow(box.Expand),
        StyleBoxFlat flat => ExpandedDrawBounds(rect, flat.ExpandMarginLeft, flat.ExpandMarginTop, flat.ExpandMarginRight, flat.ExpandMarginBottom),
        StyleBoxTexture texture => ExpandedDrawBounds(rect, texture.ExpandMarginLeft, texture.ExpandMarginTop, texture.ExpandMarginRight, texture.ExpandMarginBottom),
        _ => rect,
    };

    private static Rect2 ExpandedDrawBounds(Rect2 rect, float left, float top, float right, float bottom) =>
        new(rect.Position - new Vector2(left, top), rect.Size + new Vector2(left + right, top + bottom));

    private void CheckBindingBrowseLayout(Control section, List<string> problems)
    {
        if (section.FindChild("BindingBody", true, false) is not Control body || !body.IsVisibleInTree()) return;
        var sidebar = section.FindChild("BindingCategoryList", true, false) as ScrollContainer;
        var margin = section.FindChild("BindingCategoryMargin", true, false) as MarginContainer;
        var divider = section.FindChild("BindingColumnDivider", true, false) as VSeparator;
        bool wide = Math.Min(1500f, _sizes[_size].X - 32f) >= 1000f;
        if (sidebar is null || margin is null || divider is null)
        {
            problems.Add("bindings category inset or column divider is missing");
            return;
        }
        if (divider.IsVisibleInTree() != wide || margin.IsVisibleInTree() != wide || sidebar.IsVisibleInTree() != wide)
            problems.Add("bindings category sidebar and vertical divider do not follow the compact layout breakpoint");
        if (wide)
        {
            if (divider.Size.X <= 0f || divider.Size.Y <= 0f) problems.Add("bindings vertical divider has no visible area");
            if (section.FindChild("BindingSearch", true, false) is LineEdit search &&
                sidebar.FindChild("BindingCategory_All", true, false) is Button first)
            {
                if (_allBindings && sidebar.ScrollVertical != 0) problems.Add("All categories leaves the first sidebar line scrolled above the viewport");
                if (sidebar.ScrollVertical == 0)
                {
                    if (first.GetGlobalRect().Position.Y < sidebar.GetGlobalRect().Position.Y - .5f)
                        problems.Add("first category line is clipped above its sidebar viewport");
                    if (first.GetGlobalRect().Position.Y < search.GetGlobalRect().End.Y + 4f)
                        problems.Add("first category line overlaps the search toolbar");
                    if (first.GetGlobalRect().End.Y > sidebar.GetGlobalRect().End.Y + 1f)
                        problems.Add("first category line does not fit inside its visible sidebar");
                }
                if (_allBindings)
                {
                    if (!first.HasFocus()) problems.Add("All category focus regression did not focus the first sidebar row");
                    StyleBox focus = first.GetThemeStylebox("focus");
                    var localRect = new Rect2(Vector2.Zero, first.Size);
                    Rect2 decoration = FocusDrawBounds(focus, localRect);
                    var drawn = new Rect2(first.GetGlobalRect().Position + decoration.Position, decoration.Size);
                    if (!sidebar.GetGlobalRect().Grow(.5f).Encloses(drawn))
                        problems.Add($"focused All category decoration {drawn.Position}..{drawn.End} is clipped by its sidebar viewport");
                }
            }
            else problems.Add("bindings sidebar has no first All category line");
        }
        if (!_allBindings) return;
        if (section.FindChild("BindingRows", true, false) is not Control rows ||
            section.FindChild("ActionList", true, false) is not ScrollContainer list ||
            section.FindChild("BindingCategory", true, false) is not OptionButton category) return;
        var headers = rows.GetChildren().OfType<MarginContainer>()
            .Where(c => c.Name.ToString().StartsWith("BindingSection_", StringComparison.Ordinal)).ToArray();
        string[] names = Enumerable.Range(0, category.ItemCount).Select(category.GetItemText).Where(name => name != "All").ToArray();
        if (headers.Length != names.Length) problems.Add("All actions does not have exactly one divider for each category");
        foreach (string name in names)
        {
            MarginContainer? header = headers.FirstOrDefault(h => h.Name == "BindingSection_" + name);
            if (header is null || header.FindChild("BindingSectionLabel", true, false) is not Label label ||
                header.FindChild("BindingSectionRule", true, false) is not HSeparator rule)
            {
                problems.Add("All actions has no labeled section divider for " + name);
                continue;
            }
            if (label.Text != name || !header.GetGlobalRect().Grow(1f).Encloses(label.GetGlobalRect()))
                problems.Add("All actions clips the " + name + " category heading");
            if (rule.Size.X <= 0f || rule.Size.Y <= 0f) problems.Add("All actions has an empty divider for " + name);
        }
        if (list.ScrollVertical == 0 && headers.FirstOrDefault() is { } firstHeader)
        {
            if (firstHeader.GetGlobalRect().Position.Y < list.GetGlobalRect().Position.Y - .5f ||
                firstHeader.GetGlobalRect().End.Y > list.GetGlobalRect().End.Y + 1f)
                problems.Add("All actions clips its first category heading at the list edge");
        }
        CheckBindingColumnAlignment(body, rows, problems);
    }

    private static void CheckBindingColumnAlignment(Control body, Control rows, List<string> problems)
    {
        Rect2? primaryReference = null, alternateReference = null, moreReference = null;
        foreach (Control row in rows.GetChildren().OfType<Control>().Where(row => row.Name.ToString().StartsWith("BindingRow_", StringComparison.Ordinal)))
        {
            Button? primary = row.GetChildren().OfType<Button>().FirstOrDefault(button => button.Name.ToString().StartsWith("BindingSlot_", StringComparison.Ordinal) && button.Name.ToString().EndsWith("_0", StringComparison.Ordinal));
            Button? alternate = row.GetChildren().OfType<Button>().FirstOrDefault(button => button.Name.ToString().StartsWith("BindingSlot_", StringComparison.Ordinal) && button.Name.ToString().EndsWith("_1", StringComparison.Ordinal));
            MenuButton? more = row.GetChildren().OfType<MenuButton>().FirstOrDefault(button => button.Name.ToString().StartsWith("BindingMore_", StringComparison.Ordinal));
            if (primary is null || alternate is null || more is null) continue;
            Rect2 primaryRect = primary.GetGlobalRect(), alternateRect = alternate.GetGlobalRect(), moreRect = more.GetGlobalRect();
            if (primaryReference is { } firstPrimary && !AlignedColumn(firstPrimary, primaryRect)) problems.Add(row.Name + " Primary cell shifts when additional-binding counts differ");
            if (alternateReference is { } firstAlternate && !AlignedColumn(firstAlternate, alternateRect)) problems.Add(row.Name + " Alternate cell shifts when additional-binding counts differ");
            if (moreReference is { } firstMore && !AlignedColumn(firstMore, moreRect)) problems.Add(row.Name + " additional-binding count changes its column width or position");
            primaryReference ??= primaryRect; alternateReference ??= alternateRect; moreReference ??= moreRect;
            StyleBox normal = more.GetThemeStylebox("normal");
            float textWidth = more.GetThemeFont("font").GetStringSize(more.Text, HorizontalAlignment.Left, -1, more.GetThemeFontSize("font_size")).X;
            float padding = Math.Max(0, normal.ContentMarginLeft) + Math.Max(0, normal.ContentMarginRight);
            if (textWidth + padding > moreRect.Size.X + 1f) problems.Add(row.Name + " clips its additional-binding count " + more.Text);
        }
        Label? primaryHeading = body.FindChildren("*", nameof(Label), true, false).OfType<Label>().FirstOrDefault(label => label.Text == "Primary");
        Label? alternateHeading = body.FindChildren("*", nameof(Label), true, false).OfType<Label>().FirstOrDefault(label => label.Text == "Alternate");
        if (primaryReference is { } primaryCell && primaryHeading is not null && Math.Abs(primaryHeading.GetGlobalRect().GetCenter().X - primaryCell.GetCenter().X) > 1f)
            problems.Add("Primary column heading does not align with its binding cells");
        if (alternateReference is { } alternateCell && alternateHeading is not null && Math.Abs(alternateHeading.GetGlobalRect().GetCenter().X - alternateCell.GetCenter().X) > 1f)
            problems.Add("Alternate column heading does not align with its binding cells");
    }

    private static bool AlignedColumn(Rect2 reference, Rect2 current) =>
        Math.Abs(reference.Position.X - current.Position.X) <= 1f && Math.Abs(reference.Size.X - current.Size.X) <= 1f;

    private void CheckBehavior(OptionsMenu options)
    {
        string original = SettingsStore.Current.ToJson();
        int writes = 0;
        void Wrote() => writes++;
        InputBindings.UserFileSaved += Wrote;
        try
        {
            Expect(!options.HasUnsavedChanges, "fresh editor is clean");
            CheckBindingConflicts(options);
            CheckBindingBrowsing(options);
            _problems.AddRange(BindingEditorChecks.Run(options));
            options.Hide();
            try { _problems.AddRange(BindingRuntimeChecks.Run(options.GetTree())); }
            finally { options.Show(); }
            CheckHudEditor(options);
            var volume = (NotchedSlider)options.Controls["master_volume"];
            double originalVolume = volume.Value;
            Expect(volume.Resolve(24, released: true) == 25, "pointer release snaps near a quarter volume");
            Expect(volume.Resolve(22, released: true) == 22, "pointer remains precise outside a notch");
            Expect(volume.Resolve(24) == 24, "non-pointer changes bypass notches");
            volume.SetValueNoSignal(originalVolume);
            Expect(options.Tabs!.GetTabTitle(0) == "Spy" && options.Tabs.GetTabTitle(1) == "Mercenary", "role tabs");
            Expect(!options.Controls.ContainsKey("controls_spy_kbm.look_response"), "mouse response selector removed");
            void PickFps(string key, int fps) => ((FrameRateSlider)options.Controls[key]).Select(fps);
            string ShownFps(string key) => ((FrameRateSlider)options.Controls[key]).Text;
            void PickTelemetry(string key, int index)
            {
                var picker = (OptionButton)options.Controls[key];
                picker.Select(index);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
            }
            void SlideTelemetry(string key, float value)
            {
                var slider = (HSlider)options.Controls[key];
                slider.SetValueNoSignal(value);
                slider.EmitSignal(Godot.Range.SignalName.ValueChanged, (double)value);
            }
            void EditTelemetryAppearance(string element, double size, double opacity)
            {
                HudEditorPanel editor = options.FindChildren("*", "", true, false).OfType<HudEditorPanel>().Single();
                var search = (LineEdit)editor.FindChild("HudEditorSearch", true, false);
                search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, "");
                var role = (OptionButton)editor.FindChild("HudEditorRole", true, false);
                role.Select(0); role.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                var elements = (OptionButton)editor.FindChild("HudEditorElement", true, false);
                int selected = Enumerable.Range(0, elements.ItemCount).First(i => elements.GetItemMetadata(i).AsString() == element);
                elements.Select(selected); elements.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selected);
                void SetField(string title, double value)
                {
                    PanelContainer property = editor.FindChildren("*", nameof(PanelContainer), true, false).OfType<PanelContainer>()
                        .Single(row => row.HasMeta("hud_editor_property") && row.GetMeta("hud_editor_property").AsString() == title);
                    SpinBox field = property.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>().Single();
                    field.SetValueNoSignal(value); field.EmitSignal(Godot.Range.SignalName.ValueChanged, value);
                }
                SetField("Size %", size); SetField("Opacity %", opacity);
            }
            PickTelemetry("show_fps", 1);
            Expect(options.Editing.ShowFps && options.Editing.FpsTelemetry.DisplayMode == "Always", "FPS Always selector enables the readout");
            PickTelemetry("show_fps", 0);
            Expect(!options.Editing.ShowFps, "FPS Never selector disables the readout");
            PickTelemetry("show_fps", 2);
            PickTelemetry("show_net", 2);
            Expect(options.Editing.ShowNet && options.Editing.NetworkTelemetry.DisplayMode == "Issues", "network Issues selector enables conditional display");
            PickTelemetry("show_net", 0);
            Expect(!options.Editing.ShowNet, "network Never selector disables the readout");
            PickTelemetry("show_net", 1);
            PickTelemetry("fps_telemetry.graph", 2);
            PickTelemetry("network_telemetry.graph", 1);
            foreach (string key in new[] { "fps_telemetry", "network_telemetry" })
                Expect(!options.Controls.ContainsKey(key + ".position_x") && !options.Controls.ContainsKey(key + ".position_y") &&
                    !options.Controls.ContainsKey(key + ".scale") && !options.Controls.ContainsKey(key + ".opacity"),
                    "telemetry position, size and opacity controls live only in HUD editor");
            // Simulate retained appearance preferences from an older profile. Editing HUD layout must leave
            // these compatibility baselines intact rather than silently changing an existing readout.
            options.Editing.FpsTelemetry.Scale = 115f; options.Editing.FpsTelemetry.Opacity = 77f;
            options.Editing.NetworkTelemetry.Scale = 90f; options.Editing.NetworkTelemetry.Opacity = 65f;
            var fpsPlacement = options.Editing.HudLayout.Element(false, "Fps");
            fpsPlacement.X = -.23f; fpsPlacement.Y = .24f;
            var netPlacement = options.Editing.HudLayout.Element(false, "Network");
            netPlacement.X = -.19f; netPlacement.Y = .39f;
            EditTelemetryAppearance("Fps", 135, 42);
            SlideTelemetry("fps_telemetry.graph_seconds", 12f);
            SlideTelemetry("fps_telemetry.issue_hold_seconds", 4.5f);
            SlideTelemetry("fps_telemetry.frame_time_threshold_ms", 25f);
            EditTelemetryAppearance("Network", 85, 68);
            SlideTelemetry("network_telemetry.graph_seconds", 17f);
            SlideTelemetry("network_telemetry.issue_hold_seconds", 1.5f);
            SlideTelemetry("network_telemetry.ping_threshold_ms", 95f);
            SlideTelemetry("network_telemetry.jitter_threshold_ms", 18f);
            SlideTelemetry("network_telemetry.loss_threshold_percent", 0.5f);
            void ExpectEditedTelemetry(GameSettings settings, string when)
            {
                FpsTelemetrySettings fps = settings.FpsTelemetry;
                NetworkTelemetrySettings net = settings.NetworkTelemetry;
                Expect(settings.ShowFps && fps.DisplayMode == "Issues" && settings.ShowNet && net.DisplayMode == "Always", when + " retains independent telemetry display modes");
                Expect(settings.HudLayout.Spy["Fps"].X == -.23f && settings.HudLayout.Spy["Fps"].Y == .24f &&
                    settings.HudLayout.Spy["Network"].X == -.19f && settings.HudLayout.Spy["Network"].Y == .39f,
                    when + " retains independent telemetry positions in the HUD editor");
                Expect(Math.Abs(settings.HudLayout.Spy["Fps"].Scale - 1.35f) < .001f && Math.Abs(settings.HudLayout.Spy["Fps"].Opacity - .42f) < .001f &&
                    Math.Abs(settings.HudLayout.Spy["Network"].Scale - .85f) < .001f && Math.Abs(settings.HudLayout.Spy["Network"].Opacity - .68f) < .001f,
                    when + " retains telemetry appearance edited through HUD editor fields");
                Expect(fps.Scale == 115f && fps.Opacity == 77f && net.Scale == 90f && net.Opacity == 65f,
                    when + " preserves older-profile telemetry appearance baselines");
                Expect(fps.Graph == "Bars" && fps.GraphSeconds == 12f && fps.IssueHoldSeconds == 4.5f && fps.FrameTimeThresholdMs == 25f &&
                    net.Graph == "Line" && net.GraphSeconds == 17f && net.IssueHoldSeconds == 1.5f && net.PingThresholdMs == 95f &&
                    net.JitterThresholdMs == 18f && net.LossThresholdPercent == 0.5f, when + " retains telemetry graphs and issue thresholds");
            }
            ExpectEditedTelemetry(options.Editing, "control signals");
            Expect(original == SettingsStore.Current.ToJson(), "telemetry control signals modify only the draft");
            PickFps("max_fps", 137);
            Expect(options.Editing.FpsSpy == 137 && options.Editing.FpsMerc == 137, "custom FPS retains single-frame precision");
            PickFps("max_fps", 144);
            Expect(options.Editing.FpsSpy == 144 && options.Editing.FpsMerc == 144 &&
                ShownFps("fps_spy") == "144" && ShownFps("fps_merc") == "144", "shared FPS updates both role drafts and selectors");
            PickFps("fps_spy", 60);
            Expect(options.Editing.FpsMerc == 144 && ShownFps("max_fps") == "Per-role limits", "role FPS edit retains the other limit");
            PickFps("max_fps", DisplayFrameCap.Unlimited);
            Expect(options.Editing.FpsSpy == 0 && options.Editing.FpsMerc == 0, "Unlimited works after mixed role limits");
            PickFps("max_fps", DisplayFrameCap.Monitor);
            Expect(options.Editing.FpsSpy == DisplayFrameCap.Monitor && options.Editing.FpsMerc == DisplayFrameCap.Monitor, "Monitor works after rebuilding choices");
            PickFps("max_fps", 120);
            options.LoadLayout(PlaySide.Spy);
            var draft = options.Keys!.Draft!;
            string baseline = draft.Layout.ToIni();
            var breath = ControlActions.All.Single(a => a.Id == "breath");
            draft.Assign(breath, "X");
            draft.Assign(ControlActions.All.Single(a => a.Id == "voice"), "J");
            options.Editing.SoundVolume = 0.27f;
            Expect(writes == 0 && original == SettingsStore.Current.ToJson(), "edits do not write or apply");
            Expect(options.HasUnsavedChanges, "dirty after binding edit");
            options.Overlay = false;
            options.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            options.Overlay = true;
            var confirm = options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation");
            Expect(confirm is not null, "front-end Escape confirms discard");
            options.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Expect(confirm?.IsQueuedForDeletion() == true, "Escape dismisses discard confirmation");
            Expect(draft.Dirty, "cancel discard retains draft");
            options.Editing.SplitKeyboard = false;
            options.LoadLayout(PlaySide.Merc);
            Expect(ReferenceEquals(draft, options.Keys!.Draft), "shared edits use same draft");
            options.Editing.SplitKeyboard = true;
            options.LoadLayout(PlaySide.Merc);
            Expect(!ReferenceEquals(draft, options.Keys!.Draft), "separate role draft retained");
            // An options overlay disables gameplay input but must still use the played role's layout on Apply.
            options.Keys!.Draft!.Assign(ControlActions.All.Single(action => action.Id == "voice"), "F17");
            var mercApplyInput = new PlayInput
            {
                Name = "MercApplyInput", Enabled = false, CapturePointer = false, RequireCapture = false,
                Bindings = InputBindings.Load(PlaySide.Merc, seedInputMap: false, draft: true),
            };
            var applyScene = new Node { Name = "MercApplyScene" };
            SceneTree applyTree = options.GetTree();
            Node? previousScene = applyTree.CurrentScene;
            applyTree.Root.AddChild(applyScene);
            applyScene.AddChild(mercApplyInput);
            applyTree.CurrentScene = applyScene;
            try
            {
                Expect(options.Save() == Error.Ok, "Apply succeeds");
                Expect(BindingInput.Current?.Layout.Resolve("F17").Any(action => action.Name == "PushToTalk") == true &&
                    InputMap.ActionGetEvents(OpenScctGame.Voice.VoiceInput.PushToTalk).OfType<InputEventKey>().Any(key => key.PhysicalKeycode == Key.F17),
                    "Apply with a Mercenary PlayInput retains its global binding session and voice layout");
            }
            finally
            {
                applyTree.CurrentScene = previousScene;
                applyTree.Root.RemoveChild(applyScene);
                applyScene.QueueFree();
            }
            OpenScctGame.Voice.VoiceInput.BindLayout(draft.Layout);
            Expect(!options.HasUnsavedChanges && writes > 0, "Apply accepts draft without closing");
            Expect(InputMap.ActionGetEvents(OpenScctGame.Voice.VoiceInput.PushToTalk).OfType<InputEventKey>().Any(k => k.PhysicalKeycode == Key.J), "Apply updates voice shortcut InputMap");
            Expect(Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "Apply publishes settings");
            GameSettings savedFps = GameSettings.FromJson(SettingsStore.Current.ToJson());
            Expect(savedFps.FpsSpy == 120 && savedFps.FpsMerc == 120 && ShownFps("max_fps") == "120", "shared FPS survives save and menu rebuild");
            ExpectEditedTelemetry(savedFps, "Apply serialization");
            ExpectEditedTelemetry(options.Editing, "Apply draft rebuild");
            Expect(((OptionButton)options.Controls["show_fps"]).Selected == 2 && ((OptionButton)options.Controls["show_net"]).Selected == 1 &&
                !options.Controls.ContainsKey("fps_telemetry.opacity") && !options.Controls.ContainsKey("network_telemetry.scale"),
                "Apply rebuild retains telemetry selectors without duplicate appearance controls");
            options.LoadLayout(PlaySide.Spy);
            Expect(options.Keys!.Draft!.Keys(breath).Contains("X"), "Apply retains draft binding");
            options.Defaults();
            Expect(options.HasUnsavedChanges, "reset stages changes");
            Expect(ShownFps("max_fps") == DisplayFrameCap.Label(new GameSettings().FpsSpy, SettingsApplier.MonitorHz), "reset restores shared FPS selector");
            Expect(!options.Editing.ShowFps && !options.Editing.ShowNet && options.Editing.FpsTelemetry.DisplayMode == "Always" &&
                options.Editing.NetworkTelemetry.DisplayMode == "Always", "reset disables both telemetry readouts and restores default display modes");
            foreach (string key in new[] { "fps_telemetry", "network_telemetry" })
            {
                Expect(((OptionButton)options.Controls[key + ".graph"]).Selected == 0 &&
                    ((HSlider)options.Controls[key + ".graph_seconds"]).Value == 5d && ((HSlider)options.Controls[key + ".issue_hold_seconds"]).Value == 3d,
                    "reset restores " + key + " graph controls");
                Expect(!options.Controls.ContainsKey(key + ".scale") && !options.Controls.ContainsKey(key + ".opacity"),
                    "reset does not restore duplicate " + key + " appearance controls");
            }
            foreach (string element in new[] { "Fps", "Network" })
            {
                HudElementSettings appearance = options.Editing.HudLayout.Spy.GetValueOrDefault(element) ?? new HudElementSettings();
                Expect(appearance.Scale == 1f && appearance.Opacity == 1f, "reset restores " + element + " HUD editor appearance");
            }
            Expect(((OptionButton)options.Controls["show_fps"]).Selected == 0 && ((OptionButton)options.Controls["show_net"]).Selected == 0,
                "reset restores both telemetry visibility selectors");
            ExpectEditedTelemetry(SettingsStore.Current, "unapplied reset");
            Expect(Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "reset does not apply");
            options.Editing.SpyKbm.Look = 2f;
            options.Editing.MercKbm.Look = 3f;
            var resetSpy = options.Tabs!.GetTabControl(0).GetNode<TabContainer>("Sections").GetTabControl(0)
                .FindChild("BindingOptions", true, false) as MenuButton;
            resetSpy?.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 1L);
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(options.Editing.SpyKbm.Look == new GameSettings().SpyKbm.Look && options.Editing.MercKbm.Look == 3f, "role reset leaves other role values");
            bool closed = false;
            options.Closed += () => closed = true;
            int appliedWrites = writes;
            options.Back();
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(closed && writes == appliedWrites && Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "confirmed discard closes without applying");
            closed = false;
            var saveClose = options.FindChild("SaveAndClose", true, false) as Button;
            saveClose?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(closed && !options.HasUnsavedChanges, "Save & Close applies and closes");
            // A direct controller action works without a physical key and keeps the held byte + callback together.
            var input = new PlayInput { Bindings = InputBindings.Load(PlaySide.Spy, seedInputMap: false, draft: true), RequireCapture = false };
            var local = new LocalInput();
            input.Key(ControlActions.VirtualPrefix + "RT:altfire", true);
            input.FillTick(local);
            Expect(local[LocalInput.IndexOf("bAltFire")] == 1 && local.Commands.Any(c => c.Name == "AltFire"), "direct pad action dispatch");
            Expect(local[LocalInput.IndexOf("bHoldBreath")] == 0 && local.Commands.All(c => c.Name != "GadgetQuickChoice"), "direct pad action excludes combined keyboard actions");
            input.ClearHeld(local);
            local.EndTick();
            var behaviorSettings = new GameSettings();
            behaviorSettings.SpyKbm.WalkBehavior = "Hold";
            SettingsStore.Reset(behaviorSettings);
            input.Key(ControlActions.VirtualPrefix + "LS:speed", true);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetWalk" && c.Arg == 1), "hold Walk press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "LS:speed", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetWalk" && c.Arg == 0), "hold Walk release returns to run");
            input.ClearHeld(local);
            local.EndTick();
            behaviorSettings.MercKbm.ScopeBehavior = "Hold";
            input.Bindings = InputBindings.Load(PlaySide.Merc, seedInputMap: false, draft: true);
            input.Key(ControlActions.VirtualPrefix + "RS:scope", true);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 1), "hold scope press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RS:scope", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 0) && !local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 1), "hold scope release");
            input.ClearHeld(local);
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", true);
            input.FillTick(local);
            Expect(local.Commands.All(c => c.Name != "SetSpyFirstPerson" || c.Arg == 0), "merc ignores spy perspective action");
            input.ClearHeld(local);
            local.EndTick();
            input.Bindings = InputBindings.Load(PlaySide.Spy, seedInputMap: false, draft: true);
            behaviorSettings.SpyKbm.PerspectiveBehavior = "Hold";
            input.Key("X", true);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "hold perspective keyboard press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", true);
            input.Key("X", false);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "controller keeps perspective held after keyboard release");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 0) && !local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 1), "last perspective hold release restores third person");
            local.EndTick();
            behaviorSettings.SpyKbm.PerspectiveBehavior = "Toggle";
            input.Key("X", true);
            input.Key("X", true);
            input.Key("X", false);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "perspective toggle survives release and ignores repeated keydown");
            local.EndTick();
            input.Key("X", true);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 0, "second perspective press restores third person");
            input.Key("X", false);
            input.Key("X", true);
            input.FillTick(local);
            input.ClearHeld(local);
            Expect(local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 0), "focus release clears perspective");
            input.Free();
            // Restore the isolated settings so the layout pass starts with real defaults.
            SettingsStore.Reset(GameSettings.FromJson(original));
        }
        finally { InputBindings.UserFileSaved -= Wrote; }
    }

    private void CheckBindingConflicts(OptionsMenu options)
    {
        bool originalSplit = options.Editing.SplitKeyboard;
        int originalTab = options.Tabs!.CurrentTab;
        var originals = new Dictionary<PlaySide, KeyLayout>();
        try
        {
            options.Editing.SplitKeyboard = true;
            foreach (PlaySide side in new[] { PlaySide.Spy, PlaySide.Merc })
            {
                options.LoadLayout(side);
                var draft = options.Keys!.Draft!;
                originals[side] = draft.Layout.Clone();
                draft.Reset(DefaultKeyLayout.Create());
            }
            foreach (bool split in new[] { false, true })
            foreach (var conflict in new[]
            {
                (Side: PlaySide.Spy, Key: Key.F, Label: "Switch fire mode"),
                (Side: PlaySide.Merc, Key: Key.Z, Label: "Back to wall"),
                (Side: PlaySide.Spy, Key: Key.F3, Label: "Controls reference (unavailable)"),
            })
            {
                options.Editing.SplitKeyboard = split;
                options.Tabs.CurrentTab = conflict.Side == PlaySide.Merc ? 1 : 0;
                options.LoadLayout(conflict.Side);
                var draft = options.Keys!.Draft!;
                string before = draft.Layout.ToIni();
                string where = $"{(split ? "split" : "shared")} {conflict.Side} {conflict.Key} conflict";
                options.KeyButtons["W"].EmitSignal(BaseButton.SignalName.Pressed);
                Expect(options.Listening == "forward", where + " starts action capture");
                options._Input(new InputEventKey { Keycode = conflict.Key, PhysicalKeycode = conflict.Key, Pressed = true });
                var dialog = options.GetChildren().OfType<ConfirmationDialog>()
                    .FirstOrDefault(d => !d.IsQueuedForDeletion() && d.Title == "Key already assigned");
                Expect(dialog is not null, where + " opens the conflict popup");
                Expect(dialog?.DialogText.Contains(conflict.Label, StringComparison.Ordinal) == true,
                    where + " names its existing action");
                dialog?.Hide();
                dialog?.EmitSignal(ConfirmationDialog.SignalName.Canceled);
                Expect(options.Listening is null && draft.Layout.ToIni() == before,
                    where + " cancellation leaves bindings unchanged");
            }
        }
        finally
        {
            options.Editing.SplitKeyboard = true;
            foreach ((PlaySide side, KeyLayout layout) in originals)
            {
                options.LoadLayout(side);
                options.Keys!.Draft!.Reset(layout);
            }
            options.Editing.SplitKeyboard = originalSplit;
            options.Tabs.CurrentTab = originalTab;
            options.LoadLayout(originalTab == 1 ? PlaySide.Merc : PlaySide.Spy);
        }
        Expect(!options.HasUnsavedChanges, "conflict popup checks restore clean drafts");
    }

    private void CheckBindingBrowsing(OptionsMenu options)
    {
        bool split = options.Editing.SplitKeyboard;
        int tab = options.Tabs!.CurrentTab;
        options.Editing.SplitKeyboard = true;
        options.Tabs.CurrentTab = 0;
        options.LoadLayout(PlaySide.Spy);
        var draft = options.Keys!.Draft!;
        KeyLayout original = draft.Layout.Clone();
        var sections = options.Tabs.GetTabControl(0).GetNode<TabContainer>("Sections");
        sections.CurrentTab = 0;
        Control keyboard = sections.GetTabControl(0);
        var search = keyboard.FindChild("BindingSearch", true, false) as LineEdit;
        static void Search(LineEdit field, string text)
        {
            field.Text = text;
            field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }
        try
        {
            Expect(search is not null && keyboard.FindChild("BindingRow_forward", true, false) is not null,
                "bindings start in Movement with one named row per action");
            if (search is null) return;
            Search(search, "cloak");
            Expect(keyboard.FindChild("BindingRow_cloak", true, false) is not null &&
                keyboard.FindChild("BindingRow_forward", true, false) is null, "search spans every category");
            Search(search, "");
            Expect(keyboard.FindChild("BindingRow_forward", true, false) is not null &&
                keyboard.FindChild("BindingRow_cloak", true, false) is null, "clearing search restores Movement");
            (keyboard.FindChild("BindingCategory_Vision", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(keyboard.FindChild("BindingRow_vision1", true, false) is not null &&
                keyboard.FindChild("BindingRow_forward", true, false) is null, "category navigation limits the visible action list");
            (keyboard.FindChild("BindingCategory_Movement", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            Search(search, "no matching action zzz");
            Expect(keyboard.FindChildren("BindingSlot_*", nameof(Button), true, false).Count == 0,
                "empty search results hide stale binding rows");
            Search(search, "");
            var forward = ControlActions.All.Single(a => a.Id == "forward");
            foreach (string key in new[] { "Up", "Ctrl+F12", "Shift+F12" }) draft.Add(forward, key);
            options.LoadLayout(PlaySide.Spy);
            Expect(draft.Keys(forward).Count() == 4 &&
                keyboard.FindChild("BindingSlot_forward_0", true, false) is Button &&
                keyboard.FindChild("BindingSlot_forward_1", true, false) is Button &&
                keyboard.FindChild("BindingMore_forward", true, false) is MenuButton { Text: "+2" },
                "four inputs remain accessible through two visible slots and extras");
            sections.CurrentTab = 2;
            Control controller = sections.GetTabControl(2);
            var padSearch = controller.FindChild("BindingSearch", true, false) as LineEdit;
            if (padSearch is not null) Search(padSearch, "Primary fire");
            Expect(controller.FindChild("BindingSlot_fire_0", true, false) is Button fire && fire.TooltipText.Contains("contextual", StringComparison.OrdinalIgnoreCase),
                "controller actions expose their linked contextual assignment");
            Expect(controller.FindChild("ControllerAimSettings", true, false) is CheckButton { ButtonPressed: false },
                "controller tuning starts collapsed");
            if (padSearch is not null) Search(padSearch, "");
        }
        finally
        {
            draft.Reset(original);
            if (search is not null) Search(search, "");
            (keyboard.FindChild("BindingCategory_Movement", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            sections.CurrentTab = 0;
            options.Editing.SplitKeyboard = split;
            options.Tabs.CurrentTab = tab;
            options.LoadLayout(tab == 1 ? PlaySide.Merc : PlaySide.Spy);
        }
        Expect(!options.HasUnsavedChanges, "binding browsing checks restore clean drafts");
    }

    private void CheckHudEditor(OptionsMenu options)
    {
        var items = new List<OpenScctGame.Hud.Magma.MagmaDrawItem>
        {
            new() { Path = "Vie/label", Kind = OpenScctGame.Hud.Magma.MagmaWidgetKind.Text,
                Rect = new Rect2(10, 20, 30, 40), HeightPx = 12, Color = Colors.White },
            new() { Path = "Flash/fullscreen", Kind = OpenScctGame.Hud.Magma.MagmaWidgetKind.Text,
                Rect = new Rect2(0, 0, 100, 100), HeightPx = 12 }
        };
        var layout = new HudEditorSettings();
        var health = layout.Element(false, "Vie"); health.X = .1f; health.Y = .2f; health.Scale = 2; health.Opacity = .5f;
        OpenScctGame.Hud.HudEditorTransform.Apply(items, layout.Spy, new Vector2(1000, 500));
        Expect(items[0].Rect == new Rect2(110, 120, 60, 80) && items[0].HeightPx == 24 && items[0].Color.A == .5f, "HUD edit transforms text position, dimensions and opacity");
        Expect(items[1].Rect == new Rect2(0, 0, 100, 100), "HUD editor preserves gameplay screen effects");
        health.Visible = false;
        OpenScctGame.Hud.HudEditorTransform.Apply(items, layout.Spy, new Vector2(1000, 500));
        Expect(items.Count == 1 && items[0].Path == "Flash/fullscreen", "HUD visibility removes only its selected group");
        var assets = OpenScctGame.Hud.HudAssets.TryLoad(null, out _);
        if (assets is null) { Expect(false, "HUD editor preview assets available"); return; }
        CheckRadarAndLocationLayout(assets);
        var page = new OpenScctGame.Hud.SpyHudPage(assets);
        var sample = new OpenScctGame.Hud.HudSnapshot { Valid = true, HasPawn = true, Health = 100 };
        page.Update(sample, .1f); page.Draw(new Vector2(1280, 720), sample);
        Expect(page.DrawList.Any("Vie/"), "stock health is drawn");
        page.Options.Layout.Element(false, "Vie").Visible = false;
        page.Draw(new Vector2(1280, 720), sample);
        Expect(!page.DrawList.Any("Vie/"), "actual HUD page applies layout visibility");
        page.Options.Layout.Spy.Clear(); page.Draw(new Vector2(1280, 720), sample);
        Expect(page.DrawList.Any("Vie/"), "reset restores actual HUD without changing game visibility");
        foreach (bool merc in new[] { false, true })
        {
            OpenScctGame.Hud.HudPage rolePage = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            sample.Side = merc ? OpenScctGame.Hud.HudSide.Merc : OpenScctGame.Hud.HudSide.Spy;
            sample.ShowPlayers = true;
            sample.Players.Clear(); sample.Score.Clear();
            for (int n = 0; n < 6; n++) sample.Players.Add(new("Player " + n, n % 2, false, n, 3, 10));
            for (int n = 0; n < 6; n++) sample.Score.Add(new("Player " + n, n % 2, false, n, n, 0, 3, 10, n == 0));
            sample.Blips.Clear(); sample.Blips.Add(new(OpenScctGame.Hud.HudBlipKind.Teammate, new OpenScct.Core.FVector(100, 100, 0)));
            sample.Ballistic = .6f;
            sample.Tutorial = "Tutorial sample";
            sample.GameMessages.Clear(); sample.GameMessages.Add("Match in progress"); sample.GameMessages.Add("Queued message");
            Rect2? radar = null;
            for (int frame = 0; frame < 120; frame++)
            {
                HudEditorPreview.PreparePage(rolePage, sample);
                rolePage.Draw(new Vector2(1280, 720), sample);
                var bounds = OpenScctGame.Hud.HudEditorTransform.Bounds(rolePage.DrawList.Items);
                Expect(bounds.TryGetValue("Radar", out var current), "preview radar exists");
                if (radar is { } prior) Expect(current.IsEqualApprox(prior), "preview radar stays fixed across redraws");
                radar = current;
                var charge = rolePage.Area(merc ? "Ballistic/Ballistic" : "Ballistic");
                Expect(charge?.Frame == 6, "preview preserves the driven charge frame for both roles");
                Expect(rolePage.DrawList.TextOf("GameMessage/messageGame") == "Match in progress", "preview shows sample game message alongside tutorial, not placeholder or queue");
                Expect(rolePage.Area("TutoMessage/Tuto")?.Frame == 1, "preview preserves held-open tutorial frame");
            }
            foreach (float value in new[] { .2f, .8f })
            {
                sample.Ballistic = value;
                HudEditorPreview.PreparePage(rolePage, sample);
                Expect(rolePage.Area(merc ? "Ballistic/Ballistic" : "Ballistic")?.Frame == (int)(10 * value), "preview charge follows sample rather than fixed display frame");
            }
            Expect(!rolePage.DrawList.Items.Any(i => i.Path.StartsWith("PCConnectedPlayers/")), "HUD preview excludes the replaced compact roster");
            Expect(!HudEditorSettings.Elements.ContainsKey("Scoreboard"), "full scoreboard is independent of HUD editor placement");
        }
        var chatSetting = new HudElementSettings { X = .2f, Y = -.1f, Scale = 1.4f, Opacity = .5f };
        var chat = new OpenScctGame.Chat.ChatPanel { PreviewScreen = new Vector2(1280, 720), Clock = () => 1, LayoutOverride = () => chatSetting };
        chat.Transcript.Add(OpenScctGame.Chat.ChatChannel.All, "Sample chat", "Player", 1, 0, 0);
        options.AddChild(chat);
        chat._Process(0);
        var recent = chat.GetNode<Control>("Recent");
        var expected = OpenScctGame.Hud.HudEditorTransform.Transform(chat.PassiveBaseBounds, chatSetting, new Vector2(1280, 720));
        Expect(recent.Position.IsEqualApprox(expected.Position) && recent.Scale.IsEqualApprox(Vector2.One * 1.4f) && chat.Modulate.A == .5f, "native chat follows HUD position, scale and opacity");
        chatSetting.Visible = false; chat._Process(0); Expect(!chat.Visible, "passive chat can be hidden");
        chat.Expanded = true; chat._Process(0); Expect(chat.Visible, "explicitly opened composer remains usable");
        options.RemoveChild(chat); chat.QueueFree();
        var feed = new OpenScctGame.Hud.HudActivityFeedDrawer(); feed.Configure(assets);
        var events = new[] { new OpenScctGame.Hud.HudActivityEntry(1, OpenScctGame.Hud.HudActivityKind.Join, null, null, OpenScctGame.Hud.HudActivityCause.None, "JOIN", "Player joined", 0, false) };
        feed.Draw(new Vector2(1280, 720), events, 1, null);
        Expect(feed.DrawList.Items.Count > 0, "event feed sample renders");
        layout.Element(false, "Activity").Visible = false;
        OpenScctGame.Hud.HudEditorTransform.Apply(feed.DrawList.Items, layout.Spy, new Vector2(1280, 720));
        Expect(feed.DrawList.Items.Count == 0, "event feed visibility covers all row graphics");

    }

    private void CheckRadarAndLocationLayout(OpenScctGame.Hud.HudAssets assets)
    {
        foreach (bool merc in new[] { false, true })
        foreach (Vector2 screen in new[] { new Vector2(800, 600), new Vector2(1280, 720), new Vector2(3440, 1440) })
        {
            OpenScctGame.Hud.HudPage stock = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            OpenScctGame.Hud.HudPage edited = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            var sample = new OpenScctGame.Hud.HudSnapshot
            {
                Valid = true, HasPawn = true, Health = 100,
                Side = merc ? OpenScctGame.Hud.HudSide.Merc : OpenScctGame.Hud.HudSide.Spy,
            };
            sample.Blips.Add(new(OpenScctGame.Hud.HudBlipKind.Teammate, new OpenScct.Core.FVector(100, 100, 0)));
            var setting = edited.Options.Layout.Element(merc, "Radar");
            // Exercise scaling in the interior and translation clamped at the screen edge. Both previously
            // drifted as the rotating radar ring changed the group's axis-aligned bounds.
            foreach (bool edge in new[] { false, true })
            {
                setting.Scale = edge ? 1f : 1.6f;
                setting.X = edge ? -.9f : merc ? -.2f : .2f;
                setting.Y = edge ? -.9f : .2f;
                Vector2? firstCentre = null, firstRingPoint = null;
                bool ringAnimated = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    sample.Yaw = frame * 317;
                    stock.Update(sample, .05f); edited.Update(sample, .05f);
                    stock.Draw(screen, sample); edited.Draw(screen, sample);
                    var original = stock.DrawList.Find("Radar/fond/Element2");
                    var moved = edited.DrawList.Find("Radar/fond/Element2");
                    var ring = edited.DrawList.Find("Radar/fond/Element3");
                    Expect(original is not null && moved is not null && ring is not null, "live radar frame and animation render for both roles");
                    if (original is null || moved is null || ring is null) break;
                    Rect2 basis = stock.LayoutBounds(screen)["Radar"];
                    Rect2 placed = OpenScctGame.Hud.HudEditorTransform.Transform(basis, setting, screen);
                    Vector2 expected = placed.Position + (Centre(original) - basis.Position) * setting.Scale;
                    Vector2 actual = Centre(moved);
                    Expect(actual.DistanceTo(expected) < .01f, "live radar uses the same fixed layout frame as the editor at every size");
                    if (firstCentre is { } prior) Expect(actual.DistanceTo(prior) < .01f, "moved radar stays still while its ring, yaw and blips animate");
                    else firstCentre = actual;
                    if (firstRingPoint is { } point) ringAnimated |= ring.Points[0].DistanceTo(point) > 1f;
                    else firstRingPoint = ring.Points[0];
                }
                Expect(ringAnimated, "HUD placement preserves the live radar ring animation");
            }

            var location = new OpenScctGame.Hud.HudLocation();
            location.Draw(screen, "Warehouse", true, stock);
            var label = location.DrawList.Find("Location");
            Expect(label is not null && label.Rect.Size.X < screen.X / 2f, "zone name has selectable text bounds rather than a screen-wide box");
            if (label is not null)
            {
                Rect2 before = label.Rect;
                var layout = new HudEditorSettings();
                var zone = layout.Element(merc, "Location");
                zone.X = merc ? -.15f : .15f; zone.Y = .15f;
                OpenScctGame.Hud.HudEditorTransform.Apply(location.DrawList.Items, layout.For(merc), screen);
                Expect(label.Rect.Position.DistanceTo(before.Position + new Vector2(zone.X, zone.Y) * screen) < .01f,
                    "zone name can move horizontally and vertically independently of the radar");
                Expect(layout.Clone().For(merc).ContainsKey("Location"), "zone name placement survives layout persistence");
            }
        }

        static Vector2 Centre(OpenScctGame.Hud.Magma.MagmaDrawItem item)
        {
            Vector2 sum = Vector2.Zero;
            foreach (Vector2 point in item.Points) sum += point;
            return sum / item.Points.Length;
        }
    }

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
