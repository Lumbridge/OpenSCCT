using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Chat;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

public partial class HudEditorPanel : VBoxContainer
{
    private readonly Func<GameSettings> _settings;
    private readonly Stack<HudEditorSettings> _undo = new(), _redo = new();
    private bool _merc, _refreshing;
    private string _selected = "Vie";
    private readonly HashSet<string> _selection = new() { "Vie" };
    private HudEditorPreview _preview = null!;
    private OptionButton _elements = null!;
    private SpinBox _x = null!, _y = null!, _scale = null!, _opacity = null!;
    private CheckBox _visible = null!;
    private Label _note = null!;
    private bool _snap = true;
    private LineEdit _search = null!;
    private ScrollContainer _sidebar = null!;
    private Control _sidebarDivider = null!;
    private VBoxContainer _elementRows = null!;
    private readonly Dictionary<string, Button> _elementButtons = new();
    private Label _elementTitle = null!;
    private PopupMenu _elementMenu = null!;
    private string[] _contextElements = [];
    private bool _contextMerc;
    private bool? _compact;
    private HudEditorSettings Layout => _settings().HudLayout;
    public HudEditorPanel(Func<GameSettings> settings) { _settings = settings; Name = "HudEditorContent"; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        var searchbar = new HBoxContainer();
        _search = new LineEdit { Name = "HudEditorSearch", PlaceholderText = "Find a HUD element…", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 32) };
        searchbar.AddChild(_search);
        _elements = new OptionButton { Name = "HudEditorElement", AllowReselect = true, FitToLongestItem = false, ClipText = true, CustomMinimumSize = new Vector2(220, 32) };
        searchbar.AddChild(_elements);
        var more = new MenuButton { Name = "HudEditorTools", Text = "More…", CustomMinimumSize = new Vector2(80, 32) };
        searchbar.AddChild(more);
        AddChild(searchbar);
        var toolbar = new HBoxContainer(); AddChild(toolbar);
        var role = new OptionButton { Name = "HudEditorRole", CustomMinimumSize = new Vector2(100, 32) }; role.AddItem("Spy"); role.AddItem("Mercenary"); toolbar.AddChild(role);
        role.ItemSelected += i => { _merc = i == 1; _preview.SetRole(_merc); Refresh(); };
        var aspect = new OptionButton { Name = "HudEditorAspect", CustomMinimumSize = new Vector2(150, 32) }; aspect.AddItem("Preview 16:9"); aspect.AddItem("Preview 4:3"); aspect.AddItem("Preview 21:9"); toolbar.AddChild(aspect);
        aspect.ItemSelected += i => _preview.Aspect = i == 1 ? 4f / 3 : i == 2 ? 21f / 9 : 16f / 9;
        Button(toolbar, "Undo", () => History(_undo, _redo)); Button(toolbar, "Redo", () => History(_redo, _undo));
        var snap = new CheckBox { Text = "Snap to grid", ButtonPressed = true }; toolbar.AddChild(snap); snap.Toggled += v => _snap = v;
        var tools = more.GetPopup();
        tools.AddItem("Compact preset", 0); tools.AddItem("Large preset", 1); tools.AddSeparator();
        tools.AddItem("Reset selected elements", 2); tools.AddItem("Reset this role", 3);
        tools.AddItem("Copy this role to other", 4); tools.AddSeparator();
        tools.AddItem("Copy layout", 5); tools.AddItem("Paste layout", 6);
        tools.IdPressed += id =>
        {
            switch (id)
            {
                case 0: Change(() => { foreach (string key in HudEditorSettings.Elements.Keys) Layout.Element(_merc, key).Scale = .8f; }); break;
                case 1: Change(() => { foreach (string key in HudEditorSettings.Elements.Keys) Layout.Element(_merc, key).Scale = 1.25f; }); break;
                case 2: if (_selection.Count > 0) Change(() => ResetElements(_merc, _selection)); break;
                case 3: Change(() => Layout.For(_merc).Clear()); break;
                case 4: Change(() => { var copy = Layout.Clone(); if (_merc) Layout.Spy = copy.Merc; else Layout.Merc = copy.Spy; }); break;
                case 5: DisplayServer.ClipboardSet(JsonSerializer.Serialize(Layout)); _note.Text = "Both layouts copied."; break;
                case 6:
                    try { var imported = HudEditorSettings.Parse(DisplayServer.ClipboardGet()); Change(() => _settings().HudLayout = imported); _note.Text = "Layout imported. Apply to save."; }
                    catch (JsonException) { _note.Text = "Clipboard does not contain a valid HUD layout."; }
                    break;
            }
        };
        _elements.ItemSelected += i => SelectElement(_elements.GetItemMetadata((int)i).AsString(), Input.IsKeyPressed(Key.Ctrl));
        _elements.GuiInput += input =>
        {
            if (_elements.Disabled || _selection.Count == 0 || input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouse) return;
            _elements.AcceptEvent();
            OpenElementMenu(_selected, _elements, mouse.Position);
        };
        _search.TextChanged += _ => RebuildElements();
        var body = new HBoxContainer { Name = "HudEditorBody", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        _sidebar = new ScrollContainer { Name = "HudEditorCategories", CustomMinimumSize = new Vector2(180, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, ClipContents = true };
        var categoryMargin = new MarginContainer { Name = "HudEditorCategoryMargin", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        categoryMargin.AddThemeConstantOverride("margin_left", 2);
        categoryMargin.AddThemeConstantOverride("margin_right", 8);
        categoryMargin.AddThemeConstantOverride("margin_top", 2);
        categoryMargin.AddThemeConstantOverride("margin_bottom", 2);
        _elementRows = new VBoxContainer { Name = "HudEditorCategoryRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        categoryMargin.AddChild(_elementRows);
        _sidebar.AddChild(categoryMargin);
        body.AddChild(_sidebar);
        _sidebarDivider = new ColorRect { Name = "HudEditorPreviewDivider", CustomMinimumSize = new Vector2(1, 0), Color = UiStyle.MenuFocus with { A = .2f }, MouseFilter = MouseFilterEnum.Ignore };
        body.AddChild(_sidebarDivider);
        _preview = new HudEditorPreview(_settings, () => _selected, _selection.Contains) { Name = "HudEditorPreview", CustomMinimumSize = new Vector2(280, 140), SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        body.AddChild(_preview);
        var inspector = new VBoxContainer { Name = "HudEditorInspector", CustomMinimumSize = new Vector2(230, 0) };
        _elementTitle = new Label { Name = "HudEditorSelectedElement", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _elementTitle.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        inspector.AddChild(_elementTitle);
        var fieldsScroll = new ScrollContainer { Name = "HudEditorProperties", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var fields = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        fields.AddThemeConstantOverride("separation", 2);
        fieldsScroll.AddChild(fields);
        inspector.AddChild(fieldsScroll);
        body.AddChild(inspector);
        AddChild(body);
        _x = Field(fields, "X offset %", -100, 100, .1, v => MoveSelection(new Vector2((float)v / 100 - Element().X, 0)));
        _y = Field(fields, "Y offset %", -100, 100, .1, v => MoveSelection(new Vector2(0, (float)v / 100 - Element().Y)));
        _scale = Field(fields, "Size %", 25, 300, 1, v => EditSelection(e => e.Scale = (float)v / 100));
        _opacity = Field(fields, "Opacity %", 10, 100, 1, v => EditSelection(e => e.Opacity = (float)v / 100));
        var visibility = InspectorRow(fields, "Visible");
        _visible = new CheckBox { Name = "HudEditorVisible" }; visibility.AddChild(_visible);
        _visible.Toggled += v => { if (!_refreshing && _selection.Count > 0) Change(() => EditSelection(e => e.Visible = v)); };
        _elementMenu = new PopupMenu { Name = "HudEditorElementMenu" };
        AddChild(_elementMenu);
        _elementMenu.IdPressed += id => { if (id == 0) Change(() => ResetElements(_contextMerc, _contextElements)); };
        _preview.Selected += (id, toggle) => RevealElement(id, toggle, preserve: !toggle);
        _preview.ContextRequested += (id, position) => OpenElementMenu(id, _preview, position);
        _preview.DragStarted += Remember;
        _preview.Moved += delta => { MoveSelection(delta); Refresh(); };
        _preview.DragEnded += () =>
        {
            if (_snap && _selection.Count > 0)
            {
                var e = Element();
                MoveSelection(new Vector2(MathF.Round(e.X * 100) / 100 - e.X, MathF.Round(e.Y * 100) / 100 - e.Y));
            }
            Refresh();
        };
        _note = new Label { Name = "HudEditorHelp", Text = "Ctrl-click to add/remove elements • Drag or arrows to move selection • Right-click to reset • Apply to save.", AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, CustomMinimumSize = new Vector2(0, 44), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; AddChild(_note);
        RebuildElements();
        Refresh();
    }
    public override void _Process(double delta)
    {
        // A wide but short window still needs room for the entire preview above the footer.
        bool compact = Size.X < 1050;
        float previewHeight = GetViewportRect().Size.Y < 900 ? 140 : 260;
        if (_preview.CustomMinimumSize.Y != previewHeight) _preview.CustomMinimumSize = new Vector2(280, previewHeight);
        if (_compact == compact) return;
        _compact = compact;
        _sidebar.Visible = !compact;
        _sidebarDivider.Visible = !compact;
        _elements.Visible = compact;
    }
    private HudElementSettings Element() => Layout.Element(_merc, _selected);
    private void EditSelection(Action<HudElementSettings> edit)
    {
        foreach (string id in _selection) edit(Layout.Element(_merc, id));
    }
    private void MoveSelection(Vector2 delta)
    {
        if (_selection.Count == 0) return;
        // Clamp one shared delta so moving or snapping a group preserves its relative offsets.
        var elements = _selection.Select(id => Layout.Element(_merc, id)).ToArray();
        delta.X = Math.Clamp(delta.X, elements.Max(e => -1 - e.X), elements.Min(e => 1 - e.X));
        delta.Y = Math.Clamp(delta.Y, elements.Max(e => -1 - e.Y), elements.Min(e => 1 - e.Y));
        foreach (var e in elements) { e.X += delta.X; e.Y += delta.Y; e.Clamp(); }
    }
    private void ResetElements(bool merc, IEnumerable<string> ids)
    {
        foreach (string id in ids) Layout.For(merc).Remove(id);
    }
    private void Remember() { _undo.Push(Layout.Clone()); _redo.Clear(); }
    private void Change(Action action) { Remember(); action(); Refresh(); }
    private void History(Stack<HudEditorSettings> from, Stack<HudEditorSettings> to)
    { if (from.Count == 0) return; to.Push(Layout.Clone()); _settings().HudLayout = from.Pop(); Refresh(); }
    private static void Button(Container parent, string text, Action action)
    {
        var b = NavigationButton(text);
        b.CustomMinimumSize = new Vector2(64, 32);
        b.Pressed += action;
        parent.AddChild(b);
    }
    private static Button NavigationButton(string text)
    {
        var b = new Button { Text = text, Flat = true, ClipText = true, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 32), TooltipText = text };
        b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 8, ContentMarginRight = 8 });
        var selected = new StyleBoxFlat { BgColor = UiStyle.MenuFocus with { A = .15f }, ContentMarginLeft = 8, ContentMarginRight = 8 };
        b.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = UiStyle.MenuFocus with { A = .07f }, ContentMarginLeft = 8, ContentMarginRight = 8 });
        b.AddThemeStyleboxOverride("pressed", selected); b.AddThemeStyleboxOverride("hover_pressed", selected);
        // The global SCCT focus frame expands outside its button. Sidebar rows keep all states inside
        // their own hit area, separated from both the scroll bar and the independently interactive preview.
        b.AddThemeStyleboxOverride("focus", new UiBox
        {
            Expand = -1, Notch = 5, Border = UiStyle.MenuFocus with { A = .8f },
            Ticks = UiStyle.MenuFocus, TickLength = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 0, ContentMarginBottom = 0,
        });
        return b;
    }
    private SpinBox Field(Container parent, string title, double min, double max, double step, Action<double> action)
    {
        var box = InspectorRow(parent, title);
        var input = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(100, 32), SelectAllOnFocus = true }; box.AddChild(input);
        void Explain() => _note.Text = title switch
        {
            "X offset %" => "Move the selection horizontally. Shows the primary element's offset as a percentage of screen width.",
            "Y offset %" => "Move the selection vertically. Shows the primary element's offset as a percentage of screen height.",
            "Size %" => "Set the size of all selected HUD elements. 100% uses each element's normal size.",
            _ => "Set the opacity of all selected elements. Fields show the primary element's values.",
        };
        input.FocusEntered += Explain; input.MouseEntered += Explain;
        input.ValueChanged += v => { if (!_refreshing && _selection.Count > 0) Change(() => action(v)); }; return input;
    }
    private static HBoxContainer InspectorRow(Container parent, string title)
    {
        var panel = new PanelContainer();
        panel.SetMeta("hud_editor_property", title);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.025f, .045f, .03f, .55f), BorderColor = new Color(.19f, .3f, .22f, .45f),
            BorderWidthBottom = 1, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var row = new HBoxContainer();
        var label = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeColorOverride("font_color", UiStyle.Text);
        row.AddChild(label); panel.AddChild(row); parent.AddChild(panel);
        return row;
    }
    private void SelectElement(string id, bool toggle = false, bool preserve = false)
    {
        if (toggle && _selection.Remove(id))
        {
            if (_selected == id) _selected = _selection.LastOrDefault() ?? id;
        }
        else
        {
            if (!toggle && !preserve) _selection.Clear();
            _selection.Add(id);
            _selected = id;
        }
        Refresh();
    }
    private void RevealElement(string id, bool toggle = false, bool preserve = false)
    {
        // Rebuilding the list must not replace the current group when clearing a filter.
        SelectElement(id, toggle, preserve && _selection.Contains(id));
        if (!_elementButtons.ContainsKey(id)) { _search.Text = ""; RebuildElements(); }
        Callable.From(() =>
        {
            if (_sidebar.IsVisibleInTree() && _elementButtons.TryGetValue(_selected, out var button))
                _sidebar.EnsureControlVisible(button);
        }).CallDeferred();
    }
    private void OpenElementMenu(string id, Control source, Vector2 localPosition)
    {
        RevealElement(id, preserve: _selection.Contains(id));
        // Keep the action bound to the selection and role that opened it.
        _contextElements = _selection.ToArray();
        _contextMerc = _merc;
        _elementMenu.Clear();
        _elementMenu.AddSeparator(_selection.Count == 1 ? HudEditorSettings.Elements[id] : $"{_selection.Count} elements selected");
        _elementMenu.AddItem("Reset", 0);
        _elementMenu.SetItemTooltip(1, "Restore default position, size, opacity and visibility: " + string.Join(", ", _contextElements.Select(key => HudEditorSettings.Elements[key])));
        _elementMenu.ResetSize();
        _elementMenu.Position = (Vector2I)(GetViewport().GetScreenTransform() * source.GetGlobalTransformWithCanvas() * localPosition);
        _elementMenu.Popup();
    }
    private void RebuildElements()
    {
        string query = _search.Text.Trim();
        var matches = HudEditorSettings.Elements.Where(e => (e.Value + " " + e.Key).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (Node child in _elementRows.GetChildren()) { _elementRows.RemoveChild(child); child.QueueFree(); }
        _elementButtons.Clear(); _elements.Clear();
        foreach (var entry in matches)
        {
            _elements.AddItem(entry.Value); _elements.SetItemMetadata(_elements.ItemCount - 1, entry.Key);
            var button = NavigationButton(entry.Value); button.ToggleMode = true; button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bool? clickToggle = null;
            button.Pressed += () =>
            {
                SelectElement(entry.Key, clickToggle ?? Input.IsKeyPressed(Key.Ctrl));
                clickToggle = null;
            };
            button.GuiInput += input =>
            {
                if (input is InputEventKey { Pressed: true }) clickToggle = null;
                if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } left) clickToggle = left.CtrlPressed;
                if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouse) return;
                button.AcceptEvent();
                OpenElementMenu(entry.Key, button, mouse.Position);
            };
            _elementRows.AddChild(button); _elementButtons[entry.Key] = button;
        }
        if (matches.Length == 0) _elementRows.AddChild(new Label { Text = "No matching elements", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (_selection.Count == 1 && matches.Length > 0 && !matches.Any(e => e.Key == _selected))
        { _selected = matches[0].Key; _selection.Clear(); _selection.Add(_selected); }
        _elements.Disabled = matches.Length == 0;
        Refresh();
    }
    private void Refresh()
    {
        _refreshing = true;
        var e = Layout.For(_merc).GetValueOrDefault(_selected) ?? new HudElementSettings();
        _x.Value = e.X * 100; _y.Value = e.Y * 100; _scale.Value = e.Scale * 100; _opacity.Value = e.Opacity * 100; _visible.ButtonPressed = e.Visible;
        _x.Editable = _y.Editable = _scale.Editable = _opacity.Editable = _selection.Count > 0;
        _visible.Disabled = _selection.Count == 0;
        for (int i = 0; i < _elements.ItemCount; i++) if (_elements.GetItemMetadata(i).AsString() == _selected) _elements.Select(i);
        for (int i = 0; i < _elements.ItemCount; i++) _elements.GetPopup().SetItemChecked(i, _selection.Contains(_elements.GetItemMetadata(i).AsString()));
        foreach ((string id, Button button) in _elementButtons) button.SetPressedNoSignal(_selection.Contains(id));
        string name = HudEditorSettings.Elements.GetValueOrDefault(_selected, _selected);
        _elementTitle.Text = _selection.Count switch { 0 => "No elements selected", 1 => name, _ => $"{_selection.Count} elements selected\nPrimary: {name}" };
        _elements.Text = _selection.Count switch { 0 => "Select an element", 1 => name, _ => $"{name} (+{_selection.Count - 1})" };
        _elementTitle.TooltipText = string.Join(", ", _selection.Select(id => HudEditorSettings.Elements[id]));
        _refreshing = false; _preview.QueueRedraw();
    }
}

