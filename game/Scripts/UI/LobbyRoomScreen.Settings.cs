using System;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

public partial class LobbyRoomScreen
{
    private Control? _settingsCanvas;
    private ScrollContainer? _settingsScroll;
    private BoxContainer? _settingsColumns;
    private Control? _settingsMapPanel;
    private Label? _settingsMapTitle, _settingsMapLegend, _settingsMapHint, _settingsVoiceHint;
    private Label? _settingsBotHint, _settingsRoster;
    private CheckBox? _settingsRecordDemos;
    private Label? _settingsRecordingNote;
    private MapThumb? _settingsMapPreview;
    private string? _settingsPreviewMap;

    public TabContainer? SettingsTabs { get; private set; }
    public MapPickerView? SettingsPicker { get; private set; }

    private void BuildSettingsContent()
    {
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _settingsScroll!.AddChild(content);
        // Keep the shared bindings alive after moving the controls into their respective tabs.
        content.AddChild(ParameterRows!);
        _settingsColumns = new BoxContainer { Name = "SettingsColumns", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _settingsColumns.AddThemeConstantOverride("separation", 20);
        content.AddChild(_settingsColumns);
        VBoxContainer map = LobbyPanels.Card(_settingsColumns, "MapSelection", "Selected map");
        _settingsMapPanel = (Control)map.GetParent();
        _settingsMapPanel.SizeFlagsStretchRatio = 1.35f;
        map.AddThemeConstantOverride("separation", 10);
        _settingsMapTitle = LobbyPanels.Text();
        _settingsMapTitle.Name = "SelectedMap";
        _settingsMapTitle.Modulate = UiStyle.MenuFocus;
        _settingsMapTitle.AddThemeFontSizeOverride("font_size", 22);
        map.AddChild(_settingsMapTitle);
        _settingsMapLegend = MapLegend.Create();
        map.AddChild(_settingsMapLegend);
        _settingsMapHint = SettingsNote("");
        map.AddChild(_settingsMapHint);
        if (_mapCatalog is not null)
        {
            SettingsPicker = new MapPickerView(Parameters!, _mapCatalog, Session!.Maps, height: 0,
                playlist: SettingsStore.Current.ActiveMapPlaylist, playlistChosen: HostScreen.RememberPlaylist, compact: true)
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            map.AddChild(SettingsPicker);
        }
        _settingsMapPreview = new MapThumb(new Vector2(0, 180)) { Name = "SettingsMapPreview", SizeFlagsVertical = SizeFlags.ExpandFill };
        map.AddChild(_settingsMapPreview);

        SettingsTabs = new TabContainer { Name = "SettingsTabs", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _settingsColumns.AddChild(SettingsTabs);
        VBoxContainer game = SettingsTab("Game");
        SettingsHeading(game, "Match");
        ParameterRows!.MoveRowsTo(game, SessionRow.Mode, SessionRow.TimeLimit, SessionRow.VoiceChat);
        _settingsVoiceHint = SettingsNote("");
        _settingsVoiceHint.Name = "VoiceHint";
        game.AddChild(_settingsVoiceHint);
        SettingsHeading(game, "Players & bots");
        ParameterRows.MoveRowsTo(game, SessionRow.MaxPlayers, SessionRow.Bots, SessionRow.BotsPerSide,
            SessionRow.SpyBots, SessionRow.MercBots, SessionRow.BotSkill);
        _settingsBotHint = SettingsNote("");
        _settingsBotHint.Name = "BotHint";
        game.AddChild(_settingsBotHint);
        _settingsRoster = SettingsNote("");
        _settingsRoster.Name = "RosterPreview";
        _settingsRoster.Modulate = UiStyle.MenuFocus;
        game.AddChild(_settingsRoster);
        SettingsHeading(game, "Your recording");
        _settingsRecordDemos = new CheckBox { Name = "SettingsRecordDemos", Text = DemoRecordingText.Label,
            CustomMinimumSize = new Vector2(0, 36), TooltipText = DemoRecordingText.Tooltip };
        _settingsRecordDemos.Toggled += on => { Session?.SetRecordDemos(on); RefreshRecordingControls(); };
        game.AddChild(_settingsRecordDemos);
        game.AddChild(SettingsNote("Your choice takes effect immediately for upcoming rounds, without Apply. Full match replays are saved under Demos on this computer."));
        _settingsRecordingNote = SettingsNote("");
        game.AddChild(_settingsRecordingNote);

        VBoxContainer rules = SettingsTab("Rules");
        SettingsHeading(rules, "Round & objectives");
        ParameterRows.MoveRowsTo(rules, SessionRow.SpyLives, SessionRow.MercLives, SessionRow.Respawn,
            SessionRow.ActiveDisks, SessionRow.DisksToTake);
        rules.AddChild(SettingsNote("Set lives to 0 for unlimited respawns."));
        SettingsHeading(rules, "Teams & rotation");
        ParameterRows.MoveRowsTo(rules, SessionRow.Revenge, SessionRow.GamesInARow, SessionRow.RandomMap,
            SessionRow.AutoBalance, SessionRow.VoiceInLobby);
        SettingsHeading(rules, "Extra rules");
        ParameterRows.MoveRowsTo(rules, SessionRow.Practice, SessionRow.Sidearms, SessionRow.Alternate);
        AlternateButton = LobbyPanels.Button("Configure alternate rules", OpenAlternate, "Alternate");
        rules.AddChild(AlternateButton);
        ParameterRows.Hide();
        RefreshSettingsSummary();
    }

    private VBoxContainer SettingsTab(string title)
    {
        var scroll = new ScrollContainer { Name = title, FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        SettingsTabs!.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 14);
        scroll.AddChild(margin);
        var fields = new VBoxContainer { Name = title + "Fields", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        fields.AddThemeConstantOverride("separation", 10);
        margin.AddChild(fields);
        return fields;
    }

    private static Label SettingsNote(string text)
    {
        var note = LobbyPanels.Text(text);
        note.Modulate = UiStyle.Dim;
        note.AddThemeFontSizeOverride("font_size", 14);
        return note;
    }

    private static void SettingsHeading(Container parent, string text)
    {
        var title = new Label { Text = text, Modulate = UiStyle.MenuFocus, CustomMinimumSize = new Vector2(0, 30) };
        title.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(title);
    }

    private void RefreshSettingsSummary()
    {
        if (Parameters is null || Session is null || _settingsMapTitle is null) return;
        _settingsMapTitle.Text = Parameters.Text(SessionRow.Map);
        MapLegend.Show(_settingsMapLegend!, _mapCatalog, Parameters.Setup.Map);
        _settingsVoiceHint!.Text = Parameters.VoiceHint;
        _settingsBotHint!.Text = Parameters.BotHint;
        _settingsRoster!.Text = LobbyPresentation.PlayerSlots(Parameters.Setup,
            Session.Members.Count(player => !player.IsBot && player.Side == HudSide.Spy),
            Session.Members.Count(player => !player.IsBot && player.Side == HudSide.Merc));
        RefreshRecordingControls();
        bool canBrowse = Session.CanControl && SettingsPicker is not null;
        _settingsMapHint!.Text = canBrowse ? "Select a map below. Changing map or mode loads its starting rules."
            : "The current map and rules for this match.";
        if (SettingsPicker is not null)
        {
            SettingsPicker.Editable = Session.CanControl;
            SettingsPicker.Visible = canBrowse;
        }
        _settingsMapPreview!.Visible = !canBrowse;
        if (!canBrowse && _settingsPreviewMap != Parameters.Setup.Map)
        {
            _settingsPreviewMap = Parameters.Setup.Map;
            _settingsMapPreview.SetMap(MapImages.Preview(_mapCatalog, _settingsPreviewMap), MapTitle(_settingsPreviewMap));
        }
    }

    private void RefreshRecordingControls()
    {
        if (Session is not { } session) return;
        _recordDemos?.SetPressedNoSignal(session.RecordDemos);
        _settingsRecordDemos?.SetPressedNoSignal(session.RecordDemos);
        string note = session.DemoRecordingStatus;
        foreach (Label? label in new[] { _recordingNote, _settingsRecordingNote })
            if (label is not null)
            {
                if (label.Text != note) label.Text = note;
                label.Visible = note.Length > 0;
            }
    }

    private void FitSettingsPanel()
    {
        if (_settingsCanvas is null || _settingsPanel is null || _settingsColumns is null) return;
        float scale = Math.Max(1, Math.Min(Size.X / 1600f, Size.Y / 900f));
        if (_settingsCanvas.Scale.X != scale)
        {
            _settingsCanvas.Scale = Vector2.One * scale;
            _settingsCanvas.OversamplingWithScale = OversamplingWithScaleEnum.Disabled;
            _settingsCanvas.OversamplingWithScale = OversamplingWithScaleEnum.Enabled;
        }
        if (_settingsCanvas.Size != Size / scale) _settingsCanvas.Size = Size / scale;
        LobbyPanels.FitDialog(_settingsPanel, _settingsCanvas.Size, new Vector2(1500, 820));
        // Use the available width, rather than a child's previous minimum, to recover after a live resize.
        bool stacked = _settingsPanel.Size.X - 70 < 1000;
        _settingsColumns.Vertical = stacked;
        _settingsMapPanel!.CustomMinimumSize = new Vector2(0, stacked ? 440 : 0);
        SettingsTabs!.CustomMinimumSize = new Vector2(stacked ? 0 : 430, stacked ? 500 : 0);
    }

    private void FocusSettingsMap()
    {
        SettingsTabs!.CurrentTab = 0;
        if (SettingsPicker is { Visible: true } picker)
        {
            _settingsScroll!.EnsureControlVisible(_settingsMapPanel!);
            picker.SearchBox?.GrabFocus();
        }
    }
}
