using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Smoke;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>
/// The networked menus end to end with two game processes on this machine (docs/NETWORK-TEST.md, docs/MENUS.md):
/// <c>--svm-smoke=net-lobby</c> is player 1, the host; it starts a second game process, player 2
/// (<c>--svm-smoke=net-lobby-guest</c>), and both are driven only through their own menus and keys
/// (<see cref="MenuPilot"/>, <see cref="KeyPilot"/>: Godot input events), over UDP on localhost.
/// <para>Player 1: Create a Session; on the Create Session page Voice Chat Open, Game Length 1 minute, Revenge, the
/// map, a password, UPnP off; Create; in the Lobby Room he sees player 2 join, his "Restrict Mic to Team", his chat
/// line and hears his voice (a synthetic microphone: a 440 Hz tone), turns him down to 40% with the volume slider and
/// hears him at 40%; says hello; sees him ready, changes Respawn Length and applies, which un-readies him; launches;
/// plays both games of the revenge pair (player 2's voice still at 40% in the round, Reloaded's overlay showing it),
/// presses Next on both results, is back in the Lobby Room, sees player 2 leave, come back by Join with IP and leave
/// again; waits for player 2's process to pass; leaves.</para>
/// <para>Player 2: Find a Session; finds the host on the LAN search, picks it, types the password, joins; checks the
/// Session Parameters the host set; toggles Restrict Mic to Team; talks; chats; is ready; is un-readied by the host's
/// change, sees Respawn Length blink and gets ready again; in the first game walks with the movement key as a
/// mercenary; plays the second as a spy (revenge swaps the sides); Next, Next; back in the lobby he leaves, joins again
/// with Join with IP (address, port, password) and leaves.</para>
/// <para>Ports 28988 (game) and 28989 (LAN) unless <c>SVM_HOST_PORT</c> and <c>SVM_LAN_PORT</c> say otherwise, so the
/// test never meets a game someone hosts on this machine. <c>SVM_NETLOBBY_MAP</c> picks the map (SteeE when installed,
/// else TestBox). With a window and <c>SVM_PLAY_SHOTS</c>, both players save their window at every step (host-*.png,
/// guest-*.png). Player 2's log goes to <c>local/net-lobby/guest.log</c>.</para>
/// </summary>
public sealed class NetLobbySmoke : SmokeTest
{
    public const string TestName = "net-lobby";
    public const string GuestTestName = "net-lobby-guest";
    public const string Password = "swordfish";
    public const string HostName = "Host";
    public const string GuestName = "Guest";
    public const string ServerName = "Net lobby test";
    public const double ToneHz = 440;

    private readonly NetLobbyScript _script;
    private int _guestPid = -1;
    private string _guestLog = string.Empty;
    private double _fullRms;

    public NetLobbySmoke()
    {
        NetLobbyScript.PrepareEnvironment();
        _script = new NetLobbyScript("host", Steps());
    }

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        SmokeResult r = _script.Step(context);
        if (r.State != SmokeState.Running && _guestPid > 0 && OS.IsProcessRunning(_guestPid))
        {
            // Only the process this test started.
            OS.Kill(_guestPid);
        }

