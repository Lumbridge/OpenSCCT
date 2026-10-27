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
    private AccountService? _accounts;

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

    public override void _Ready()
    {
        BuildFrame("Enhanced Versus", 520f);
        Body.AddChild(UiStyle.Wordmark());

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
        online.AddChild(ArmouryScreen.MenuButton(this));
        _account = new Label { Name = "AccountLine", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Center };
        online.AddChild(_account);
        _accounts = AccountService.Get(GetTree());
        _accounts.Changed += ShowAccount;
        ShowAccount();

        OptionsButton = UiStyle.MenuButton("Profile Options", () => FrontEnd.ShowOptions(GetTree()), "Options");
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
    }

    public override void _ExitTree()
    {
        if (_accounts is not null)
        {
            _accounts.Changed -= ShowAccount;
        }
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
