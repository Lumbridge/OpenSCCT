using System;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>A responsive briefing dashboard with direct play routes and live, optional information.</summary>
public partial class MainMenu : MenuPage
{
    private Label? _status, _account, _xpText, _unlockText, _releaseTitle, _releaseNotes, _releaseState;
    private Label? _gameActivity, _hubActivity, _announcement, _announcementState;
    private RankBadge? _rank;
    private UiProgress? _xp, _unlockProgress;
    private Button? _unlockButton;
    private Control? _announcementFrame;
    private MenuDashboardService? _data;
    private IDisposable? _watch;
    private MenuUnlock? _unlock;
    private RejoinService? _rejoin;
    private VBoxContainer? _rejoinPanel;
    private Label? _rejoinText;

    /// <summary>Restore information-card focus after returning from its details.</summary>
    public string? InitialFocus { get; init; }
    public Button? HostButton { get; private set; }
    public Button? FindButton { get; private set; }
    public Button? AccountButton { get; private set; }
    public Button? OptionsButton { get; private set; }
    public Button? QuitButton { get; private set; }
    public Button? RejoinButton { get; private set; }
    public Button? WhatsNewButton { get; private set; }

    public override void _Ready()
    {
        BuildFrame(SplashTrivia.Pick(), 1160f, responsive: true);
        UseSplashTitle();
        Body.AddThemeConstantOverride("separation", 12);
        Control wordmark = UiStyle.Wordmark();
        if (wordmark is UiWordmark mark) mark.MarkSize = 40f;
        wordmark.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        Body.AddChild(wordmark);
        VBoxContainer profile = Column("ProfileColumn", 220f);
        VBoxContainer play = Column("PlayColumn", 400f);
        VBoxContainer news = Column("NewsColumn", 220f);
        var columns = new DashboardColumns
        {
            Name = "Dashboard", Profile = profile, Play = play, News = news,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // Play-first tree order gives keyboard navigation the same priority as a narrow layout.
        columns.AddChild(play);
        columns.AddChild(profile);
        columns.AddChild(news);
        Body.AddChild(columns);
        BuildPlay(play);
        BuildProfile(profile);
        BuildNews(news);
        _status = TextLine(Body, "Status", string.Empty);
        _status.Visible = false;
        OptionsButton = PageButton("Options", () => FrontEnd.ShowOptions(GetTree()), "Options");
        QuitButton = PageButton("Quit", AskQuit, "Quit");
        PageCanvas.AddChild(new BuildCornerLabel
        {
            Name = "BuildInfo", ForceCompact = true, Modulate = UiStyle.Dim,
            HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore,
        });
        _data = MenuDashboardService.Get(GetTree());
        _data.Changed += ShowInformation;
        _watch = _data.Watch();
        ShowInformation();
        if (OnlineService.Get(GetTree()).LastError is { } error) ShowStatus(error);
        _rejoin = RejoinService.Get(GetTree());
        _rejoin.Changed += ShowRejoin;
        ShowRejoin();
        _rejoin.Check();
        if (_rejoin.Offer is null)
        {
            Button? restore = InitialFocus is { } focus ? FindChild(focus, true, false) as Button : null;
            (restore is { Disabled: false } && restore.IsVisibleInTree() ? restore : FindButton)?.GrabFocus();
        }
        else RejoinButton?.GrabFocus();
    }

    private void BuildPlay(VBoxContainer parent)
    {
        _rejoinPanel = Card(parent, "RejoinPanel", "Match in progress");
        _rejoinText = TextLine(_rejoinPanel, "RejoinText", string.Empty);
        RejoinButton = AddButton(_rejoinPanel, "Rejoin", () => _rejoin?.Rejoin(), "Rejoin");
        AddButton(_rejoinPanel, "Not now", () => _rejoin?.Decline(), "RejoinDecline");
        var play = Card(parent, "Online", "Play");
        FindButton = AddButton(play, "Find a Session", () => FrontEnd.ShowServerBrowser(GetTree()), "FindGame");
        FindButton.CustomMinimumSize = new Vector2(0f, 64f);
        FindButton.AddThemeFontSizeOverride("font_size", 24);
        FindButton.Modulate = UiStyle.MenuFocus;
        HostButton = AddButton(play, "Create a Session", () => FrontEnd.ShowHost(GetTree()), "Host");
        HostButton.CustomMinimumSize = new Vector2(0f, 44f);
        AddButton(play, OpenScctGame.Hub.HubScreen.MenuButton(this)).CustomMinimumSize = new Vector2(0f, 44f);
        var collection = Card(parent, "PlayerLinks", "Your collection");
        AccountButton = AddButton(collection, "Account", () => FrontEnd.ShowAccount(GetTree()), "Account");
        var collectionLinks = LinkRow(collection, "CollectionLinks");
        AddButton(collectionLinks, "Stats", () => FrontEnd.ShowStats(GetTree()), "StatsPage");
        AddButton(collectionLinks, AchievementsScreen.MenuButton(this));
        AddButton(collectionLinks, ArmouryScreen.MenuButton(this));
        var explore = Card(parent, "Explore", "Explore");
        var row = LinkRow(explore, "ExploreLinks");
        AddButton(row, TutorialsScreen.MenuButton(this));
        AddButton(row, DemosScreen.MenuButton(this));
        AddButton(row, ContentScreen.MenuButton(this));
    }

    private void BuildProfile(VBoxContainer parent)
    {
        var profile = Card(parent, "YourProgress", "Your progress", compact: true);
        _account = TextLine(profile, "AccountLine", "Not signed in");
        _account.AddThemeFontSizeOverride("font_size", 18);
        _account.MaxLinesVisible = 2;
        _account.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _rank = new RankBadge(30, 16) { Name = "AccountRank", Visible = false };
        profile.AddChild(_rank);
        _xp = new UiProgress { Name = "LevelProgress", Fill = UiStyle.MenuFocus, Visible = false };
        profile.AddChild(_xp);
        _xpText = TextLine(profile, "XpText", "Sign in to track your rank and unlocks.", dim: true, small: true);
        _unlockText = TextLine(profile, "NextUnlockText", string.Empty, small: true);
        _unlockText.MaxLinesVisible = 3;
        _unlockText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _unlockProgress = new UiProgress { Name = "UnlockProgress", Fill = UiStyle.MenuFocus, Visible = false };
        profile.AddChild(_unlockProgress);
        _unlockButton = AddCompactButton(profile, "View next unlock", () => FrontEnd.Show(GetTree(),
            new AchievementsScreen { InitialAchievement = _unlock?.AchievementId }), "NextUnlock");
        var activity = Card(parent, "CommunityActivity", "Community activity", compact: true);
        _gameActivity = TextLine(activity, "GameActivity", "Checking game sessions…", small: true);
        _hubActivity = TextLine(activity, "HubActivity", "Checking hubs…", small: true);
        TextLine(activity, "ActivityScope", "Players in publicly listed sessions", dim: true, small: true);
    }

    private void BuildNews(VBoxContainer parent)
    {
        var news = Card(parent, "WhatsNewPanel", "What’s new", compact: true);
        _releaseTitle = TextLine(news, "LatestRelease", "Latest update");
        _releaseTitle.AddThemeFontSizeOverride("font_size", 18);
        _releaseNotes = TextLine(news, "ReleasePreview", "Checking release notes…");
        _releaseNotes.AddThemeFontSizeOverride("font_size", 16);
        _releaseNotes.MaxLinesVisible = 4;
        _releaseNotes.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _releaseState = TextLine(news, "ReleaseState", string.Empty, dim: true, small: true);
        WhatsNewButton = AddCompactButton(news, "Read recent changes", () => DashboardDetailsScreen.ShowChangelog(GetTree()), "WhatsNew");
        var preview = (Control)news.GetParent();
        preview.MouseDefaultCursorShape = CursorShape.PointingHand;
        preview.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            GetViewport().SetInputAsHandled();
            DashboardDetailsScreen.ShowChangelog(GetTree());
        };
        var notice = Card(parent, "AnnouncementPanel", "Announcement", compact: true);
        _announcementFrame = (Control)notice.GetParent();
        _announcement = TextLine(notice, "AnnouncementPreview", string.Empty, small: true);
        _announcement.MaxLinesVisible = 2;
        _announcement.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _announcementState = TextLine(notice, "AnnouncementState", string.Empty, dim: true, small: true);
        AddCompactButton(notice, "Read announcement", () => DashboardDetailsScreen.ShowAnnouncement(GetTree()), "ReadAnnouncement");
        _announcementFrame.Visible = false;
    }