        return r.State == SmokeState.Failed ? SmokeResult.Fail(r.Detail + GuestTail()) : r;
    }

    private string GuestTail()
    {
        if (!File.Exists(_guestLog))
        {
            return string.Empty;
        }

        string[] lines = ReadShared(_guestLog).Where(l => l.Contains("SVM_NETLOBBY", StringComparison.Ordinal) ||
            l.Contains("SVM_SMOKE", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).TakeLast(12).ToArray();
        return " | guest: " + string.Join(" / ", lines);
    }

    /// <summary>The lines of a log another process still writes.</summary>
    private static string[] ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n');
    }

    private IEnumerable<NetLobbyStep> Steps()
    {
        NetLobbyScript s = null!;
        LobbyRoomScreen? Room() => s.Tree.CurrentScene as LobbyRoomScreen;
        OnlineLobbyPlayer? Guest() => Room()?.Session?.Members.FirstOrDefault(m => m.Name == GuestName);
        OnlineVoiceChat? Voice() => OnlineService.Get(s.Tree).VoiceChat;

        yield return new("start", 30, sc =>
        {
            s = sc;
            SettingsStore.Current.PlayerName = HostName;
            return !s.OpenMainMenu() && s.Tree.CurrentScene is MainMenu menu && s.Pilot.Click(menu.HostButton, "Create a Session");
        });
        yield return new("create session page", 30, _ => s.Tree.CurrentScene is HostScreen { Model: not null });

        // The Create Session rows, with the arrows and boxes as a player sets them.
        yield return new("voice chat Open", 20, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            return page.Model!.Setup.VoiceMode == EVoiceChatMode.VCM_Open ||
                (s.Pilot.Click(page.Rows!.Find("VoiceChatNext"), "Voice Chat next") && false);
        });
        yield return new("map", 30, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            return string.Equals(page.Model!.Setup.Map, NetLobbyScript.MapName(), StringComparison.OrdinalIgnoreCase) ||
                (s.Pilot.Click(page.Rows!.Find("MapNext"), "Map next") && false);
        });
        yield return new("game length 1 min", 20, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            return page.Model!.Setup.TimeLimit == 1 ||
                (s.Pilot.Click(page.Rows!.Find(page.Model.Setup.TimeLimit > 1 ? "TimeLimitPrev" : "TimeLimitNext"), "Game Length") && false);
        });
        yield return new("revenge", 10, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            return page.Model!.Setup.Revenge || (s.Pilot.Click(page.Rows!.Find("RevengeCheck"), "Revenge") && false);
        });
        yield return new("password", 10, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            return page.Model!.Password == Password || (s.Pilot.Type(page.Rows!.Find("PasswordEdit") as LineEdit, Password, "Password") && false);
        });
        yield return new("upnp off", 10, _ =>
        {
            var page = (HostScreen)s.Tree.CurrentScene!;
            CheckBox? upnp = s.Pilot.Find<CheckBox>("Upnp");
            return upnp is { ButtonPressed: false } || (s.Pilot.Click(upnp, "UPnP") && false);
        });
        yield return new("server name", 10, _ => s.Typed("ServerName", ServerName));
        yield return new("create", 10, _ =>
        {
            s.Shot("create-session");
            var page = (HostScreen)s.Tree.CurrentScene!;
            return s.Pilot.Click(page.HostButton, "Create");
        });
        yield return new("lobby room as host", 30, _ => Room() is { Session: { IsHost: true, CanControl: true } } && OnlineService.Get(s.Tree).Hosting is not null);
        yield return new("start player 2", 10, _ =>
        {
            int port = OnlineService.Get(s.Tree).Hosting!.Port;
            _guestLog = Path.Combine(NetLobbyScript.OutDir(), "guest.log");
            _guestPid = NetLobbyScript.StartGuest(port, _guestLog);
            s.Note($"player 2 started: pid {_guestPid}, port {port}, log {_guestLog}");
            return _guestPid > 0;
        });

        // Player 2 arrives through the LAN list; everything he does reaches this lobby.
        bool sawTeamVoice = false;
        yield return new("player 2 in the lobby", 120, _ =>
        {
            s.Shot("lobby-guest-joined", once: true);
            return Guest() is { Side: HudSide.Merc } && Room()!.PlayerLine(GuestName) is not null;
        });
        yield return new("player 2 restricts his mic", 60, _ =>
        {
            sawTeamVoice |= Guest()?.TeamVoiceOnly ?? false;
            return sawTeamVoice;
        });
        yield return new("player 2 chats", 60, _ =>
            Room()!.Session!.Messages.Any(m => m.Sender == GuestName && m.Text == "hello from player 2") &&
            s.Text("Messages").Contains("Guest: hello from player 2", StringComparison.Ordinal));
        yield return new("player 2 is heard", 60, _ =>
        {
            Voice()!.ClearHeard();
            return Room()!.Session!.Speaking(GuestName) || false;
        });
        yield return new("hear player 2 at 100%", 30, _ =>
        {
            (double rms, long n) = Voice()!.Heard(GuestName);
            if (n < OpenScct.Net.Voice.VoiceK.SampleRate)
            {
                return false;
            }

            _fullRms = rms;
            s.Note(string.Create(CultureInfo.InvariantCulture, $"player 2 heard in the lobby: rms {rms:0.000} over {n} samples"));
            return rms > 0.05;
        });
        yield return new("turn player 2 down", 20, _ =>
        {
            s.Shot("lobby-speaking");
            var slider = (HSlider?)Room()!.PlayerLine(GuestName)?.FindChild("Volume", recursive: true, owned: false);
            float now = Room()!.Session!.VoiceVolume(GuestName);
            if (Math.Abs(now - 0.4f) < 0.06f)
            {
                Voice()!.ClearHeard();
                return true;
            }

            s.Note(string.Create(CultureInfo.InvariantCulture,
                $"player 2's slider reads {slider?.Value:0}, at {slider?.GetGlobalRect()}, in the tree {slider?.IsVisibleInTree()}, volume {now:0.00}"));
            s.Pilot.ClickSliderAt(slider, 40, "player 2's volume at 40%");
            return false;
        });
        yield return new("hear player 2 at 40%", 30, _ =>
        {
            (double rms, long n) = Voice()!.Heard(GuestName);
            if (n < OpenScct.Net.Voice.VoiceK.SampleRate)
            {
                return false;
            }

            double ratio = rms / _fullRms;
            s.Note(string.Create(CultureInfo.InvariantCulture, $"player 2 at the slider's {Room()!.Session!.VoiceVolume(GuestName):0.00}: rms {rms:0.000}, {ratio:0.00} of full"));
            return s.Expect(ratio is > 0.3 and < 0.5, $"volume ratio {ratio:0.00} (expected about 0.4)");
        });
        yield return new("say hello", 10, _ => s.Typed("ChatLine", "hello from player 1"));
        yield return new("send", 10, _ => s.Pilot.Click(s.Pilot.FindButton("Say"), "Say"));
        yield return new("player 2 ready", 90, _ => Guest() is { Ready: true });
        yield return new("change respawn", 10, _ =>
        {
            s.Shot("lobby-guest-ready");
            return s.Pilot.Click(Room()!.ParameterRows!.Find("RespawnNext"), "Respawn Length next");
        });
        yield return new("apply", 10, _ => s.Pilot.Click(Room()!.ApplyButton, "Apply"));
        yield return new("the change un-readies player 2", 20, _ => Guest() is { Ready: false });
        yield return new("player 2 ready again", 60, _ => Guest() is { Ready: true });
        yield return new("launch", 10, _ => Room()!.MainButton?.Text == "Launch" && s.Pilot.Click(Room()!.MainButton, "Launch"));

        foreach (NetLobbyStep step in RoundSteps(() => s, 1, Voice, () => _fullRms))
        {
            yield return step;
        }

        foreach (NetLobbyStep step in RoundSteps(() => s, 2, Voice, () => _fullRms))
        {
            yield return step;
        }

        yield return new("back in the lobby", 120, _ => Room() is { Session.Members.Count: 2 } && OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Lobby);
        yield return new("sides as chosen in the lobby", 10, _ =>
            s.Expect(Guest() is { Side: HudSide.Merc } && Room()!.Session!.Members.Any(m => m.IsLocal && m.Side == HudSide.Spy),
                "after the revenge pair player 1 is a spy and player 2 a mercenary again, as they chose in the lobby"));
        yield return new("player 2 leaves", 90, _ =>
        {
            s.Shot("lobby-after-revenge", once: true);
            return Room() is { } room && room.Session!.Members.All(m => m.Name != GuestName);
        });
        yield return new("player 2 back by Join with IP", 120, _ => Guest() is not null);
        yield return new("player 2 leaves again", 90, _ => Room() is { } room && room.Session!.Members.All(m => m.Name != GuestName));
        yield return new("player 2's checks passed", 60, _ =>
        {
            if (OS.IsProcessRunning(_guestPid))
            {
                return false;
            }

            int code = OS.GetProcessExitCode(_guestPid);
            return s.Expect(code == 0, $"player 2's process ended with exit code {code}");
        });
        yield return new("leave", 10, _ => s.Pilot.Click(Room()?.LeaveButton, "Leave"));
        yield return new("main menu", 30, _ => s.Tree.CurrentScene is MainMenu && OnlineService.Get(s.Tree).Hosting is null);
    }

    /// <summary>The host's steps of one game of the revenge pair.</summary>
    private static IEnumerable<NetLobbyStep> RoundSteps(Func<NetLobbyScript> script, int game, Func<OnlineVoiceChat?> voice, Func<double> fullRms)
    {
        yield return NetLobbyScript.NoHudBeforeReady(script, game);
        yield return new($"game {game} on the map", 180, _ =>
        {
            NetLobbyScript s = script();
            return s.Tree.CurrentScene is PlayRoot { IsLoaded: true, Session: { IsRemote: true, LocalPawn: not null } } &&
                OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Match;
        });
        yield return new($"game {game} sides", 20, _ =>
        {
            NetLobbyScript s = script();
            var root = (PlayRoot)s.Tree.CurrentScene!;
            int team = root.Session!.Client.LocalPlayer?.PlayerReplicationInfo?.Team?.TeamIndex ?? -1;
            s.Shot($"game{game}");
            return s.Expect(team == (game == 1 ? 0 : 1), $"player 1 on team {team} in game {game} (revenge swaps the sides)");
        });
        yield return new($"game {game} voice at 40%", 40, _ =>
        {
            NetLobbyScript s = script();
            OnlineVoiceChat v = voice()!;
            (double rms, long n) = v.Heard(GuestName);
            if (n < OpenScct.Net.Voice.VoiceK.SampleRate)
            {
                return false;
            }

            double ratio = rms / fullRms();
            bool overlay = s.LabelShown("Guest - volume 40%");
            s.Note(string.Create(CultureInfo.InvariantCulture, $"game {game}: player 2 heard at {ratio:0.00} of full, overlay {overlay}"));
            v.ClearHeard();
            return s.Expect(ratio is > 0.3 and < 0.5, $"game {game} volume ratio {ratio:0.00}") && s.Expect(overlay, "the voice overlay shows 'Guest - volume 40%'");
        });
        // Player 1 is a spy in game 1 and a merc in game 2; the mercs win on time.
        yield return NetLobbyScript.EndBanner(script, game, game == 2);
        yield return new($"game {game} results", 150, _ =>
        {
            NetLobbyScript s = script();
            return s.Tree.CurrentScene is ResultsScreen { BannerShowing: false, RematchButton.Visible: true } r &&
                s.Expect(r.Result?.LocalWon == (game == 2), $"the end menu's result says won={r.Result?.LocalWon} in game {game}");
        });
        yield return new($"game {game} next", 10, _ =>
        {
            NetLobbyScript s = script();
            s.Shot($"results{game}");
            var results = (ResultsScreen)s.Tree.CurrentScene!;
            return results.RematchButton!.Text == "Next" && s.Pilot.Click(results.RematchButton, "Next");
        });
    }
}

