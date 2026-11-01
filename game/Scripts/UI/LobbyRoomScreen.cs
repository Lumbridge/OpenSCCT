using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// The online lobby as the original's Lobby Room page (Lobby_Create; spec/game/lobby-and-match-creation.md rules 11 to
/// 18; docs/MENUS.md): the Spies and Mercenaries lists ("Waiting for players" when empty) with each player's ready
/// state, ping and voice (speaking, and the local player's volume and mute for him, Reloaded's per-player control),
/// the spectators, "Switch Team", "Restrict Mic to Team", the Session Parameters (the Create Session rows: the host
/// edits them and applies, a client reads them and sees changed values blink), the chat box with Say and TeamSay and
/// its spam delay, kicking with "Do you want to kick" (and, beyond the original, Ban and Voice Ban with the same
/// question), and the page buttons: Leave, Options, Strategic Map (the selected map's floors) and Launch (host;
/// Cancel while the launch counts down) or Ready / Not Ready (client).
/// </summary>
public partial class LobbyRoomScreen : MenuPage
{
    /// <summary>What a server bot's row reads in the ready column.</summary>
    public const string BotText = "Bot";

    /// <summary>
    /// The mark after a signed-in (verified) player's name: a check mark when the menu font draws one, else "[v]"
    /// (the original look's bitmap font has no such glyph).
    /// </summary>
    private static string VerifiedMark =>
        (UiStyle.IsOriginal ? UiStyle.MenuTheme.DefaultFont : UiArt.BodyFont) is { } font && font.HasChar('✓') ? " ✓" : " [v]";

    private readonly Dictionary<int, PlayerRow> _rows = new();
    private readonly LobbyRoomModel _room = new();
    private VBoxContainer? _spies;
    private VBoxContainer? _mercs;
    private VBoxContainer? _spectators;
    private Label? _spiesWaiting;
    private Label? _mercsWaiting;
    private Label? _status;
    private HBoxContainer? _mapFetchBar;
    private Label? _mapFetchText;
    private ProgressBar? _mapFetchProgress;
    private Button? _mapFetchButton;
    private Label? _microphone;
    private RichTextLabel? _messages;
    private LineEdit? _chat;
    private Control? _confirm;
    private Label? _confirmText;
    private int _confirmKick = -1;
    private ConfirmAction _confirmAction;
    private int _shownMessages = -1;
    private double _countdownEnd;
    private float _lastCountdown;
    private LobbySetup? _modelSetup;
    private OpenScct.Server.MapCatalog? _mapCatalog;

    /// <summary>What the Yes of the confirmation does.</summary>
    private enum ConfirmAction
    {
        Kick,
        Ban,
        VoiceBan,
        VoiceUnban,
    }

    // The Admin menu's item ids (also their indexes).
    private const int AdminKick = 0;
    private const int AdminBan = 1;
    private const int AdminVoiceBan = 2;

    private sealed class PlayerRow
    {
        public required HBoxContainer Line { get; init; }

        public required TextureRect Speaker { get; init; }

        public required Label Name { get; init; }

        public required RankBadge Rank { get; init; }

        public required Label Ready { get; init; }

        public required Label Ping { get; init; }

        public HSlider? Volume { get; init; }

        public Label? VolumeText { get; init; }

        public CheckBox? Mute { get; init; }

        /// <summary>The host's Kick, Ban and Voice Ban, in one menu (null on the local player's and bots' rows).</summary>
        public MenuButton? Admin { get; init; }

        public bool VoiceBanned { get; set; }

        public string PlayerName { get; set; } = string.Empty;
    }

    public IOnlineLobbySession? Session { get; set; }

    /// <summary>The Session Parameters rows' rules (the host edits through them).</summary>
    public CreateSessionModel? Parameters { get; private set; }

    public SessionRowsView? ParameterRows { get; private set; }

    /// <summary>The selected map's picture in Session Parameters.</summary>
    public MapThumb? MapPicture { get; private set; }

