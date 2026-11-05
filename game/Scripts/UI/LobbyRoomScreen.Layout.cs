using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

public partial class LobbyRoomScreen
{
    private PanelContainer? _settingsPanel;
    private Label? _overviewTitle, _overviewRules, _overviewObjective, _loadout, _draftStatus;
    private Button? _rulesButton, _joinSpiesButton, _mapButton;
    private Label? _settingsDraftNote;
    private CheckBox? _recordDemos;
    private Label? _recordingNote;
    private bool _draftConflict;
    private readonly Dictionary<Control, FocusModeEnum> _backgroundFocus = new();
    private OptionButton? _chatChannel;
    private readonly List<string> _activity = new();
    private int _shownActivity = -1;
    public Button? JoinMercButton { get; private set; }
    public Control? MatchSettingsPanel => _settingsPanel;

    private string MapTitle(string map) => _mapCatalog is { } maps ? MapTitles.Of(maps, map) : map;

    private void BuildLobby()
    {
        var session = Session ?? throw new InvalidOperationException("A lobby session is required.");
        _mapCatalog = GetTree().Root.GetNodeOrNull<OpenScctGame.Online.OnlineService>(OpenScctGame.Online.OnlineService.NodeName)?.Catalog.Maps;
        BuildMapFetchBar();
        // GUI.Lobby_Create: teams flank the central actions; map below left, chat below right.
        // Keep these landmarks at 1024px; stack them when a smaller screen cannot fit readable player rows.
        var teams = new LobbyColumns { Name = "LobbyTeams" };
        teams.AddThemeConstantOverride("separation", 12);
        Body.AddChild(teams);
        var spies = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var actions = new VBoxContainer { Name = "LobbyActions", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.65f };
        var mercs = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        teams.AddChild(spies); teams.AddChild(actions); teams.AddChild(mercs);
        _spies = LobbyPanels.Card(spies, "Spies", "Spies");
        _spiesWaiting = LobbyPanels.Text("Open slots — join Spies"); _spies.AddChild(_spiesWaiting);
        var sideActions = new HBoxContainer(); spies.AddChild(sideActions);
        _joinSpiesButton = LobbyPanels.Button("Join Spies", () => session.SetSide(HudSide.Spy), "JoinSpies"); _joinSpiesButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; sideActions.AddChild(_joinSpiesButton);
        _mercs = LobbyPanels.Card(mercs, "Mercs", "Mercenaries");
        _mercsWaiting = LobbyPanels.Text("Open slots — join Mercenaries"); _mercs.AddChild(_mercsWaiting);
        JoinMercButton = LobbyPanels.Button("Join Mercenaries", () => session.SetSide(HudSide.Merc), "JoinMercs"); mercs.AddChild(JoinMercButton);
        _spectators = LobbyPanels.Card(spies, "Spectators", "Spectators");

        SwitchTeamButton = LobbyPanels.Button("Switch Team", SwitchTeam, "SwitchTeam"); actions.AddChild(SwitchTeamButton);
        EquipmentButton = LobbyPanels.Button("Equipment", OpenEquipment, "Equipment"); actions.AddChild(EquipmentButton);
        ArmouryButton = LobbyPanels.Button("Cosmetics", OpenArmoury, "Armoury"); actions.AddChild(ArmouryButton);
        _rulesButton = LobbyPanels.Button("Session Options", OpenMatchSettings, "MatchSettings"); actions.AddChild(_rulesButton);
        BriefingButton = LobbyPanels.Button("Briefing", OpenBriefing, "Briefing"); actions.AddChild(BriefingButton);
        actions.AddChild(LobbyPanels.Button("Strategic map", OpenStrategicMap, "StrategicMap"));
        SpectateButton = LobbyPanels.Button("Spectate", session.Spectate, "Spectate"); SpectateButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; sideActions.AddChild(SpectateButton);

        foreach (Control action in actions.GetChildren()) action.CustomMinimumSize = new Vector2(0, 32);
        var lower = new LobbyColumns { Name = "LobbyDetails", SizeFlagsVertical = SizeFlags.ExpandFill };
        lower.AddThemeConstantOverride("separation", 12);
        Body.AddChild(lower);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.8f };
        lower.AddChild(left); lower.AddChild(right);
        var overview = LobbyPanels.Card(left, "MatchOverview", session.ServerName);
        ((Control)overview.GetParent()).SizeFlagsVertical = SizeFlags.ExpandFill;
        _overviewTitle = LobbyPanels.Text(); overview.AddChild(_overviewTitle);
        _mapButton = LobbyPanels.Button("", () => { OpenMatchSettings(); FocusSettingsMap(); }, "SelectMap");
        _mapButton.CustomMinimumSize = new Vector2(100, 80);
        _mapButton.SizeFlagsVertical = SizeFlags.ExpandFill;
        _mapButton.ThemeTypeVariation = UiStyle.TileVariation;
        _mapButton.MouseDefaultCursorShape = CursorShape.PointingHand;
        overview.AddChild(_mapButton);
        MapPicture = new MapThumb(Vector2.Zero);
        _mapButton.AddChild(MapPicture);
        MapPicture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
        _overviewRules = LobbyPanels.Text(); overview.AddChild(_overviewRules);
        _overviewObjective = LobbyPanels.Text(); _overviewObjective.Visible = false; overview.AddChild(_overviewObjective);
        _draftStatus = LobbyPanels.Text(); _draftStatus.Visible = false; overview.AddChild(_draftStatus);
        BuildLastGame(overview);