/// <summary>Player 2's half of <see cref="NetLobbySmoke"/>.</summary>
public sealed class NetLobbyGuestSmoke : SmokeTest
{
    private readonly NetLobbyScript _script;
    private Vector3? _walkStart;
    private KeyPilot? _keys;

    public NetLobbyGuestSmoke() => _script = new NetLobbyScript("guest", Steps());

    public override string Name => NetLobbySmoke.GuestTestName;

    public override SmokeResult Step(SmokeContext context) => _script.Step(context);

    private IEnumerable<NetLobbyStep> Steps()
    {
        NetLobbyScript s = null!;
        LobbyRoomScreen? Room() => s.Tree.CurrentScene as LobbyRoomScreen;
        OnlineLobbyPlayer? Me() => Room()?.Session?.Members.FirstOrDefault(m => m.IsLocal);
        string address = System.Environment.GetEnvironmentVariable("SVM_NETLOBBY_ADDRESS") ?? "127.0.0.1:28988";

        yield return new("start", 30, sc =>
        {
            s = sc;
            SettingsStore.Current.PlayerName = NetLobbySmoke.GuestName;
            return !s.OpenMainMenu() && s.Tree.CurrentScene is MainMenu menu && s.Pilot.Click(menu.FindButton, "Find a Session");
        });
        TreeItem? row = null;
        yield return new("the host in the LAN list", 60, _ =>
        {
            if (s.Tree.CurrentScene is not ServerBrowserScreen { Table: { } table })
            {
                return false;
            }

            row = table.GetRoot()?.GetChildren().FirstOrDefault(i => i.GetText(0).Contains(NetLobbySmoke.ServerName, StringComparison.Ordinal));
            return row is not null && s.Expect(row.GetText(7) == "Joinable", $"the host's row says '{row.GetText(7)}'") &&
                s.Expect(row.GetText(0).StartsWith("[locked]", StringComparison.Ordinal), "the row shows the password lock");
        });
        yield return new("pick it", 10, _ =>
        {
            s.Shot("browser");
            var browser = (ServerBrowserScreen)s.Tree.CurrentScene!;
            // The list is drawn again when a server answers: the row found last frame may be gone, so find it again.
            row = browser.Table!.GetRoot()?.GetChildren().FirstOrDefault(i => i.GetText(0).Contains(NetLobbySmoke.ServerName, StringComparison.Ordinal));
            return row is not null && s.Pilot.ClickTreeItem(browser.Table!, row, "the host's row");
        });
        yield return new("password", 10, _ => s.Typed("Password", NetLobbySmoke.Password));
        yield return new("join", 10, _ => s.Pilot.Click(((ServerBrowserScreen)s.Tree.CurrentScene!).JoinButton, "Join"));
        yield return new("lobby room as player 2", 60, _ => Room() is { Session: { IsHost: false } } && Me() is not null);
        yield return new("the host's session parameters", 20, _ =>
        {
            LobbySetup setup = Room()!.Parameters!.Setup;
            s.Shot("lobby", once: true);
            return s.Expect(setup.TimeLimit == 1 && setup.Revenge && setup.GamesInARow == 2 && setup.VoiceMode == EVoiceChatMode.VCM_Open,
                    "Session Parameters show 1 min, Revenge, 2 games, voice Open: " + setup.Describe()) &&
                s.Expect(s.Text("Row_VoiceChat").Contains("Open", StringComparison.Ordinal) || s.ValueText("VoiceChatValue") == "Open", "the Voice Chat row reads Open") &&
                s.Expect(s.ValueText("TimeLimitValue") == "1" && s.ValueText("RevengeValue") == "Yes", "the rows read 1 and Yes");
        });
        yield return new("talk", 10, _ =>
        {
            // The synthetic microphone: a 440 Hz tone through the capture bus (or straight to the client headless).
            OnlineService.Get(s.Tree).VoiceChat!.ToneHz = NetLobbySmoke.ToneHz;
            return true;
        });
        yield return new("transmitting", 20, _ =>
        {
            OnlineVoiceChat v = OnlineService.Get(s.Tree).VoiceChat!;
            bool on = v.Voice?.Voice.Transmitting ?? false;
            if (on)
            {
                s.Note($"the microphone transmits (tone via the {v.ToneRoute}, '{v.MicrophoneLine()}')");
            }

            return on;
        });
        yield return new("restrict mic to team", 10, _ => s.Pilot.Click(Room()!.TeamVoiceOnlyBox, "Restrict Mic to Team"));
        yield return new("restricted", 20, _ => Me() is { TeamVoiceOnly: true });
        yield return new("hold it a moment", 3, _ => s.StepSeconds > 1.5);
        yield return new("unrestrict", 10, _ => s.Pilot.Click(Room()!.TeamVoiceOnlyBox, "Restrict Mic to Team"));
        yield return new("unrestricted", 20, _ => Me() is { TeamVoiceOnly: false });
        yield return new("chat", 10, _ => s.Typed("ChatLine", "hello from player 2"));
        yield return new("say", 10, _ => s.Pilot.Click(s.Pilot.FindButton("Say"), "Say"));
        yield return new("my line in the chat box", 20, _ => s.Text("Messages").Contains("Guest: hello from player 2", StringComparison.Ordinal));
        yield return new("the host's hello", 120, _ => s.Text("Messages").Contains("Host: hello from player 1", StringComparison.Ordinal));
        yield return new("ready", 10, _ => Room()!.MainButton?.Text == "Ready" && s.Pilot.Click(Room()!.MainButton, "Ready"));
        yield return new("is ready", 20, _ => Me() is { Ready: true } && Room()!.MainButton?.Text == "Not Ready");
        yield return new("un-readied by the host's change", 120, _ => Me() is { Ready: false });
        yield return new("respawn blinks", 10, _ =>
        {
            s.Shot("lobby-blink");
            return s.Expect(Room()!.ParameterRows!.Blink!.Blinking(SessionRow.Respawn, Time.GetTicksMsec() / 1000.0), "Respawn Length blinks after the host's change");
        });
        yield return new("ready again", 10, _ => Room()!.MainButton?.Text == "Ready" && s.Pilot.Click(Room()!.MainButton, "Ready"));

        // Game 1: a mercenary; the movement key walks him on the server's map.
        yield return NetLobbyScript.NoHudBeforeReady(() => s, 1);
        yield return new("game 1 on the map", 180, _ =>
            s.Tree.CurrentScene is PlayRoot { IsLoaded: true, Session: { IsRemote: true, LocalPawn: not null } } root && root.Controls?.Bindings is not null);
        yield return new("game 1 as a mercenary", 10, _ =>
        {
            var root = (PlayRoot)s.Tree.CurrentScene!;
            int team = root.Session!.Client.LocalPlayer?.PlayerReplicationInfo?.Team?.TeamIndex ?? -1;
            s.Shot("game1");
            return s.Expect(team == 1, $"player 2 on team {team} in game 1");
        });
        yield return new("walk", 15, _ =>
        {
            var root = (PlayRoot)s.Tree.CurrentScene!;
            _keys ??= KeyPilot.Attach(root);
            _keys.Update();
            if (!_keys.Ready)
            {
                return false;
            }

            FVector at = root.Session!.LocalPawn!.Location;
            _walkStart ??= new Vector3(at.X, at.Y, at.Z);
            _keys.Press("BT_Up");
            return s.StepSeconds > 2.0;
        });
        yield return new("walked", 10, _ =>
        {
            var root = (PlayRoot)s.Tree.CurrentScene!;
            _keys!.Release("BT_Up");
            _keys.Update();
            FVector at = root.Session!.LocalPawn!.Location;
            float moved = new Vector3(at.X, at.Y, at.Z).DistanceTo(_walkStart!.Value);
            s.Note(string.Create(CultureInfo.InvariantCulture, $"walked {moved:0} uu with the movement key ({_keys.Delivered} key events seen)"));
            s.Shot("game1-walked");
            return s.Expect(moved > 60f, $"the pawn moved {moved:0} uu");
        });
        foreach (int game in new[] { 1, 2 })
        {
            if (game == 2)
            {
                yield return new("game 2 on the map", 180, _ => s.Tree.CurrentScene is PlayRoot { IsLoaded: true, Session: { IsRemote: true, LocalPawn: not null } });
                yield return new("game 2 as a spy", 10, _ =>
                {
                    var root = (PlayRoot)s.Tree.CurrentScene!;
                    int team = root.Session!.Client.LocalPlayer?.PlayerReplicationInfo?.Team?.TeamIndex ?? -1;
                    s.Shot("game2");
                    return s.Expect(team == 0, $"player 2 on team {team} in game 2 (revenge swaps the sides)");
                });
            }

            // Player 2 is a merc in game 1 and a spy in game 2; the mercs win on time.
            yield return NetLobbyScript.EndBanner(() => s, game, game == 1);
            yield return new($"game {game} results", 150, _ => s.Tree.CurrentScene is ResultsScreen { BannerShowing: false, RematchButton.Visible: true } r &&
                s.Expect(r.Result?.LocalWon == (game == 1), $"the end menu's result says won={r.Result?.LocalWon} in game {game}"));
            yield return new($"game {game} next", 10, _ =>
            {
                s.Shot($"results{game}");
                var results = (ResultsScreen)s.Tree.CurrentScene!;
                return results.RematchButton!.Text == "Next" && s.Pilot.Click(results.RematchButton, "Next");
            });
        }

        yield return new("back in the lobby", 120, _ => Room() is { Session.Members.Count: 2 } && OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Lobby);
        yield return new("leave", 10, _ =>
        {
            s.Shot("lobby-after-revenge");
            return s.Pilot.Click(Room()!.LeaveButton, "Leave");
        });
        yield return new("main menu", 30, _ => s.Tree.CurrentScene is MainMenu menu && s.Pilot.Click(menu.JoinButton, "Join with IP"));
        yield return new("join with ip page", 10, _ => s.Tree.CurrentScene is JoinWithIpScreen);
        yield return new("server address", 10, _ => s.Typed("Address", address.Split(':')[0]));
        yield return new("server port", 10, _ => s.Typed("Port", address.Split(':')[1]));
        yield return new("password again", 10, _ => s.Typed("Password", NetLobbySmoke.Password));
        yield return new("join by ip", 10, _ =>
        {
            s.Shot("join-with-ip");
            return s.Pilot.Click(((JoinWithIpScreen)s.Tree.CurrentScene!).JoinButton, "Join");
        });
        yield return new("lobby again", 60, _ => Room() is { Session: { IsHost: false } } && Me() is not null);
        yield return new("leave again", 10, _ => s.Pilot.Click(Room()!.LeaveButton, "Leave"));
        yield return new("main menu again", 30, _ => s.Tree.CurrentScene is MainMenu);
    }
}

