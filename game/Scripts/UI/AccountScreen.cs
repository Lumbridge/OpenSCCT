using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// The Account page: signing in with Discord through our master (the master gives a short code and a page; the game
/// opens the page in the browser and asks every 2 s until the sign-in is done, expired or refused), picking the
/// account's name, and the signed-in account: name, rank (level, XP, prestige, title; spec/additions/progression.md),
/// stats and Sign out (achievements have their own page). A signed-in player's settings
/// and key layouts follow the account (<see cref="AccountService"/>). Without a master there are no accounts.
/// </summary>
public partial class AccountScreen : MenuPage
{
    /// <summary>Optional context for an account sign-in requested from elsewhere in the game.</summary>
    public string? Notice { get; init; }

    private const ulong PollIntervalMs = 2000;

    internal enum View
    {
        NoMaster,
        SignedOut,
        Waiting,
        PickName,
        SignedIn,
    }

    // Isolated visual fixtures use the same builders without starting sign-in or changing saved settings.
    internal sealed record PreviewState(View View, AccountInfo? Account = null, LinkStartResponse? Link = null,
        bool ChangingName = false, bool PrestigeArmed = false, string? Status = null);

    internal PreviewState? Preview { get; init; }

    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    private AccountService? _service;
    private VBoxContainer? _content;
    private Label? _status;
    private View _view;

    private Task<LinkStartResponse>? _start;
    private Task<LinkPollResponse>? _poll;
    private LinkStartResponse? _link;
    private ulong _nextPoll;
    private ulong _expires;

    private Task<AccountInfo>? _claim;
    private Task<AccountInfo>? _refresh;
    private Task? _signOut;
    private Task<AccountInfo>? _prestige;
    private LineEdit? _nameEdit;
    private Label? _titleSummary;
    private bool _showLockedTitles;

    /// <summary>The Prestige button was pressed once: the next press confirms.</summary>
    private bool _prestigeArmed;

    /// <summary>True while the name picker is a change of an already claimed name (Cancel goes back).</summary>
    private bool _changingName;