    /// <summary>Launch / Cancel (host) or Ready / Not Ready (client).</summary>
    public Button? MainButton { get; private set; }

    public Button? LeaveButton { get; private set; }

    public Button? SwitchTeamButton { get; private set; }

    public Button? SpectateButton { get; private set; }

    public Button? ApplyButton { get; private set; }

    /// <summary>"Maps": the host's map list with playlists and search.</summary>
    public Button? MapsButton { get; private set; }

    public CheckBox? TeamVoiceOnlyBox { get; private set; }

    /// <summary>"Alternate": the Alternate Mode page, shown while the setup (or the host's edit) has it on.</summary>
    public Button? AlternateButton { get; private set; }

    private AlternateModePanel? _alternate;

    /// <summary>"Briefing": the setup's map and mode briefing for the local player's side (GUI.Briefing, BriefingDeux).</summary>
    public Button? BriefingButton { get; private set; }

    /// <summary>OpenSCCT's own: opens the Armoury over the lobby to change cosmetics.</summary>
    public Button? ArmouryButton { get; private set; }

    private void OpenBriefing()
    {
        if (Session is not { } s || FindChild("Briefing", recursive: false, owned: false) is not null)
        {
            return;
        }

        // A spectator reads the mercs' words, as the original's pages do for any team but the spies.
        bool spy = Local()?.Side == HudSide.Spy;
        string map = s.Setup.Map;
        BriefingPages pages = BriefingPages.For(s.MapSettings(map), s.Setup.Mode, spy);
        string title = (_mapCatalog is { } maps ? MapTitles.Of(maps, map) : map) + " - " + CreateSessionModel.ModeName(s.Setup.Mode);
        AddChild(new BriefingPanel(pages, MapImages.Loading(_mapCatalog, map, !spy), title));
    }

    /// <summary>"Equipment" (Lobby_Create's Button3, which opens Edit_Profile): the gear picker for the local player's side.</summary>
    public Button? EquipmentButton { get; private set; }

