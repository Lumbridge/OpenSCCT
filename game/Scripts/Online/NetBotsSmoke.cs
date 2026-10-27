using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server.Online;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Smoke;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>
/// Server bots in an online match, end to end with two game processes on this machine (docs/NETWORK-TEST.md, "Bots"):
/// <c>--svm-smoke=net-bots</c> is player 1, the host; it starts player 2 (<c>--svm-smoke=net-bots-guest</c>), and both
/// are driven only through their menus and keys, over UDP on localhost.
/// <para>Player 1: Create a Session; Max Players 4, Game Length 1 minute, Bots Fill Slots, Bot Skill Easy, the map, UPnP
/// off; Create; the Lobby Room shows him on the spies with one spy bot and two merc bots; player 2 joins by Join with IP
/// on the mercenaries and a merc bot makes room (a 2v2: one bot on each side); launches when player 2 is ready; the
/// round has four players, the bots' pawns among them; the results list the bots; Next; back in the Lobby Room with
/// the same bots; player 2 leaves and a merc bot takes his place; leaves.</para>
/// <para>Player 2: Join with IP; sees the bots as rows that read Bot, without a volume slider; ready; in the round
/// sees the bots' pawns, walks with the movement key; Next; leaves.</para>
/// <para>Ports 28990 (game) and 28991 (LAN) unless SVM_HOST_PORT and SVM_LAN_PORT say otherwise, so it can run beside
/// <see cref="NetLobbySmoke"/>. The map is SVM_NETLOBBY_MAP, SteeE when installed, else TestBox. Player 2's log goes to
/// <c>local/net-bots/guest.log</c>. The host's saved Create Session values get Bots Off again when the test ends.</para>
/// </summary>
public sealed class NetBotsSmoke : SmokeTest
{
    public const string TestName = "net-bots";
    public const string GuestTestName = "net-bots-guest";
    public const string HostName = "Host";
    public const string GuestName = "Guest";
    public const string ServerName = "Net bots test";

    private readonly NetLobbyScript _script;
    private int _guestPid = -1;
    private string _guestLog = string.Empty;
    private bool _restored;

    public NetBotsSmoke()
    {
        Ports();
        NetLobbyScript.PrepareEnvironment();
        _script = new NetLobbyScript("host", Steps());
    }

    public override string Name => TestName;

    /// <summary>This test's own ports, before the online settings are read.</summary>
    internal static void Ports()
    {
        if (System.Environment.GetEnvironmentVariable("SVM_HOST_PORT") is not { Length: > 0 })
        {
            System.Environment.SetEnvironmentVariable("SVM_HOST_PORT", "28990");
        }

        if (System.Environment.GetEnvironmentVariable("SVM_LAN_PORT") is not { Length: > 0 })
        {
            System.Environment.SetEnvironmentVariable("SVM_LAN_PORT", "28991");
        }
    }

    public override SmokeResult Step(SmokeContext context)
    {
        SmokeResult r = _script.Step(context);
        if (r.State != SmokeState.Running)
        {
            RestoreHostSetup();
            if (_guestPid > 0 && OS.IsProcessRunning(_guestPid))
            {
                // Only the process this test started.
                OS.Kill(_guestPid);
            }
        }

        return r.State == SmokeState.Failed ? SmokeResult.Fail(r.Detail + GuestTail()) : r;
    }