    public override void _Ready()
    {
        Name = "Account";
        BuildFrame("Account", 1180f, responsive: true);
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").AddChild(new CareerScroll());
        if (!string.IsNullOrWhiteSpace(Notice))
            CareerPanels.Text(Body, "Notice", Notice, size: 14).Modulate = UiStyle.MenuFocus;
        _status = CareerPanels.Text(Body, "Status", string.Empty, size: 14);
        _status.Modulate = UiStyle.MenuFocus;
        _status.Visible = false;
        _content = new VBoxContainer { Name = "Content" };
        _content.AddThemeConstantOverride("separation", 10);
        Body.AddChild(_content);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        PageButton("Stats", () => FrontEnd.ShowStats(GetTree()), "StatsPage");
        foreach (Button button in PageButtons) button.CustomMinimumSize = new Vector2(144f, 44f);

        if (Preview is { } preview)
        {
            _link = preview.Link;
            _changingName = preview.ChangingName;
            _prestigeArmed = preview.PrestigeArmed;
            ShowView(preview.View);
            if (preview.Status is { } status) ShowStatus(status);
            return;
        }

        _service = AccountService.Get(GetTree());
        _service.Changed += OnAccountUpdated;
        Body.AddChild(new AppearanceSyncPanel());

        AccountSettings account = AccountSettings.Current;
        if (!AccountService.HasMaster)
        {
            ShowView(View.NoMaster);
        }
        else if (!account.SignedIn)
        {
            ShowView(View.SignedOut);
        }
        else
        {
            ShowView(account.Name.Length == 0 ? View.PickName : View.SignedIn);
            _refresh = AccountService.Call(c => c.AccountAsync());
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Preview is not null) return;
        ProcessPronouns();
        if (_start is { IsCompleted: true } start)
        {
            _start = null;
            if (start.IsCompletedSuccessfully)
            {
                Started(start.Result);
            }
            else
            {
                ShowView(View.SignedOut);
                ShowStatus(AccountService.ErrorOf(start));
            }
        }

        if (_view == View.Waiting && _link is { } link && _poll is null && _start is null)
        {
            ulong now = Time.GetTicksMsec();
            if (_expires != 0 && now >= _expires)
            {
                _link = null;
                ShowView(View.SignedOut);
                ShowStatus("The sign-in code expired. Try again.");
            }
            else if (now >= _nextPoll)
            {
                _poll = AccountService.Call(c => c.PollLinkAsync(link.Code, link.PollSecret), withToken: false);
            }
        }

        if (_poll is { IsCompleted: true } poll)
        {
            _poll = null;
            _nextPoll = Time.GetTicksMsec() + PollIntervalMs;
            if (_view == View.Waiting)
            {
                Polled(poll);
            }
        }

        if (_claim is { IsCompleted: true } claim)
        {
            _claim = null;
            if (claim.IsCompletedSuccessfully)
            {
                _service!.Update(claim.Result);
                _changingName = false;
                ShowView(View.SignedIn);
                ShowStatus("Name claimed: " + claim.Result.Name);
            }
            else if (AccountService.IsSignedOutError(claim))
            {
                SignedOutByMaster();
            }
            else
            {
                ShowStatus(AccountService.ErrorOf(claim));
            }
        }

        if (_refresh is { IsCompleted: true } refresh)
        {
            _refresh = null;
            if (refresh.IsCompletedSuccessfully)
            {
                _service!.Update(refresh.Result);
                if (_view is (View.SignedIn or View.PickName) && !_changingName)
                {
                    ShowView(refresh.Result.Name is null ? View.PickName : View.SignedIn);
                }
            }
            else if (AccountService.IsSignedOutError(refresh))
            {
                SignedOutByMaster();
            }
            else
            {
                ShowStatus(AccountService.ErrorOf(refresh));
            }
        }

        if (_prestige is { IsCompleted: true } prestige)
        {
            _prestige = null;
            _prestigeArmed = false;
            if (prestige.IsCompletedSuccessfully)
            {
                _service!.Update(prestige.Result);
                ShowView(View.SignedIn);
                int p = prestige.Result.Progress?.Prestige ?? 0;
                ShowStatus($"Prestige {p}: back to level 1 with a new emblem" +
                    (TitleCatalog.Find("prestige_" + p.ToString(CultureInfo.InvariantCulture)) is { } t ? $" and the title {t.Text}." : "."));
            }
            else if (AccountService.IsSignedOutError(prestige))
            {
                SignedOutByMaster();
            }
            else
            {
                ShowStatus(AccountService.ErrorOf(prestige));
                ShowView(View.SignedIn);
            }
        }

        if (_signOut is { IsCompleted: true })
        {
            // Best effort: whatever the master said, this PC forgets the sign-in.
            _signOut = null;
            _service!.SignOutLocally();
            ShowView(View.SignedOut);
            ShowStatus("Signed out.");
        }
    }

    public override void _ExitTree()
    {
        if (_service is not null) _service.Changed -= OnAccountUpdated;
    }

    private void OnAccountUpdated()
    {
        if (_view == View.SignedIn && !_changingName && _service?.Account is not null) ShowView(View.SignedIn);
    }

    private void SignedOutByMaster()
    {
        _service!.SignOutLocally();
        ShowView(View.SignedOut);
        ShowStatus("Your sign-in ended. Sign in again.");
    }

    // ---- signing in ----------------------------------------------------------------------------------------

    private void SignIn()
    {
        if (Preview is not null || _start is not null)
        {
            return;
        }

        ShowStatus("Asking the master server...");
        _start = AccountService.Call(c => c.StartLinkAsync(), withToken: false);
    }

    private void Started(LinkStartResponse link)
    {
        _link = link;
        _expires = link.ExpiresSeconds > 0 ? Time.GetTicksMsec() + (ulong)link.ExpiresSeconds * 1000UL : 0;
        _nextPoll = Time.GetTicksMsec() + PollIntervalMs;
        ShowView(View.Waiting);
        ShowStatus(string.Empty);
        if (link.Url.Length > 0 && OS.ShellOpen(link.Url) != Error.Ok)
        {
            ShowStatus("The browser did not open: open the address above yourself.");
        }
    }

