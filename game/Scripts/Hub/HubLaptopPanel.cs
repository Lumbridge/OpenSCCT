using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Maps;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;
using OpenScctGame.Online;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// A match pad's laptop (an OpenSCCT addition, docs/HUB.md "Pad laptops"): the pad's match as a lobby's Session
/// Parameters (<see cref="SessionRowsView"/> over a <see cref="CreateSessionModel"/>), opened when the player presses U
/// at the laptop. Anyone at the laptop may change it; Apply sends the setup to the hub (<c>hub padsetup</c>), which
/// checks the player still stands there and tells the pad's queue. Max Players sets the pad's places (half a side, or
/// everyone in Merc Deathmatch). The rows a hub match cannot use are left out: the password (the hub gives each match
/// its own), games in a row, revenge and random map (a hub match is one game), voice in the lobby (the match's lobby
/// launches at once). If the pad changes while the laptop is open, the rows follow unless the player has edited them.
/// Choose Map steps through the player's maps and every map of OpenSCCT-Content (docs/CONTENT-PACKS.md); for one the
/// player lacks, a Download button fetches it (the hub fetches its own copy when the setup is applied).
/// </summary>
public partial class HubLaptopPanel : Control
{
    /// <summary>The lobby's rows the laptop shows.</summary>
    public static IReadOnlyList<SessionRow> Rows { get; } =
    [
        SessionRow.Map, SessionRow.Mode, SessionRow.VoiceChat, SessionRow.MaxPlayers, SessionRow.AutoBalance, SessionRow.Alternate,
        SessionRow.TimeLimit, SessionRow.SpyLives, SessionRow.MercLives, SessionRow.Respawn, SessionRow.ActiveDisks,
        SessionRow.DisksToTake, SessionRow.Bots, SessionRow.BotsPerSide, SessionRow.BotSkill, SessionRow.Practice,
    ];

    private readonly HubClient _client;
    private readonly string _padId;
    private readonly MapCatalog? _maps;
    private CreateSessionModel? _model;
    private Label? _status;
    private Label? _format;
    private Button? _alternate;
    private bool _edited;
    private bool _loading;
    private Label? _mapNote;
    private Button? _download;
    private readonly List<(string Id, string Title)> _list = new();
    private bool _indexListed;
    private int _generation = ContentPacks.Generation;