    /// <summary>The next Create Session page (net-lobby's, a player's) opens without this test's bots.</summary>
    private void RestoreHostSetup()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        OnlineSettings s = OnlineSettings.Current;
        if (s.HostSetup.Length > 0)
        {
            s.HostSetup = (LobbySetup.FromOptions(s.HostSetup) with { Bots = BotFill.Off }).ToOptions();
            s.Save();
        }
    }

    private string GuestTail()
    {
        if (!File.Exists(_guestLog))
        {
            return string.Empty;
        }

        using var stream = new FileStream(_guestLog, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string[] lines = reader.ReadToEnd().Split('\n').Where(l => l.Contains("SVM_NETLOBBY", StringComparison.Ordinal) ||
            l.Contains("SVM_SMOKE", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)).TakeLast(12).ToArray();
        return " | guest: " + string.Join(" / ", lines);
    }

    /// <summary>The bots of the round as this player's game sees them: pawns whose player name is a bot's.</summary>
    internal static int BotPawns(PlayRoot root)
    {
        int n = 0;
        foreach (Pawn p in root.Session!.Client.World.Level.AllActors<Pawn>())
        {
            if (!p.bDeleteMe && p.PlayerReplicationInfo?.PlayerName is { } name &&
                (name.StartsWith("SpyBot", StringComparison.Ordinal) || name.StartsWith("MercBot", StringComparison.Ordinal)))
            {
                n++;
            }
        }

        return n;
    }

    internal static int Bots(LobbyRoomScreen? room, int team) =>
        room?.Session?.Members.Count(m => m.IsBot && m.Side == (team == 0 ? Hud.HudSide.Spy : Hud.HudSide.Merc)) ?? -1;

    private IEnumerable<NetLobbyStep> Steps()
    {
        NetLobbyScript s = null!;
        LobbyRoomScreen? Room() => s.Tree.CurrentScene as LobbyRoomScreen;
        OnlineLobbyPlayer? Guest() => Room()?.Session?.Members.FirstOrDefault(m => m.Name == GuestName);
        HostScreen Page() => (HostScreen)s.Tree.CurrentScene!;

        yield return new("start", 30, sc =>
        {
            s = sc;
            SettingsStore.Current.PlayerName = HostName;
            return !s.OpenMainMenu() && s.Tree.CurrentScene is MainMenu menu && s.Pilot.Click(menu.HostButton, "Create a Session");
        });
        yield return new("create session page", 30, _ => s.Tree.CurrentScene is HostScreen { Model: not null });
        yield return new("map", 30, _ => string.Equals(Page().Model!.Setup.Map, NetLobbyScript.MapName(), StringComparison.OrdinalIgnoreCase) ||
            (s.Pilot.Click(Page().Rows!.Find("MapNext"), "Map next") && false));
        yield return new("max players 4", 20, _ => Page().Model!.Setup.MaxPlayers == 4 ||
            (s.Pilot.Click(Page().Rows!.Find(Page().Model!.Setup.MaxPlayers > 4 ? "MaxPlayersPrev" : "MaxPlayersNext"), "Max Players") && false));
        yield return new("game length 1 min", 20, _ => Page().Model!.Setup.TimeLimit == 1 ||
            (s.Pilot.Click(Page().Rows!.Find(Page().Model!.Setup.TimeLimit > 1 ? "TimeLimitPrev" : "TimeLimitNext"), "Game Length") && false));
        yield return new("no revenge", 10, _ => !Page().Model!.Setup.Revenge || (s.Pilot.Click(Page().Rows!.Find("RevengeCheck"), "Revenge") && false));
        yield return new("bots fill slots", 20, _ => Page().Model!.Setup.Bots == BotFill.Fill ||
            (s.Pilot.Click(Page().Rows!.Find("BotsNext"), "Bots next") && false));
        yield return new("bot skill easy", 20, _ => Page().Model!.Setup.BotSkill == OpenScct.Game.AI.AiSkill.Easy ||
            (s.Pilot.Click(Page().Rows!.Find("BotSkillPrev"), "Bot Skill previous") && false));
        yield return new("bot rows read", 10, _ =>
            s.Expect(s.ValueText("BotsValue") == "Fill Slots" && s.ValueText("BotSkillValue") == "Easy",
                $"the rows read '{s.ValueText("BotsValue")}' and '{s.ValueText("BotSkillValue")}'"));
        yield return new("no password", 10, _ => Page().Model!.Password.Length == 0 ||
            (s.Pilot.Type(Page().Rows!.Find("PasswordEdit") as LineEdit, string.Empty, "Password") && false));
        yield return new("upnp off", 10, _ =>
        {
            CheckBox? upnp = s.Pilot.Find<CheckBox>("Upnp");
            return upnp is { ButtonPressed: false } || (s.Pilot.Click(upnp, "UPnP") && false);
        });
        yield return new("server name", 10, _ => s.Typed("ServerName", ServerName));
        yield return new("create", 10, _ =>
        {
            s.Shot("create-session");
            return s.Pilot.Click(Page().HostButton, "Create");
        });
        yield return new("lobby room as host", 30, _ => Room() is { Session: { IsHost: true, CanControl: true } } && OnlineService.Get(s.Tree).Hosting is not null);
        yield return new("bots fill the empty places", 20, _ => Bots(Room(), 0) == 1 && Bots(Room(), 1) == 2 &&
            MarkedBot(Room(), "SpyBot1"));
        yield return new("start player 2", 10, _ =>
        {
            s.Shot("lobby-bots");
            int port = OnlineService.Get(s.Tree).Hosting!.Port;
            string dir = SmokePaths.Local("net-bots");
            Directory.CreateDirectory(dir);
            _guestLog = Path.Combine(dir, "guest.log");
            _guestPid = StartGuest(port, _guestLog);
            s.Note($"player 2 started: pid {_guestPid}, port {port}, log {_guestLog}");
            return _guestPid > 0;
        });
        yield return new("player 2 in the lobby, a bot makes room", 120, _ =>
            Guest() is { Side: Hud.HudSide.Merc } && Bots(Room(), 0) == 1 && Bots(Room(), 1) == 1 && Room()!.Session!.Members.Count == 4);
        yield return new("player 2 ready", 120, _ =>
        {
            s.Shot("lobby-2v2", once: true);
            return Guest() is { Ready: true };
        });
        yield return new("launch", 10, _ => Room()!.MainButton?.Text == "Launch" && s.Pilot.Click(Room()!.MainButton, "Launch"));
        yield return new("the round on the map", 180, _ =>
            s.Tree.CurrentScene is PlayRoot { IsLoaded: true, Session: { IsRemote: true, LocalPawn: not null } } &&
            OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Match);
        yield return new("the bots play", 60, _ =>
        {
            var root = (PlayRoot)s.Tree.CurrentScene!;
            if (BotPawns(root) < 2)
            {
                return false;
            }

            s.Shot("round-bots");
            s.Note($"{BotPawns(root)} bot pawns in the round, {root.Session!.Client.World.Level.AllActors<PlayerReplicationInfo>().Count()} players");
            return true;
        });
        yield return new("results", 240, _ =>
            s.Tree.CurrentScene is ResultsScreen { BannerShowing: false, RematchButton.Visible: true } &&
            OnlineService.Get(s.Tree).Client?.Results is { } results &&
            s.Expect(results.Rows.Count == 4 && results.Rows.Any(r => r.Name == "SpyBot1"),
                "the results list four players, the bots among them: " + string.Join(", ", results.Rows.Select(r => r.Name))));
        yield return new("next", 10, _ =>
        {
            s.Shot("results");
            var results = (ResultsScreen)s.Tree.CurrentScene!;
            return s.Pilot.Click(results.RematchButton, "Next");
        });
        yield return new("back in the lobby with the bots", 120, _ =>
            Room() is { Session.Members.Count: 4 } && OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Lobby &&
            Bots(Room(), 0) == 1 && Bots(Room(), 1) == 1);
        yield return new("player 2 leaves, a bot takes his place", 120, _ =>
        {
            s.Shot("lobby-after-round", once: true);
            return Guest() is null && Bots(Room(), 1) == 2;
        });
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

    /// <summary>The player's row is drawn and its ready column reads "Bot".</summary>
    internal static bool MarkedBot(LobbyRoomScreen? room, string name) =>
        room?.PlayerLine(name)?.FindChild("ReadyState", recursive: true, owned: false) is Label { Text: LobbyRoomScreen.BotText };

    /// <summary>Starts player 2: this game again, as the guest smoke, with the same window mode.</summary>
    private static int StartGuest(int port, string logFile)
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
        args.Add("--svm-smoke=" + GuestTestName);
        args.Add("--svm-smoke-timeout=600");
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

/// <summary>Player 2's half of <see cref="NetBotsSmoke"/>.</summary>
public sealed class NetBotsGuestSmoke : SmokeTest
{
    private readonly NetLobbyScript _script;
    private Vector3? _walkStart;
    private KeyPilot? _keys;

    public NetBotsGuestSmoke()
    {
        NetBotsSmoke.Ports();
        _script = new NetLobbyScript("guest", Steps());
    }

    public override string Name => NetBotsSmoke.GuestTestName;

    public override SmokeResult Step(SmokeContext context) => _script.Step(context);

    private IEnumerable<NetLobbyStep> Steps()
    {
        NetLobbyScript s = null!;
        LobbyRoomScreen? Room() => s.Tree.CurrentScene as LobbyRoomScreen;
        OnlineLobbyPlayer? Me() => Room()?.Session?.Members.FirstOrDefault(m => m.IsLocal);
        string address = System.Environment.GetEnvironmentVariable("SVM_NETLOBBY_ADDRESS") ?? "127.0.0.1:28990";

        yield return new("start", 30, sc =>
        {
            s = sc;
            SettingsStore.Current.PlayerName = NetBotsSmoke.GuestName;
            return !s.OpenMainMenu() && s.Tree.CurrentScene is MainMenu menu && s.Pilot.Click(menu.JoinButton, "Join with IP");
        });
        yield return new("join with ip page", 10, _ => s.Tree.CurrentScene is JoinWithIpScreen);
        yield return new("server address", 10, _ => s.Typed("Address", address.Split(':')[0]));
        yield return new("server port", 10, _ => s.Typed("Port", address.Split(':')[1]));
        yield return new("join", 10, _ => s.Pilot.Click(((JoinWithIpScreen)s.Tree.CurrentScene!).JoinButton, "Join"));
        yield return new("lobby room as player 2", 60, _ => Room() is { Session: { IsHost: false } } && Me() is not null);
        yield return new("on the mercenaries", 20, _ => Me() is { Side: Hud.HudSide.Merc } || (s.Pilot.Click(Room()!.SwitchTeamButton, "Switch Team") && false));
        yield return new("the bots as rows", 30, _ =>
        {
            if (NetBotsSmoke.Bots(Room(), 0) != 1 || NetBotsSmoke.Bots(Room(), 1) != 1)
            {
                return false;
            }

            s.Shot("lobby-bots", once: true);
            return s.Expect(NetBotsSmoke.MarkedBot(Room(), "SpyBot1"), "the spy bot's row reads Bot") &&
                s.Expect(Room()!.PlayerLine("SpyBot1")?.FindChild("Volume", recursive: true, owned: false) is null, "a bot's row has no volume slider") &&
                s.Expect(Room()!.PlayerLine(NetBotsSmoke.HostName)?.FindChild("Volume", recursive: true, owned: false) is not null, "the host's row has one");
        });
        yield return new("ready", 10, _ => Room()!.MainButton?.Text == "Ready" && s.Pilot.Click(Room()!.MainButton, "Ready"));
        yield return new("the round on the map", 180, _ =>
            s.Tree.CurrentScene is PlayRoot { IsLoaded: true, Session: { IsRemote: true, LocalPawn: not null } } root && root.Controls?.Bindings is not null);
        yield return new("the bots in the round", 60, _ =>
        {
            var root = (PlayRoot)s.Tree.CurrentScene!;
            return NetBotsSmoke.BotPawns(root) >= 2;
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
            if (root.Session?.LocalPawn is not { } pawn)
            {
                // Killed by a bot meanwhile: the round goes on.
                s.Note("no pawn after the walk (killed)");
                return true;
            }

            FVector at = pawn.Location;
            float moved = new Vector3(at.X, at.Y, at.Z).DistanceTo(_walkStart!.Value);
            s.Note(string.Create(CultureInfo.InvariantCulture, $"walked {moved:0} uu with the movement key, {NetBotsSmoke.BotPawns(root)} bot pawns seen"));
            s.Shot("round-walked");
            return s.Expect(moved > 60f, $"the pawn moved {moved:0} uu");
        });
        yield return new("results", 240, _ => s.Tree.CurrentScene is ResultsScreen { BannerShowing: false, RematchButton.Visible: true });
        yield return new("next", 10, _ =>
        {
            s.Shot("results");
            return s.Pilot.Click(((ResultsScreen)s.Tree.CurrentScene!).RematchButton, "Next");
        });
        yield return new("back in the lobby", 120, _ => Room() is { Session.Members.Count: 4 } && OnlineService.Get(s.Tree).Client?.State == OnlineClientState.Lobby);
        yield return new("leave", 10, _ =>
        {
            s.Shot("lobby-after-round");
            return s.Pilot.Click(Room()!.LeaveButton, "Leave");
        });
        yield return new("main menu", 30, _ => s.Tree.CurrentScene is MainMenu);
    }
}