/// <summary>One step of a scripted menu run: a label, a time limit, and a check that may act and returns true when done.</summary>
public sealed record NetLobbyStep(string Label, double TimeoutSeconds, Func<NetLobbyScript, bool> Try);

/// <summary>Runs the steps of one player of <see cref="NetLobbySmoke"/>, one a frame, with the pilot, notes and shots.</summary>
public sealed class NetLobbyScript
{
    /// <summary>
    /// The round's first frames on this player's screen: no HUD page until the player is ready (SpawnMagmaHud opens it
    /// once the PRI has a team); then the page of his side.
    /// </summary>
    public static NetLobbyStep NoHudBeforeReady(Func<NetLobbyScript> script, int game)
    {
        int early = 0, unpossessed = 0;
        return new($"game {game} no HUD before the player is ready", 180, _ =>
        {
            NetLobbyScript s = script();
            if (s.Tree.CurrentScene is not PlayRoot { IsLoaded: true } root || root.Session?.Client.LocalPlayer is not SPlayerController pc)
            {
                return false;
            }

            if (!HudLayer.MagmaHudOpen(pc))
            {
                early++;
                s.Expect(root.Hud?.ActivePage is null, $"a HUD page ({root.Hud?.ActivePage?.GetType().Name}) drawn before the player's team is known");
                return false;
            }

            // Until the pawn is possessed the page is the one SpawnMagmaHud opened for the PRI's team.
            HudSide side = pc.PlayerReplicationInfo!.Team!.TeamIndex == 1 ? HudSide.Merc : HudSide.Spy;
            if (root.Hud?.ActivePage is HudPage page && page.Side != side)
            {
                s.Shot($"game{game}-wrong-page");
                s.Expect(false, $"the {page.Side} page drawn for a player of team {pc.PlayerReplicationInfo.Team.TeamIndex} before his pawn ({(pc.Pawn is null ? "none" : "possessed")})");
                return false;
            }

            if (pc.Pawn is null)
            {
                unpossessed++;
                return false;
            }

            s.Note($"game {game}: {early} drawn frames before the player was ready and {unpossessed} before his pawn, every one without a HUD page or with his side's");
            return true;
        });
    }