    private void Polled(Task<LinkPollResponse> poll)
    {
        if (!poll.IsCompletedSuccessfully)
        {
            // A missed answer is not the end: ask again in 2 s, and say so.
            ShowStatus(AccountService.ErrorOf(poll));
            return;
        }

        LinkPollResponse answer = poll.Result;
        switch (answer.Status)
        {
            case LinkStatus.Pending:
                return;
            case LinkStatus.Done when answer.SessionToken is { Length: > 0 } token && answer.Account is { } account:
                _link = null;
                _service!.SignedIn(token, account);
                ShowView(account.Name is null ? View.PickName : View.SignedIn);
                ShowStatus(account.Name is null ? "Signed in. Pick the name other players will see." : "Signed in as " + account.Name + ".");
                return;
            case LinkStatus.Expired:
                _link = null;
                ShowView(View.SignedOut);
                ShowStatus("The sign-in code expired. Try again.");
                return;
            default:
                _link = null;
                ShowView(View.SignedOut);
                ShowStatus(answer.Error ?? "The sign-in was refused.");
                return;
        }
    }

    private void CancelSignIn()
    {
        _link = null;
        _start = null;
        _poll = null;
        ShowView(View.SignedOut);
        ShowStatus("Sign-in cancelled.");
    }

    // ---- the name ------------------------------------------------------------------------------------------

    private void Claim()
    {
        if (Preview is not null) return;
        string name = _nameEdit?.Text.Trim() ?? string.Empty;
        if (PlayerNames.ClaimProblem(name) is { } problem)
        {
            ShowStatus(problem);
            return;
        }

        if (_claim is null)
        {
            ShowStatus("Claiming " + name + "...");
            _claim = AccountService.Call(c => c.ClaimNameAsync(name));
        }
    }

    private void SignOut()
    {
        if (Preview is not null || _signOut is not null)
        {
            return;
        }

        ShowStatus("Signing out...");
        _signOut = AccountService.Call(async c =>
        {
            await c.SignOutAsync().ConfigureAwait(false);
            return true;
        });
    }

    // ---- views ---------------------------------------------------------------------------------------------

    private void ShowView(View view)
    {
        bool sameView = _view == view;
        _view = view;
        if (_content is null)
        {
            return;
        }

        Control? focus = GetViewport().GuiGetFocusOwner();
        bool restoreFocus = focus is not null && _content.IsAncestorOf(focus);
        string? returnAction = focus?.Name.ToString() switch
        {
            "CancelPrestige" => "Prestige",
            "CancelName" => "ChangeName",
            _ => null,
        };
        NodePath? focusPath = restoreFocus && sameView ? _content.GetPathTo(focus!) : null;
        CareerPanels.Clear(_content);
        _nameEdit = null;
        _titleSummary = null;
        PreferredBodyWidth = view == View.SignedIn ? 1180f : 760f;
        if (!sameView) PageCanvas.GetNode<ScrollContainer>("BodyScroll").ScrollVertical = 0;

        switch (view)
        {
            case View.NoMaster:
                BuildNoMaster();
                break;
            case View.SignedOut:
                BuildSignedOut();
                break;
            case View.Waiting:
                BuildWaiting();
                break;
            case View.PickName:
                BuildPickName();
                break;
            default:
                BuildSignedIn();
                break;
        }

        if (restoreFocus)
        {
            Control? next = focusPath is not null ? _content.GetNodeOrNull<Control>(focusPath) : null;
            if (next is Button { Disabled: true } || next is { } hidden && !hidden.IsVisibleInTree()) next = null;
            if (next is null && returnAction is not null) next = _content.FindChild(returnAction, true, false) as Control;
            if (next is Button { Disabled: true }) next = null;
            next ??= (Control?)_nameEdit ?? _content.FindChildren("*", nameof(Button), true, false).OfType<Button>()
                .FirstOrDefault(button => button.Name != "SignOut" && !button.Disabled && button.IsVisibleInTree());
            next ??= PageButtons[0];
            next?.GrabFocus();
        }
    }