public partial class HudEditorPreview : Control
{
    internal const float PreviewPadding = 12;
    private readonly Func<GameSettings> _settings;
    private readonly Func<HudEditorSettings> _layout;
    private readonly Func<string> _selected;
    private readonly Func<string, bool> _isSelected;
    private HudAssets? _assets;
    private HudPage? _page;
    private MagmaCanvas? _canvas;
    private readonly HudActivityFeedDrawer _feed = new();
    private readonly HudFps _fps = new();
    private readonly HudNet _net = new();
    private readonly HudLocation _location = new();
    private ChatPanel? _chat;
    private static readonly HudActivityEntry[] SampleEvents =
    [
        new(1, HudActivityKind.Kill, new(1, "Raven", 0), new(2, "Sentinel", 1), HudActivityCause.NeckBreak, "", "", 0, true),
        new(2, HudActivityKind.Join, new(3, "Echo", 0), null, HudActivityCause.None, "JOIN", "Echo joined the match", 0, false),
    ];
    private readonly Dictionary<string, Rect2> _bounds = new();
    internal IReadOnlyDictionary<string, Rect2> ElementBounds => _bounds;
    internal Rect2 ScreenRect { get; private set; }
    private bool _merc, _dragging;
    private Vector2 _screen, _displaySize;
    private string? _error;
    public event Action<string, bool>? Selected;
    public event Action<string, Vector2>? ContextRequested;
    public event Action? DragStarted, DragEnded;
    public event Action<Vector2>? Moved;
    public HudEditorPreview(Func<GameSettings> settings, Func<string> selected, Func<string, bool> isSelected) { _settings = settings; _layout = () => settings().HudLayout; _selected = selected; _isSelected = isSelected; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All; }
    public override void _Ready()
    {
        _assets = HudAssets.TryLoad(null, out _error);
        _canvas = new MagmaCanvas { Assets = _assets }; AddChild(_canvas);
        if (_assets is not null)
        {
            _feed.Configure(_assets); _fps.Configure(_assets); _net.Configure(_assets);
            for (int i = 0; i < 120; i++) _fps.Add(1f / 144f);
            NetReadout sample = new(true, 47000, 3000, 10000, 5000, 60, 0, 60, 0, 0, 60,
                BufferMs: 67, SnapshotAgeMs: 40);
            _net.Source = () => sample;
            var screen = new Vector2(1280, 720);
            _net.Draw(screen, 0f, true, true, editorPreview: true);
            sample = sample with { InBytes = 25000, OutBytes = 11000, InPackets = 120, OutPackets = 120 };
            _net.Draw(screen, 1f, true, true, editorPreview: true);
        }
        _chat = new ChatPanel { Clock = () => 1, LayoutOverride = () => _layout().For(_merc).GetValueOrDefault("Chat") };
        _chat.Transcript.Add(ChatChannel.All, "Ready when you are", "Raven", 1, 0, 0);
        _chat.Transcript.Add(ChatChannel.Team, "I'll cover the terminal", "Echo", 3, 0, 0);
        AddChild(_chat);
        IgnoreMouse(_chat);
        SetRole(false);
    }
    public void SetRole(bool merc)
    {
        _merc = merc;
        if (_assets is not null) _page = merc ? new MercHudPage(_assets) : new SpyHudPage(_assets);
        QueueRedraw();
    }
    public float Aspect { get; set; } = 16f / 9;
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        QueueRedraw();
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(.035f, .055f, .065f));
        Vector2 available = (Size - Vector2.One * (PreviewPadding * 2)).Max(Vector2.Zero);
        float width = Math.Min(available.X, available.Y * Aspect);
        _displaySize = new Vector2(width, width / Aspect);
        // Render a full-size sample and fit it as a unit. Native chat/feed minimum font sizes
        // must not turn a small editor preview into an unrealistically tiny gameplay viewport.
        _screen = new Vector2(720 * Aspect, 720);
        float fit = _displaySize.Y / _screen.Y;
        Vector2 offset = (Size - _displaySize) / 2;
        ScreenRect = new Rect2(offset, _displaySize);
        DrawRect(new Rect2(offset, _displaySize), new Color(.045f, .07f, .075f));
        for (int i = 1; i < 10; i++)
        {
            DrawLine(offset + new Vector2(_displaySize.X * i / 10, 0), offset + new Vector2(_displaySize.X * i / 10, _displaySize.Y), new Color(1, 1, 1, .06f));
            DrawLine(offset + new Vector2(0, _displaySize.Y * i / 10), offset + new Vector2(_displaySize.X, _displaySize.Y * i / 10), new Color(1, 1, 1, .06f));
        }
        if (_page is null) { DrawString(ThemeDB.FallbackFont, new Vector2(12, 30), "HUD preview unavailable: " + _error, fontSize: 14); return; }
        var sample = new HudSnapshot { Valid = true, HasPawn = true, HasWeapon = true, ClipAmmo = 24, ActiveWeaponName = "Rifle", FireModeName = "Auto", Side = _merc ? HudSide.Merc : HudSide.Spy, Health = 85, EnergyLevel = 70, EnergyLevelMax = 100, Ammo = 96, TimeLimit = 10, ElapsedTime = 125, ShowReticle = true, SpeakPrompt = "Interact", Gadget = new HudGadget("Sample gadget", 3, "", 0, 0) };
        sample.TeamGadgetSlots = 4; sample.Ballistic = .6f;
        sample.HackCountdown = new HudCountdown(18); sample.AlarmSeconds = 12;
        sample.ObjectiveCountdowns.Add(new HudCountdown(35)); sample.ObjectiveCountdowns.Add(new HudCountdown(50));
        sample.SystemMessages.Add("Objective updated"); sample.StateMessages.Add("Ready");
        sample.InfoMessages.Add("Warehouse"); sample.GameMessages.Add("Match in progress");
        sample.Tutorial = "Use your gadgets to support your team";
        sample.Blips.Add(new HudBlip(HudBlipKind.Teammate, new OpenScct.Core.FVector(100, 100, 0)));
        sample.ShowPlayers = true;
        sample.Players.Add(new HudPlayerRow("Raven", 0, false, 2, 3, 12));
        sample.Players.Add(new HudPlayerRow("Echo", 0, false, 1, 2, 18));
        sample.Players.Add(new HudPlayerRow("Sentinel", 1, false, 1, 3, 10));
        _page.Options.StretchHud = !_settings().WidescreenHud;
        PreparePage(_page, sample);
        if (_canvas is not null) { _canvas.Position = offset; _canvas.Scale = Vector2.One * fit; }
        _page.Draw(_screen, sample);
        _location.Draw(_screen, "Warehouse", true, _page);
        _page.DrawList.Items.AddRange(_location.DrawList.Items);
        var layout = _layout().For(_merc);
        _fps.Settings = _settings().FpsTelemetry;
        _net.Settings = _settings().NetworkTelemetry;
        _net.ShowLoss = _settings().ShowNetLoss;
        _fps.Draw(_screen, true, layout.GetValueOrDefault("Fps"), editorPreview: true);
        _net.FpsBounds = _fps.Bounds;
        _net.Draw(_screen, 0f, true, true, layout.GetValueOrDefault("Network"), editorPreview: true);
        _feed.Draw(_screen, SampleEvents, 1, _page, layout: layout.GetValueOrDefault("Activity"), editorPreview: true);
        var raw = _page.LayoutBounds(_screen);
        raw["Activity"] = _feed.Bounds;
        raw["Fps"] = _fps.EditorBounds;
        raw["Network"] = _net.EditorBounds;
        if (_chat is not null)
        {
            _chat.PreviewScreen = _screen;
            _chat.Side = _merc ? HudSide.Merc : HudSide.Spy;
            _chat.Position = offset;
            _chat.Scale = Vector2.One * fit;
            _chat._Process(0);
            raw["Chat"] = _chat.PassiveBaseBounds;
        }
        _bounds.Clear();
        foreach (var entry in raw)
        {
            var e = _layout().For(_merc).GetValueOrDefault(entry.Key) ?? new HudElementSettings();
            var rect = entry.Key is "Activity" or "Fps" or "Network" ? entry.Value : HudEditorTransform.Transform(entry.Value, e, _screen);
            rect = new Rect2(rect.Position * fit + offset, rect.Size * fit);
            _bounds[entry.Key] = rect;
            DrawRect(rect, _isSelected(entry.Key) ? Colors.Cyan : new Color(1, 1, 1, .25f), false, 1);
            if (_isSelected(entry.Key) && entry.Key == _selected()) DrawString(ThemeDB.FallbackFont,
                new Vector2(Math.Clamp(rect.Position.X, ScreenRect.Position.X, Math.Max(ScreenRect.Position.X, ScreenRect.End.X - 160)),
                    Math.Clamp(rect.Position.Y - 4, ScreenRect.Position.Y + 16, Math.Max(ScreenRect.Position.Y + 16, ScreenRect.End.Y - 4))),
                HudEditorSettings.Elements[entry.Key], fontSize: 14);
        }
        HudEditorTransform.Apply(_page.DrawList.Items, layout, _screen, raw);
        _page.DrawList.Items.AddRange(_feed.DrawList.Items);
        _page.DrawList.Items.AddRange(_fps.DrawList.Items);
        _page.DrawList.Items.AddRange(_net.DrawList.Items);
        _canvas?.Submit(_page.DrawList.Items);
    }
    internal static void PreparePage(HudPage page, HudSnapshot sample)
    {
        page.Update(sample, 0);
        foreach (var id in HudEditorSettings.Elements.Keys)
            if (page.Area(id) is { } node)
            {
                node.SetVisible(true, children: false);
                if (id != "Ballistic") node.SetFrame(6);
            }
        // Gameplay intentionally suppresses game messages while a tutorial is shown. The editor
        // displays both samples, so populate the forced-visible area rather than its asset placeholder.
        HudPage.SetText(page.Area("GameMessage"), "messageGame",
            sample.GameMessages.Count > 0 ? sample.GameMessages[0] : "Match in progress");
        // Layout editing is a still sample. In particular, the radar's child timeline must not
        // pulse while its parent is repeatedly rewound; neither frame rate nor redraw count may change it.
        Freeze(page.Root);
        static void Freeze(MagmaNode node, bool radar = false)
        {
            node.Playing = false;
            radar |= node.Name == "Radar";
            if (radar) node.SetFrame(0);
            // Preserve frames explicitly selected by gameplay drivers: Merc throw charge lives
            // inside a wrapper, and the tutorial panel has its own held-open frame.
            foreach (var child in node.Children) Freeze(child, radar);
        }
    }
    private static void IgnoreMouse(Node node)
    {
        if (node is Control control) control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (Node child in node.GetChildren()) IgnoreMouse(child);
    }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button)
        {
            if (!button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.Left) { if (_dragging) DragEnded?.Invoke(); _dragging = false; }
                return;
            }
            if (button.ButtonIndex == MouseButton.Right && _dragging) { DragEnded?.Invoke(); _dragging = false; }
            string? hit = null; float area = float.MaxValue;
            foreach (var entry in _bounds) if (entry.Value.Grow(4).HasPoint(button.Position) && entry.Value.Area < area) { hit = entry.Key; area = entry.Value.Area; }
            if (hit is not null)
            {
                GrabFocus();
                if (button.ButtonIndex == MouseButton.Right) ContextRequested?.Invoke(hit, button.Position);
                else
                {
                    Selected?.Invoke(hit, button.CtrlPressed);
                    if (!button.CtrlPressed && _isSelected(hit)) { DragStarted?.Invoke(); _dragging = true; }
                }
                AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion && _dragging && Size.X > 0 && Size.Y > 0) { Moved?.Invoke(motion.Relative / _displaySize); AcceptEvent(); }
        if (input is InputEventKey { Pressed: true } key)
        {
            Vector2 d = key.Keycode switch { Key.Left => Vector2.Left, Key.Right => Vector2.Right, Key.Up => Vector2.Up, Key.Down => Vector2.Down, _ => Vector2.Zero };
            if (d != Vector2.Zero && _isSelected(_selected())) { DragStarted?.Invoke(); Moved?.Invoke(d * (key.ShiftPressed ? .01f : .001f)); AcceptEvent(); }
        }
    }
}
