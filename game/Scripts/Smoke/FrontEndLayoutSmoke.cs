using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Hud;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Resize the same front-end pages across small, standard, ultrawide and 4K windows.</summary>
public sealed class FrontEndLayoutSmoke : SmokeTest
{
    public const string TestName = "front-end-layout";
    public override string Name => TestName;

    private static readonly Vector2I[] Sizes =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160), new(800, 600),
    ];

    private static readonly (string Name, Func<Control> Create, bool List)[] Pages = SelectPages(
    [
        ("main", () => new MainMenu(), false),
        ("confirmation", () => new MainMenu(), false),
        ("sessions", () => new ServerBrowserScreen(), false),
        ("join-ip", () => new JoinWithIpScreen(), false),
        ("account", () => new AccountScreen(), false),
        ("account-no-master", () => AccountPreview("no-master"), false),
        ("account-signed-out", () => AccountPreview("signed-out"), false),
        ("account-waiting", () => AccountPreview("waiting"), false),
        ("account-pick-name", () => AccountPreview("pick-name"), false),
        ("account-change-name", () => AccountPreview("change-name"), false),
        ("account-signed-in", () => AccountPreview("signed-in"), false),
        ("account-locked-titles", () => AccountPreview("new"), false),
        ("account-actions", () => AccountPreview("actions"), false),
        ("account-loading", () => AccountPreview("loading"), false),
        ("account-legacy", () => AccountPreview("legacy"), false),
        ("account-prestige", () => AccountPreview("prestige"), false),
        ("account-prestige-confirm", () => AccountPreview("prestige-confirm"), false),
        ("account-master", () => AccountPreview("master"), false),
        ("account-new", () => AccountPreview("new"), false),
        ("stats", () => new StatsScreen(), false),
        ("content", () => new ContentScreen(), false),
        ("armoury", () => new ArmouryScreen(), false),
        ("armoury-merc", () => new ArmouryScreen(), false),
        ("armoury-pistol", () => new ArmouryScreen(), false),
        ("achievements", () => new AchievementsScreen(), false),
        ("achievements-maps", () => new AchievementsScreen(), false),
        ("tutorials", () => new TutorialsScreen(), false),
        ("tips", () => new TipsScreen(), false),
        ("tip-detail", () => new TipsScreen { Tip = 0 }, false),
        ("tips-merc", () => new TipsScreen { Team = 1 }, false),
        ("tip-detail-merc", () => new TipsScreen { Team = 1, Tip = 0 }, false),
        ("gadgets", () => new GadgetDetailsScreen(), false),
        ("gadget-detail", () => new GadgetDetailsScreen { Gadget = 0 }, false),
        ("gadgets-merc", () => new GadgetDetailsScreen { Team = 1 }, false),
        ("gadget-detail-merc", () => new GadgetDetailsScreen { Team = 1, Gadget = 0 }, false),
        ("tutorial-maps", () => new TutorialMapsScreen(), false),
        ("tutorial-map-list", () => new TutorialMapsScreen(), true),
        ("tutorial-maps-merc", () => new TutorialMapsScreen { Team = 1 }, false),
        ("tutorial-map-list-merc", () => new TutorialMapsScreen { Team = 1 }, true),
        ("visit-map", () => new VisitMapScreen(), false),
        ("visit-map-list", () => new VisitMapScreen(), true),
        ("strategic-map", () => new StrategicMapScreen(StrategicMapData.Empty), false),
        ("loading", () => new LoadingScreen { Map = "AquaE" }, false),
        ("spy-hud-legend", () => new HudLegendScreen { Page = HudLegendPage.SpyHud }, false),
        ("merc-hud-legend", () => new HudLegendScreen { Page = HudLegendPage.MercHud }, false),
        ("keyboard-legend-1", () => new HudLegendScreen { Page = HudLegendPage.Keyboard1 }, false),
        ("keyboard-legend-2", () => new HudLegendScreen { Page = HudLegendPage.Keyboard2 }, false),
        ("results", () => Results(false), false),
        ("deathmatch-results", () => Results(true), false),
    ]);

    private readonly List<string> _problems = new();
    private Control? _screen;
    private int _page, _size, _frames;
    private int _accountAction;
    private string? _savedTitle;
    private bool _savedList;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_screen is null)
        {
            if (_page == 0) _savedList = SettingsStore.Current.MapListView;
            SettingsStore.Current.MapListView = Pages[_page].List;
            Resize(context.Tree, Sizes[_size]);
            _screen = FrontEnd.Show(context.Tree, Pages[_page].Create());
            string? collectionTab = Pages[_page].Name switch
            {
                "armoury-merc" => "SideMerc",
                "armoury-pistol" => "SlotTabPistol",
                "achievements-maps" => "FilterMaps",
                _ => null,
            };
            if (collectionTab is not null)
            {
                if (_screen.FindChild(collectionTab, true, false) is Button tab)
                {
                    tab.ButtonPressed = true;
                    tab.EmitSignal(BaseButton.SignalName.Pressed);
                }
                else _problems.Add("Missing collection tab: " + collectionTab);
            }
            if (Pages[_page].Name == "account-locked-titles" && _screen.FindChild("BrowseTitles", true, false) is Button browse)
                browse.ButtonPressed = true;
            if (Pages[_page].Name == "account-actions") _savedTitle = SettingsStore.Current.Title;
            if (Pages[_page].Name == "confirmation")
                ConfirmBox.Open(_screen, new ConfirmBox
                {
                    Question = "Discard the changes to your match settings and return to the lobby?",
                    YesCaption = "Discard changes", NoCaption = "Keep editing",
                });
            _frames = 0;
        }

        if (++_frames < 18) return SmokeResult.Running;
        Vector2I size = Sizes[_size];
        string where = $"{Pages[_page].Name} {size.X}x{size.Y}";
        _problems.AddRange(LayoutCheck.BoundsProblems(_screen, size).Select(p => where + ": " + p));
        if (Pages[_page].Name == "armoury-pistol" && _screen.FindChild("SlotPistol", true, false) is null)
            _problems.Add(where + ": pistol tab did not show its equipment slot");
        if (Pages[_page].Name == "armoury-merc" && _screen.FindChild("SlotTabGunAr", true, false) is null)
            _problems.Add(where + ": mercenary tab did not show its weapon slots");
        if (Pages[_page].Name == "achievements-maps" &&
            (_screen.FindChild("HeaderMap", true, false) is null || _screen.FindChild("HeaderGeneral", true, false) is not null))
            _problems.Add(where + ": map filter did not restrict achievement groups");
        // The main menu's optional splash line intentionally ellipsizes. Content must remain readable.
        Node content = _screen.FindChild("Body", true, false) ?? _screen;
        _problems.AddRange(LayoutCheck.Problems(content, size.X).Select(p => where + ": " + p));
        foreach (var button in (_screen is MenuPage page ? page.PageButtons : Array.Empty<Button>()).Where(b => b.IsVisibleInTree()))
        {
            if (!new Rect2(Vector2.Zero, size).Encloses(button.GetGlobalRect()))
                _problems.Add(where + ": footer action outside the viewport: " + button.Name);
        }
        if (_screen is AchievementsScreen)
        {
            foreach (var node in _screen.FindChildren("Achievement_*", nameof(Button), true, false))
            {
                if (node is not Button tile || !tile.IsVisibleInTree()) continue;
                if (tile.FindChild("Title", true, false) is not Label title || string.IsNullOrWhiteSpace(title.Text) ||
                    title.Size.Y < 20f || !tile.GetGlobalRect().Encloses(title.GetGlobalRect()))
                    _problems.Add(where + ": achievement title missing or outside its card: " + tile.Name);
            }
        }
        if (Pages[_page].Name == "account-actions" && _screen is AccountScreen actionScreen)
        {
            MenuPage.Shot(_screen, $"front-end-account-actions-{size.X}x{size.Y}-{_accountAction}");
            if (!CheckAccountActions(actionScreen, where))
            {
                _frames = 0;
                return SmokeResult.Running;
            }
        }
        else if (_screen is AccountScreen { Preview: { } preview } account)
            CheckAccountPreview(account, preview, where);
        MenuPage.Shot(_screen, $"front-end-{Pages[_page].Name}-{size.X}x{size.Y}");

        if (++_size < Sizes.Length)
        {
            Resize(context.Tree, Sizes[_size]);
            _accountAction = 0;
            _frames = 0;
            return SmokeResult.Running;
        }

        GD.Print($"SVM_FRONT_END_LAYOUT {Pages[_page].Name} checked {Sizes.Length} sizes");
        _screen.QueueFree();
        _screen = null;
        _size = 0;
        if (++_page < Pages.Length) return SmokeResult.Running;
        SettingsStore.Current.MapListView = _savedList;
        return _problems.Count == 0
            ? SmokeResult.Pass($"{Pages.Length} pages at 9 resolutions, including resize back to 800x600")
            : SmokeResult.Fail(string.Join("; ", _problems.Distinct().Take(25)));
    }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        tree.Root.Size = size;
    }

    private static ResultsScreen Results(bool deathmatch) => new()
    {
        Result = new MatchResult
        {
            Reason = MatchEndReason.Aborted,
            Mode = deathmatch ? OpenScct.Core.EGameMode.GM_MercDeathMatch : OpenScct.Core.EGameMode.GM_Multi,
            Rows = Enumerable.Range(0, 8).Select(i => new MatchResultRow(
                "Player_with_a_long_display_name_" + i, i % 2 == 0 ? HudSide.Spy : HudSide.Merc,
                100 + i, i == 0 ? 20 : 12, 3, 2, i == 0)).ToList(),
        },
    };

    // Fixture data is passed to the page directly; it never signs in, polls or changes saved account settings.
    private static AccountScreen AccountPreview(string state)
    {
        var progress = new ProgressInfo
        {
            Level = 42, BestLevel = 55, Prestige = 3,
            Xp = PlayerRank.XpToReach(42) + 375, TotalXp = 123456789,
            Titles = TitleCatalog.All.Where(title => title.Source != TitleSource.Prestige || title.Value <= 3)
                .Select(title => title.Id).ToArray(),
        };
        var account = new AccountInfo
        {
            Id = 1, Name = "WideNameWWWWWWWW", Admin = true,
            NameChangeAllowed = DateTimeOffset.UtcNow.AddDays(30),
            Stats = new AccountStats
            {
                Matches = 123456, SpyWins = 45678, MercWins = 34567, Losses = 43211,
                Kills = 123456789, Deaths = 98765432,
            },
            Progress = progress,
        };
        AccountScreen.PreviewState preview = state switch
        {
            "no-master" => new(AccountScreen.View.NoMaster),
            "signed-out" => new(AccountScreen.View.SignedOut,
                Status: "The master server could not be reached. Check your connection and try signing in again."),
            "waiting" => new(AccountScreen.View.Waiting, Link: new LinkStartResponse
            {
                Code = "K7PX-29QD", Url = "https://accounts.example.invalid/sign-in/confirm?code=K7PX-29QD&source=OpenSCCT",
                ExpiresSeconds = 600,
            }),
            "pick-name" => new(AccountScreen.View.PickName, account with { Name = null, Admin = false }),
            "change-name" => new(AccountScreen.View.PickName, account with { NameChangeAllowed = null }, ChangingName: true),
            "loading" => new(AccountScreen.View.SignedIn),
            "legacy" => new(AccountScreen.View.SignedIn, account with { Progress = null, NameChangeAllowed = null }),
            "prestige" or "prestige-confirm" or "actions" => new(AccountScreen.View.SignedIn, account with
            {
                NameChangeAllowed = state == "actions" ? null : account.NameChangeAllowed,
                Progress = progress with { Level = ProgressionK.MaxLevel, Xp = PlayerRank.XpToReach(ProgressionK.MaxLevel) },
            }, PrestigeArmed: state == "prestige-confirm"),
            "master" => new(AccountScreen.View.SignedIn, account with
            {
                Progress = progress with
                {
                    Level = ProgressionK.MaxLevel, Prestige = ProgressionK.MaxPrestige,
                    Xp = PlayerRank.XpToReach(ProgressionK.MaxLevel), Titles = TitleCatalog.All.Select(title => title.Id).ToArray(),
                },
            }),
            "new" => new(AccountScreen.View.SignedIn, new AccountInfo
            {
                Id = 2, Name = "New_player", Progress = new ProgressInfo(),
            }),
            _ => new(AccountScreen.View.SignedIn, account),
        };
        return new AccountScreen { Preview = preview };
    }

    private void CheckAccountPreview(AccountScreen screen, AccountScreen.PreviewState preview, string where)
    {
        string[] labels = screen.FindChildren("*", nameof(Label), true, false).OfType<Label>()
            .Where(label => label.IsVisibleInTree()).Select(label => label.Text).ToArray();
        if (preview.View == AccountScreen.View.SignedIn && preview.Account?.Name is { } name &&
            !labels.Any(text => text.Contains(name, StringComparison.Ordinal)))
            _problems.Add(where + ": signed-in view lost the player name");
        if (preview.View == AccountScreen.View.Waiting && preview.Link is { } link &&
            (!labels.Any(text => text.Contains(link.Code, StringComparison.Ordinal)) ||
             !screen.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().Any(field => field.Text == link.Url)))
            _problems.Add(where + ": waiting view lost its sign-in code or fallback URL");
        if (preview.View == AccountScreen.View.PickName &&
            !screen.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().Any(field => field.Editable))
            _problems.Add(where + ": name picker has no editable field");
        if (preview.Status is { Length: > 0 } status && !labels.Contains(status))
            _problems.Add(where + ": account status was lost");
        if (Pages[_page].Name == "account-locked-titles")
        {
            var locked = screen.FindChild("LockedTitles", true, false) as Container;
            if (locked is null || !locked.IsVisibleInTree() || locked.GetChildCount() != TitleCatalog.All.Count ||
                locked.GetChildren().OfType<Button>().Any(button => !button.Disabled))
                _problems.Add(where + ": locked-title disclosure lost locked titles or allowed equipping them");
        }
    }

    private bool CheckAccountActions(AccountScreen screen, string where)
    {
        void Require(bool condition, string message)
        {
            if (!condition) _problems.Add(where + $" action {_accountAction}: " + message);
        }
        Button? Button(string name) => screen.FindChild(name, true, false) as Button;
        void Press(string name)
        {
            Button? button = Button(name);
            Require(button is { Disabled: false }, "missing enabled action " + name);
            if (button is not { Disabled: false }) return;
            button.GrabFocus();
            if (button.ToggleMode) button.ButtonPressed = true;
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        void FocusIs(string name) => Require(screen.GetViewport().GuiGetFocusOwner()?.Name.ToString() == name,
            "focus should remain on " + name);

        switch (_accountAction++)
        {
            case 0:
                Press("Prestige");
                break;
            case 1:
                Require(Button("Prestige")?.Text == "Confirm prestige" && Button("CancelPrestige") is not null,
                    "prestige did not show explicit confirmation and cancel");
                FocusIs("Prestige");
                Press("CancelPrestige");
                break;
            case 2:
                Require(Button("Prestige")?.Text.StartsWith("Enter prestige", StringComparison.Ordinal) == true && Button("CancelPrestige") is null,
                    "cancel did not disarm prestige");
                FocusIs("Prestige");
                Press("BrowseTitles");
                break;
            case 3:
                Require(screen.FindChild("LockedTitles", true, false) is Control { Visible: true }, "locked titles did not expand");
                if (Button("BrowseTitles") is { } browse) browse.ButtonPressed = false;
                break;
            case 4:
                Require(screen.FindChild("LockedTitles", true, false) is Control { Visible: false }, "locked titles did not collapse");
                FocusIs("BrowseTitles");
                Press("Title_level_10");
                break;
            case 5:
                Require(screen.FindChild("TitleSummary", true, false) is Label { Text: "Recruit" } && Button("Title_level_10") is { ButtonPressed: true },
                    "equipping a title did not update its summary and selection");
                Require(SettingsStore.Current.Title == _savedTitle, "preview title changed saved settings");
                Press("ChangeName");
                break;
            case 6:
                Require(screen.FindChild("Name", true, false) is LineEdit { Editable: true } field && field.Text == screen.Preview?.Account?.Name,
                    "change-name action did not retain the current name");
                FocusIs("Name");
                Press("CancelName");
                break;
            default:
                Require(screen.FindChild("AccountName", true, false) is Label player && player.Text == screen.Preview?.Account?.Name,
                    "cancelling the name change lost the signed-in view");
                FocusIs("ChangeName");
                Require(SettingsStore.Current.Title == _savedTitle, "preview interactions changed saved settings");
                return true;
        }
        return false;
    }

    // A focused repeat or screenshot capture still uses the same resize assertions as the full suite.
    private static (string Name, Func<Control> Create, bool List)[] SelectPages(
        (string Name, Func<Control> Create, bool List)[] pages)
    {
        string? selection = System.Environment.GetEnvironmentVariable("SVM_FRONT_END_LAYOUT_PAGES");
        if (string.IsNullOrWhiteSpace(selection)) return pages;
        var names = selection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = pages.Where(page => names.Contains(page.Name) || names.Any(name =>
            name.EndsWith('*') && page.Name.StartsWith(name[..^1], StringComparison.OrdinalIgnoreCase))).ToArray();
        return selected.Length > 0 ? selected : throw new InvalidOperationException("No matching front-end layout pages: " + selection);
    }
}