    /// <summary>
    /// The gear picker over the lobby, for the side the local player is on and the setup's gadget rules; OK saves the
    /// choice, which the server takes when the round spawns the player (spec/additions/gear-change.md).
    /// </summary>
    [OpenScct.Provenance.PortOf("uc:GUI.Lobby_Create:ButtonClick", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    private void OpenEquipment()
    {
        if (Session is not { } s || FindChild("GearMenu", recursive: false, owned: false) is not null)
        {
            return;
        }

        int team = Local()?.Side switch
        {
            HudSide.Spy => 0,
            HudSide.Merc => 1,
            _ => -1,
        };
        GearMenuModel? model = GearMenuModel.ForLobby(team, s.Setup, SettingsStore.Current.Loadout,
            OpenScctGame.Online.AccountService.Get(GetTree()).Account?.Progress?.Level ?? 1, s.SidearmsUnrestricted);
        var gear = new GearMenu(model) { OwnKeys = true };
        gear.Applied += m =>
        {
            GameSettings settings = SettingsStore.Current;
            m.SaveTo(settings.Loadout);
            SettingsStore.Save(settings);
            gear.QueueFree();
            UpdateLoadout();
        };
        gear.Closed += gear.QueueFree;
        AddChild(gear);
    }

    private void ShowAlternateButton()
    {
        if (AlternateButton is not null && Session is { } s && Parameters is not null)
        {
            AlternateButton.Visible = s.CanControl ? Parameters.Setup.Alternate.On : s.Setup.Alternate.On;
        }
    }

    private PanelContainer? _mapsPanel;

    /// <summary>
    /// The host's map list (rl:qol.lobbymaps): the same pictures, playlist choice and search as Create Session, over the
    /// page; a pick goes into Session Parameters, and Apply (or OK here) sends it.
    /// </summary>
    private void OpenMaps()
    {
        if (Session is not { CanControl: true } s || Parameters is null || _mapCatalog is null || IsInstanceValid(_mapsPanel))
        {
            return;
        }

        var panel = new PanelContainer
        {
            Name = "MapsPanel", AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetTop = 16f, OffsetRight = -16f, OffsetBottom = -16f,
        };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);
        column.AddChild(new Label { Name = "Title", Text = "Choose Map", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle });
        // The modal supplies the height; a fixed scroll minimum would push its footer off short screens.
        var picker = new MapPickerView(Parameters, _mapCatalog, s.Maps, 0f, SettingsStore.Current.ActiveMapPlaylist, HostScreen.RememberPlaylist)
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(picker);
        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        Button ok = UiStyle.MenuButton("OK", () =>
        {
            panel.QueueFree();
        }, "MapsOk");
        Button close = UiStyle.MenuButton("Close", panel.QueueFree, "MapsClose");
        ok.CustomMinimumSize = close.CustomMinimumSize = new Vector2(160f, 36f);
        buttons.AddChild(close);
        buttons.AddChild(ok);
        _mapsPanel = panel;
        AddChild(panel);
    }

    private void OpenAlternate()
    {
        if (Session is not { } s || Parameters is null || IsInstanceValid(_alternate))
        {
            return;
        }

        bool edit = s.CanControl;
        _alternate = new AlternateModePanel(edit ? Parameters.Setup.Alternate : s.Setup.Alternate, edit, rules =>
        {
            Parameters.SetAlternate(rules);
        });
        AddChild(_alternate);
    }

    public override void _Ready()
    {
        Name = "LobbyRoom";
        BuildFrame("Lobby", 0f);
        LobbyPanels.ExpandBody(Body);
        BuildLobby();
        BuildConfirm();
        Session!.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Session is not null)
        {
            Session.Changed -= Refresh;
        }

        if (Parameters is not null)
        {
            Parameters.Changed -= ShowMapPicture;
            Parameters.Changed -= ShowAlternateButton;
        }
    }

    private void ShowMapPicture()
    {
        if (MapPicture is null || Parameters is null)
        {
            return;
        }

        string map = Session?.Setup.Map ?? Parameters.Setup.Map;
        MapPicture.SetMap(MapImages.Preview(_mapCatalog, map), _mapCatalog is { } maps ? MapTitles.Of(maps, map) : map);
    }

