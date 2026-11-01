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
/// map and mode, the session rows (Password, Voice Chat with its hint, Max Players) and the rule boxes (Revenge,
/// Random Map, Voice in Lobby, Games in a Row, Game Length, both sides' lives or the kill limit, Respawn Length, the
/// disks), a list of the mode's maps with their pictures to click (ours, <see cref="MapPickerView"/>), plus what hosting from the game needs that the original took from Ubi.com (server name, UDP port, the
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
            Model.Changed -= ShowAlternateButton;
        }
    }

    private void ShowAlternateButton()
    {
        if (AlternateButton is not null && Model is not null)
        {
            AlternateButton.Disabled = !Model.Setup.Alternate.On;
        }
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
        BuildFrame("Create Session", 1060f);
        OnlineSettings s = OnlineSettings.Current;
        OnlineService service = OnlineService.Get(GetTree());
        MapCatalog maps = service.Catalog.Maps;
        var list = new List<(string Id, string Title)>();
        foreach (MapListEntry m in maps.ListMaps())
        {
            list.Add((m.Id, MapTitles.Of(maps, m.Id)));
        }

        LobbySetup start = StartSetup(s, maps, list);
        Model = new CreateSessionModel(list, maps.GetMapSettings, start);
        Model.SetPassword(s.HostPassword);
        Rows = new SessionRowsView(Model, CreateSessionModel.Rows, showPassword: true);
        if (Rows.Find("PasswordEdit") is LineEdit password)
        {
            password.Text = Model.Password;
        }

        Body.AddChild(Rows);

        // Create_Match's "Alternate Mode" button: the ability and gadget switches, once Alternate is checked.
        AlternateButton = UiStyle.MenuButton("Alternate Mode", OpenAlternate, "AlternateMode");
        AlternateButton.CustomMinimumSize = new Vector2(220f, 36f);
        AlternateButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        Body.AddChild(AlternateButton);
        Model.Changed += ShowAlternateButton;
        ShowAlternateButton();

        // Every hostable map of the mode as a picture to click (the Choose Map arrows still step through them).
        VBoxContainer mapList = Panel(Body, "Maps", "Maps");
        Picker = new MapPickerView(Model, maps, list, playlist: SettingsStore.Current.ActiveMapPlaylist, playlistChosen: RememberPlaylist);
        mapList.AddChild(Picker);
        VBoxContainer net = Panel(Body, "Network", "Hosting");
        var grid = new GridContainer { Name = "NetGrid", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        net.AddChild(grid);
        _name = Field(grid, "Server Name", new LineEdit
        {
            Name = "ServerName", MaxLength = 32,
            Text = s.HostName.Length > 0 ? s.HostName : SettingsStore.Current.PlayerName + "'s game",
        });
        _port = Field(grid, "UDP port", new SpinBox { Name = "Port", MinValue = 1024, MaxValue = 65535, Value = s.HostPort, Rounded = true });
        _master = Field(grid, "Master server", new LineEdit { Name = "Master", Text = s.MasterUrl, PlaceholderText = "none: LAN and direct address only" });
        _listed = Field(grid, "List on the master", new CheckBox { Name = "Listed", ButtonPressed = s.HostListed });
        _upnp = Field(grid, "Map the port (UPnP)", new CheckBox { Name = "Upnp", ButtonPressed = s.HostUpnp });
        _requireAccounts = Field(grid, "Signed-in players only", new CheckBox { Name = "RequireAccounts", ButtonPressed = s.HostRequireAccounts });
        _record = Field(grid, "Record demos", new CheckBox { Name = "RecordDemos", ButtonPressed = s.HostRecordDemos });
        _requireNote = new Label { Name = "RequireAccountsNote", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        net.AddChild(_requireNote);
        _master.TextChanged += _ => ShowRequireAccounts();
        ShowRequireAccounts();
        net.AddChild(new Label
        {
            Text = "Behind carrier-grade NAT no port can be opened: list the game on a master and players join through its relay.",
            Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);

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
        HostButton = PageButton("Create", Host, "Host");
        HostButton.GrabFocus();
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

    private static T Field<T>(GridContainer grid, string label, T control)
        where T : Control
    {
        grid.AddChild(new Label { Text = label });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        control.CustomMinimumSize = new Vector2(control is CheckBox ? 0f : 220f, 0f);
        grid.AddChild(control);
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
        s.HostRecordDemos = _record!.ButtonPressed;
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
            DemoFolder = s.HostRecordDemos ? OpenScctGame.Play.Demo.DemoLibrary.SavedFolder : null,
        };
        if (!service.Host(request) && _status is not null)
        {
            _status.Text = service.LastError ?? "Could not host.";
        }
    }

    /// <summary>The page's port as text (tests).</summary>
    public string PortText => ((int)(_port?.Value ?? 0)).ToString(CultureInfo.InvariantCulture);
}