        var chat = LobbyPanels.Card(right, "Chat", "Chat & activity");
        ((Control)chat.GetParent()).SizeFlagsVertical = SizeFlags.ExpandFill;
        _messages = new RichTextLabel { Name = "Messages", CustomMinimumSize = new Vector2(0, 100), SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ScrollFollowing = true, BbcodeEnabled = true };
        chat.AddChild(_messages);
        var chatRow = new HBoxContainer(); chat.AddChild(chatRow);
        _chatChannel = new OptionButton { Name = "ChatChannel" }; _chatChannel.AddItem("All"); _chatChannel.AddItem("Team");
        chatRow.AddChild(_chatChannel);
        _chat = new LineEdit { Name = "ChatLine", PlaceholderText = "Message the lobby", MaxLength = 100, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _chat.TextSubmitted += _ => SendChat(_chatChannel.Selected == 1);
        chatRow.AddChild(_chat);
        chatRow.AddChild(LobbyPanels.Button("Send", () => SendChat(_chatChannel.Selected == 1), "SendChat"));
        _microphone = LobbyPanels.Text(); chat.AddChild(_microphone);
        TeamVoiceOnlyBox = new CheckBox { Name = "TeamVoiceOnly", Text = "Voice to team only" };
        TeamVoiceOnlyBox.Toggled += on => Session?.SetTeamVoiceOnly(on); chat.AddChild(TeamVoiceOnlyBox);
        var recording = LobbyPanels.Card(right, "PersonalRecording", "Match demos");
        _recordDemos = new CheckBox { Name = "PersonalRecordDemos", Text = DemoRecordingText.Label, CustomMinimumSize = new Vector2(0, 36),
            TooltipText = DemoRecordingText.Tooltip };
        _recordDemos.Toggled += on => { Session?.SetRecordDemos(on); RefreshRecordingControls(); };
        recording.AddChild(_recordDemos);
        _recordingNote = LobbyPanels.Text();
        _recordingNote.Name = "RecordingStatus";
        _recordingNote.AddThemeFontSizeOverride("font_size", 14);
        _recordingNote.Modulate = UiStyle.Dim;
        recording.AddChild(_recordingNote);
        Parameters = new CreateSessionModel(session.Maps, session.MapSettings, session.Setup);
        _modelSetup = session.Setup;
        var rows = new List<SessionRow>(CreateSessionModel.Rows); rows.Remove(SessionRow.Password);
        ParameterRows = new SessionRowsView(Parameters, rows, showPassword: false, form: true) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (_mapCatalog is not null) Parameters.MapFilter = MapPickerView.FilterFor(_mapCatalog, SettingsStore.Current.ActiveMapPlaylist);
        Parameters.Changed += ShowMapPicture; Parameters.Changed += ShowAlternateButton; Parameters.Changed += UpdateSettingsDraft;
        ParameterRows.PendingEditsChanged += UpdateSettingsDraft;
        BuildSettingsPanel();
        var preparation = LobbyPanels.Preparation(this);
        _loadout = LobbyPanels.Text(); _loadout.Name = "LoadoutSummary";
        _loadout.AutowrapMode = TextServer.AutowrapMode.Off;
        _loadout.ClipText = true;
        _loadout.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        preparation.AddChild(_loadout);
        _status = LobbyPanels.Text(); _status.Name = "PreparationStatus"; _status.MaxLinesVisible = 2; _status.Modulate = UiStyle.MenuFocus; preparation.AddChild(_status);
        LeaveButton = PageButton("Leave lobby", () => ConfirmLeave(() => Session?.Leave(),
            Session is { CanControl: true } s && Parameters!.Setup != s.Setup), "Leave");
        PageButton("Options", OpenOptions, "Options");
        HelpButton = HelpUtility(() => HelpFeedbackScreen.Open(this, HelpButton,
            GameReportContext.Capture(GetTree(), Session), HelpActions));

        MainButton = PageButton("Ready", PressMain, "Main");
        foreach (Button button in PageButtons)
        {
            button.CustomMinimumSize = new Vector2(160, 44);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        MainButton.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        ShowMapPicture();
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = new PanelContainer { Name = "MatchSettingsPanel", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _settingsPanel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 12);
        _settingsPanel.AddChild(margin);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 14); margin.AddChild(body);
        var title = new Label { Text = "Match settings", Modulate = UiStyle.MenuFocus };
        title.AddThemeFontSizeOverride("font_size", 26); body.AddChild(title);
        _settingsDraftNote = LobbyPanels.Text("Choose the map and rules, then apply your changes.");
        body.AddChild(_settingsDraftNote);
        _settingsScroll = new ScrollContainer { Name = "SettingsScroll", FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_settingsScroll);
        BuildSettingsContent();
        var buttons = new HFlowContainer { Name = "SettingsButtons", Alignment = FlowContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("h_separation", 10); buttons.AddThemeConstantOverride("v_separation", 8); body.AddChild(buttons);
        buttons.AddChild(LobbyPanels.Button("Discard / close", RequestCloseSettings, "DiscardSettings"));
        ApplyButton = LobbyPanels.Button("Apply changes", Apply, "Apply"); buttons.AddChild(ApplyButton);
        ApplyButton.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
        foreach (Control button in buttons.GetChildren()) button.CustomMinimumSize = new Vector2(0, 42);
        var dim = new ColorRect { Name = "SettingsDim", Color = new Color(0, 0, 0, 0.8f), Visible = false };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        _settingsPanel.VisibilityChanged += () =>
        {
            dim.Visible = _settingsPanel.Visible;
            if (!_settingsPanel.Visible)
            {
                foreach (var entry in _backgroundFocus)
                    if (IsInstanceValid(entry.Key)) entry.Key.FocusMode = entry.Value;
                _backgroundFocus.Clear();
                _rulesButton?.CallDeferred(Control.MethodName.GrabFocus);
            }
        };
        _settingsCanvas = new Control { Name = "SettingsCanvas", MouseFilter = MouseFilterEnum.Ignore,
            OversamplingWithScale = OversamplingWithScaleEnum.Enabled };
        AddChild(_settingsCanvas);
        _settingsCanvas.AddChild(_settingsPanel);
        FitSettingsPanel();
    }

    private void OpenMatchSettings()
    {
        if (Session is null || _settingsPanel is null) return;
        if (!_settingsPanel.Visible)
            foreach (Control control in FindChildren("*", nameof(Control), true, false).OfType<Control>())
                if (control != _settingsPanel && !_settingsPanel.IsAncestorOf(control) && control.FocusMode != FocusModeEnum.None)
                {
                    _backgroundFocus[control] = control.FocusMode;
                    control.FocusMode = FocusModeEnum.None;
                }
        GetNode<Control>("SettingsDim").MoveToFront();
        _settingsCanvas!.MoveToFront();
        _settingsPanel.Visible = true;
        FitSettingsPanel();
        UpdateSettingsDraft();
        ApplyButton!.Visible = Session.CanControl;
        SettingsTabs!.CurrentTab = 0;
        if (Session.CanControl) ParameterRows!.Find("ModeChoice")?.GrabFocus();
        else _settingsPanel.FindChild("DiscardSettings", true, false)?.Call("grab_focus");
    }

    private void UpdateSettingsDraft()
    {
        if (Session is not { } session || Parameters is null) return;
        RefreshSettingsSummary();
        bool dirty = session.CanControl && (Parameters.Setup != session.Setup || ParameterRows!.HasPendingEdits);
        if (_draftStatus is not null)
        {
            _draftStatus.Visible = dirty;
            _draftStatus.Text = _draftConflict ? "The match changed while you were editing. Review your changes before applying."
                : "Unapplied changes — open match settings to review.";
        }
        if (_settingsDraftNote is not null) _settingsDraftNote.Text = !session.CanControl ? "These are the host's current match settings."
            : dirty ? _draftConflict ? _draftStatus!.Text : "You have unapplied changes. Apply to update the match for everyone."
            : "Choose the map and rules, then apply your changes.";
        if (ApplyButton is not null) ApplyButton.Disabled = !dirty;
        if (_settingsPanel?.FindChild("DiscardSettings", true, false) is Button close) close.Text = dirty ? "Discard changes" : "Close";
    }

    private void DiscardSettings()
    {
        _draftConflict = false;
        if (Session is { } s) Parameters?.Load(s.Setup);
        _settingsPanel!.Visible = false;
        _rulesButton?.GrabFocus();
        Refresh();
    }

    private void UpdateOverview()
    {
        if (Session is not { } s || _overviewTitle is null) return;
        _overviewTitle.Text = MapTitle(s.Setup.Map) + "\n" + CreateSessionModel.ModeName(s.Setup.Mode);
        _overviewRules!.Text = LobbyPresentation.Rules(s.Setup);
        _overviewObjective!.Text = LobbyPresentation.Objective(s.Setup.Mode);
        ShowMapPicture();
        MapPicture!.TooltipText = _overviewObjective.Text;
        _mapButton!.TooltipText = (s.CanControl ? "Select a map" : "View map settings") + "\n" + _overviewObjective.Text;
    }

    private void UpdateLoadout()
    {
        if (_loadout is null || Session is not { } s) return;
        var local = Local();
        if (local?.Side is null) { _loadout.Text = "Your role: Spectator"; return; }
        bool spy = local.Side == HudSide.Spy;
        var model = GearMenuModel.ForLobby(spy ? 0 : 1, s.Setup, SettingsStore.Current.Loadout,
            OpenScctGame.Online.AccountService.Get(GetTree()).Account?.Progress?.Level ?? 1, !OpenScctGame.Online.AccountService.HasMaster);
        string equipment = model is null ? "Choose your equipment" : string.Join(", ", model.Gadgets.Take(model.Slots).Select(GearMenuModel.NameOf));
        if (!spy && model is not null) equipment = GearMenuModel.NameOf(model.ChosenWeapon) + " · " + equipment;
        _loadout.Text = "You: " + (spy ? "Spy" : "Mercenary") + " · " + equipment;
        _loadout.TooltipText = _loadout.Text;
    }

    private void UpdateTeamCards()
    {
        if (Session is not { } s) return;
        foreach (var entry in new[] { (_spies!, HudSide.Spy, "Spies"), (_mercs!, HudSide.Merc, "Mercenaries") })
        {
            int count = s.Members.Count(p => p.Side == entry.Item2);
            bool unavailable = entry.Item2 == HudSide.Spy && s.Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch;
            ((Label)entry.Item1.GetChild(0)).Text = entry.Item3 + " · " + count + (count == 1 ? " player" : " players");
            var slots = entry.Item2 == HudSide.Spy ? _spiesWaiting! : _mercsWaiting!;
            slots.Visible = !unavailable && count == 0;
            slots.Text = "Open slots — join this team";
            slots.Modulate = UiStyle.MenuDisabled;
        }
    }

    private void UpdatePreparation(IOnlineLobbySession s, int seconds)
    {
        var local = Local();
        string? download = s.MapFetch is { } fetch ? fetch.Text + (fetch.Running ? " — " + Math.Round(fetch.Fraction * 100) + "%" : "") : null;
        _status!.Text = s.PreparingBots ? "Preparing bots for this map…" : LobbyPresentation.Preparation(s.CanControl, local is not null, local?.Side is null, local?.Ready ?? false,
            s.Launching, seconds, s.Members.Where(p => p.Side is not null && !p.IsBot && !p.IsHost && !p.Ready).Select(p => p.Name),
            s.Members.Where(p => p.Side is not null && p.MapNote.Length > 0).Select(p => p.Name + ": " + p.MapNote), download);
        if (!s.Launching && s.Members.All(p => p.Side is null)) _status.Text = "No players have joined a team.";
        _status.TooltipText = _status.Text;
        MainButton!.TooltipText = _status.Text;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (_confirm is { Visible: true })
            {
                ConfirmKick(false);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (_settingsPanel is { Visible: true })
            {
                GetViewport().SetInputAsHandled();
                RequestCloseSettings();
                return;
            }
        }
        base._UnhandledKeyInput(e);
    }

    private void RequestCloseSettings()
    {
        if (Session is { CanControl: true } s && (Parameters!.Setup != s.Setup || ParameterRows!.HasPendingEdits))
            ConfirmBox.Open(this, new ConfirmBox { Name = "DiscardMatchSettings", Question = "Discard unapplied match settings?", YesCaption = "Discard", NoCaption = "Keep editing", Yes = DiscardSettings });
        else DiscardSettings();
    }
}