    private void BuildNoMaster()
    {
        VBoxContainer panel = CareerPanels.Card(_content!, "NoMaster", "Play your way");
        CareerPanels.Text(panel, "Heading", "Connect your account", size: 26);
        CareerPanels.Text(panel, "Help", "Choose a master server in Find a Session to sign in and track your progression.");
        CareerPanels.Text(panel, "OfflineHelp", "Offline and LAN play remain available without an account.", dim: true, size: 14);
        Actions(panel, "MasterActions").AddChild(ActionButton("Find a Session", () => FrontEnd.Show(GetTree(), new ServerBrowserScreen()), "FindSession"));
    }

    private void BuildSignedOut()
    {
        VBoxContainer panel = CareerPanels.Card(_content!, "SignedOut", "Your OpenSCCT account");
        CareerPanels.Text(panel, "Heading", "Make your progress count", size: 26);
        CareerPanels.Text(panel, "Help", "Sign in with Discord to claim your player name and keep your career with you.");
        var benefits = CareerPanels.Grid(panel, "AccountBenefits", 210f, 3);
        Benefit(benefits, "Identity", "Your identity", "Reserve your name and join servers that require an account.");
        Benefit(benefits, "Progress", "Your progression", "Track stats, earn achievements and unlock cosmetics.");
        Benefit(benefits, "Settings", "Your settings", "Take your controls and preferences to another PC.");
        Actions(panel, "SignInActions").AddChild(ActionButton("Sign in with Discord", SignIn, "SignIn"));
        CareerPanels.Text(panel, "GuestHelp", "You can still play on servers that allow guests, offline and on LAN without signing in.", dim: true, size: 14);
    }