    /// <summary>
    /// The round's end on this player's screen: the HUD opens P_EndGame over the world as the controller enters
    /// GameEnded (spec/game/end-of-round.md rule 17), You Win or You Lose for the side this player played in the round.
    /// </summary>
    public static NetLobbyStep EndBanner(Func<NetLobbyScript> script, int game, bool won)
    {
        double endedAt = -1;
        double shownAt = -1;
        return new($"game {game} end banner", 150, _ =>
        {
            NetLobbyScript s = script();
            if (s.Tree.CurrentScene is not PlayRoot root || root.Session?.Client.LocalPlayer is not SPlayerController pc)
            {
                return s.Expect(false, $"the play scene was gone before the end banner of game {game}");
            }

            if (!pc.bGameEnded)
            {
                return false;
            }

            if (endedAt < 0)
            {
                endedAt = s.StepSeconds;
            }

            string? shown = root.Hud?.EndGame?.Shown;
            if (shown is null)
            {
                return s.StepSeconds - endedAt > 3.0 && s.Expect(false, $"no end banner {s.StepSeconds - endedAt:0.0} s after the round ended");
            }

            if (shownAt < 0)
            {
                shownAt = s.StepSeconds;
                s.Note(string.Create(CultureInfo.InvariantCulture, $"game {game}: {shown} shown {s.StepSeconds - endedAt:0.00} s after GameEnded"));
                string expected = won ? EndGamePage.YouWin : EndGamePage.YouLose;
                s.Expect(shown == expected, $"game {game} banner {shown}, expected {expected}");
                return false;
            }

            // Its text is in full from frame 33 (3.3 s at 10 fps); the end menu opens 10 s after the winner is known.
            if (s.StepSeconds - shownAt < 3.6)
            {
                return false;
            }

            s.Shot($"endbanner{game}");
            return s.Expect(root.Hud!.EndGame!.DrawList.Items.Count > 0, "the banner is drawn");
        });
    }