    /// <summary>
    /// The line under the status when the local player lacks the setup's map (or has another version): what is
    /// missing, a Download button from OpenSCCT-Content (Stop while it runs) and the progress (docs/ONLINE.md
    /// "Missing maps").
    /// </summary>
    private void BuildMapFetchBar()
    {
        _mapFetchBar = new HBoxContainer { Name = "MapFetch", Visible = false };
        _mapFetchBar.AddThemeConstantOverride("separation", 12);
        _mapFetchText = new Label
        {
            Name = "Text", SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _mapFetchProgress = new ProgressBar
        {
            Name = "Progress", CustomMinimumSize = new Vector2(220f, 20f), MinValue = 0, MaxValue = 1, Step = 0.001,
            ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter, Visible = false,
        };
        _mapFetchButton = UiStyle.MenuButton("Download", PressMapFetch, "MapFetchButton");
        _mapFetchButton.CustomMinimumSize = new Vector2(180f, 36f);
        _mapFetchBar.AddChild(_mapFetchText);
        _mapFetchBar.AddChild(_mapFetchProgress);
        _mapFetchBar.AddChild(_mapFetchButton);
        Body.AddChild(_mapFetchBar);
    }

    private void PressMapFetch()
    {
        if (Session?.MapFetch is not { } fetch)
        {
            return;
        }

        if (fetch.Running)
        {
            Session.CancelMapFetch();
        }
        else if (fetch.CanStart)
        {
            Session.StartMapFetch();
        }
    }

    private void ShowMapFetch(IOnlineLobbySession s)
    {
        s.UpdateMapFetch();
        if (_mapFetchBar is null || _mapFetchText is null || _mapFetchProgress is null || _mapFetchButton is null)
        {
            return;
        }

        LobbyMapFetch? fetch = s.MapFetch;
        _mapFetchBar.Visible = fetch is not null;
        if (fetch is null)
        {
            return;
        }

        _mapFetchText.Text = fetch.Text;
        _mapFetchText.Modulate = fetch.Problem ? UiStyle.MenuDisabled : UiStyle.MenuFocus;
        _mapFetchProgress.Visible = fetch.Running;
        _mapFetchProgress.Value = fetch.Fraction;
        _mapFetchButton.Visible = fetch.Running || fetch.CanStart;
        _mapFetchButton.Text = fetch.Running ? "Stop" : fetch.Problem ? "Try again" : "Download";
    }

    private void BuildConfirm()
    {
        _confirm = new PanelContainer { Name = "KickConfirm", Visible = false, AnchorLeft = 0.35f, AnchorRight = 0.65f, AnchorTop = 0.4f, AnchorBottom = 0.55f };
        _confirm.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer();
        _confirmText = new Label { Name = "Text", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_confirmText);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        Button yes = UiStyle.MenuButton("Yes", () => ConfirmKick(true), "KickYes");
        Button no = UiStyle.MenuButton("No", () => ConfirmKick(false), "KickNo");
        yes.CustomMinimumSize = no.CustomMinimumSize = new Vector2(140f, 40f);
        buttons.AddChild(yes);
        buttons.AddChild(no);
        column.AddChild(buttons);
        _confirm.AddChild(column);
        AddChild(_confirm);
    }

    private void SwitchTeam()
    {
        if (Session is null)
        {
            return;
        }

        OnlineLobbyPlayer? local = Local();
        Session.SetSide(local?.Side == HudSide.Spy ? HudSide.Merc : HudSide.Spy);
    }

    private void PressMain()
    {
        if (Session is not { } s)
        {
            return;
        }

        if (s.CanControl)
        {
            s.Start(GetTree());
        }
        else
        {
            s.SetReady(!(Local()?.Ready ?? false));
        }
    }

    private void Apply()
    {
        if (Session is { CanControl: true } s && Parameters is not null)
        {
            s.ApplySetup(Parameters.Setup with { MaxPlayers = s.Setup.MaxPlayers });
            if (_settingsPanel is not null) _settingsPanel.Visible = false;
        }
    }

    private void SendChat(bool teamOnly)
    {
        if (Session is not { } s || _chat is not { Text.Length: > 0 } chat)
        {
            return;
        }

        if (!_room.TrySendChat(Time.GetTicksMsec() / 1000.0))
        {
            _messages?.AppendText(Dim(LobbyRoomModel.SpamText) + "\n");
            return;
        }

        if (teamOnly && Local()?.Side is null)
        {
            _messages?.AppendText(Dim("Join a team before sending team chat.") + "\n");
            return;
        }
        s.Chat(chat.Text, teamOnly);
        chat.Text = string.Empty;
    }

    /// <summary>
    /// OpenSCCT's own: the Armoury over the lobby. Picks are saved as from the main menu; on Back the saved looks go to
    /// the server, which keeps what the player unlocks and puts them on from the next round.
    /// </summary>
    private void OpenArmoury()
    {
        if (FindChild("Armoury", recursive: false, owned: false) is not null)
        {
            return;
        }

        ArmouryScreen? armoury = null;
        armoury = new ArmouryScreen
        {
            Closed = () =>
            {
                Session?.SendLook();
                armoury?.QueueFree();
            },
        };
        AddChild(armoury);
    }

    private void OpenOptions()
    {
        var options = new OptionsMenu { Overlay = true };
        options.Closed += options.QueueFree;
        AddChild(options);
    }

    /// <summary>
    /// An OpenSCCT addition: the selected map's strategic map (floors, Up and Down, the legend) over the lobby, without
    /// markers (spec/hud/strategic-map.md).
    /// </summary>
    private void OpenStrategicMap()
    {
        string map = Parameters?.Setup.Map ?? string.Empty;
        string? folder = string.IsNullOrEmpty(map) ? null : OpenScctGame.Render.MapBuilder.FindMapFolder(map);
        var page = new StrategicMapScreen(StrategicMapData.Load(folder)) { OwnKeys = true };
        page.Closed += page.QueueFree;
        AddChild(page);
    }

    private void AskKick(int id, string name, ConfirmAction action = ConfirmAction.Kick)
    {
        if (Session is not { CanModerate: true }) return;
        _confirmKick = id;
        _confirmAction = action;
        if (_confirmText is not null)
        {
            _confirmText.Text = action switch
            {
                ConfirmAction.Ban => "Do you want to ban " + name + " from this server?",
                ConfirmAction.VoiceBan => "Do you want to ban " + name + " from voice chat?",
                ConfirmAction.VoiceUnban => "Do you want to let " + name + " use voice chat again?",
                _ => "Do you want to kick " + name + "?",
            };
        }

        if (_confirm is not null)
        {
            _confirm.MoveToFront();
            _confirm.Visible = true;
        }
    }

    private void ConfirmKick(bool yes)
    {
        if (yes && _confirmKick >= 0 && Session is { CanModerate: true })
        {
            switch (_confirmAction)
            {
                case ConfirmAction.Ban:
                    Session?.Ban(_confirmKick);
                    break;
                case ConfirmAction.VoiceBan or ConfirmAction.VoiceUnban:
                    Session?.SetVoiceBanned(_confirmKick, _confirmAction == ConfirmAction.VoiceBan);
                    break;
                default:
                    Session?.Kick(_confirmKick);
                    break;
            }
        }

        _confirmKick = -1;
        if (_confirm is not null)
        {
            _confirm.Visible = false;
        }
    }

    /// <summary>
    /// Reloaded's join notification (rl:qol.lobby, rl:audio.join): a sound when a person (not a bot) comes into the
    /// lobby after the page opened.
    /// </summary>
    private void NoteJoins(IOnlineLobbySession s)
    {
        bool first = _seenMembers is null;
        _seenMembers ??= new HashSet<int>();
        bool joined = false;
        foreach (OnlineLobbyPlayer p in s.Members)
        {
            if (_seenMembers.Add(p.Id) && !first && !p.IsLocal && !p.IsBot)
            {
                joined = true;
            }
        }

        if (joined)
        {
            OpenScctGame.Audio.FrontEndAudio.Get(GetTree()).PlayJoin();
        }
    }

    private HashSet<int>? _seenMembers;

    private OnlineLobbyPlayer? Local()
    {
        if (Session is null)
        {
            return null;
        }

        foreach (OnlineLobbyPlayer p in Session.Members)
        {
            if (p.IsLocal)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>Redraws the lists, the parameters and the buttons from the session.</summary>
    public void Refresh()
    {
        if (Session is not { } s || _spies is null || _mercs is null || _spectators is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (OnlineLobbyPlayer p in s.Members)
        {
            seen.Add(p.Id);
            if (!_rows.TryGetValue(p.Id, out PlayerRow? row))
            {
                row = MakeRow(p, s);
                _rows[p.Id] = row;
            }

            VBoxContainer list = p.Side switch
            {
                HudSide.Spy => _spies,
                HudSide.Merc => _mercs,
                _ => _spectators,
            };
            if (row.Line.GetParent() != list)
            {
                row.Line.GetParent()?.RemoveChild(row.Line);
                list.AddChild(row.Line);
            }

            row.PlayerName = p.Name;
            row.Name.Text = p.Name + (p.IsLocal ? " (you)" : "") + (p.IsVerified ? VerifiedMark : string.Empty) + (p.IsHost ? " (host)" : string.Empty) +
                (p.TeamVoiceOnly ? " (mic: team)" : string.Empty) + (p.VoiceBanned ? " (voice banned)" : string.Empty) +
                (p.MapNote.Length > 0 ? "  [" + p.MapNote + "]" : string.Empty);
            row.Name.Modulate = p.IsLocal ? UiStyle.SideColour(p.Side ?? HudSide.Spy) : UiStyle.MenuText;
            row.Rank.Show(OpenScct.Net.Cosmetics.PlayerRank.Unpack(p.Rank), withTitle: false);
            // A bot is always ready: its column says what it is instead.
            row.Ready.Text = p.IsBot ? BotText : p.Side is null || p.IsHost ? string.Empty : p.Ready ? "Ready" : "Not ready";
            row.Ping.Text = p.PingMs > 0 ? string.Create(CultureInfo.InvariantCulture, $"{p.PingMs} ms") : string.Empty;
            row.VoiceBanned = p.VoiceBanned;
            if (row.Admin is not null)
            {
                row.Admin.Visible = s.CanModerate;
                row.Admin.GetPopup().SetItemText(AdminVoiceBan, p.VoiceBanned ? "Voice Unban" : "Voice Ban");
            }
        }

        foreach (int id in new List<int>(_rows.Keys))
        {
            if (!seen.Contains(id))
            {
                _rows[id].Line.GetParent()?.RemoveChild(_rows[id].Line);
                _rows[id].Line.QueueFree();
                _rows.Remove(id);
            }
        }

        _spiesWaiting!.Visible = _spies.GetChildCount() <= 2;
        _mercsWaiting!.Visible = _mercs.GetChildCount() <= 2;

        // The spectators' frame only when someone watches (the original lobby has no such list).
        if (_spectators.GetParent() is Control spectatorFrame)
        {
            spectatorFrame.Visible = _spectators.GetChildCount() > 1;
        }

        // Session Parameters: the host's edits stay until applied; the others follow the server (changes blink).
        bool control = s.CanControl;
        if (_modelSetup is { } previous && previous != s.Setup)
        {
            string notice = LobbyPresentation.ChangeNotice(previous, s.Setup, MapTitle(s.Setup.Map));
            if (notice.Length > 0) _activity.Add(notice);
        }
        if (_modelSetup != s.Setup)
        {
            _draftConflict = control && Parameters!.Setup != _modelSetup && Parameters.Setup != s.Setup;
            // A client always shows the server's values; the host keeps his edits until he applies them.
            if (!control || Parameters!.Setup == _modelSetup)
            {
                Parameters!.Load(s.Setup);
            }

            _modelSetup = s.Setup;
        }

        ParameterRows!.Editable = control;
        ApplyButton!.Visible = control;
        MapsButton!.Visible = control && _mapCatalog is not null;
        if (!control && _confirm is { Visible: true }) ConfirmKick(false);
        if (!control && IsInstanceValid(_mapsPanel))
        {
            _mapsPanel!.QueueFree();
        }

        ShowAlternateButton();
        if (!control && IsInstanceValid(_alternate))
        {
            _alternate!.ShowRules(s.Setup.Alternate);
        }
        TeamVoiceOnlyBox!.SetPressedNoSignal(s.TeamVoiceOnly);
        OnlineLobbyPlayer? local = Local();
        SwitchTeamButton!.Disabled = local is null || (local.Ready && !control) || s.Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch;
        SpectateButton!.Disabled = local is null || local.Side is null || (local.Ready && !control);
        EquipmentButton!.Disabled = local?.Side is null || s.Launching;
        NoteJoins(s);
        MainButton!.Text = LobbyRoomModel.MainButton(control, local?.Ready ?? false, s.Launching);
        MainButton.Disabled = local is null || (!control && local.Side is null) || (control && !s.Launching && !s.CanStart);
        UpdateOverview();
        UpdateLoadout();
        UpdateTeamCards();
        if (JoinMercButton is not null)
        {
            bool locked = local is null || (local.Ready && !control) || s.Launching;
            SwitchTeamButton.Disabled = locked || s.Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch;
            _joinSpiesButton!.Disabled = locked || local?.Side == HudSide.Spy || s.Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch;
            JoinMercButton.Disabled = locked || local?.Side == HudSide.Merc;
            SpectateButton.Disabled = locked || local?.Side is null;
        }
        if (_rulesButton is not null) _rulesButton.Text = "Session Options";
        if (_draftStatus is not null)
        {
            _draftStatus.Visible = control && Parameters!.Setup != s.Setup;
            _draftStatus.Text = _draftConflict ? "The match changed while you were editing. Review your draft before applying." : "Unapplied changes — open match settings to review.";
            if (_settingsDraftNote is not null) _settingsDraftNote.Text = _draftStatus.Visible ? _draftStatus.Text : "The overview shows the live match. Apply changes sends your draft to the server.";
        }

        if (_messages is not null && (s.Messages.Count != _shownMessages || _activity.Count != _shownActivity))
        {
            _shownMessages = s.Messages.Count;
            _shownActivity = _activity.Count;
            var lines = new System.Text.StringBuilder();
            // Server and network notes dimmed so the players' lines stand out; a team line in the team's colour.
            foreach (LobbyMessage m in s.Messages)
            {
                if (m.Sender is null)
                {
                    lines.AppendLine(Dim(m.Text));
                    continue;
                }

                string said = Escape(m.Sender) + ": " + Escape(m.Text);
                lines.AppendLine(m.TeamOnly
                    ? "[color=#" + UiStyle.SideColour(m.Side ?? HudSide.Spy).ToHtml(false) + "](Team) " + said + "[/color]"
                    : said);
            }

            foreach (string notice in _activity) lines.AppendLine(Dim(notice));
            _messages.Text = lines.ToString();
        }
    }

    private PlayerRow MakeRow(OnlineLobbyPlayer p, IOnlineLobbySession s)
    {
        var line = new HBoxContainer { Name = "Player" + p.Id.ToString(CultureInfo.InvariantCulture) };
        line.AddThemeConstantOverride("separation", 6);
        var speaker = new TextureRect
        {
            Name = "Speaking", Texture = UiStyle.MenuTexture("Menu_PC", "CommunicatorON"), CustomMinimumSize = new Vector2(24f, 24f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Modulate = UiStyle.MenuFocus,
        };
        line.AddChild(speaker);
        var rank = new RankBadge(24, 14) { Name = "Rank", CustomMinimumSize = new Vector2(96f, 0f), Visible = false };
        line.AddChild(rank);
        // The name takes the row's spare width and the row grows for a long one ("Name#3d08 (host)"), nothing cut.
        var name = new Label { Name = "Name", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(80f, 0f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var ready = new Label { Name = "ReadyState", CustomMinimumSize = new Vector2(72f, 0f) };
        var ping = new Label { Name = "Ping", CustomMinimumSize = new Vector2(50f, 0f) };
        line.AddChild(name);
        line.AddChild(ready);
        line.AddChild(ping);
        HSlider? volume = null;
        Label? volumeText = null;
        CheckBox? mute = null;
        MenuButton? admin = null;
        // A bot never talks and follows the Bots setting: no volume, mute or kick on its row.
        if (!p.IsLocal && !p.IsBot)
        {
            // Reloaded's "scroll to adjust" and "click to mute", as a slider and a box per player.
            string player = p.Name;
            volume = new HSlider
            {
                Name = "Volume", MinValue = 0, MaxValue = 200, Step = 5, Value = s.VoiceVolume(player) * 100f,
                CustomMinimumSize = new Vector2(90f, 20f), TooltipText = "This player's voice volume",
            };
            volumeText = new Label { Name = "VolumeText", CustomMinimumSize = new Vector2(48f, 0f) };
            void Apply(double v)
            {
                s.SetVoiceVolume(player, (float)v / 100f);
                volumeText.Text = string.Create(CultureInfo.InvariantCulture, $"{v:0}%");
            }

            // A click on the track moves the grabber without value_changed (Godot's Slider blocks it on press): take the
            // value when the press ends too.
            volume.ValueChanged += Apply;
            volume.DragEnded += _ => Apply(volume.Value);
            volumeText.Text = string.Create(CultureInfo.InvariantCulture, $"{volume.Value:0}%");
            mute = new CheckBox { Name = "Mute", Text = "Mute", ButtonPressed = s.VoiceMuted(player) };
            mute.Toggled += on => s.SetVoiceMuted(player, on);
            var popup = new PopupPanel { Name = "PlayerMenu" };
            line.AddChild(popup);
            var menu = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
            popup.AddChild(menu);
            menu.AddChild(LobbyPanels.Text(p.Name));
            menu.AddChild(new Label { Text = "Voice volume" });
            menu.AddChild(volume);
            menu.AddChild(volumeText);
            menu.AddChild(mute);
            var playerMenu = LobbyPanels.Button("•••", () => popup.PopupCentered(new Vector2I(320, 220)), "PlayerActions");
            playerMenu.TooltipText = "Voice and player actions for " + p.Name;
            line.AddChild(playerMenu);
            admin = new MenuButton { Name = "Admin", Text = "Admin ▾", Flat = false, TooltipText = "Kick, ban or voice ban this player" };
            PopupMenu items = admin.GetPopup();
            items.AddItem("Kick", AdminKick);
            items.AddItem("Ban", AdminBan);
            items.AddItem("Voice Ban", AdminVoiceBan);
            menu.AddChild(admin);
        }

        var row = new PlayerRow
        {
            Line = line, Speaker = speaker, Name = name, Rank = rank, Ready = ready, Ping = ping, Volume = volume, VolumeText = volumeText,
            Mute = mute, Admin = admin,
        };
        if (admin is not null)
        {
            int id = p.Id;
            string player = p.Name;
            admin.GetPopup().IdPressed += item => AskKick(id, player, item switch
            {
                AdminBan => ConfirmAction.Ban,
                AdminVoiceBan => row.VoiceBanned ? ConfirmAction.VoiceUnban : ConfirmAction.VoiceBan,
                _ => ConfirmAction.Kick,
            });
        }

        return row;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Session is not { } s || _status is null)
        {
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        if (Math.Abs(s.Countdown - _lastCountdown) > 0.05f)
        {
            _lastCountdown = s.Countdown;
            _countdownEnd = now + s.Countdown;
        }

        double left = _countdownEnd - now;
        UpdatePreparation(s, (int)Math.Ceiling(Math.Max(0, left)));
        ShowMapFetch(s);
        foreach (PlayerRow row in _rows.Values)
        {
            row.Speaker.Visible = s.Speaking(row.PlayerName);
        }

        if (_microphone is not null)
        {
            _microphone.Text = s.MicrophoneLine;
        }
    }

    /// <summary>Chat text with its BBCode brackets made literal (a player can type "[b]").</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private static string Dim(string text) => "[color=#" + UiStyle.MenuDisabled.ToHtml(true) + "]" + Escape(text) + "[/color]";

    private static string HostName(IOnlineLobbySession s)
    {
        foreach (OnlineLobbyPlayer p in s.Members)
        {
            if (p.IsHost)
            {
                return p.Name;
            }
        }

        return "a dedicated server";
    }

    /// <summary>The row of a player by name (tests and the menu pilot).</summary>
    public Control? PlayerLine(string playerName)
    {
        foreach (PlayerRow row in _rows.Values)
        {
            if (row.PlayerName == playerName)
            {
                return row.Line;
            }
        }

        return null;
    }
}