    private void BuildWaiting()
    {
        VBoxContainer panel = CareerPanels.Card(_content!, "Waiting", "Discord sign-in");
        CareerPanels.Text(panel, "Heading", "Finish signing in with your browser", size: 24);
        CareerPanels.Text(panel, "Help", "Check the browser shows this code, then sign in with Discord. This page will update automatically.");
        Label code = CareerPanels.Text(panel, "Code", _link?.Code ?? string.Empty, size: 36);
        code.HorizontalAlignment = HorizontalAlignment.Center;
        code.Modulate = UiStyle.MenuFocus;
        CareerPanels.Text(panel, "UrlHelp", "If the browser did not open, copy this address:", dim: true, size: 14);
        panel.AddChild(new LineEdit { Name = "Url", Text = _link?.Url ?? string.Empty, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        CareerPanels.Text(panel, "WaitingStatus", "Waiting for Discord…", dim: true, size: 14);
        Actions(panel, "WaitingActions").AddChild(ActionButton("Cancel sign-in", CancelSignIn, "CancelSignIn"));
    }

    private void BuildPickName()
    {
        VBoxContainer panel = CareerPanels.Card(_content!, "PickName", _changingName ? "Your identity" : "One last step");
        CareerPanels.Text(panel, "Heading", _changingName ? "Change your player name" : "Choose your player name", size: 26);
        CareerPanels.Text(panel, "NameHelp",
            $"The name other players see. {PlayerNames.MinClaimLength} to {PlayerNames.MaxClaimLength} letters, digits, _ - and .; " +
            "nobody else can use it while it is yours.");
        string start = Preview is { } preview ? preview.Account?.Name ?? string.Empty : AccountSettings.Current.Name;
        if (start.Length == 0 && PlayerNames.ClaimProblem(SettingsStore.Current.PlayerName) is null)
        {
            start = SettingsStore.Current.PlayerName;
        }

        _nameEdit = new LineEdit
        {
            Name = "Name", Text = start, MaxLength = PlayerNames.MaxClaimLength, PlaceholderText = "your name", SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _nameEdit.TextSubmitted += _ => Claim();
        panel.AddChild(_nameEdit);
        var buttons = Actions(panel, "NameButtons");
        buttons.AddChild(ActionButton(_changingName ? "Save name" : "Claim name", Claim, "Claim"));
        if (_changingName)
        {
            buttons.AddChild(ActionButton("Cancel", () =>
            {
                _changingName = false;
                ShowView(View.SignedIn);
            }, "CancelName"));
        }
        else
        {
            buttons.AddChild(ActionButton("Sign out", SignOut, "SignOut"));
        }

        _nameEdit.GrabFocus();
    }

    private void BuildSignedIn()
    {
        AccountSettings s = AccountSettings.Current;
        AccountInfo? info = Preview is { } preview ? preview.Account : _service?.Account;
        string playerName = Preview is not null ? info?.Name ?? "Your account" : s.Name;
        bool admin = Preview is not null ? info?.Admin == true : s.Admin;
        VBoxContainer panel = CareerPanels.Card(_content!, "SignedIn", admin ? "Signed in · Administrator" : "Signed in");
        CareerPanels.Text(panel, "AccountName", playerName, size: 28).Modulate = UiStyle.MenuTitle;
        CareerPanels.Text(panel, "SettingsHelp", "Settings and controls follow your account. Display and microphone settings stay on this PC.", dim: true, size: 14);

        var buttons = Actions(panel, "AccountButtons");
        Button change = ActionButton("Change name", () =>
        {
            _changingName = true;
            ShowView(View.PickName);
        }, "ChangeName");
        buttons.AddChild(change);
        buttons.AddChild(ActionButton("Sign out", SignOut, "SignOut"));
        if (info?.NameChangeAllowed is { } allowed && allowed > DateTimeOffset.UtcNow)
        {
            change.Disabled = true;
            CareerPanels.Text(panel, "NameCooldown", "You can change your name again on " + allowed.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + ".", dim: true, size: 14);
        }

        BuildPronouns(info);
        var overview = CareerPanels.Grid(_content!, "AccountOverview", 470f);
        BuildRank(overview, info);
        VBoxContainer stats = CareerPanels.Card(overview, "Stats", "Career snapshot");
        if (info is null)
        {
            CareerPanels.Text(stats, "LoadingStats", "Asking the master server…", dim: true);
        }
        else
        {
            AccountStats st = info.Stats;
            CareerPanels.StatRow(stats, "Matches played", StatsModel.Number(st.Matches));
            CareerPanels.StatRow(stats, "Spy wins", StatsModel.Number(st.SpyWins));
            CareerPanels.StatRow(stats, "Mercenary wins", StatsModel.Number(st.MercWins));
            CareerPanels.StatRow(stats, "Losses", StatsModel.Number(st.Losses));
            CareerPanels.StatRow(stats, "Kills", StatsModel.Number(st.Kills));
            CareerPanels.StatRow(stats, "Deaths", StatsModel.Number(st.Deaths));
            CareerPanels.Text(stats, "CountingHelp", st.Matches == 0 ? "Your career starts with your first counted official round." : "Totals from counted official rounds.", dim: true, size: 14);
        }
        var careerActions = Actions(stats, "CareerActions");
        careerActions.AddChild(ActionButton("Full stats", () => FrontEnd.ShowStats(GetTree()), "FullStats"));
        careerActions.AddChild(ActionButton("Match history", () => FrontEnd.ShowMatchHistory(GetTree()), "MatchHistory"));

        if (info?.Progress is { } progress) BuildTitles(progress);
    }

    // ---- rank ----------------------------------------------------------------------------------------------

    private void BuildRank(Container parent, AccountInfo? info)
    {
        VBoxContainer rankPanel = CareerPanels.Card(parent, "Rank", "Rank & progression");
        if (info is null)
        {
            CareerPanels.Text(rankPanel, "LoadingRank", "Asking the master server…", dim: true);
            return;
        }

        if (info.Progress is not { } progress)
        {
            CareerPanels.Text(rankPanel, "LegacyRank", "This master server has no levels yet.", dim: true);
            return;
        }

        PlayerRank rank = RankBadge.OfAccount(info);
        rankPanel.AddChild(new RankBadge(48, 24) { Name = "Badge" }.Show(rank));

        var bar = new UiProgress { Name = "XpBar", Value = rank.Fraction, Fill = RankIcons.ColourOf(rank), CustomMinimumSize = new Vector2(0f, 8f) };
        rankPanel.AddChild(bar);
        CareerPanels.Text(rankPanel, "NextLevel", rank.AtCap
            ? $"Level {ProgressionK.MaxLevel} · Maximum level"
            : string.Create(CultureInfo.InvariantCulture,
                $"{rank.XpIntoLevel:N0} / {rank.XpForNext:N0} XP to level {rank.Level + 1}"), size: 16);
        CareerPanels.Text(rankPanel, "TotalXp", string.Create(CultureInfo.InvariantCulture, $"{progress.TotalXp:N0} lifetime XP"), dim: true, size: 14);
        Label xpHelp = CareerPanels.Text(rankPanel, "XpHelp", "Earn XP through score, wins, objectives and achievements on official servers.", dim: true, size: 14);
        xpHelp.MouseFilter = MouseFilterEnum.Stop;
        xpHelp.TooltipText = string.Create(CultureInfo.InvariantCulture,
            $"{ProgressionK.ScoreXp} XP per score point · {ProgressionK.FinishXp} for finishing · {ProgressionK.WinXp} for winning · " +
            $"{ProgressionK.ObjectiveXp} per objective · {ProgressionK.AchievementXp}–{ProgressionK.MetaAchievementXp} per achievement");

        if (rank.CanPrestige)
        {
            CareerPanels.Text(rankPanel, "PrestigeHelp", _prestigeArmed
                ? $"Prestige {rank.Prestige + 1} puts you back to level 1 with a new emblem and title. Level-based pistols and finishes relock. Your achievements, mastery rewards, titles and stats stay. Press again to confirm."
                : $"Return to level 1 for the prestige {rank.Prestige + 1} emblem and a new title.", size: 14);
            Button prestigeButton = ActionButton(_prestigeArmed ? "Confirm prestige" : $"Enter prestige {rank.Prestige + 1}", Prestige, "Prestige");
            prestigeButton.Disabled = _prestige is not null;
            var actions = Actions(rankPanel, "PrestigeActions");
            actions.AddChild(prestigeButton);
            if (_prestigeArmed) actions.AddChild(ActionButton("Cancel", () =>
            {
                _prestigeArmed = false;
                ShowView(View.SignedIn);
            }, "CancelPrestige"));
        }
        else if (rank.IsMaster)
        {
            CareerPanels.Text(rankPanel, "MasterRank", "Master prestige · The highest rank.", dim: true, size: 14);
        }

        BuildEmblems(rankPanel, rank);
    }

    private static void BuildEmblems(Container panel, PlayerRank rank)
    {
        CareerPanels.Text(panel, "EmblemsCaption", "PRESTIGE EMBLEMS", dim: true, size: 12);
        var row = new HFlowContainer { Name = "EmblemRow" };
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 6);
        for (int p = 1; p <= ProgressionK.MaxPrestige + 1; p++)
        {
            bool master = p > ProgressionK.MaxPrestige;
            bool reached = master ? rank.IsMaster : rank.Prestige >= p;
            var icon = new TextureRect
            {
                Texture = master ? RankIcons.Master(32) : RankIcons.Prestige(p, 32), CustomMinimumSize = new Vector2(32f, 32f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = reached ? Colors.White : new Color(1f, 1f, 1f, 0.22f), MouseFilter = MouseFilterEnum.Stop,
                TooltipText = Progression.PrestigeNames[p] + (reached ? string.Empty : master
                    ? $" (reach level {ProgressionK.MaxLevel} at prestige {ProgressionK.MaxPrestige})"
                    : " (not reached yet)"),
            };
            row.AddChild(icon);
        }

        panel.AddChild(row);
    }

    private void BuildTitles(ProgressInfo progress)
    {
        VBoxContainer panel = CareerPanels.Card(_content!, "Titles", "Player title");
        string? chosen = SettingsStore.Current.Title;
        if (chosen is not null && !progress.Titles.Contains(chosen))
        {
            chosen = null;
        }

        _titleSummary = CareerPanels.Text(panel, "TitleSummary", TitleCatalog.Find(chosen)?.Text ?? "No title equipped", size: 20);
        CareerPanels.Text(panel, "TitlesHelp", $"Shown under your name in lobbies and the hub. {progress.Titles.Count} of {TitleCatalog.All.Count} unlocked.", dim: true, size: 14);
        var flow = new HFlowContainer { Name = "TitleList" };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        var group = new ButtonGroup();
        flow.AddChild(TitleButton("None", null, chosen is null, true, "No title", group));
        var locked = new HFlowContainer { Name = "LockedTitles", Visible = _showLockedTitles };
        locked.AddThemeConstantOverride("h_separation", 6);
        locked.AddThemeConstantOverride("v_separation", 6);
        foreach (TitleItem t in TitleCatalog.All)
        {
            bool have = progress.Titles.Contains(t.Id);
            (have ? flow : locked).AddChild(TitleButton(t.Text, t.Id, chosen == t.Id, have, have ? t.HowToUnlock.Replace("Reach", "Reached", StringComparison.Ordinal)
                .Replace("Earn the", "Earned the", StringComparison.Ordinal) : "Locked. " + t.HowToUnlock, group));
        }

        panel.AddChild(flow);
        var actions = Actions(panel, "CollectionActions");
        if (locked.GetChildCount() > 0)
        {
            var browse = ActionButton($"Locked titles ({locked.GetChildCount()})", () => { }, "BrowseTitles");
            browse.ToggleMode = true;
            browse.SetPressedNoSignal(_showLockedTitles);
            browse.Toggled += shown =>
            {
                _showLockedTitles = shown;
                locked.Visible = shown;
            };
            actions.AddChild(browse);
        }
        actions.AddChild(ActionButton("Achievements", () => FrontEnd.Show(GetTree(), new AchievementsScreen()), "AchievementsPage"));
        actions.AddChild(ActionButton("Armoury", () => FrontEnd.Show(GetTree(), new ArmouryScreen()), "ArmouryPage"));
        panel.AddChild(locked);
    }

    private Button TitleButton(string text, string? id, bool on, bool unlocked, string tooltip, ButtonGroup group)
    {
        var button = new Button
        {
            Text = unlocked ? text : text + " (locked)", ToggleMode = true, ButtonPressed = on, ButtonGroup = group, Disabled = !unlocked,
            TooltipText = tooltip, Name = "Title_" + (id ?? "none"), CustomMinimumSize = new Vector2(0f, 32f),
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += () =>
        {
            if (Preview is null)
            {
                GameSettings settings = SettingsStore.Current;
                settings.Title = id;
                SettingsStore.Save(settings);
            }
            if (_titleSummary is not null) _titleSummary.Text = id is null ? "No title equipped" : text;
            ShowStatus(id is null ? "No title shown." : $"Title set: {text}. Players see it from your next join.");
        };
        return button;
    }

    private void Prestige()
    {
        if (_prestige is not null)
        {
            return;
        }

        if (!_prestigeArmed)
        {
            _prestigeArmed = true;
            ShowView(View.SignedIn);
            return;
        }

        if (Preview is not null) return;
        _prestige = AccountService.Call(c => c.PrestigeAsync());
        ShowStatus("Asking the master server...");
    }

    private static HFlowContainer Actions(Container parent, string name)
    {
        var actions = new HFlowContainer { Name = name };
        actions.AddThemeConstantOverride("h_separation", 8);
        actions.AddThemeConstantOverride("v_separation", 8);
        parent.AddChild(actions);
        return actions;
    }

    private static Button ActionButton(string caption, Action pressed, string name)
    {
        Button button = UiStyle.MenuButton(caption, pressed, name);
        button.CustomMinimumSize = new Vector2(0f, 36f);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

    private static void Benefit(Container parent, string name, string caption, string text)
    {
        var column = new VBoxContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        parent.AddChild(column);
        CareerPanels.Text(column, "Heading", caption, size: 16).Modulate = UiStyle.MenuFocus;
        CareerPanels.Text(column, "Description", text, dim: true, size: 14);
    }

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
            _status.Visible = !string.IsNullOrWhiteSpace(text);
        }
    }
}
