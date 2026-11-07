using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;
using LobbyPlayer = OpenScctGame.UI.LobbyPlayer;

namespace OpenScctGame.Smoke;

public sealed class LobbyLayoutSmoke : SmokeTest
{
    public const string TestName = "lobby-layout";
    public override string Name => TestName;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160),
    ];
    private int _frame, _size, _variant;
    private Control? _screen;
    private FixtureLobby? _session;
    private IReadOnlyList<(string Id, string Title)>? _maps;
    private MapBrowserView _savedMapView;
    private string? _savedPlaylist;
    private bool _liveResize;
    private Button? _lastMapTile;
    private Texture2D? _previewTexture;
    private LobbySetup? _beforeMapSearch;
    private MenuPilot? _pilot;
    private int _numberStage;
    private float _pictureMapHeight;
    private int _mapGroupBeforeView;
    private int _randomMapStage, _randomMapReadyFrame;
    private LobbySetup? _beforeRandomMap;
    private string? _expectedRandomMap;

    private Vector2I ViewportSize => _liveResize ? new Vector2I(800, 600) : _sizes[_size];
    private string Resolution => $"{ViewportSize.X}x{ViewportSize.Y}" + (_liveResize ? "-resized" : "");

    public override SmokeResult Step(SmokeContext context)
    {
        if (_maps is null)
        {
            _pilot = new MenuPilot(context.Tree);
            _savedMapView = SettingsStore.Current.MapView;
            _savedPlaylist = SettingsStore.Current.ActiveMapPlaylist;
            SettingsStore.Current.ActiveMapPlaylist = MapCatalog.AllMapsPlaylist;
            // Create the same catalog service the live lobby uses for its embedded map browser.
            MapCatalog catalog = OnlineService.Get(context.Tree).Catalog.Maps;
            _maps = catalog.GetHostMaps().Select(map => (map.Id, MapTitles.Of(catalog, map.Id))).ToArray();
            if (_maps.Count == 0)
                _maps = Enumerable.Range(1, 80).Select(i => ($"LayoutMap{i}", $"Map {i:00}")).ToArray();
        }
        if (_screen is null)
        {
            Resize(context.Tree, _sizes[_size]);
            _session = new FixtureLobby { CanControl = _variant != 1, Maps = _maps };
            if (_variant == 3) _session.FillCapacityRoster();
            if (_maps.Any(map => string.Equals(map.Id, "AquaE", StringComparison.OrdinalIgnoreCase)))
                _session.Setup = _session.Setup with { Map = "AquaE" };
            SettingsStore.Current.MapListView = false;
            if (_variant == 2)
            {
                var offline = new LobbyScreen { Session = _session }; _screen = offline;
                context.Tree.Root.AddChild(offline);
            }
            else
            {
                var room = new LobbyRoomScreen { Session = _session }; _screen = room;
                context.Tree.Root.AddChild(room);
            }
            context.Tree.CurrentScene = _screen;
            if (_screen is MenuPage page) page._Process(1.0);
            _frame = 0;
            _numberStage = 0;
            _randomMapStage = 0;
        }
        _pilot!.Update();
        if (++_frame < 10) return SmokeResult.Running;
        if (_screen is LobbyRoomScreen fullRoom && _variant == 3)
        {
            if (_frame == 10)
            {
                CheckLayout(fullRoom, "Full 12v12 lobby");
                Expect(_session!.Members.Count == 26, "24 players and two spectators are listed");
                foreach (int id in Enumerable.Range(1, 26))
                    Expect(fullRoom.FindChild("Player" + id, true, false) is Control, "full roster contains player " + id);
                EnsureVisible((Control)fullRoom.FindChild("Player12", true, false)!);
                return SmokeResult.Running;
            }
            if (_frame < 16) return SmokeResult.Running;
            if (_frame == 16)
            {
                Expect(IsReachable((Control)fullRoom.FindChild("Player12", true, false)!), "twelfth spy can be reached by scrolling");
                EnsureVisible((Control)fullRoom.FindChild("Player24", true, false)!);
                return SmokeResult.Running;
            }
            if (_frame < 22) return SmokeResult.Running;
            if (_frame == 22)
            {
                Expect(IsReachable((Control)fullRoom.FindChild("Player24", true, false)!), "twelfth mercenary can be reached by scrolling");
                EnsureVisible((Control)fullRoom.FindChild("Player26", true, false)!);
                return SmokeResult.Running;
            }
            if (_frame < 28) return SmokeResult.Running;
            Expect(IsReachable((Control)fullRoom.FindChild("Player26", true, false)!), "spectators remain reachable with 24 players");
            foreach (Button button in fullRoom.PageButtons)
                Expect(ViewportRect.Encloses(button.GetGlobalRect()), "full lobby footer action visible: " + button.Name);
            MenuPage.Shot(fullRoom, $"lobby-12v12-{Resolution}");
        }
        else if (_screen is LobbyRoomScreen roomScreen)
        {
            if (_frame == 10)
            {
                CheckInteractions(roomScreen, _session!);
                // Exercise an actual texture: empty placeholders hid the tiny-image regression.
                var texture = MapImages.Preview(null, "AquaE");
                if (texture is null)
                {
                    using var image = Image.CreateEmpty(256, 256, false, Image.Format.Rgba8);
                    image.Fill(Colors.SeaGreen);
                    texture = ImageTexture.CreateFromImage(image);
                }
                roomScreen.MapPicture!.SetMap(texture, "Enhanced Aquarius");
                _previewTexture = texture;
                return SmokeResult.Running;
            }
            if (_frame < 16) return SmokeResult.Running;
            if (_frame == 16)
            {
                // Deferred focus changes from the interaction fixture may have refreshed its absent TestBox art.
                roomScreen.MapPicture!.SetMap(_previewTexture, "Enhanced Aquarius");
                CheckLayout(roomScreen, "Lobby overview");
                CheckLandmarks(roomScreen);
                MenuPage.Shot(roomScreen, $"lobby-overview-{Resolution}-{_variant}");
                Click(roomScreen, "MatchSettings");
                return SmokeResult.Running;
            }
            if (_frame < 22) return SmokeResult.Running;
            if (_frame == 22)
            {
                if (_session!.CanControl && _numberStage < 4)
                {
                    // Exercise the first edit from a clean draft through actual mouse/key input. In particular,
                    // Apply must work on the first click while a numeric editor still owns keyboard focus.
                    _frame = 21;
                    if (_pilot.Busy) return SmokeResult.Running;
                    var duration = (SpinBox)roomScreen.ParameterRows!.Find("TimeLimitValue")!;
                    if (_numberStage == 0)
                    {
                        Expect(roomScreen.Parameters!.Setup == _session.Setup, "real numeric input starts from a clean draft");
                        // SpinBox enters text editing on a deferred callback, which resets its text. Let that
                        // native transition settle before typing, as a person does after clicking the field.
                        if (_pilot.Click(duration.GetLineEdit(), "focus match duration")) _numberStage = 1;
                    }
                    else if (_numberStage == 1)
                    {
                        if (_pilot.Type(duration.GetLineEdit(), "9", "match duration")) _numberStage = 2;
                    }
                    else if (_numberStage == 2)
                    {
                        Expect(_session.Setup.TimeLimit == 8, "typing leaves the live match unchanged");
                        if (_pilot.Click(roomScreen.ApplyButton, "Apply typed match duration")) _numberStage = 3;
                    }
                    else
                    {
                        Expect(_session.Setup.TimeLimit == 9 && !roomScreen.MatchSettingsPanel!.Visible,
                            "first Apply click commits a typed number from a clean draft");
                        Click(roomScreen, "MatchSettings");
                        _numberStage = 4;
                        _frame = 16;
                    }
                    return SmokeResult.Running;
                }
                CheckLayout(roomScreen, "Lobby Game settings");
                CheckSettings(roomScreen);
                CheckFormPermissions(roomScreen);
                MenuPage.Shot(roomScreen, $"lobby-settings-game-{Resolution}-{_variant}");
                ShowSettingsTab(roomScreen, 1);
                return SmokeResult.Running;
            }
            if (_frame < 28) return SmokeResult.Running;
            if (_frame == 28)
            {
                CheckLayout(roomScreen, "Lobby Rules settings");
                CheckSettings(roomScreen);
                Expect(IsReachable(roomScreen.ParameterRows!.Find("SpyLivesValue")!), "first Rules field is reachable");
                MenuPage.Shot(roomScreen, $"lobby-settings-rules-{Resolution}-{_variant}");
                EnsureVisible(roomScreen.AlternateButton!);
                return SmokeResult.Running;
            }
            if (_frame < 34) return SmokeResult.Running;
            if (_frame == 34)
            {
                Expect(IsReachable(roomScreen.AlternateButton!), "last Rules action is reachable by scrolling");
                roomScreen.AlternateButton!.GrabFocus();
                Click(roomScreen, "Alternate");
                return SmokeResult.Running;
            }
            if (_frame < 40) return SmokeResult.Running;
            if (_frame == 40)
            {
                CheckAlternate(roomScreen);
                Expect(roomScreen.ApplyButton!.FocusMode == Control.FocusModeEnum.None, "alternate dialog removes keyboard focus from underlying settings");
                var alternate = (AlternateModePanel)roomScreen.FindChild("AlternateMode", true, false)!;
                ((ScrollContainer)alternate.FindChild("Scroll", true, false)!).EnsureControlVisible((Control)alternate.FindChild("MercSlots", true, false)!);
                if (_session!.CanControl)
                {
                    var ability = (CheckBox)alternate.FindChild("SpyGrab", true, false)!;
                    ability.ButtonPressed = !ability.ButtonPressed;
                    Expect(alternate.Rules != roomScreen.Parameters!.Setup.Alternate, "alternate changes wait for Save rules");
                }
                return SmokeResult.Running;
            }
            if (_frame < 46) return SmokeResult.Running;
            if (_frame == 46)
            {
                var alternate = (AlternateModePanel)roomScreen.FindChild("AlternateMode", true, false)!;
                var scroll = (ScrollContainer)alternate.FindChild("Scroll", true, false)!;
                Expect(scroll.GetGlobalRect().Grow(1).Encloses(((Control)alternate.FindChild("MercSlots", true, false)!).GetGlobalRect()),
                    "last alternate rule is reachable by scrolling");
                var live = _session!.Setup;
                var rules = alternate.Rules;
                Click(alternate, _session.CanControl ? "AlternateOk" : "AlternateClose");
                if (_session.CanControl)
                    Expect(roomScreen.Parameters!.Setup.Alternate == rules && _session.Setup == live, "Save rules updates the draft without applying it to the match");
                return SmokeResult.Running;
            }
            if (_frame < 52) return SmokeResult.Running;
            if (_frame == 52)
            {
                Expect(roomScreen.GetViewport().GuiGetFocusOwner() == roomScreen.AlternateButton, "closing alternate restores keyboard focus to its settings action");
                Expect(roomScreen.MainButton!.FocusMode == Control.FocusModeEnum.None, "closing alternate keeps the lobby behind settings unfocusable");
                ShowSettingsTab(roomScreen, 0);
                if (_session!.CanControl)
                {
                    SettingsStore.Current.MapListView = false;
                    RevealMaps(roomScreen);
                }
                return SmokeResult.Running;
            }
            if (_session!.CanControl)
            {
                if (_frame < 58) return SmokeResult.Running;
                if (_frame == 58)
                {
                    CheckMapChooser(roomScreen, list: false);
                    ScrollToLastMap(roomScreen);
                    return SmokeResult.Running;
                }
                if (_frame < 64)
                {
                    if (_frame == 61 && _lastMapTile is not null) EnsureVisible(_lastMapTile);
                    return SmokeResult.Running;
                }
                if (_frame == 64)
                {
                    CheckLastMapVisible(roomScreen);
                    SettingsStore.Current.MapListView = true;
                    return SmokeResult.Running;
                }
                if (_frame < 70) return SmokeResult.Running;
                if (_frame == 70)
                {
                    CheckMapChooser(roomScreen, list: true);
                    ScrollToLastMap(roomScreen);
                    return SmokeResult.Running;
                }
                if (_frame < 76)
                {
                    if (_frame == 73 && _lastMapTile is not null) EnsureVisible(_lastMapTile);
                    return SmokeResult.Running;
                }
                if (_frame == 76)
                {
                    CheckLastMapVisible(roomScreen);
                    _beforeMapSearch = roomScreen.Parameters!.Setup;
                    var search = roomScreen.SettingsPicker!.SearchBox!;
                    EnsureVisible(search);
                    search.Text = "__no_lobby_map_matches__";
                    search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
                    _mapGroupBeforeView = roomScreen.SettingsPicker.PlaylistBox!.Selected;
                    var view = roomScreen.SettingsPicker.Grid.ViewToggle;
                    Expect(view.ItemCount == 3 && view.GetItemText(2) == "Thumbnail list", "map display offers thumbnail rows");
                    view.Select(2);
                    view.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
                    return SmokeResult.Running;
                }
                if (_frame < 82) return SmokeResult.Running;
                if (_frame == 82)
                {
                    var picker = roomScreen.SettingsPicker!;
                    Expect(!picker.Grid.GetChildren().OfType<Button>().Any(tile => tile.Visible) &&
                        picker.FindChild("MapEmpty", true, false) is Label { Visible: true }, "map search has an honest empty state");
                    Expect(roomScreen.Parameters!.Setup == _beforeMapSearch, "map filters do not change the settings draft");
                    Expect(SettingsStore.Current.MapView == MapBrowserView.ThumbnailList &&
                        picker.Grid.ViewToggle.Selected == 2 && picker.PlaylistBox!.Selected == _mapGroupBeforeView &&
                        picker.SearchBox!.Text == "__no_lobby_map_matches__", "switching map view preserves the active search and group");
                    CheckLayout(roomScreen.MatchSettingsPanel!, "Lobby empty map search");
                    CheckSettings(roomScreen);
                    MenuPage.Shot(roomScreen, $"lobby-map-empty-{Resolution}");
                    Click(picker, "ResetMapFilters");
                    return SmokeResult.Running;
                }
                if (_frame < 88) return SmokeResult.Running;
                if (_frame == 88)
                {
                    CheckMapChooser(roomScreen, list: true, thumbnails: true);
                    ScrollToLastMap(roomScreen);
                    return SmokeResult.Running;
                }
                if (_frame == 92 && _lastMapTile is not null) EnsureVisible(_lastMapTile);
            }
            if (_frame < 94) return SmokeResult.Running;
            if (_frame == 94)
            {
                if (_session!.CanControl)
                {
                    CheckLastMapVisible(roomScreen);
                    var picker = roomScreen.SettingsPicker!;
                    if (_size == 0 && !_liveResize) CheckMapFilters(roomScreen);
                    var tile = picker.Grid.GetChildren().OfType<Button>().FirstOrDefault(button => button.Visible && !button.ButtonPressed);
                    Expect(tile is not null, "reset filters restores selectable maps");
                    if (tile is not null)
                    {
                        var live = _session.Setup;
                        tile.EmitSignal(BaseButton.SignalName.Pressed);
                        Expect(roomScreen.Parameters!.Setup.Map == tile.Name.ToString()[4..] && _session.Setup == live,
                            "embedded map selection changes the draft without applying");
                        roomScreen.Parameters.Load(live);
                    }
                    if (_size == 4 && _lastMapTile is { } lastMapTile)
                        CheckMapDoubleClick(roomScreen, _session, lastMapTile);
                }
                ShowSettingsTab(roomScreen, 0);
                return SmokeResult.Running;
            }
            if (_frame == 96 && !roomScreen.MatchSettingsPanel!.Visible) Click(roomScreen, "MatchSettings");
            if (_frame < 100) return SmokeResult.Running;
            if (_frame == 100)
            {
                EnsureVisible(roomScreen.ParameterRows!.Find("MercBotsValue")!);
                return SmokeResult.Running;
            }
            if (_frame < 106) return SmokeResult.Running;
            if (_frame == 106)
            {
                Expect(IsReachable(roomScreen.ParameterRows!.Find("MercBotsValue")!), "exact bot count is reachable in the Game tab");
                if (roomScreen.FindChild("SettingsRecordDemos", true, false) is Control recording) EnsureVisible(recording);
                return SmokeResult.Running;
            }
            if (_frame < 112) return SmokeResult.Running;
            if (_frame == 112)
            {
                if (roomScreen.FindChild("SettingsRecordDemos", true, false) is CheckBox recordDemos)
                    Expect(IsReachable(recordDemos) && !recordDemos.Disabled, "personal recording is reachable for hosts and clients");
                else Expect(false, "settings contain a personal recording control");
                CheckLayout(roomScreen, "Lobby player and recording settings");
                CheckSettings(roomScreen);
                MenuPage.Shot(roomScreen, $"lobby-settings-recording-{Resolution}-{_variant}");
                if (_size == _sizes.Length - 1 && !_liveResize)
                {
                    // Shrink the same modal, tabs and map grid instead of rebuilding them at the new resolution.
                    _liveResize = true;
                    Resize(context.Tree, ViewportSize);
                    SettingsStore.Current.MapListView = false;
                    ShowSettingsTab(roomScreen, 0);
                    _frame = 16;
                    return SmokeResult.Running;
                }
                roomScreen.MatchSettingsPanel!.Visible = false;
            }
            if (_variant == 0 && (_size == 0 || _size == 4) && !StepRandomMap(roomScreen, _session!))
                return SmokeResult.Running;
            if (_liveResize)
            {
                roomScreen.MapPicture!.SetMap(_previewTexture, "Enhanced Aquarius");
                CheckLayout(roomScreen, "Lobby after live resize");
                CheckLandmarks(roomScreen);
            }
        }
        else
        {
            CheckLayout(_screen, "Offline lobby");
            foreach (Button button in ((MenuPage)_screen).PageButtons)
                Expect(ViewportRect.Encloses(button.GetGlobalRect()), "offline footer action visible: " + button.Name);
            MenuPage.Shot(_screen, $"lobby-offline-{Resolution}");
        }
        _screen.QueueFree(); _screen = null;
        _liveResize = false;
        if (++_variant < 4) return SmokeResult.Running;
        _variant = 0;
        if (++_size < _sizes.Length) return SmokeResult.Running;
        SettingsStore.Current.MapView = _savedMapView;
        SettingsStore.Current.ActiveMapPlaylist = _savedPlaylist!;
        SettingsStore.Save(SettingsStore.Current);
        return _problems.Count == 0 ? SmokeResult.Pass("host/client/offline and full 12v12 rosters with spectators, Game and Rules tabs, exact bot counts and player slots, personal recording, editable numbers and draft/apply/discard, alternate rules, embedded map pictures/list/thumbnail rows/search and five map groups, double-click map apply, random map playlist/cancel/apply, 9 resolutions from 800x600 to 3840x2160 and live resize from 4K to 800x600")
            : SmokeResult.Fail(string.Join("; ", _problems.Take(15)));
    }

    private Rect2 ViewportRect => new(Vector2.Zero, ViewportSize);

    private void CheckLayout(Node root, string where)
    {
        _problems.AddRange(LayoutCheck.Problems(root, ViewportSize.X).Select(p => $"{where} {Resolution} variant {_variant}: {p}"));
        _problems.AddRange(LayoutCheck.BoundsProblems(root, ViewportSize).Select(p => $"{where} {Resolution} variant {_variant}: {p}"));
    }

    private void ScrollToLastMap(LobbyRoomScreen room)
    {
        if (room.SettingsPicker is not { } picker || picker.Grid.GetParent() is not ScrollContainer scroll) return;
        _lastMapTile = picker.Grid.GetChildren().OfType<Button>().LastOrDefault(tile => tile.IsVisibleInTree());
        // Settle the inner scroll before revealing its tile in the outer settings scroll.
        if (_lastMapTile is not null) scroll.EnsureControlVisible(_lastMapTile);
    }

    private void CheckLastMapVisible(LobbyRoomScreen room)
    {
        if (_lastMapTile is null || room.FindChild("MapScroll", true, false) is not ScrollContainer scroll)
        {
            Expect(false, "last map can be reached");
            return;
        }
        Rect2 tileRect = _lastMapTile.GetGlobalRect(), scrollRect = scroll.GetGlobalRect();
        // Scroll offsets round to logical pixels before the menu canvas scales them into global coordinates.
        Transform2D transform = scroll.GetGlobalTransform();
        float rounding = Math.Max(transform.X.Length(), transform.Y.Length()) + 0.01f;
        string diagnostic = $"view={SettingsStore.Current.MapView}, tile={tileRect}, scroll={scrollRect}, " +
            $"scale={transform.Y.Length():0.###}, rounding={rounding:0.###}, offset={scroll.ScrollVertical}";
        Expect(scrollRect.Grow(rounding).Encloses(tileRect), "last map is fully visible after scrolling: " + diagnostic);
        var outer = room.FindChild("SettingsScroll", true, false) as ScrollContainer;
        Expect(IsReachable(_lastMapTile), "last map is reachable through both nested scroll areas: " + diagnostic +
            $", outer={outer?.GetGlobalRect()}, outerOffset={outer?.ScrollVertical}");
        if (((Control)_lastMapTile.GetParent()).Size.Y > scroll.Size.Y)
            Expect(scroll.ScrollVertical > 0, "long map browser scrolls down to its last map");
    }

    private void CheckSettings(LobbyRoomScreen room)
    {
        Control panel = room.MatchSettingsPanel!;
        Expect(ViewportRect.Encloses(panel.GetGlobalRect()), "settings panel stays inside viewport");
        Expect(panel.Size.X <= 1500.01f && panel.Size.Y <= 820.01f, "settings keep a readable logical maximum size");
        Expect(room.SettingsTabs?.GetTabCount() == 2 && room.SettingsTabs.GetTabTitle(0) == "Game" &&
            room.SettingsTabs.GetTabTitle(1) == "Rules", "settings separate game and rules");
        if (panel.FindChild("SettingsColumns", true, false) is BoxContainer columns &&
            panel.FindChild("MapSelection", true, false) is Control maps && room.SettingsTabs is { } tabs)
        {
            Rect2 mapRect = maps.GetGlobalRect(), tabRect = tabs.GetGlobalRect();
            Expect(columns.Vertical ? mapRect.End.Y <= tabRect.Position.Y + 1 : mapRect.End.X <= tabRect.Position.X + 1,
                "map browser and settings reflow without overlapping");
        }
        foreach (Button button in panel.FindChildren("*", nameof(Button), true, false).OfType<Button>())
            if (button.IsVisibleInTree() && button.GetParent().Name == "SettingsButtons")
                Expect(panel.GetGlobalRect().Encloses(button.GetGlobalRect()) && ViewportRect.Encloses(button.GetGlobalRect()),
                    "settings action stays visible: " + button.Name);
    }

    private void CheckFormPermissions(LobbyRoomScreen room)
    {
        bool editable = _session!.CanControl;
        foreach (string name in new[] { "ModeChoice", "VoiceChatChoice", "BotsChoice", "BotSkillChoice" })
            Expect(room.ParameterRows!.Find(name) is OptionButton choice && choice.Disabled != editable, "dropdown permissions: " + name);
        foreach (string name in new[] { "TimeLimitValue", "MaxPlayersValue", "SpyBotsValue", "MercBotsValue", "SpyLivesValue", "MercLivesValue", "RespawnValue" })
            Expect(room.ParameterRows!.Find(name) is SpinBox number && number.Editable == editable, "number permissions: " + name);
        Expect(room.ParameterRows!.Find("RevengeCheck") is CheckBox revenge && revenge.Disabled != editable, "rule permissions");
        Expect(room.ApplyButton!.Visible == editable, "only hosts can apply settings");
        Expect((room.SettingsPicker?.IsVisibleInTree() ?? false) == editable, "clients see the selected map without a mutable map browser");
        Expect(room.ParameterRows.Find("SpyBotsValue")!.IsVisibleInTree() && room.ParameterRows.Find("MercBotsValue")!.IsVisibleInTree(),
            "custom team counts are visible in the Game tab");
        Expect(room.FindChild("SettingsRecordDemos", true, false) is CheckBox { Disabled: false },
            "personal recording remains editable independently of match permissions");
    }

    private static void ShowSettingsTab(LobbyRoomScreen room, int tab)
    {
        room.SettingsTabs!.CurrentTab = tab;
        if (room.SettingsTabs.GetCurrentTabControl() is ScrollContainer scroll) scroll.ScrollVertical = 0;
        if (room.FindChild("SettingsScroll", true, false) is ScrollContainer bodyScroll)
            bodyScroll.ScrollVertical += Mathf.RoundToInt((room.SettingsTabs.GetGlobalRect().Position.Y - bodyScroll.GetGlobalRect().Position.Y) /
                bodyScroll.GetGlobalTransform().Y.Length());
    }

    private static void RevealMaps(LobbyRoomScreen room)
    {
        if (room.FindChild("SettingsScroll", true, false) is ScrollContainer scroll) scroll.ScrollVertical = 0;
    }

    private static void EnsureVisible(Control control)
    {
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(control);
    }

    private bool IsReachable(Control control)
    {
        Rect2 rect = control.GetGlobalRect();
        if (!control.IsVisibleInTree() || !ViewportRect.Grow(1).Encloses(rect)) return false;
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll)
            {
                // Godot stores scroll offsets as integer logical pixels, including on a scaled menu canvas.
                float rounding = 2f * Math.Max(1f, scroll.GetGlobalTransform().Y.Length());
                if (!scroll.GetGlobalRect().Grow(rounding).Encloses(rect)) return false;
            }
        return true;
    }

    private void CheckAlternate(LobbyRoomScreen room)
    {
        if (room.FindChild("AlternateMode", true, false) is not AlternateModePanel alternate ||
            alternate.FindChild("AlternatePanel", true, false) is not Control panel)
        {
            Expect(false, "alternate rules opens from match settings");
            return;
        }
        CheckLayout(alternate, "Alternate rules");
        Expect(ViewportRect.Encloses(panel.GetGlobalRect()), "alternate panel stays inside viewport");
        foreach (string name in _session!.CanControl ? new[] { "AlternateClose", "AlternateOk" } : new[] { "AlternateClose" })
            Expect(panel.GetGlobalRect().Encloses(((Control)alternate.FindChild(name, true, false)!).GetGlobalRect()), "alternate footer is visible: " + name);
        Expect(((CheckBox)alternate.FindChild("SpyGrab", true, false)!).Disabled != _session.CanControl, "alternate permissions follow host");
        MenuPage.Shot(room, $"lobby-alternate-{Resolution}-{_variant}");
    }

    private void CheckMapChooser(LobbyRoomScreen room, bool list, bool thumbnails = false)
    {
        if (room.MatchSettingsPanel is not Control panel || room.SettingsPicker is not { } picker ||
            picker.FindChild("MapScroll", true, false) is not ScrollContainer scroll)
        {
            Expect(false, "map browser is embedded in match settings");
            return;
        }
        CheckLayout(panel, thumbnails ? "Map thumbnail list" : list ? "Map list" : "Map pictures");
        Expect(Math.Abs(scroll.GetGlobalRect().End.Y - picker.GetGlobalRect().End.Y) <= 1f,
            "map scroll uses all available picker height");
        Expect(scroll.Size.Y >= 150, "map browser has a usable scrolling area");
        Expect(picker.Grid.GetChildCount() > 0, "map chooser contains maps");
        Expect(picker.Grid.Columns == 1 || !list, "map list uses one column");
        if (!list) _pictureMapHeight = picker.Grid.GetChildren().OfType<Button>().FirstOrDefault(tile => tile.Visible)?.Size.Y ?? 0;
        if (thumbnails) _problems.AddRange(ThumbnailProblems(picker.Grid, _pictureMapHeight).Select(problem => $"{Resolution}: {problem}"));
        CheckSettings(room);
        MenuPage.Shot(room, $"lobby-map-{(thumbnails ? "thumbnails" : list ? "list" : "pictures")}-{Resolution}");
    }

    internal static IEnumerable<string> ThumbnailProblems(MapBrowserGrid grid, float pictureMapHeight)
    {
        if (grid.Columns != 1) yield return "Map thumbnail list has more than one column";
        foreach (Button tile in grid.GetChildren().OfType<Button>().Where(tile => tile.Visible))
        {
            string id = tile.Name.ToString()[4..];
            if (tile.GetNodeOrNull<Control>("Row") is not { Visible: true } row ||
                row.GetNodeOrNull<Control>("MapThumb") is not { } picture ||
                row.GetNodeOrNull<Label>("Details/Title") is not { } title ||
                row.GetNodeOrNull<Label>("Details/Code") is not { } code)
            {
                yield return "Map thumbnail row is missing its picture, title or code: " + id;
                continue;
            }
            if (tile.Size.Y > 88 || tile.Size.Y < 57 || tile.Size.X < grid.Size.X - 20 ||
                (pictureMapHeight > 0 && tile.Size.Y >= pictureMapHeight))
                yield return "Map thumbnail row is not compact and full width: " + id;
            Rect2 bounds = tile.GetGlobalRect().Grow(1f);
            if (!bounds.Encloses(picture.GetGlobalRect()) || !bounds.Encloses(title.GetGlobalRect()) ||
                !bounds.Encloses(code.GetGlobalRect()))
                yield return "Map thumbnail content extends outside its row: " + id;
            if (picture.Size.X < 64 || picture.Size.X > 100 || picture.Size.Y < 36 || picture.Size.Y > 60)
                yield return "Map thumbnail is not a small landscape preview: " + id;
            if (picture.GetGlobalRect().End.X > Math.Min(title.GetGlobalRect().Position.X, code.GetGlobalRect().Position.X) + 1f ||
                title.GetGlobalRect().End.Y > code.GetGlobalRect().Position.Y + 1f)
                yield return "Map thumbnail, title and code overlap: " + id;
            if (string.IsNullOrWhiteSpace(title.Text) || !string.Equals(code.Text, id, StringComparison.OrdinalIgnoreCase) ||
                title.Size.Y < 12 || code.Size.Y < 10)
                yield return "Map thumbnail row has no readable title and short code: " + id;
        }
    }

    private void CheckMapFilters(LobbyRoomScreen room)
    {
        var picker = room.SettingsPicker!;
        var box = picker.PlaylistBox!;
        Expect(Enumerable.Range(0, box.ItemCount).Select(box.GetItemText).SequenceEqual(new[]
        {
            "All installed maps", "Enhanced maps", "Stock maps", "Community maps", "3v3 optimised maps",
        }), "map group dropdown offers all five groups");
        var catalog = OnlineService.Get(room.GetTree()).Catalog.Maps;
        var filters = catalog.GetMapFilters();
        if (box.ItemCount != filters.Count) return;
        var tiles = picker.Grid.GetChildren().OfType<Button>().ToArray();
        var allVisible = tiles.Where(tile => tile.Visible).ToArray();
        var draft = room.Parameters!.Setup;
        var live = _session!.Setup;
        for (int i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            bool all = filter.Name == MapCatalog.AllMapsPlaylist;
            var included = new HashSet<string>(filter.Maps, StringComparer.OrdinalIgnoreCase);
            box.Select(i);
            box.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i);
            Expect(SettingsStore.Current.ActiveMapPlaylist == filter.Name, "map group choice is remembered: " + filter.Name);
            Expect((picker.Model.MapFilter is null) == all, "map arrows receive the selected group: " + filter.Name);
            foreach (var tile in tiles)
            {
                string id = tile.Name.ToString()[4..];
                bool inGroup = all || included.Contains(id);
                bool modePlays = catalog.GetCatalogEntry(id)?.Mode(draft.Mode) is not { Playable: false };
                Expect(tile.Visible == (inGroup && modePlays), "map group shows exactly its playable maps: " + filter.Name + "/" + id);
                Expect((picker.Model.MapFilter?.Invoke(id) ?? true) == inGroup,
                    "map arrows and grid use the same group: " + filter.Name + "/" + id);
            }
            if (!all && !included.Contains(draft.Map) && picker.Tile(draft.Map) is { } selected)
                Expect(!selected.Visible, "selected map outside the group is hidden: " + filter.Name);

            var groupVisible = tiles.Where(tile => tile.Visible).ToArray();
            if (groupVisible.FirstOrDefault() is { } first)
            {
                string query = first.Name.ToString()[4..].ToLowerInvariant();
                var search = picker.SearchBox!;
                search.Text = query;
                search.EmitSignal(LineEdit.SignalName.TextChanged, query);
                var matches = groupVisible.Where(tile => tile.TooltipText.Contains(query, StringComparison.OrdinalIgnoreCase));
                Expect(first.Visible && tiles.Where(tile => tile.Visible).SequenceEqual(matches),
                    "map code search combines with the selected group: " + filter.Name);
                search.Text = string.Empty;
                search.EmitSignal(LineEdit.SignalName.TextChanged, string.Empty);
                Expect(tiles.Where(tile => tile.Visible).SequenceEqual(groupVisible), "clearing search keeps the selected group: " + filter.Name);
            }
            Expect(room.Parameters.Setup == draft && _session.Setup == live, "map group filtering keeps the selected match unchanged: " + filter.Name);
        }
        Click(picker, "ResetMapFilters");
        Expect(box.Selected == 0 && picker.Model.MapFilter is null && picker.SearchBox!.Text.Length == 0 &&
            SettingsStore.Current.ActiveMapPlaylist == MapCatalog.AllMapsPlaylist &&
            tiles.Where(tile => tile.Visible).SequenceEqual(allVisible), "reset clears the group and restores all playable maps");
        if (picker.Tile("AquaE") is { } aqua)
        {
            foreach (string query in new[] { "AquaE", "aquae" })
            {
                picker.SearchBox!.Text = query;
                picker.SearchBox.EmitSignal(LineEdit.SignalName.TextChanged, query);
                Expect(aqua.Visible && tiles.Count(tile => tile.Visible) == 1,
                    "map code search finds Enhanced Aquarius: " + query);
                Expect(box.Selected == 0 && picker.Model.MapFilter is null, "map code search keeps the selected group");
                Expect(room.Parameters.Setup == draft && _session.Setup == live, "map code search keeps the selected match unchanged");
            }
            picker.SearchBox!.Text = string.Empty;
            picker.SearchBox.EmitSignal(LineEdit.SignalName.TextChanged, string.Empty);
        }
    }

    private void CheckLandmarks(LobbyRoomScreen room)
    {
        Rect2 Rect(string name) => ((Control)room.FindChild(name, true, false)!).GetGlobalRect();
        var spies = Rect("Spies"); var mercs = Rect("Mercs"); var actions = Rect("LobbyActions");
        var map = Rect("MatchOverview"); var chat = Rect("Chat");
        if (room.FindChild("LobbyTeams", true, false) is LobbyColumns { Vertical: true })
        {
            Expect(spies.End.Y <= actions.Position.Y && actions.End.Y <= mercs.Position.Y, "narrow lobby stacks teams and actions");
            Expect(map.Position.Y >= mercs.End.Y && chat.Position.Y >= map.End.Y, "narrow lobby stacks map and chat");
        }
        else
        {
            Expect(spies.End.X <= actions.Position.X && actions.End.X <= mercs.Position.X, "teams flank the central actions");
            Expect(map.Position.Y >= spies.End.Y && chat.Position.Y >= mercs.End.Y && map.End.X <= chat.Position.X, "map below left, chat below right");
        }
        var picture = room.MapPicture!;
        Expect(picture.Picture is not null, "preview check uses a texture");
        if (ViewportSize.Y >= 1080)
            Expect(picture.Size.Y >= ViewportSize.Y * 0.35f, "map preview uses available vertical space");
        foreach (var button in room.PageButtons)
            Expect(ViewportRect.Encloses(button.GetGlobalRect()), "footer action visible: " + button.Name);
    }

    private void Expect(bool condition, string message) { if (!condition) _problems.Add($"{Resolution} variant {_variant}: {message}"); }
    private static void Click(Node root, string name) => (root.FindChild(name, true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);

    private void CheckMapDoubleClick(LobbyRoomScreen room, FixtureLobby session, Button tile)
    {
        var live = session.Setup;
        string map = tile.Name.ToString()[4..];
        Vector2 at = tile.GetGlobalTransformWithCanvas() * (tile.Size * 0.5f);
        Viewport viewport = tile.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, inLocalCoords: true);
        ClickMap(doubleClick: false);
        Expect(map != live.Map && room.Parameters!.Setup.Map == map && session.Setup == live && room.MatchSettingsPanel!.Visible,
            "a real single click selects a map in the draft");
        ClickMap(doubleClick: true);
        Expect(session.Setup.Map == map && !room.MatchSettingsPanel!.Visible,
            "double-click applies the selected map and closes match settings");
        void ClickMap(bool doubleClick)
        {
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = doubleClick, Position = at, GlobalPosition = at,
            }, inLocalCoords: true);
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at,
            }, inLocalCoords: true);
        }
    }

    private bool StepRandomMap(LobbyRoomScreen room, FixtureLobby session)
    {
        var open = (Button)room.FindChild("RandomMap", true, false)!;
        var dialog = room.GetNodeOrNull<RandomMapDialog>("RandomMapDialog");
        switch (_randomMapStage)
        {
            case 0:
                _beforeRandomMap = session.Setup;
                EnsureVisible(open);
                break;
            case 1:
                if (!_pilot!.Click(open, "open random map")) return false;
                break;
            case 2:
                if (_pilot!.Busy) return false;
                Expect(dialog is not null && room.MainButton!.FocusMode == Control.FocusModeEnum.None, "random map opens as a modal");
                if (dialog is null) return true;
                Click(dialog, "SpinMap");
                break;
            case 3:
                room.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                Expect(dialog?.IsQueuedForDeletion() == true && room.GetNodeOrNull("LeaveLobbyConfirmation") is null &&
                    session.Setup == _beforeRandomMap, "Escape cancels a spin without leaving or changing the lobby");
                break;
            case 4:
                Click(room, "RandomMap");
                break;
            case 5:
                if (dialog is null) { Expect(false, "random map can reopen after cancellation"); return true; }
                var playlist = (OptionButton)dialog.FindChild("RandomMapPlaylist", true, false)!;
                // The same test also runs in CI without shared map assets, where only All has entries.
                int selected = Enumerable.Range(1, playlist.ItemCount - 1).FirstOrDefault(i => !playlist.IsItemDisabled(i));
                playlist.Select(selected);
                playlist.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selected);
                Click(dialog, "SpinMap");
                Expect(playlist.Disabled && ((Button)dialog.FindChild("UseRandomMap", true, false)!).Disabled,
                    "playlist and apply are locked while cards are moving");
                break;
            case 6:
                if (dialog is null) { Expect(false, "random map stays open until confirmed"); return true; }
                var use = (Button)dialog.FindChild("UseRandomMap", true, false)!;
                if (use.Disabled) return false;
                var panel = (Control)dialog.FindChild("RandomMapPanel", true, false)!;
                var reel = (Control)dialog.FindChild("MapReel", true, false)!;
                var card = reel.GetNode<Control>("ReelTrack/WinningMapCard");
                var title = (Label)card.FindChild("MapTitle", true, false)!;
                var result = (Label)dialog.FindChild("RandomMapResult", true, false)!;
                Expect(ViewportRect.Encloses(panel.GetGlobalRect()) && panel.GetGlobalRect().Encloses(use.GetGlobalRect()),
                    "random map and its confirmation fit the viewport");
                Expect(Mathf.IsEqualApprox(card.GetGlobalRect().GetCenter().X, reel.GetGlobalRect().GetCenter().X) &&
                    title.Text == result.Text && session.Setup == _beforeRandomMap,
                    "reel centers the revealed map and waits for confirmation");
                var selectedPlaylist = (OptionButton)dialog.FindChild("RandomMapPlaylist", true, false)!;
                var filters = OnlineService.Get(room.GetTree()).Catalog.Maps.GetMapFilters();
                MapPlaylist? group = selectedPlaylist.Selected == 0 ? null : filters.FirstOrDefault(filter =>
                    selectedPlaylist.Text.StartsWith(MapPickerView.PlaylistTitle(filter) + "  ·", StringComparison.Ordinal));
                _expectedRandomMap = session.Maps.FirstOrDefault(map => map.Title == result.Text &&
                    (group is null || group.Maps.Contains(map.Id, StringComparer.OrdinalIgnoreCase))).Id;
                Expect(_expectedRandomMap is not null, "the revealed map belongs to the selected playlist");
                _randomMapReadyFrame = _frame;
                break;
            case 7:
                if (_frame < _randomMapReadyFrame + 12) return false;
                MenuPage.Shot(room, $"lobby-random-map-{Resolution}");
                if (!_pilot!.Click(dialog?.FindChild("UseRandomMap", true, false) as Button, "use random map")) return false;
                break;
            default:
                if (_pilot!.Busy) return false;
                Expect(dialog is null && session.Setup.Map == _expectedRandomMap &&
                    room.GetViewport().GuiGetFocusOwner() == open, "using the winner changes the lobby map and restores focus");
                Click(room, "RandomMap");
                var pending = room.GetNodeOrNull<RandomMapDialog>("RandomMapDialog");
                session.Launching = true;
                session.Notify();
                Expect(open.Disabled && pending?.IsQueuedForDeletion() == true, "starting a match closes and disables the random map picker");
                return true;
        }
        _randomMapStage++;
        return false;
    }

    private void CheckInteractions(LobbyRoomScreen room, FixtureLobby session)
    {
        Expect((room.FindChild("RandomMap", true, false) as Button)?.Visible == session.CanControl,
            "only hosts can use Random map");
        room.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        var leave = room.GetNodeOrNull<ConfirmBox>("LeaveLobbyConfirmation");
        Expect(leave is not null && session.Leaves == 0, "Escape asks before leaving lobby");
        room.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Expect(leave?.IsQueuedForDeletion() == true && session.Leaves == 0, "Escape cancels leave confirmation");
        if (leave is not null) room.RemoveChild(leave);
        Click(room, "Leave");
        var confirmedLeave = room.GetNodeOrNull<ConfirmBox>("LeaveLobbyConfirmation");
        confirmedLeave?.Answer(true);
        if (confirmedLeave is not null) room.RemoveChild(confirmedLeave);
        Expect(session.Leaves == 1, "Leave button requires confirmation");
        var chat = room.FindChild("ChatLine", true, false) as LineEdit;
        var channel = room.FindChild("ChatChannel", true, false) as OptionButton;
        chat!.Text = "preserve my draft";
        session.Notify();
        Expect(chat.Text == "preserve my draft", "refresh preserves chat draft");
        channel!.Selected = 1;
        chat.EmitSignal(LineEdit.SignalName.TextSubmitted, chat.Text);
        Expect(session.LastTeamChat && session.LastChat == "preserve my draft", "Enter sends to selected Team channel");
        CheckPersonalRecording(room, session);
        Click(room, "MatchSettings");
        Expect(room.MatchSettingsPanel!.Visible && room.ParameterRows!.Editable == session.CanControl, "settings permissions");
        CheckBotControls(room, session);
        var duration = (SpinBox)room.ParameterRows!.Find("TimeLimitValue")!;
        if (session.CanControl)
        {
            var original = session.Setup;
            duration.GetLineEdit().Text = "7";
            duration.Apply();
            Expect(room.Parameters!.Setup.TimeLimit == 7, "typing a number edits the settings draft");
            session.Notify();
            Expect(session.Setup == original && room.Parameters.Setup.TimeLimit == 7, "host settings draft survives refresh without applying");
            session.Setup = original with { MercLives = 4 }; session.Notify();
            Expect(room.Parameters.Setup.TimeLimit == 7, "remote changes retain host draft");
            Click(room, "DiscardSettings");
            Expect(room.MatchSettingsPanel.Visible && room.Parameters.Setup.TimeLimit == 7, "discard waits for confirmation");
            var discard = room.GetNodeOrNull<ConfirmBox>("DiscardMatchSettings");
            discard?.Answer(true);
            if (discard is not null) room.RemoveChild(discard);
            Expect(room.Parameters.Setup == session.Setup && !room.MatchSettingsPanel.Visible, "discard resets draft to live settings");
            Click(room, "MatchSettings");
            duration.GetLineEdit().GrabFocus();
            duration.GetLineEdit().Text = "8";
            Click(room, "Apply");
            Expect(session.Setup.TimeLimit == 8 && !room.MatchSettingsPanel.Visible, "Apply commits the focused numeric editor");

            Click(room, "MatchSettings");
            duration.GetLineEdit().GrabFocus();
            duration.GetLineEdit().Text = "11";
            Expect(room.ParameterRows.HasPendingEdits && room.Parameters.Setup == session.Setup, "unsubmitted text is a pending edit on a clean draft");
            room._UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            var pendingDiscard = room.GetNodeOrNull<ConfirmBox>("DiscardMatchSettings");
            Expect(pendingDiscard is not null && room.MatchSettingsPanel.Visible && session.Setup.TimeLimit == 8,
                "Escape asks before discarding a pending numeric edit");
            pendingDiscard?.Answer(true);
            if (pendingDiscard is not null) room.RemoveChild(pendingDiscard);
            Expect(!room.MatchSettingsPanel.Visible && room.Parameters.Setup == session.Setup, "pending discard restores the live match settings");

            Click(room, "MatchSettings");
            duration.Value = 7;
            session.CanControl = false;
            session.Notify();
            Expect(!duration.Editable && room.Parameters.Setup == session.Setup && duration.Value == 8 && !room.ApplyButton!.Visible,
                "losing control replaces an unapplied draft with read-only live settings");
            session.CanControl = true;
            session.Notify();
            duration.GetLineEdit().GrabFocus();
            duration.GetLineEdit().Text = "11";
            session.CanControl = false;
            session.Notify();
            Expect(!duration.Editable && duration.GetLineEdit().Text == "8" && room.Parameters.Setup == session.Setup,
                "losing control clears pending numeric text even when the model is still clean");
            session.CanControl = true;
            session.Notify();
            Click(room, "DiscardSettings");
        }
        else
        {
            Expect(!duration.Editable, "client numeric field is read only");
            session.Setup = session.Setup with { TimeLimit = 12 };
            session.Notify();
            Expect(duration.Value == 12 && room.Parameters!.Setup == session.Setup, "client form follows live server settings");
            Click(room, "DiscardSettings");
        }
        var initialSide = session.Members.First(m => m.IsLocal).Side;
        Click(room, "SwitchTeam");
        Expect(session.Members.First(m => m.IsLocal).Side != initialSide, "central Switch Team changes side");
        Click(room, "SwitchTeam");
        Expect(session.Members.First(m => m.IsLocal).Side == initialSide, "central Switch Team switches back");
        var local = session.Members.First(m => m.IsLocal);
        if (!local.Ready) Click(room, "JoinMercs");
        Expect(session.Members.First(m => m.IsLocal).Side == HudSide.Merc, "direct Join Mercenaries");
        session.Launching = true; session.Countdown = 3; session.Notify();
        Expect(room.MainButton!.Text == (session.CanControl ? "Cancel countdown" : session.Members.First(p => p.IsLocal).Ready ? "Not ready" : "Ready"), "countdown primary action");
        session.Launching = false; session.Countdown = 0; session.Notify();
        session.Fetch = new LobbyMapFetch("Downloading map", false, true, 0.64, false);
        room._Process(0);
        Expect((room.FindChild("PreparationStatus", true, false) as Label)?.Text.Contains("64%") == true, "download progress shown next to primary action");
        session.Fetch = new LobbyMapFetch("Download failed — retry", true, false, 0, true);
        room._Process(0);
        Expect((room.FindChild("PreparationStatus", true, false) as Label)?.Text.Contains("retry") == true, "download failure is actionable");
        session.Fetch = null;
        session.Spectate();
        Expect(room.MainButton!.Disabled == !session.CanControl, "spectator readiness restriction");
        session.SetSide(HudSide.Merc);
        var remote = room.PlayerLine("A player with a deliberately long display name");
        var volume = remote?.FindChild("Volume", true, false) as HSlider;
        if (volume is not null) volume.Value = 40;
        Expect(Math.Abs(session.VoiceVolume("A player with a deliberately long display name") - 0.4f) < 0.01f, "player menu volume applies");
        var admin = remote?.FindChild("Admin", true, false) as MenuButton;
        if (session.CanControl && admin is not null)
        {
            admin.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
            Expect(session.Kicks == 0 && (room.FindChild("KickConfirm", true, false) as Control)?.Visible == true, "kick requires confirmation");
            Click(room, "KickNo");
            Expect(session.Kicks == 0, "cancel kick has no effect");
            admin.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 0L);
            Click(room, "KickYes");
            Expect(session.Kicks == 1, "confirmed kick dispatches once");
        }
        else Expect(admin?.Visible == false, "moderation hidden from clients");
        CheckPublicLaunchControls(room, session);
    }

    private void CheckPublicLaunchControls(LobbyRoomScreen room, FixtureLobby session)
    {
        bool control = session.CanControl;
        session.Dedicated = true;
        foreach (bool admin in new[] { true, false })
        {
            session.CanControl = admin;
            session.SetReady(false);
            Expect(room.MainButton!.Text == "Ready" && !room.MainButton.Disabled,
                "public player can ready regardless of admin permissions");
            Expect(room.StartButton is { Visible: true, Disabled: true }, "public start waits for everyone's ready flag");
            Click(room, "Main");
            Expect(session.Members[0].Ready && !session.Launching && room.MainButton.Text == "Not ready",
                "public Ready sets readiness without launching");
            Expect(room.StartButton is { Visible: true, Disabled: false }, "every ready public player may start");
            Click(room, "StartMatch");
            Expect(session.Launching && room.StartButton!.Text == "Cancel countdown", "public Start dispatches launch");
            session.Launching = false;
        }
        session.Dedicated = false;
        session.CanControl = control;
        session.SetReady(false);
    }

    private void CheckPersonalRecording(LobbyRoomScreen room, FixtureLobby session)
    {
        if (room.FindChild("PersonalRecordDemos", true, false) is not CheckBox lobbyRecording ||
            room.FindChild("SettingsRecordDemos", true, false) is not CheckBox settingsRecording)
        {
            Expect(false, "lobby and settings both expose personal recording");
            return;
        }
        var original = session.Setup;
        Expect(!lobbyRecording.Disabled && !settingsRecording.Disabled, "every player can change their recording preference");
        lobbyRecording.ButtonPressed = true;
        Expect(session.RecordDemos && settingsRecording.ButtonPressed && session.Setup == original,
            "lobby recording preference updates independently of the host's match settings");
        Click(room, "MatchSettings");
        settingsRecording.ButtonPressed = false;
        Expect(!session.RecordDemos && !lobbyRecording.ButtonPressed && session.Setup == original && room.ApplyButton!.Visible == session.CanControl,
            "settings recording preference mirrors to the lobby without applying a match draft");
        Expect(room.Parameters!.Setup == session.Setup && room.ApplyButton!.Disabled,
            "personal recording does not dirty shared match settings");
        Click(room, "DiscardSettings");
    }

    private void CheckBotControls(LobbyRoomScreen room, FixtureLobby session)
    {
        var slots = room.ParameterRows!.Find("MaxPlayersValue") as SpinBox;
        var spies = room.ParameterRows.Find("SpyBotsValue") as SpinBox;
        var mercs = room.ParameterRows.Find("MercBotsValue") as SpinBox;
        var mode = room.ParameterRows.Find("BotsChoice") as OptionButton;
        if (slots is null || spies is null || mercs is null || mode is null)
        {
            Expect(false, "player capacity and exact team bot controls exist");
            return;
        }
        Expect(slots.MinValue == 2 && slots.MaxValue == 24, "player slots support 12v12");
        Expect(spies.MinValue == 0 && spies.MaxValue == 24 && mercs.MinValue == 0 && mercs.MaxValue == 24,
            "exact counts allow zero through 24 requested bots per team within the total slot limit");
        if (!session.CanControl)
        {
            Expect(!slots.Editable && !spies.Editable && !mercs.Editable && mode.Disabled,
                "clients inspect bot counts and player capacity read-only");
            return;
        }
        var original = session.Setup;
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)BotFill.Off);
        Expect(!spies.IsVisibleInTree() && !mercs.IsVisibleInTree(), "exact team fields hide when bots are off");
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)BotFill.Custom);
        Expect(spies.IsVisibleInTree() && mercs.IsVisibleInTree(), "Custom teams exposes both exact bot counts");
        slots.Value = 4;
        spies.Value = 0;
        mercs.Value = 3;
        Expect(room.Parameters!.Setup is { MaxPlayers: 4, Bots: BotFill.Custom, SpyBots: 0, MercBots: 3 } && session.Setup == original,
            "slot and exact bot changes stay in the draft");
        Expect(room.FindChild("RosterPreview", true, false) is Label roster && roster.Text.Contains("Only 2 of 3 requested bots fit", StringComparison.Ordinal),
            "roster preview warns when human players limit requested bots");
        Click(room, "Apply");
        Expect(session.Setup is { MaxPlayers: 4, Bots: BotFill.Custom, SpyBots: 0, MercBots: 3 } && !room.MatchSettingsPanel!.Visible,
            "Apply sends player slots and asymmetric bot requests");
        Click(room, "MatchSettings");
        slots.Value = 6;
        spies.Value = 2;
        Click(room, "DiscardSettings");
        var discard = room.GetNodeOrNull<ConfirmBox>("DiscardMatchSettings");
        Expect(discard is not null && session.Setup.MaxPlayers == 4 && session.Setup.SpyBots == 0,
            "discarding bot changes waits for confirmation without changing the live roster");
        discard?.Answer(true);
        if (discard is not null) room.RemoveChild(discard);
        Expect(room.Parameters!.Setup == session.Setup && !room.MatchSettingsPanel!.Visible, "discard restores the applied bot counts and capacity");
        session.Setup = original;
        session.Notify();
        Click(room, "MatchSettings");
    }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless") { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(size); }
        tree.Root.Size = size;
    }

    internal sealed class FixtureLobby : IOnlineLobbySession
    {
        public OpenScct.Net.Scoreboards.RoundScoreboard? LastScoreboard { get; set; }
        public int LastScoreboardLocalParticipantId { get; set; } = -1;
        public bool CanControl { get; set; } = true;
        public bool Dedicated { get; set; }
        public bool IsHost => CanControl && !Dedicated;
        public bool CanLaunch => CanControl || (Dedicated && _members[0].Side is not null);
        public LobbySetup Setup { get; set; } = new() { Map = "TestBox", TimeLimit = 10, SpyLives = 3, MercLives = 3,
            Bots = BotFill.Custom, SpyBots = 1, MercBots = 0,
            Alternate = AlternateRules.Default with { On = true } };
        public string Map => Setup.Map;
        public string Status => "Offline practice";
        public string ServerName => "Lobby layout test";
        public bool CanStart => !Dedicated || _members.All(p => p.Side is null || p.Ready);
        public bool Launching { get; set; }
        public float Countdown { get; set; }
        public bool TeamVoiceOnly { get; private set; }
        public bool RecordDemos { get; private set; }
        public string DemoRecordingStatus => RecordDemos ? "Your matches will be saved to Demos." : "Recording is off.";
        public string MicrophoneLine => "Microphone ready";
        public LobbyMapFetch? Fetch { get; set; }
        public LobbyMapFetch? MapFetch => Fetch;
        public int Kicks { get; private set; }
        private float _volume = 1;
        public bool LastTeamChat { get; private set; }
        public string LastChat { get; private set; } = "";
        private readonly List<OnlineLobbyPlayer> _members = new()
        {
            new(1, "You", HudSide.Spy, false, true, true, 0, ""),
            new(2, "A player with a deliberately long display name", HudSide.Merc, true, false, false, 42, ""),
            new(3, "Bot", HudSide.Spy, true, false, false, 0, "", IsBot: true),
            new(4, "Spectator", null, false, false, false, 65, ""),
        };
        public IReadOnlyList<OnlineLobbyPlayer> Members => _members;
        internal void AddRemotePlayer()
        {
            _members.Add(new(9, "New remote player", HudSide.Merc, false, false, false, 42, ""));
            Notify();
        }
        public void FillCapacityRoster()
        {
            Setup = Setup with { MaxPlayers = 24, Bots = BotFill.Off };
            _members.Clear();
            for (int id = 1; id <= 26; id++)
                _members.Add(new(id, "Player " + id, id <= 12 ? HudSide.Spy : id <= 24 ? HudSide.Merc : null,
                    true, id == 1, id == 1, 42, ""));
        }
        public IReadOnlyList<LobbyPlayer> Players => _members.Where(p => p.Side is not null).Select(p => new LobbyPlayer(p.Name, p.Side!.Value, p.Ready, p.IsLocal, p.IsBot)).ToArray();
        public IReadOnlyList<LobbyMessage> Messages => Array.Empty<LobbyMessage>();
        public IReadOnlyList<(string Id, string Title)> Maps { get; init; } = new[] { ("TestBox", "Test Box") };
        public event Action? Changed;
        public void Notify() => Changed?.Invoke();
        public void SetSide(HudSide side) { _members[0] = _members[0] with { Side = side, Ready = false }; Notify(); }
        public void SetReady(bool ready) { _members[0] = _members[0] with { Ready = ready }; Notify(); }
        public void Spectate() { _members[0] = _members[0] with { Side = null }; Notify(); }
        public void ApplySetup(LobbySetup setup) { Setup = setup; Notify(); }
        public LobbySetup DefaultsFor(string map, EGameMode mode) => new() { Map = map, Mode = mode };
        public USMapSettings? MapSettings(string map) => null;
        public void Start(SceneTree tree) { Launching = !Launching; Notify(); }
        public int Leaves { get; private set; }
        public void Leave() { Leaves++; }
        public void Chat(string text, bool teamOnly) { LastChat = text; LastTeamChat = teamOnly; }
        public void Kick(int id) { Kicks++; }
        public void Ban(int id) { }
        public void SetVoiceBanned(int id, bool banned) { }
        public void SendLook() { }
        public void SetTeamVoiceOnly(bool only) { TeamVoiceOnly = only; Notify(); }
        public void SetRecordDemos(bool record) { RecordDemos = record; Notify(); }
        public float VoiceVolume(string name) => _volume;
        public void SetVoiceVolume(string name, float volume) { _volume = volume; }
        public bool VoiceMuted(string name) => false;
        public void SetVoiceMuted(string name, bool muted) { }
        public bool Speaking(string name) => false;
        public void StartMapFetch() { }
        public void CancelMapFetch() { }
        public void UpdateMapFetch() { }
    }
}