    private readonly IEnumerator<NetLobbyStep> _steps;
    private readonly string _role;
    private readonly List<string> _notes = new();
    private readonly HashSet<string> _shots = new();
    private NetLobbyStep? _step;
    private double _stepStart;
    private double _now;
    private string? _failure;
    private SceneTree? _tree;
    private MenuPilot? _pilot;
    private int _teardown = -1;
    private Node? _scene;
    private int _sceneFrames;
    private int _typeTries;
    private int _loggedActions;

    /// <summary>Frames a new page gets to lay itself out before the script acts on it.</summary>
    public const int SettleFrames = 10;

    public NetLobbyScript(string role, IEnumerable<NetLobbyStep> steps)
    {
        _role = role;
        _steps = steps.GetEnumerator();
    }

    public SceneTree Tree => _tree!;

    public MenuPilot Pilot => _pilot!;

    /// <summary>Seconds since the running step began.</summary>
    public double StepSeconds => _now - _stepStart;

    public SmokeResult Step(SmokeContext context)
    {
        _tree ??= context.Tree;
        _pilot ??= new MenuPilot(context.Tree);
        _now = context.ElapsedSeconds;
        if (_teardown >= 0)
        {
            // Leave the visit before quitting, so the other player hears the host closed or the guest left.
            if (_teardown++ == 0)
            {
                OnlineService.Get(context.Tree).Leave();
            }

            return _teardown < 30 ? SmokeResult.Running :
                _failure is null ? SmokeResult.Pass(string.Join("; ", _notes)) : SmokeResult.Fail(_failure + " | " + string.Join("; ", _notes.TakeLast(6)));
        }

        _pilot.Update();
        if (_pilot.Busy)
        {
            return SmokeResult.Running;
        }

        // A page that just opened is laid out over the next frames: act on it once it settled.
        if (!ReferenceEquals(context.Tree.CurrentScene, _scene))
        {
            _scene = context.Tree.CurrentScene;
            _sceneFrames = 0;
        }

        if (++_sceneFrames < SettleFrames)
        {
            return SmokeResult.Running;
        }

        // Every page as it opens and as it changes (players arriving add rows): nothing a player needs past the edge.
        if ((_sceneFrames == SettleFrames || _sceneFrames % 30 == 0) && OffTheWindow() is { } wide)
        {
            Fail($"{wide} lies past the right edge of the {Tree.Root.GetVisibleRect().Size.X:0} px window on {Tree.CurrentScene?.Name}");
        }

        // The lobby and the session pages: no caption or value cut short.
        if (_sceneFrames == SettleFrames * 2 && Tree.CurrentScene is LobbyRoomScreen or HostScreen &&
            LayoutCheck.Problems(Tree.CurrentScene, Tree.Root.GetVisibleRect().Size.X) is { Count: > 0 } cut)
        {
            Fail($"text cut short on {Tree.CurrentScene.Name}: {string.Join("; ", cut.Take(6))}");
        }

        if (_step is null)
        {
            if (!_steps.MoveNext())
            {
                Note($"{_pilot.Clicks} clicks, all steps passed");
                _teardown = 0;
                return SmokeResult.Running;
            }

            _step = _steps.Current;
            _stepStart = _now;
        }

        bool done;
        try
        {
            done = _step.Try(this);
        }
        catch (Exception e) when (e is InvalidCastException or NullReferenceException or InvalidOperationException)
        {
            done = false;
            if (StepSeconds > _step.TimeoutSeconds)
            {
                Fail($"step '{_step.Label}' threw {e.GetType().Name}: {e.Message}");
            }
        }

        if (_failure is not null)
        {
            Shot("failure-" + Safe(_step.Label));
            _teardown = 0;
            return SmokeResult.Running;
        }

        if (done)
        {
            string last = Pilot.Log.Count > _loggedActions ? " [" + string.Join("; ", Pilot.Log.Skip(_loggedActions)) + "]" : string.Empty;
            _loggedActions = Pilot.Log.Count;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_NETLOBBY {_role} step '{_step.Label}' done at {_now:0.0} s ({StepSeconds:0.0} s){last}"));
            _step = null;
        }
        else if (StepSeconds > _step.TimeoutSeconds)
        {
            Fail($"step '{_step.Label}' timed out after {_step.TimeoutSeconds:0} s on {Tree.CurrentScene?.Name} ({Describe()})");
            Shot("timeout-" + Safe(_step.Label));
            _teardown = 0;
        }

        return SmokeResult.Running;
    }

