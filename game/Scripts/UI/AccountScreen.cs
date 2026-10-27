using System;
using System.Globalization;
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
/// account's name, and the signed-in account: name, stats and Sign out (achievements have their own page). A signed-in player's settings
/// and key layouts follow the account (<see cref="AccountService"/>). Without a master there are no accounts.
/// </summary>
public partial class AccountScreen : MenuPage
{
    private const ulong PollIntervalMs = 2000;

    private enum View
    {
        NoMaster,
        SignedOut,
        Waiting,
        PickName,
        SignedIn,
    }

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
    private LineEdit? _nameEdit;

    /// <summary>True while the name picker is a change of an already claimed name (Cancel goes back).</summary>
    private bool _changingName;

    public override void _Ready()
    {
        Name = "Account";
        BuildFrame("Account", 760f);
        _service = AccountService.Get(GetTree());
        _content = new VBoxContainer { Name = "Content" };
        _content.AddThemeConstantOverride("separation", 8);
        Body.AddChild(_content);
        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();

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

        if (_signOut is { IsCompleted: true })
        {
            // Best effort: whatever the master said, this PC forgets the sign-in.
            _signOut = null;
            _service!.SignOutLocally();
            ShowView(View.SignedOut);
            ShowStatus("Signed out.");
        }
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
        if (_start is not null)
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
        if (_signOut is not null)
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
        _view = view;
        if (_content is null)
        {
            return;
        }

        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

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
    }

    private void BuildNoMaster()
    {
        VBoxContainer panel = Panel(_content!, "NoMaster", "Account");
        panel.AddChild(Text("Accounts live on the master server. Set a master server first (Find a Session)."));
    }

    private void BuildSignedOut()
    {
        VBoxContainer panel = Panel(_content!, "SignedOut", "Not signed in");
        panel.AddChild(Text(
            "Sign in with Discord to claim your name, earn achievements and stats, join servers for signed-in players, " +
            "and take your settings and keys to other PCs. You can still play everywhere else without signing in."));
        panel.AddChild(UiStyle.MenuButton("Sign in with Discord", SignIn, "SignIn"));
    }

    private void BuildWaiting()
    {
        VBoxContainer panel = Panel(_content!, "Waiting", "Signing in");
        var code = new Label
        {
            Name = "Code", Text = "Your code: " + (_link?.Code ?? string.Empty), HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiStyle.MenuTitle,
        };
        code.AddThemeFontSizeOverride("font_size", 36);
        panel.AddChild(code);
        panel.AddChild(Text("Your browser opens the sign-in page. Check it shows the same code, then sign in with Discord."));
        panel.AddChild(Text("If the browser did not open, go to:"));
        panel.AddChild(new LineEdit { Name = "Url", Text = _link?.Url ?? string.Empty, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        panel.AddChild(Text("Waiting for Discord..."));
        panel.AddChild(UiStyle.MenuButton("Cancel", CancelSignIn, "CancelSignIn"));
    }

    private void BuildPickName()
    {
        VBoxContainer panel = Panel(_content!, "PickName", _changingName ? "Change name" : "Pick your name");
        panel.AddChild(Text(
            $"The name other players see. {PlayerNames.MinClaimLength} to {PlayerNames.MaxClaimLength} letters, digits, _ - and .; " +
            "nobody else can use it while it is yours."));
        string start = AccountSettings.Current.Name;
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
        var buttons = new HBoxContainer { Name = "NameButtons" };
        buttons.AddChild(UiStyle.MenuButton("Claim", Claim, "Claim"));
        if (_changingName)
        {
            buttons.AddChild(UiStyle.MenuButton("Cancel", () =>
            {
                _changingName = false;
                ShowView(View.SignedIn);
            }, "CancelName"));
        }
        else
        {
            buttons.AddChild(UiStyle.MenuButton("Sign out", SignOut, "SignOut"));
        }

        panel.AddChild(buttons);
        _nameEdit.GrabFocus();
    }

    private void BuildSignedIn()
    {
        AccountSettings s = AccountSettings.Current;
        AccountInfo? info = _service?.Account;
        VBoxContainer panel = Panel(_content!, "SignedIn", "Signed in");
        var name = new Label
        {
            Name = "AccountName", Text = s.Name + (s.Admin ? " (admin)" : string.Empty), Modulate = UiStyle.MenuTitle,
        };
        name.AddThemeFontSizeOverride("font_size", 28);
        panel.AddChild(name);

        var buttons = new HBoxContainer { Name = "AccountButtons" };
        Button change = UiStyle.MenuButton("Change name", () =>
        {
            _changingName = true;
            ShowView(View.PickName);
        }, "ChangeName");
        buttons.AddChild(change);
        buttons.AddChild(UiStyle.MenuButton("Sign out", SignOut, "SignOut"));
        panel.AddChild(buttons);
        if (info?.NameChangeAllowed is { } allowed && allowed > DateTimeOffset.UtcNow)
        {
            change.Disabled = true;
            panel.AddChild(Text("You can change it again on " + allowed.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + "."));
        }

        panel.AddChild(Text("Your settings and key layouts follow this account to other PCs (display and microphone stay on each PC)."));

        VBoxContainer stats = Panel(_content!, "Stats", "Stats");
        if (info is null)
        {
            stats.AddChild(Text("Asking the master server..."));
        }
        else
        {
            AccountStats st = info.Stats;
            stats.AddChild(Text(string.Create(CultureInfo.InvariantCulture,
                $"Matches {st.Matches}    Spy wins {st.SpyWins}    Merc wins {st.MercWins}    Losses {st.Losses}")));
            stats.AddChild(Text(string.Create(CultureInfo.InvariantCulture, $"Kills {st.Kills}    Deaths {st.Deaths}")));
        }
    }

    private static Label Text(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(700f, 0f),
    };

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
