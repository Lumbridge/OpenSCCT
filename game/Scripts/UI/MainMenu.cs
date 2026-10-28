using System;
using Godot;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The main menu in the original's multiplayer menu (Menu_Multi, "Enhanced Versus") and LAN Session page (LAN_Menu):
/// Create a Session (host; also how to play offline), Find a Session (the server browser: LAN, saved addresses and the
/// master's list), Join with IP, Options, Quit (docs/MENUS.md).
/// </summary>
public partial class MainMenu : MenuPage
{
    private Label? _status;
    private Label? _account;
    private RankBadge? _rank;
    private AccountService? _accounts;
    private RejoinService? _rejoin;
    private VBoxContainer? _rejoinPanel;
    private Label? _rejoinText;

    /// <summary>Create a Session.</summary>
    public Button? HostButton { get; private set; }

    /// <summary>Join with IP.</summary>
    public Button? JoinButton { get; private set; }

    /// <summary>Find a Session (the server browser).</summary>
    public Button? FindButton { get; private set; }

    /// <summary>The Account page.</summary>
    public Button? AccountButton { get; private set; }

    public Button? OptionsButton { get; private set; }

    public Button? QuitButton { get; private set; }

    /// <summary>Rejoin, on the panel shown while a round the player dropped out of is still being played.</summary>
    public Button? RejoinButton { get; private set; }

    public override void _Ready()
    {
        // The original's title here is "Enhanced Versus"; ours is a Splinter Cell fact, a new one each time.
        BuildFrame(SplashTrivia.Pick(), 520f);
        UseSplashTitle();
        Body.AddChild(UiStyle.Wordmark());

        // A round the player dropped out of (a crash, a lost connection, a quit) that is still being played.
        _rejoinPanel = Panel(Body, "RejoinPanel", "Match in progress");
        _rejoinText = new Label
        {
            Name = "RejoinText", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _rejoinPanel.AddChild(_rejoinText);
        RejoinButton = UiStyle.MenuButton("Rejoin", () => _rejoin?.Rejoin(), "Rejoin");
        _rejoinPanel.AddChild(RejoinButton);
        _rejoinPanel.AddChild(UiStyle.MenuButton("Not now", () => _rejoin?.Decline(), "RejoinDecline"));

        // LAN_Menu: "Create a Session", "Find a Session"; JoinWithIp.
        VBoxContainer online = Panel(Body, "Online", "LAN and internet");
        HostButton = UiStyle.MenuButton("Create a Session", () => FrontEnd.ShowHost(GetTree()), "Host");
        FindButton = UiStyle.MenuButton("Find a Session", () => FrontEnd.ShowServerBrowser(GetTree()), "FindGame");
        JoinButton = UiStyle.MenuButton("Join with IP", () => FrontEnd.ShowJoinWithIp(GetTree()), "Join");
        online.AddChild(HostButton);
        online.AddChild(FindButton);
        online.AddChild(JoinButton);
        online.AddChild(OpenScctGame.Hub.HubScreen.MenuButton(this));
        AccountButton = UiStyle.MenuButton("Account", () => FrontEnd.ShowAccount(GetTree()), "Account");
        online.AddChild(AccountButton);
        online.AddChild(AchievementsScreen.MenuButton(this));
        online.AddChild(ArmouryScreen.MenuButton(this));
        online.AddChild(DemosScreen.MenuButton(this));
        _account = new Label { Name = "AccountLine", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Center };
        online.AddChild(_account);
        _rank = new RankBadge(28) { Name = "AccountRank", SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Visible = false };
        online.AddChild(_rank);
        _accounts = AccountService.Get(GetTree());
        _accounts.Changed += ShowAccount;
        ShowAccount();

        OptionsButton = UiStyle.MenuButton("Options", () => FrontEnd.ShowOptions(GetTree()), "Options");
        Body.AddChild(OptionsButton);
        _status = new Label
        {
            Name = "Status", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Body.AddChild(_status);

        QuitButton = PageButton("Quit", () => GetTree().Quit(), "Quit");
        if (OnlineService.Get(GetTree()).LastError is { } error)
        {
            ShowStatus(error);
        }

        AddChild(BuildLabel.Corner());
        HostButton.GrabFocus();
        _rejoin = RejoinService.Get(GetTree());
        _rejoin.Changed += ShowRejoin;
        ShowRejoin();
        _rejoin.Check();
    }

    public override void _ExitTree()
    {
        if (_accounts is not null)
        {
            _accounts.Changed -= ShowAccount;
        }

        if (_rejoin is not null)
        {
            _rejoin.Changed -= ShowRejoin;
        }
    }

    /// <summary>The Match in progress panel: shown with the round's server and map while the rejoin is offered.</summary>
    private void ShowRejoin()
    {
        if (_rejoinPanel?.GetParent() is not Control frame || _rejoinText is null)
        {
            return;
        }

        RejoinChoice? offer = _rejoin?.Offer;
        frame.Visible = offer is not null;
        if (offer is null)
        {
            return;
        }

        string map = offer.Map.Length > 0 ? " on " + offer.Map : string.Empty;
        _rejoinText.Text = $"You left a round on {offer.ServerName}{map} that is still being played. Rejoin to play on with your side.";
        RejoinButton?.GrabFocus();
    }

    /// <summary>The Online panel's line: who is signed in.</summary>
    private void ShowAccount()
    {
        if (_account is null)
        {
            return;
        }

        AccountSettings a = AccountSettings.Current;
        _account.Text = !a.SignedIn ? "Not signed in" : a.Name.Length > 0 ? "Signed in as " + a.Name : "Signed in (no name yet)";
        _rank?.Show(a.SignedIn ? RankBadge.OfAccount(_accounts?.Account) : OpenScct.Net.Cosmetics.PlayerRank.None, withTitle: true);
    }

    /// <summary>A line under the buttons (why the last online visit ended, a bad address).</summary>
    public void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