    /// <summary>
    /// Types <paramref name="text"/> into the edit box <paramref name="name"/> as key presses, until the box holds it
    /// (true then; the step's time limit fails a box that never takes it).
    /// </summary>
    public bool Typed(string name, string text)
    {
        LineEdit? edit = Pilot.Find<LineEdit>(name);
        if (edit?.Text == text)
        {
            return true;
        }

        if (edit is not null && _typeTries++ % 4 == 0)
        {
            Note($"typing into {name} (it holds '{edit.Text}')");
        }

        Pilot.Type(edit, text, name);
        return false;
    }

    /// <summary>A smoke run starts on the boot scene; the game's normal start opens the main menu, so this does.</summary>
    public bool OpenMainMenu()
    {
        if (Tree.CurrentScene is not OpenScctGame.Boot)
        {
            return false;
        }

        FrontEnd.ShowMainMenu(Tree);
        return true;
    }

    /// <summary>A visible button or edit box of the page wider than the window (a player could not reach it), or null.</summary>
    private string? OffTheWindow()
    {
        float width = Tree.Root.GetVisibleRect().Size.X;
        foreach (Node n in Tree.CurrentScene?.FindChildren("*", nameof(Control), recursive: true, owned: false) ?? [])
        {
            if (n is BaseButton or LineEdit or Godot.Range && n is Control c && c.IsVisibleInTree() && c.GetGlobalRect().End.X > width + 1f)
            {
                var chain = new List<string>();
                for (Node? up = c; up is Control uc && chain.Count < 8; up = up.GetParent())
                {
                    chain.Add(string.Create(CultureInfo.InvariantCulture, $"{uc.Name} {uc.GetGlobalRect().Position.X:0}+{uc.Size.X:0}"));
                }

                return c.Name + (c is Button b ? " ('" + b.Text + "')" : string.Empty) + " [" + string.Join(" < ", chain) + "]";
            }
        }

        return null;
    }