    private void ShowInformation()
    {
        if (_data is null) return;
        bool signedIn = _data.SignedIn;
        AccountInfo? account = signedIn ? _data.Account : null;
        _account!.Text = signedIn ? account?.Name is { Length: > 0 } name ? name : "Signed in" : "Welcome, agent";
        AccountButton!.Text = signedIn ? "Manage account" : "Sign in / Account";
        PlayerRank rank = RankBadge.OfAccount(account);
        _rank!.Show(rank, withTitle: true);
        _xp!.Visible = !rank.IsNone;
        _xp.Value = rank.Fraction;
        _xpText!.Text = !signedIn ? "Sign in to track your rank and unlocks." : rank.IsNone ? "Progress is currently unavailable."
            : rank.Level >= ProgressionK.MaxLevel ? "Maximum level reached for this prestige"
            : string.Create(CultureInfo.InvariantCulture, $"{rank.XpIntoLevel:N0} / {rank.XpForNext:N0} XP to level {rank.Level + 1}");
        if (signedIn && account is not null && _data.AccountStale) _xpText.Text += " · Last received";
        _unlock = MenuDashboardModel.NextUnlock(account);
        _unlockText!.Visible = _unlock is not null;
        _unlockProgress!.Visible = _unlock is not null;
        if (_unlock is { } unlock)
        {
            _unlockText.Text = $"Next unlock · {unlock.RewardName}\n{unlock.Title} · {unlock.Current:N0} / {unlock.Target:N0}";
            _unlockProgress.Value = unlock.Fraction;
        }
        _unlockButton!.Visible = _unlock is not null;
        _gameActivity!.Text = Activity(_data.Games, ServerKind.Game, "game session", "game sessions");
        _hubActivity!.Text = Activity(_data.Hubs, ServerKind.Hub, "hub", "hubs");
        ChangelogEntry? latest = _data.Changelog.Value?.Entries.FirstOrDefault();
        bool unavailable = _data.Changelog.Unavailable || _data.Changelog.Value?.Unavailable == true;
        _releaseTitle!.Text = latest is null ? "Latest update" : latest.Tag;
        _releaseNotes!.Text = latest is not null ? MenuDashboardModel.Preview(latest.Notes)
            : _data.Changelog.Loading ? "Checking release notes…" : unavailable ? "Release notes are currently unavailable." : "No published updates yet.";
        if (latest is not null && string.IsNullOrWhiteSpace(_releaseNotes.Text)) _releaseNotes.Text = "No release notes were provided for this version.";
        _releaseState!.Text = latest is not null ? latest.Published.ToString("d MMM yyyy", CultureInfo.InvariantCulture) +
            (unavailable ? " · Last received" : string.Empty) : "Read the latest features, improvements and fixes.";
        _announcement!.Text = _data.Announcement;
        _announcementState!.Text = _data.Status.Stale ? "Last received · Service unavailable" : string.Empty;
        _announcementState.Visible = _data.Status.Stale;
        bool hasAnnouncement = !string.IsNullOrWhiteSpace(_data.Announcement);
        if (!hasAnnouncement && GetViewport().GuiGetFocusOwner() is { } owner && _announcementFrame!.IsAncestorOf(owner)) FindButton?.GrabFocus();
        _announcementFrame!.Visible = hasAnnouncement;
    }

