using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Online;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// Create Session (Enhanced 4.0's Create_Match LAN page, spec/game/lobby-and-match-creation.md; docs/MENUS.md): the
/// map browser beside tabbed game, rules and hosting settings. The model retains the original's session rules,
/// with direct choices and editable numbers. Hosting adds what the original took from Ubi.com (server name, UDP port, the
/// master to list on, listing and UPnP: rl:net.listing, rl:net.upnp). Create starts the listen server and opens the
/// Lobby Room. The values are kept for next time, as the original keeps them in the profile.
/// </summary>
public partial class HostScreen : MenuPage
{
    private LineEdit? _name;
    private SpinBox? _port;
    private CheckBox? _listed;
    private CheckBox? _upnp;
    private CheckBox? _requireAccounts;
    private CheckBox? _record;
    private Label? _requireNote;
    private LineEdit? _master;
    private Label? _status;
    private BoxContainer? _columns;
    private Control? _mapPanel;
    private Label? _selectedMap, _mapLegend;
    private MapCatalog? _maps;
    private Label? _voiceHint;
    private Label? _botHint;
    private Label? _listingHint;
    private ScrollContainer? _bodyScroll;

    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    public TabContainer? SettingsTabs { get; private set; }

    /// <summary>The page's rules (tests and the menu pilot read and drive it).</summary>
    public CreateSessionModel? Model { get; private set; }

    public SessionRowsView? Rows { get; private set; }

    /// <summary>The map list with pictures.</summary>
    public MapPickerView? Picker { get; private set; }

    /// <summary>Create (the page's A button).</summary>
    public Button? HostButton { get; private set; }

    /// <summary>"Alternate Mode": opens the Alternate Mode page (enabled while Alternate is checked).</summary>
    public Button? AlternateButton { get; private set; }

    public override void _ExitTree()
    {
        if (Model is not null)
        {
            Model.Changed -= RefreshSummary;
        }
    }

    private void RefreshSummary()
    {
        if (AlternateButton is not null && Model is not null)
        {
            AlternateButton.Disabled = !Model.Setup.Alternate.On;
        }
        if (Model is null) return;
        if (_selectedMap is not null) _selectedMap.Text = Model.Text(SessionRow.Map);
        if (_mapLegend is not null) MapLegend.Show(_mapLegend, _maps, Model.Setup.Map);
        if (_voiceHint is not null) _voiceHint.Text = Model.VoiceHint;
        if (_botHint is not null) _botHint.Text = Model.BotHint;
    }

    private void OpenAlternate()
    {
        if (Model is { } model)
        {
            AddChild(new AlternateModePanel(model.Setup.Alternate, editable: true, model.SetAlternate));
        }
    }

