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
    private bool _savedMapList;
    private string? _savedPlaylist;
    private bool _liveResize;
    private Button? _lastMapTile;

    private Vector2I ViewportSize => _liveResize ? new Vector2I(800, 600) : _sizes[_size];
    private string Resolution => $"{ViewportSize.X}x{ViewportSize.Y}" + (_liveResize ? "-resized" : "");

    public override SmokeResult Step(SmokeContext context)
    {
        if (_maps is null)
        {
            _savedMapList = SettingsStore.Current.MapListView;
            _savedPlaylist = SettingsStore.Current.ActiveMapPlaylist;
            SettingsStore.Current.ActiveMapPlaylist = MapCatalog.AllMapsPlaylist;
            // Create the same catalog service the live lobby uses, so its actual map chooser opens.
            MapCatalog catalog = OnlineService.Get(context.Tree).Catalog.Maps;
            _maps = catalog.GetHostMaps().Select(map => (map.Id, MapTitles.Of(catalog, map.Id))).ToArray();
            if (_maps.Count == 0)
                _maps = Enumerable.Range(1, 80).Select(i => ($"LayoutMap{i}", $"Map {i:00}")).ToArray();
        }
        if (_screen is null)
        {
            Resize(context.Tree, _sizes[_size]);
            _session = new FixtureLobby { CanControl = _variant != 1, Maps = _maps };
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
        }
        if (++_frame < 10) return SmokeResult.Running;
        if (_screen is LobbyRoomScreen roomScreen)
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
                return SmokeResult.Running;
            }
            if (_frame < 16) return SmokeResult.Running;
            if (_frame == 16)
            {
                CheckLayout(roomScreen, "Lobby overview");
                CheckLandmarks(roomScreen);
                MenuPage.Shot(roomScreen, $"lobby-overview-{Resolution}-{_variant}");
                Click(roomScreen, "MatchSettings");
                return SmokeResult.Running;
            }
            if (_frame < 22) return SmokeResult.Running;
            if (_frame == 22)
            {
                CheckLayout(roomScreen, "Lobby settings");
                CheckSettings(roomScreen);
                MenuPage.Shot(roomScreen, $"lobby-settings-{Resolution}-{_variant}");
                if (_session!.CanControl)
                {
                    SettingsStore.Current.MapListView = false;
                    Click(roomScreen, "Maps");
                    return SmokeResult.Running;
                }
            }
            if (_session!.CanControl)
            {
                if (_frame < 28) return SmokeResult.Running;
                if (_frame == 28)
                {
                    CheckMapChooser(roomScreen, list: false);
                    ScrollToLastMap(roomScreen);
                    return SmokeResult.Running;
                }
                if (_frame < 34) return SmokeResult.Running;
                if (_frame == 34)
                {
                    CheckLastMapVisible(roomScreen);
                    SettingsStore.Current.MapListView = true;
                    return SmokeResult.Running;
                }
                if (_frame < 40) return SmokeResult.Running;
                if (_frame == 40)
                {
                    CheckMapChooser(roomScreen, list: true);
                    ScrollToLastMap(roomScreen);
                    return SmokeResult.Running;
                }
                if (_frame < 46) return SmokeResult.Running;
                CheckLastMapVisible(roomScreen);
                if (_size == _sizes.Length - 1 && !_liveResize)
                {
                    // Shrink the existing controls after 4K, without rebuilding the modal or its grid.
                    _liveResize = true;
                    Resize(context.Tree, ViewportSize);
                    SettingsStore.Current.MapListView = false;
                    _frame = 22;
                    return SmokeResult.Running;
                }
                if (_liveResize)
                {
                    CheckSettings(roomScreen);
                    ((Control)roomScreen.FindChild("MapsPanel", true, false)!).Visible = false;
                    roomScreen.MatchSettingsPanel!.Visible = false;
                    CheckLayout(roomScreen, "Lobby after live resize");
                    CheckLandmarks(roomScreen);
                }
                Click(roomScreen, "MapsClose");
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
        if (++_variant < 3) return SmokeResult.Running;
        _variant = 0;
        if (++_size < _sizes.Length) return SmokeResult.Running;
        SettingsStore.Current.MapListView = _savedMapList;
        SettingsStore.Current.ActiveMapPlaylist = _savedPlaylist!;
        return _problems.Count == 0 ? SmokeResult.Pass("host/client/offline, settings, map pictures/list scrolling, 9 resolutions from 800x600 to 3840x2160 and live resize from 4K to 800x600")
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
        if (room.FindChild("MapPicker", true, false) is not MapPickerView picker || picker.Grid.GetParent() is not ScrollContainer scroll) return;
        _lastMapTile = picker.Grid.GetChildren().OfType<Button>().LastOrDefault(tile => tile.IsVisibleInTree());
        if (_lastMapTile is not null) scroll.EnsureControlVisible(_lastMapTile);
    }

    private void CheckLastMapVisible(LobbyRoomScreen room)
    {
        if (_lastMapTile is null || room.FindChild("MapScroll", true, false) is not ScrollContainer scroll)
        {
            Expect(false, "last map can be reached");
            return;
        }
        Expect(scroll.GetGlobalRect().Grow(1f).Encloses(_lastMapTile.GetGlobalRect()), "last map is fully visible after scrolling");
        if (((Control)_lastMapTile.GetParent()).Size.Y > scroll.Size.Y)
            Expect(scroll.ScrollVertical > 0, "long map browser scrolls down to its last map");
    }

    private void CheckSettings(LobbyRoomScreen room)
    {
        Control panel = room.MatchSettingsPanel!;
        Expect(ViewportRect.Encloses(panel.GetGlobalRect()), "settings panel stays inside viewport");
        foreach (Button button in panel.FindChildren("*", nameof(Button), true, false).OfType<Button>())
            if (button.IsVisibleInTree() && button.GetParent().Name == "SettingsButtons")
                Expect(panel.GetGlobalRect().Encloses(button.GetGlobalRect()), "settings action visible: " + button.Name);
    }

    private void CheckMapChooser(LobbyRoomScreen room, bool list)
    {
        if (room.FindChild("MapsPanel", true, false) is not Control panel ||
            panel.FindChild("MapPicker", true, false) is not MapPickerView picker ||
            picker.FindChild("MapScroll", true, false) is not ScrollContainer scroll)
        {
            Expect(false, "map chooser opens");
            return;
        }
        CheckLayout(panel, list ? "Map list" : "Map pictures");
        Expect(ViewportRect.Encloses(panel.GetGlobalRect()), "map chooser stays inside viewport");
        Expect(Math.Abs(scroll.GetGlobalRect().End.Y - picker.GetGlobalRect().End.Y) <= 1f,
            "map scroll uses all available picker height");
        Expect(scroll.Size.Y >= panel.Size.Y - 180f, "map scroll occupies the modal body");
        Expect(picker.Grid.GetChildCount() > 0, "map chooser contains maps");
        Expect(picker.Grid.Columns == 1 || !list, "map list uses one column");
        foreach (string name in new[] { "MapsClose", "MapsOk" })
        {
            var button = (Control)panel.FindChild(name, true, false)!;
            Expect(panel.GetGlobalRect().Encloses(button.GetGlobalRect()) && button.GetGlobalRect().Position.Y >= scroll.GetGlobalRect().End.Y,
                "map chooser footer stays visible below scroll: " + name);
        }
        MenuPage.Shot(room, $"lobby-map-{(list ? "list" : "pictures")}-{Resolution}");
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

    private void CheckInteractions(LobbyRoomScreen room, FixtureLobby session)
    {
        room.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        var leave = room.GetNodeOrNull<ConfirmBox>("LeaveLobbyConfirmation");
        Expect(leave is not null && session.Leaves == 0, "Escape asks before leaving lobby");
        room.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Expect(leave?.IsQueuedForDeletion() == true && session.Leaves == 0, "Escape cancels leave confirmation");
        if (leave is not null) room.RemoveChild(leave);
        Click(room, "Leave");
        room.GetNodeOrNull<ConfirmBox>("LeaveLobbyConfirmation")?.Answer(true);
        Expect(session.Leaves == 1, "Leave button requires confirmation");
        var chat = room.FindChild("ChatLine", true, false) as LineEdit;
        var channel = room.FindChild("ChatChannel", true, false) as OptionButton;
        chat!.Text = "preserve my draft";
        session.Notify();
        Expect(chat.Text == "preserve my draft", "refresh preserves chat draft");
        channel!.Selected = 1;
        chat.EmitSignal(LineEdit.SignalName.TextSubmitted, chat.Text);
        Expect(session.LastTeamChat && session.LastChat == "preserve my draft", "Enter sends to selected Team channel");
        Click(room, "MatchSettings");
        Expect(room.MatchSettingsPanel!.Visible && room.ParameterRows!.Editable == session.CanControl, "settings permissions");
        if (session.CanControl)
        {
            var original = session.Setup;
            room.Parameters!.Load(original with { TimeLimit = 7 });
            session.Notify();
            Expect(session.Setup == original && room.Parameters.Setup.TimeLimit == 7, "host settings draft survives refresh without applying");
            session.Setup = original with { MercLives = 3 }; session.Notify();
            Expect(room.Parameters.Setup.TimeLimit == 7, "remote changes retain host draft");
            Click(room, "DiscardSettings");
            Expect(room.MatchSettingsPanel.Visible && room.Parameters.Setup.TimeLimit == 7, "discard waits for confirmation");
            room.GetNodeOrNull<ConfirmBox>("DiscardMatchSettings")?.Answer(true);
            Expect(room.Parameters.Setup == session.Setup && !room.MatchSettingsPanel.Visible, "discard resets draft to live settings");
            Click(room, "MatchSettings");
            room.Parameters.Load(session.Setup with { TimeLimit = 8 });
            Click(room, "Apply");
            Expect(session.Setup.TimeLimit == 8 && !room.MatchSettingsPanel.Visible, "Apply sends settings");
        }
        else Click(room, "DiscardSettings");
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
    }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless") { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(size); }
        tree.Root.Size = size;
    }

    private sealed class FixtureLobby : IOnlineLobbySession
    {
        public bool CanControl { get; set; } = true;
        public bool IsHost => CanControl;
        public LobbySetup Setup { get; set; } = new() { Map = "TestBox", TimeLimit = 10, SpyLives = 3, MercLives = 3 };
        public string Map => Setup.Map;
        public string Status => "Offline practice";
        public string ServerName => "Lobby layout test";
        public bool CanStart => true;
        public bool Launching { get; set; }
        public float Countdown { get; set; }
        public bool TeamVoiceOnly { get; private set; }
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