    private static string Activity(MenuDashboardFeed<ServerListResponse> feed, ServerKind kind, string singular, string plural)
    {
        if (feed.Value is not { } list) return feed.Loading ? $"Checking {plural}…" : $"{char.ToUpperInvariant(plural[0])}{plural[1..]} unavailable";
        MenuActivity activity = MenuDashboardModel.Activity(list, kind);
        return $"{activity.Players:N0} players · {activity.Sessions:N0} {(activity.Sessions == 1 ? singular : plural)}" + (feed.Stale ? " · Last received" : string.Empty);
    }

    private static VBoxContainer Column(string name, float minimum) => new()
    {
        Name = name, CustomMinimumSize = new Vector2(minimum, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private static VBoxContainer Card(Container parent, string name, string title, bool compact = false)
    {
        var column = Panel(parent, name, title);
        column.AddThemeConstantOverride("separation", compact ? 4 : 6);
        parent.AddThemeConstantOverride("separation", compact ? 10 : 12);
        return column;
    }

    private static HFlowContainer LinkRow(Container parent, string name)
    {
        var row = new HFlowContainer { Name = name };
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 8);
        parent.AddChild(row);
        return row;
    }

    private static Label TextLine(Container parent, string name, string text, bool dim = false, bool small = false)
    {
        var label = new Label
        {
            Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = dim ? UiStyle.Dim : UiStyle.Text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (small) label.AddThemeFontSizeOverride("font_size", 14);
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Container parent, string text, Action action, string name) => AddButton(parent, UiStyle.MenuButton(text, action, name));

    private static Button AddCompactButton(Container parent, string text, Action action, string name)
    {
        Button button = AddButton(parent, text, action, name);
        button.CustomMinimumSize = new Vector2(0f, 32f);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

    private static Button AddButton(Container parent, Button button)
    {
        button.CustomMinimumSize = new Vector2(0f, 38f);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(button);
        return button;
    }

    public override void _ExitTree()
    {
        if (_data is not null) _data.Changed -= ShowInformation;
        _watch?.Dispose();
        if (_rejoin is not null) _rejoin.Changed -= ShowRejoin;
    }

    [OpenScct.Provenance.PortOf("uc:GUI.ConfirmQuitPage:YesButtonClick", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    [OpenScct.Provenance.PortOf("uc:GUI.ConfirmQuitPage:NoButtonClick", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    private void AskQuit()
    {
        const string section = "ConfirmQuitPage";
        ConfirmBox.Open(PageCanvas, new ConfirmBox
        {
            Name = "ConfirmQuit",
            Question = OpenScctGame.Tutorials.TutorialText.Lines(
                OpenScctGame.Tutorials.TutorialText.Caption(section, "LabelCaption", "Are you sure|you want to quit?")),
            NoCaption = OpenScctGame.Tutorials.TutorialText.Caption(section, "sBT_Cancel", "Cancel"),
            YesCaption = OpenScctGame.Tutorials.TutorialText.Caption(section, "sBT_Yes", "Quit Game").Trim(),
            Yes = () => GetTree().Quit(), No = () => QuitButton?.GrabFocus(),
        });
    }

    private void ShowRejoin()
    {
        if (_rejoinPanel?.GetParent() is not Control frame || _rejoinText is null) return;
        RejoinChoice? offer = _rejoin?.Offer;
        if (offer is null && GetViewport().GuiGetFocusOwner() is { } owner && frame.IsAncestorOf(owner)) FindButton?.GrabFocus();
        frame.Visible = offer is not null;
        if (offer is null) return;
        string map = offer.Map.Length > 0 ? " on " + offer.Map : string.Empty;
        _rejoinText.Text = $"Your round on {offer.ServerName}{map} is still in progress.";
    }

    public void ShowStatus(string text)
    {
        if (_status is null) return;
        _status.Text = text;
        _status.Visible = !string.IsNullOrWhiteSpace(text);
    }
}
