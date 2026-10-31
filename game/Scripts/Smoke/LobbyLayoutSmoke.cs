using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.UI;
using LobbyPlayer = OpenScctGame.UI.LobbyPlayer;

namespace OpenScctGame.Smoke;

public sealed class LobbyLayoutSmoke : SmokeTest
{
    public const string TestName = "lobby-layout";
    public override string Name => TestName;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes = [new(1280, 720), new(1024, 768), new(1920, 1080)];
    private int _frame, _size, _variant;
    private Control? _screen;
    private FixtureLobby? _session;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_screen is null)
        {
            Resize(context.Tree, _sizes[_size]);
            _session = new FixtureLobby { CanControl = _variant != 1 };
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
                return SmokeResult.Running;
            }
            if (_frame < 16) return SmokeResult.Running;
            if (_frame == 16)
            {
                _problems.AddRange(LayoutCheck.Problems(roomScreen, _sizes[_size].X).Select(p => "Lobby overview: " + p));
                MenuPage.Shot(roomScreen, $"lobby-overview-{_sizes[_size].X}-{_variant}");
                Click(roomScreen, "MatchSettings");
                return SmokeResult.Running;
            }
            if (_frame < 22) return SmokeResult.Running;
            if (roomScreen.FindChild("LobbyColumns", true, false) is LobbyColumns columns)
                Expect(columns.Vertical == (_sizes[_size].X < 1100), "responsive lobby columns follow window width");
        }
        string where = $"Lobby {_sizes[_size].X} variant {_variant}";
        _problems.AddRange(LayoutCheck.Problems(_screen, _sizes[_size].X).Select(p => where + ": " + p));
        MenuPage.Shot(_screen, $"lobby-{_sizes[_size].X}-{_variant}");
        _screen.QueueFree(); _screen = null;
        if (++_variant < 3) return SmokeResult.Running;
        _variant = 0;
        if (++_size < _sizes.Length) return SmokeResult.Running;
        return _problems.Count == 0 ? SmokeResult.Pass("host/client/offline, settings drafts, chat, teams, status and responsive layouts")
            : SmokeResult.Fail(string.Join("; ", _problems.Take(15)));
    }

    private void Expect(bool condition, string message) { if (!condition) _problems.Add(message); }
    private static void Click(Node root, string name) => (root.FindChild(name, true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);

    private void CheckInteractions(LobbyRoomScreen room, FixtureLobby session)
    {
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
            Expect(room.Parameters.Setup == session.Setup && !room.MatchSettingsPanel.Visible, "discard resets draft to live settings");
            Click(room, "MatchSettings");
            room.Parameters.Load(session.Setup with { TimeLimit = 8 });
            Click(room, "Apply");
            Expect(session.Setup.TimeLimit == 8 && !room.MatchSettingsPanel.Visible, "Apply sends settings");
        }
        else Click(room, "DiscardSettings");
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
        public IReadOnlyList<(string Id, string Title)> Maps => new[] { ("TestBox", "Test Box") };
        public event Action? Changed;
        public void Notify() => Changed?.Invoke();
        public void SetSide(HudSide side) { _members[0] = _members[0] with { Side = side, Ready = false }; Notify(); }
        public void SetReady(bool ready) { _members[0] = _members[0] with { Ready = ready }; Notify(); }
        public void Spectate() { _members[0] = _members[0] with { Side = null }; Notify(); }
        public void ApplySetup(LobbySetup setup) { Setup = setup; Notify(); }
        public LobbySetup DefaultsFor(string map, EGameMode mode) => new() { Map = map, Mode = mode };
        public USMapSettings? MapSettings(string map) => null;
        public void Start(SceneTree tree) { Launching = !Launching; Notify(); }
        public void Leave() { }
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