    public HubLaptopPanel(HubClient client, string padId, MapCatalog? maps)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _padId = padId ?? throw new ArgumentNullException(nameof(padId));
        _maps = maps ?? client.Settings.Maps;
        Name = "HubLaptop";
    }

    /// <summary>Close or Apply was pressed (the HUD frees the panel and gives the game its input back).</summary>
    public Action? Closed { get; set; }

    public override void _Ready()
    {
        Theme = UiStyle.MenuTheme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1f, AnchorBottom = 1f });

        var panel = new PanelContainer { Name = "Panel", AnchorLeft = 0.12f, AnchorRight = 0.88f, AnchorTop = 0.08f, AnchorBottom = 0.92f };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(panel);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(new Label
        {
            Name = "Title", Text = $"Pad {PadNumber} - next match", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle,
        });
        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus };
        column.AddChild(_status);

        if (_maps is not null)
        {
            foreach (MapListEntry m in _maps.ListMaps())
            {
                _list.Add((m.Id, MapTitles.Of(_maps, m.Id)));
            }
        }

        ContentPacks.RefreshIndex();
        ListIndexMaps();

        // The model keeps this list: maps of the index join it when the index arrives.
        _model = new CreateSessionModel(_list, map => _maps?.GetMapSettings(map), Current());
        _model.Changed += OnModelChanged;
        var scroll = new ScrollContainer
        {
            Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var rows = new SessionRowsView(_model, [.. Rows], showPassword: false) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(rows);

        var mapLine = new HBoxContainer { Name = "MapLine" };
        mapLine.AddThemeConstantOverride("separation", 12);
        column.AddChild(mapLine);
        _mapNote = new Label { Name = "MapNote", SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        mapLine.AddChild(_mapNote);
        _download = UiStyle.MenuButton("Download", DownloadMap, "Download");
        mapLine.AddChild(_download);

        _format = new Label { Name = "Format", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_format);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        column.AddChild(buttons);
        _alternate = UiStyle.MenuButton("Alternate Mode", OpenAlternate, "AlternateMode");
        buttons.AddChild(_alternate);
        buttons.AddChild(UiStyle.MenuButton("Map Defaults", Defaults, "Defaults"));
        buttons.AddChild(UiStyle.MenuButton("Apply", Apply, "Apply"));
        buttons.AddChild(UiStyle.MenuButton("Close (Esc)", () => Closed?.Invoke(), "Close"));

        _client.PadSetupChanged += OnPadSetupChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        _client.PadSetupChanged -= OnPadSetupChanged;
        if (_model is not null)
        {
            _model.Changed -= OnModelChanged;
        }
    }

    public override void _Process(double delta)
    {
        if (!_indexListed && ListIndexMaps())
        {
            Refresh();
        }

        if (ContentPacks.Generation != _generation && _model is { } m)
        {
            // A map came in (the hub service refreshed the catalogs): its title, modes and rules are known now.
            _generation = ContentPacks.Generation;
            _list.Clear();
            _indexListed = false;
            if (_maps is not null)
            {
                foreach (MapListEntry e in _maps.ListMaps())
                {
                    _list.Add((e.Id, MapTitles.Of(_maps, e.Id)));
                }
            }

            ListIndexMaps();
            bool edited = _edited;
            _loading = true;
            m.SelectMap(m.Setup.Map);
            _loading = false;
            _edited = edited;
        }

        RefreshMapLine();
    }

    /// <summary>Adds the index's maps the player lacks to Choose Map (once the index is in). True when it did.</summary>
    private bool ListIndexMaps()
    {
        if (_indexListed || ContentPacks.Index is not { } index)
        {
            return false;
        }

        _indexListed = true;
        foreach (ContentMap map in index.PlayableMaps())
        {
            if (_maps?.FindMap(map.Code) is null && !_list.Exists(e => string.Equals(e.Id, map.Code, StringComparison.OrdinalIgnoreCase)))
            {
                _list.Add((map.Code, $"{map.DisplayTitle} - download {Megabytes(map.Full.Size)}"));
            }
        }

        return true;
    }

    private static string Megabytes(long bytes) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0} MB");

    /// <summary>Whether the player has the chosen map, and its download.</summary>
    private void RefreshMapLine()
    {
        if (_model is not { } m || _mapNote is null || _download is null)
        {
            return;
        }

        string code = m.Setup.Map;
        if (_maps?.FindMap(code) is not null)
        {
            _mapNote.Text = string.Empty;
            _download.Visible = false;
            return;
        }

        if (ContentPacks.Job(code) is { } job && job.Error is null)
        {
            _mapNote.Text = job.Describe();
            _download.Visible = false;
            return;
        }

        if (ContentPacks.Find(code) is { } map)
        {
            string modes = map.Modes.Count > 0 ? " Modes: " + string.Join(", ", map.Modes) + "." : string.Empty;
            string failed = ContentPacks.Job(code)?.Error is { } error ? $" Last try failed: {error}." : string.Empty;
            _mapNote.Text = $"You do not have {map.DisplayTitle} ({Megabytes(map.Full.Size)}). Download it to play it; the hub gets its own copy.{modes}{failed}";
            _download.Visible = true;
            return;
        }

        _mapNote.Text = ContentPacks.Index is null && ContentPacks.IndexError is null
            ? "Reading the map list..."
            : $"You do not have {code}{(ContentPacks.IndexError is { } e ? " (the map list could not be read: " + e + ")" : string.Empty)}.";
        _download.Visible = false;
    }

    private void DownloadMap()
    {
        if (_model is { } m && ContentPacks.Find(m.Setup.Map) is { } map)
        {
            ContentPacks.Download(map);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Closed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>"1" of "Pad1".</summary>
    private string PadNumber => _padId.Length > 0 && char.IsDigit(_padId[^1]) ? _padId[^1].ToString() : _padId;

    /// <summary>The pad's settings as the hub last said, else its match with the map's defaults.</summary>
    private LobbySetup Current()
    {
        if (_client.PadSetups.TryGetValue(_padId, out var known))
        {
            return known.Setup;
        }

        if (_client.Pads.TryGetValue(_padId, out HubPadInfo? pad))
        {
            int slots = pad.SpySlots + pad.MercSlots;
            return LobbySetup.ForMap(pad.Map, pad.Mode, _maps?.GetMapSettings(pad.Map), Math.Max(LobbySetup.MinMaxPlayers, slots));
        }

        return new LobbySetup();
    }

    private void OnModelChanged()
    {
        if (!_loading)
        {
            _edited = true;
        }

        Refresh();
    }

    /// <summary>Someone set the pad (here or at the laptop): the rows follow unless this player is editing them.</summary>
    private void OnPadSetupChanged(string padId)
    {
        if (!string.Equals(padId, _padId, StringComparison.OrdinalIgnoreCase) || _model is null)
        {
            return;
        }

        if (!_edited)
        {
            Load(Current());
        }

        Refresh();
    }

    private void Load(LobbySetup setup)
    {
        if (_model is null)
        {
            return;
        }

        _loading = true;
        _model.Load(setup);
        _loading = false;
    }

    private void Defaults()
    {
        if (_model is { } m)
        {
            // The map's own rules for the mode, keeping the map, mode, players and bots.
            m.SelectMode(m.Setup.Mode);
        }
    }

    private void OpenAlternate()
    {
        if (_model is { } m)
        {
            AddChild(new AlternateModePanel(m.Setup.Alternate, editable: true, m.SetAlternate));
        }
    }

    private void Apply()
    {
        if (_model is { } m)
        {
            _client.SetPadSetup(_padId, m.Setup);
        }

        Closed?.Invoke();
    }

    private void Refresh()
    {
        if (_model is not { } m || _status is null || _format is null)
        {
            return;
        }

        bool changed = _client.PadSetups.TryGetValue(_padId, out var known) && known.Changed;
        string state = _client.Pads.TryGetValue(_padId, out HubPadInfo? pad) && pad.State == HubPadState.Starting
            ? "  Its match is starting: change it once the players have gone."
            : string.Empty;
        _status.Text = (_edited ? "Not applied yet." : changed ? "Set at this laptop." : "The hub's own match for this pad.") + state;

        LobbySetup s = m.Setup;
        bool mercsOnly = s.Mode == EGameMode.GM_MercDeathMatch;
        int side = Math.Clamp((s.MaxPlayers + 1) / 2, 1, 4);
        string format = mercsOnly ? $"{s.MaxPlayers} players, all against all" : $"{side}v{side}";
        string title = _maps is null ? s.Map : MapTitles.Of(_maps, s.Map);
        _format.Text = $"This pad will play {title}{CreateSessionModel.ModeName(s.Mode)} {format}. " +
            "Max Players sets the places: half each side (8 is 4v4), everyone in Merc Deathmatch. Applying un-readies the pad's queue.";
        if (_alternate is not null)
        {
            _alternate.Visible = s.Alternate.On;
        }
    }
}
