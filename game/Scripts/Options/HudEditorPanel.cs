using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Chat;

namespace OpenScctGame.Options;

public partial class HudEditorPanel : VBoxContainer
{
    private readonly Func<GameSettings> _settings;
    private readonly Stack<HudEditorSettings> _undo = new(), _redo = new();
    private bool _merc, _refreshing;
    private string _selected = "Vie";
    private HudEditorPreview _preview = null!;
    private OptionButton _elements = null!;
    private SpinBox _x = null!, _y = null!, _scale = null!, _opacity = null!;
    private CheckBox _visible = null!;
    private Label _note = null!;
    private bool _snap = true;
    private HFlowContainer _tools = null!;
    private CheckBox _showTools = null!;
    private bool? _compact;
    private HudEditorSettings Layout => _settings().HudLayout;
    public HudEditorPanel(Func<GameSettings> settings) { _settings = settings; Name = "HudEditorContent"; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill; }
    public override void _Ready()
    {
        AddChild(new Label { Text = "Drag to move • Arrow keys to nudge • Apply to save", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var toolbar = new HFlowContainer(); AddChild(toolbar);
        var role = new OptionButton { Name = "HudEditorRole" }; role.AddItem("Spy"); role.AddItem("Mercenary"); toolbar.AddChild(role);
        role.ItemSelected += i => { _merc = i == 1; _preview.SetRole(_merc); Refresh(); };
        var aspect = new OptionButton { Name = "HudEditorAspect" }; aspect.AddItem("Preview 16:9"); aspect.AddItem("Preview 4:3"); aspect.AddItem("Preview 21:9"); toolbar.AddChild(aspect);
        aspect.ItemSelected += i => _preview.Aspect = i == 1 ? 4f / 3 : i == 2 ? 21f / 9 : 16f / 9;
        _showTools = new CheckBox { Text = "Layout tools", ButtonPressed = true }; toolbar.AddChild(_showTools);
        _tools = new HFlowContainer(); AddChild(_tools);
        _showTools.Toggled += value => _tools.Visible = value;
        toolbar = _tools;
        Button(toolbar, "Compact preset", () => Change(() => { foreach (string id in HudEditorSettings.Elements.Keys) Layout.Element(_merc, id).Scale = .8f; }));
        Button(toolbar, "Large preset", () => Change(() => { foreach (string id in HudEditorSettings.Elements.Keys) Layout.Element(_merc, id).Scale = 1.25f; }));
        var snap = new CheckBox { Text = "Snap to grid", ButtonPressed = true }; toolbar.AddChild(snap); snap.Toggled += v => _snap = v;
        Button(toolbar, "Undo", () => History(_undo, _redo)); Button(toolbar, "Redo", () => History(_redo, _undo));
        Button(toolbar, "Reset element", () => Change(() => Layout.For(_merc).Remove(_selected)));
        Button(toolbar, "Reset role", () => Change(() => Layout.For(_merc).Clear()));
        Button(toolbar, "Copy role to other", () => Change(() => { var copy = Layout.Clone(); if (_merc) Layout.Spy = copy.Merc; else Layout.Merc = copy.Spy; }));
        Button(toolbar, "Copy layout", () => { DisplayServer.ClipboardSet(JsonSerializer.Serialize(Layout)); _note.Text = "Both layouts copied."; });
        Button(toolbar, "Paste layout", () =>
        {
            try { var imported = HudEditorSettings.Parse(DisplayServer.ClipboardGet()); Change(() => _settings().HudLayout = imported); _note.Text = "Layout imported. Apply to save."; }
            catch (JsonException) { _note.Text = "Clipboard does not contain a valid HUD layout."; }
        });
        _elements = new OptionButton();
        foreach (var entry in HudEditorSettings.Elements) { _elements.AddItem(entry.Value); _elements.SetItemMetadata(_elements.ItemCount - 1, entry.Key); }
        AddChild(_elements); _elements.ItemSelected += i => { _selected = _elements.GetItemMetadata((int)i).AsString(); Refresh(); };
        var fields = new HFlowContainer(); AddChild(fields);
        _x = Field(fields, "X offset %", -100, 100, .1, v => Element().X = (float)v / 100);
        _y = Field(fields, "Y offset %", -100, 100, .1, v => Element().Y = (float)v / 100);
        _scale = Field(fields, "Size %", 25, 300, 1, v => Element().Scale = (float)v / 100);
        _opacity = Field(fields, "Opacity %", 10, 100, 1, v => Element().Opacity = (float)v / 100);
        _visible = new CheckBox { Text = "Visible" }; fields.AddChild(_visible);
        _visible.Toggled += v => { if (!_refreshing) Change(() => Element().Visible = v); };
        _preview = new HudEditorPreview(_settings, () => _selected) { CustomMinimumSize = new Vector2(280, 260), SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        _preview.Selected += id => { _selected = id; Refresh(); };
        _preview.DragStarted += Remember;
        _preview.Moved += delta => { var e = Element(); e.X += delta.X; e.Y += delta.Y; e.Clamp(); Refresh(); };
        _preview.DragEnded += () => { if (_snap) { var e = Element(); e.X = MathF.Round(e.X * 100) / 100; e.Y = MathF.Round(e.Y * 100) / 100; } Refresh(); };
        AddChild(_preview);
        _note = new Label { Text = "Preview uses sample gameplay. Hidden elements keep selectable outlines. Enable FPS, network and zone names in HUD; position them here.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(_note);
        Refresh();
    }
    public override void _Process(double delta)
    {
        // A wide but short window still needs room for the entire preview above the footer.
        bool compact = Size.X < 900 || GetViewportRect().Size.Y < 900;
        if (_compact == compact) return;
        _compact = compact;
        _showTools.ButtonPressed = !compact;
        _note.Visible = !compact;
        _preview.CustomMinimumSize = new Vector2(280, compact ? 140 : 260);
    }
    private HudElementSettings Element() => Layout.Element(_merc, _selected);
    private void Remember() { _undo.Push(Layout.Clone()); _redo.Clear(); }
    private void Change(Action action) { Remember(); action(); Refresh(); }
    private void History(Stack<HudEditorSettings> from, Stack<HudEditorSettings> to)
    { if (from.Count == 0) return; to.Push(Layout.Clone()); _settings().HudLayout = from.Pop(); Refresh(); }
    private static void Button(Container parent, string text, Action action) { var b = new Button { Text = text }; b.Pressed += action; parent.AddChild(b); }
    private SpinBox Field(Container parent, string title, double min, double max, double step, Action<double> action)
    {
        var box = new VBoxContainer(); box.AddChild(new Label { Text = title });
        var input = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(130, 0) }; box.AddChild(input); parent.AddChild(box);
        input.ValueChanged += v => { if (!_refreshing) Change(() => action(v)); }; return input;
    }
    private void Refresh()
    {
        _refreshing = true;
        var e = Layout.For(_merc).GetValueOrDefault(_selected) ?? new HudElementSettings();
        _x.Value = e.X * 100; _y.Value = e.Y * 100; _scale.Value = e.Scale * 100; _opacity.Value = e.Opacity * 100; _visible.ButtonPressed = e.Visible;
        for (int i = 0; i < _elements.ItemCount; i++) if (_elements.GetItemMetadata(i).AsString() == _selected) _elements.Select(i);
        _refreshing = false; _preview.QueueRedraw();
    }
}

public partial class HudEditorPreview : Control
{
    private readonly Func<GameSettings> _settings;
    private readonly Func<HudEditorSettings> _layout;
    private readonly Func<string> _selected;
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
    private bool _merc, _dragging;
    private Vector2 _screen, _displaySize;
    private string? _error;
    public event Action<string>? Selected;
    public event Action? DragStarted, DragEnded;
    public event Action<Vector2>? Moved;
    public HudEditorPreview(Func<GameSettings> settings, Func<string> selected) { _settings = settings; _layout = () => settings().HudLayout; _selected = selected; MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All; }
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
        float width = Math.Min(Size.X, Size.Y * Aspect);
        _displaySize = new Vector2(width, width / Aspect);
        // Render a full-size sample and fit it as a unit. Native chat/feed minimum font sizes
        // must not turn a small editor preview into an unrealistically tiny gameplay viewport.
        _screen = new Vector2(720 * Aspect, 720);
        float fit = _displaySize.Y / _screen.Y;
        Vector2 offset = (Size - _displaySize) / 2;
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
            DrawRect(rect, entry.Key == _selected() ? Colors.Cyan : new Color(1, 1, 1, .25f), false, 1);
            if (entry.Key == _selected()) DrawString(ThemeDB.FallbackFont, new Vector2(Math.Clamp(rect.Position.X, 0, Math.Max(0, Size.X - 160)), Math.Clamp(rect.Position.Y - 4, 16, Size.Y - 4)), HudEditorSettings.Elements[entry.Key], fontSize: 14);
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
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed) { if (_dragging) DragEnded?.Invoke(); _dragging = false; return; }
            string? hit = null; float area = float.MaxValue;
            foreach (var entry in _bounds) if (entry.Value.Grow(4).HasPoint(button.Position) && entry.Value.Area < area) { hit = entry.Key; area = entry.Value.Area; }
            if (hit is not null) { Selected?.Invoke(hit); DragStarted?.Invoke(); _dragging = true; GrabFocus(); AcceptEvent(); }
        }
        if (input is InputEventMouseMotion motion && _dragging && Size.X > 0 && Size.Y > 0) { Moved?.Invoke(motion.Relative / _displaySize); AcceptEvent(); }
        if (input is InputEventKey { Pressed: true } key)
        {
            Vector2 d = key.Keycode switch { Key.Left => Vector2.Left, Key.Right => Vector2.Right, Key.Up => Vector2.Up, Key.Down => Vector2.Down, _ => Vector2.Zero };
            if (d != Vector2.Zero) { DragStarted?.Invoke(); Moved?.Invoke(d * (key.ShiftPressed ? .01f : .001f)); AcceptEvent(); }
        }
    }
}