    /// <summary>Checks a condition; a false one fails the run with <paramref name="what"/>.</summary>
    public bool Expect(bool ok, string what)
    {
        if (!ok)
        {
            Fail(what);
        }

        return ok;
    }

    private void Fail(string what)
    {
        _failure ??= $"{_role}: {what}";
        GD.PrintErr("SVM_NETLOBBY " + _role + " FAIL " + what);
    }

    public void Note(string text)
    {
        _notes.Add(text);
        GD.Print("SVM_NETLOBBY " + _role + " " + text);
    }

    /// <summary>Saves the window (windowed runs with SVM_PLAY_SHOTS) as role-label.png; with <paramref name="once"/> only the first time.</summary>
    public void Shot(string label, bool once = false)
    {
        string name = _role + "-" + label;
        if (once && !_shots.Add(name))
        {
            return;
        }

        if (MenuPage.Shot(Tree.Root, name) is { } file)
        {
            GD.Print("SVM_NETLOBBY " + _role + " shot " + file);
        }
    }

    /// <summary>The text of a named label, edit box or rich text on the current page ("" when missing).</summary>
    public string Text(string name)
    {
        Node? n = Tree.CurrentScene?.FindChild(name, recursive: true, owned: false);
        return n switch
        {
            RichTextLabel r => r.GetParsedText(),
            Label l => l.Text,
            LineEdit e => e.Text,
            Container c => string.Join(" ", c.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(x => x.Text)),
            _ => string.Empty,
        };
    }

    public string ValueText(string name) => Tree.CurrentScene?.FindChild(name, recursive: true, owned: false) is Label l ? l.Text : string.Empty;

    /// <summary>Whether a visible label anywhere in the tree reads <paramref name="text"/> (overlays included).</summary>
    public bool LabelShown(string text)
    {
        foreach (Node n in Tree.Root.FindChildren("*", nameof(Label), recursive: true, owned: false))
        {
            if (n is Label { Visible: true } l && l.Text.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string Describe()
    {
        OnlineService service = OnlineService.Get(Tree);
        return $"visit={service.Client?.State} error={service.Client?.Error ?? service.LastError} map={service.Client?.CurrentMap} " +
            $"log={string.Join("/", service.Client?.ConnectLog ?? [])} pilot={string.Join(" / ", Pilot.Log.TakeLast(3))}";
    }

    private static string Safe(string label) => label.Replace(' ', '-').Replace('\'', '-').Replace('%', 'p');

    // ---- the two processes ------------------------------------------------------------------------------------

    /// <summary>The test's ports and quiet audio, before anything reads the online settings.</summary>
    public static void PrepareEnvironment()
    {
        if (System.Environment.GetEnvironmentVariable("SVM_HOST_PORT") is not { Length: > 0 })
        {
            System.Environment.SetEnvironmentVariable("SVM_HOST_PORT", "28988");
        }

        if (System.Environment.GetEnvironmentVariable("SVM_LAN_PORT") is not { Length: > 0 })
        {
            System.Environment.SetEnvironmentVariable("SVM_LAN_PORT", "28989");
        }

        if (System.Environment.GetEnvironmentVariable("SVM_AUDIO_MUTE") is "1" or "true" && AudioServer.GetBusIndex("Master") is >= 0 and var master)
        {
            AudioServer.SetBusVolumeDb(master, -80f);
        }
    }

    /// <summary>The map the host sets up: SVM_NETLOBBY_MAP, else SteeE when installed, else TestBox.</summary>
    public static string MapName()
    {
        if (System.Environment.GetEnvironmentVariable("SVM_NETLOBBY_MAP") is { Length: > 0 } map)
        {
            return map;
        }

        return OnlineService.Get((SceneTree)Engine.GetMainLoop()).Catalog.Maps.FindMap("SteeE") is not null ? "SteeE" : MapCatalog.TestBox;
    }

    /// <summary>local/net-lobby under the checkout (logs of player 2).</summary>
    public static string OutDir()
    {
        // An export's res:// does not globalize (an empty path): SmokePaths finds the checkout from the exe there.
        string dir = SmokePaths.Local("net-lobby");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Starts player 2: this game again, as the guest smoke, with the same window mode, beside this window.</summary>
    public static int StartGuest(int port, string logFile)
    {
        System.Environment.SetEnvironmentVariable("SVM_NETLOBBY_ADDRESS", "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
        var args = new List<string>();
        if (DisplayServer.GetName() == "headless")
        {
            args.Add("--headless");
        }
        else
        {
            args.Add("--position");
            args.Add("700,80");
        }

        string project = ProjectSettings.GlobalizePath("res://");
        if (project.Length > 0 && File.Exists(Path.Combine(project, "project.godot")))
        {
            args.Add("--path");
            args.Add(project);
        }

        args.Add("--log-file");
        args.Add(logFile);
        args.Add("--");
        args.Add("--svm-smoke=" + NetLobbySmoke.GuestTestName);
        args.Add("--svm-smoke-timeout=900");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--svm-maps=", StringComparison.Ordinal) || arg.StartsWith("--svm-game-root=", StringComparison.Ordinal))
            {
                args.Add(arg);
            }
        }

        return OS.CreateProcess(OS.GetExecutablePath(), args.ToArray());
    }
}