    public override void _Ready()
    {
        Name = "Host";
        BuildFrame("Create Session", 1500f, responsive: true);
        _bodyScroll = PageCanvas.GetNode<ScrollContainer>("BodyScroll");
        OnlineSettings s = OnlineSettings.Current;
        OnlineService service = OnlineService.Get(GetTree());
        MapCatalog maps = _maps = service.Catalog.Maps;
        var list = new List<(string Id, string Title)>();
        foreach (MapListEntry m in maps.ListMaps())
        {
            list.Add((m.Id, MapTitles.Of(maps, m.Id)));
        }

        LobbySetup start = StartSetup(s, maps, list);
        Model = new CreateSessionModel(list, maps.GetMapSettings, start);
        Model.SetPassword(s.HostPassword);
        Rows = new SessionRowsView(Model, CreateSessionModel.Rows, showPassword: true, form: true);
        Body.AddChild(Rows);
        _columns = new BoxContainer { Name = "HostColumns" };
        _columns.AddThemeConstantOverride("separation", 20);
        Body.AddChild(_columns);

        VBoxContainer mapList = Panel(_columns, "Maps", "Selected map");
        _mapPanel = (Control)mapList.GetParent();
        _mapPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _mapPanel.SizeFlagsStretchRatio = 1.35f;
        mapList.AddThemeConstantOverride("separation", 10);
        _selectedMap = new Label
        {
            Name = "SelectedMap", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiStyle.MenuFocus,
        };
        _selectedMap.AddThemeFontSizeOverride("font_size", 22);
        mapList.AddChild(_selectedMap);
        _mapLegend = MapLegend.Create();
        mapList.AddChild(_mapLegend);
        mapList.AddChild(Note("Select a map below. Its game mode supplies the starting rules."));
        Picker = new MapPickerView(Model, maps, list, height: 220f, playlist: SettingsStore.Current.ActiveMapPlaylist,
            playlistChosen: RememberPlaylist, compact: true) { SizeFlagsVertical = SizeFlags.ExpandFill };
        mapList.AddChild(Picker);

        SettingsTabs = new TabContainer { Name = "SettingsTabs", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _columns.AddChild(SettingsTabs);
        VBoxContainer game = SettingsTab("Game");
        _name = Field(game, "Session name", new LineEdit
        {
            Name = "ServerName", MaxLength = 32,
            Text = s.HostName.Length > 0 ? s.HostName : SettingsStore.Current.PlayerName + "'s game",
        });
        Rows.MoveRowsTo(game, SessionRow.Mode, SessionRow.TimeLimit, SessionRow.VoiceChat);
        _voiceHint = Note("");
        _voiceHint.Name = "VoiceHint";
        game.AddChild(_voiceHint);
        Heading(game, "Players & bots");
        Rows.MoveRowsTo(game, SessionRow.MaxPlayers, SessionRow.Bots, SessionRow.BotsPerSide,
            SessionRow.SpyBots, SessionRow.MercBots, SessionRow.BotSkill);
        _botHint = Note("");
        _botHint.Name = "BotHint";
        game.AddChild(_botHint);
        Heading(game, "Your recording");
        _record = Toggle(game, DemoRecordingText.Label, "RecordDemos", s.RecordDemos);
        _record.TooltipText = DemoRecordingText.Tooltip;
        game.AddChild(Note("Save full match replays to Demos on this computer. You can change this in the lobby; each player chooses for themselves."));

        VBoxContainer rules = SettingsTab("Rules");
        Heading(rules, "Round & objectives");
        Rows.MoveRowsTo(rules, SessionRow.SpyLives, SessionRow.MercLives, SessionRow.Respawn,
            SessionRow.ActiveDisks, SessionRow.DisksToTake);
        rules.AddChild(Note("Set lives to 0 for unlimited respawns."));
        Heading(rules, "Teams & rotation");
        Rows.MoveRowsTo(rules, SessionRow.Revenge, SessionRow.GamesInARow, SessionRow.RandomMap, SessionRow.AutoBalance,
            SessionRow.VoiceInLobby);
        Heading(rules, "Extra rules");
        Rows.MoveRowsTo(rules, SessionRow.Practice, SessionRow.Sidearms, SessionRow.Alternate);
        AlternateButton = UiStyle.MenuButton("Configure alternate rules", OpenAlternate, "AlternateMode");
        AlternateButton.CustomMinimumSize = new Vector2(0, 36);
        rules.AddChild(AlternateButton);

        VBoxContainer net = SettingsTab("Hosting");
        Heading(net, "Access & discovery");
        _listed = Toggle(net, "Show in the server browser", "Listed", s.HostListed);
        _listingHint = Note("");
        net.AddChild(_listingHint);
        Rows.MoveRowsTo(net, SessionRow.Password);
        net.AddChild(Note("Leave the password empty to let anyone join."));
        _requireAccounts = Toggle(net, "Signed-in players only", "RequireAccounts", s.HostRequireAccounts);
        _requireNote = new Label { Name = "RequireAccountsNote", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        net.AddChild(_requireNote);
        Heading(net, "Connection");
        _upnp = Toggle(net, "Open router port automatically (UPnP)", "Upnp", s.HostUpnp);
        _port = Field(net, "UDP port", new SpinBox { Name = "Port", MinValue = 1024, MaxValue = 65535, Value = s.HostPort, Rounded = true });
        _master = Field(net, "Master server", new LineEdit { Name = "Master", Text = s.MasterUrl, PlaceholderText = "Leave empty for LAN / direct connections" });
        net.AddChild(Note("Public sessions can use the master server's relay when a direct connection is unavailable."));
        _master.TextChanged += _ => { ShowRequireAccounts(); ShowListingHint(); };
        _listed.Toggled += _ => ShowListingHint();
        ShowRequireAccounts();
        ShowListingHint();
        Rows.Hide(); // The map is selected through the browser; every other row now lives in a settings tab.
        Model.Changed += RefreshSummary;
        RefreshSummary();

        _status = new Label
        {
            Name = "Status", Visible = false, Modulate = UiStyle.MenuFocus,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 1, AnchorBottom = 1,
            OffsetTop = -112, OffsetBottom = -80, HorizontalAlignment = HorizontalAlignment.Center,
        };
        PageCanvas.AddChild(_status);

        var originalSetup = Model.Setup;
        var originalPassword = Model.Password;
        (string, double, string, bool, bool, bool, bool) NetworkDraft() =>
            (_name.Text, _port.Value, _master.Text, _listed.ButtonPressed, _upnp.ButtonPressed,
                _requireAccounts.ButtonPressed, _record.ButtonPressed);
        var originalNetwork = NetworkDraft();
        void Back()
        {
            if (Model.Setup != originalSetup || Model.Password != originalPassword || NetworkDraft() != originalNetwork)
                ConfirmBox.Open(this, new ConfirmBox
                {
                    Question = "Discard changes to the session setup?", YesCaption = "Discard", NoCaption = "Keep editing",
                    Yes = () => FrontEnd.ShowMainMenu(GetTree()),
                });
            else FrontEnd.ShowMainMenu(GetTree());
        }
        PageButton("Back", Back, "Back");
        HostButton = PageButton("Create session", Host, "Host");
        HostButton.GrabFocus();
        FitHostLayout();
    }

    private VBoxContainer SettingsTab(string title)
    {
        var scroll = new ScrollContainer
        {
            Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true,
        };
        SettingsTabs!.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        scroll.AddChild(margin);
        var content = new VBoxContainer { Name = title + "Fields", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        return content;
    }

    private static Label Note(string text)
    {
        var note = new Label { Text = text, Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        note.AddThemeFontSizeOverride("font_size", 14);
        return note;
    }

    private static void Heading(Container parent, string text)
    {
        var heading = new Label { Text = text, Modulate = UiStyle.MenuFocus, CustomMinimumSize = new Vector2(0, 30) };
        heading.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(heading);
    }

    private static CheckBox Toggle(Container parent, string text, string name, bool value)
    {
        var toggle = new CheckBox { Name = name, Text = text, ButtonPressed = value, CustomMinimumSize = new Vector2(0, 36) };
        parent.AddChild(toggle);
        return toggle;
    }

    private void ShowListingHint()
    {
        if (_listingHint is null) return;
        _listingHint.Text = _listed!.ButtonPressed && _master!.Text.Trim().Length > 0
            ? "Players can find this session in the server browser."
            : "Players can join through LAN or a direct address.";
    }

    /// <summary>Keeps the map list's playlist for the next time (settings key <c>active_map_playlist</c>).</summary>
    internal static void RememberPlaylist(string name)
    {
        GameSettings settings = SettingsStore.Current;
        if (settings.ActiveMapPlaylist != name)
        {
            settings.ActiveMapPlaylist = name;
            SettingsStore.Save(settings);
        }
    }

    /// <summary>The last session's values when its map is still installed, else the first map's defaults.</summary>
    private static LobbySetup StartSetup(OnlineSettings s, MapCatalog maps, List<(string Id, string Title)> list)
    {
        if (s.HostSetup.Length > 0)
        {
            LobbySetup last = LobbySetup.FromOptions(s.HostSetup);
            if (maps.FindMap(last.Map) is not null)
            {
                return last with { MaxPlayers = s.HostMaxPlayers };
            }
        }

        string map = maps.FindMap("SteeE") is not null ? "SteeE" : list.Count > 0 ? list[0].Id : MapCatalog.TestBox;
        return LobbySetup.ForMap(map, EGameMode.GM_Multi, maps.GetMapSettings(map), s.HostMaxPlayers);
    }

    /// <summary>Signed-in players only needs a master (the tickets come from it).</summary>
    private void ShowRequireAccounts()
    {
        if (_requireAccounts is null || _requireNote is null)
        {
            return;
        }

        bool master = (_master?.Text.Trim().Length ?? 0) > 0;
        _requireAccounts.Disabled = !master;
        _requireNote.Visible = !master;
        _requireNote.Text = "Signed-in players only needs a master server: players get their tickets from it.";
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        FitHostLayout();
    }

    private void FitHostLayout()
    {
        if (_columns is null || _mapPanel is null || SettingsTabs is null) return;
        bool stacked = AvailableBodyWidth < 1100;
        _columns.Vertical = stacked;
        float statusHeight = _status?.Visible == true ? Math.Max(40, _status.GetCombinedMinimumSize().Y + 8) : 0;
        if (_bodyScroll is not null) _bodyScroll.OffsetBottom = -78 - statusHeight;
        if (_status is not null) _status.OffsetTop = -80 - statusHeight;
        float height = Math.Max(440, LayoutSize.Y - 168 - statusHeight);
        _mapPanel.CustomMinimumSize = new Vector2(0, stacked ? 480 : height);
        SettingsTabs.CustomMinimumSize = new Vector2(stacked ? 0 : 430, stacked ? 540 : height);
    }

    private static T Field<T>(Container parent, string label, T control)
        where T : Control
    {
        var field = new VBoxContainer();
        field.AddThemeConstantOverride("separation", 6);
        parent.AddChild(field);
        field.AddChild(new Label { Text = label });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        control.CustomMinimumSize = new Vector2(0, 38);
        field.AddChild(control);
        return control;
    }

    private void Host()
    {
        OnlineSettings s = OnlineSettings.Current;
        LobbySetup setup = Model!.Setup;
        s.HostName = _name!.Text.Trim();
        s.HostPort = (int)_port!.Value;
        s.HostMaxPlayers = setup.MaxPlayers;
        s.HostPassword = Model.Password;
        s.MasterUrl = _master!.Text.Trim();
        s.HostListed = _listed!.ButtonPressed;
        s.HostUpnp = _upnp!.ButtonPressed;
        s.HostRequireAccounts = _requireAccounts!.ButtonPressed;
        s.RecordDemos = _record!.ButtonPressed;
        s.HostSetup = setup.ToOptions();
        s.Save();

        OnlineService service = OnlineService.Get(GetTree());
        var request = new HostRequest
        {
            Name = s.HostName.Length > 0 ? s.HostName : "OpenSCCT game",
            Setup = setup,
            Port = s.HostPort,
            MaxPlayers = setup.MaxPlayers,
            Password = Model.Password.Length > 0 ? Model.Password : null,
            Listed = s.HostListed,
            Upnp = s.HostUpnp,
            MasterUrl = s.MasterUrl.Length > 0 ? s.MasterUrl : null,
            InviteKey = s.InviteKey.Length > 0 ? s.InviteKey : null,
            LanPort = s.LanPort,
            RequireAccounts = s.HostRequireAccounts && s.MasterUrl.Length > 0,
        };
        AccountService.Get(GetTree()).PrepareAppearance(() =>
        {
            if (!service.Host(request) && _status is not null)
            {
                _status.Text = service.LastError ?? "Could not host.";
                _status.Show();
            }
        });
    }

    /// <summary>The page's port as text (tests).</summary>
    public string PortText => ((int)(_port?.Value ?? 0)).ToString(CultureInfo.InvariantCulture);
}
