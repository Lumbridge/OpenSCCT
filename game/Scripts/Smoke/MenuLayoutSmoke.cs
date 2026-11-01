using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=menu-layout</c>: the dashboard and its details, options and Create Session across small,
/// 4:3, 16:10, widescreen, ultrawide and 4K
/// viewports, checked for unreadable text and off-screen controls, and saved with <c>SVM_PLAY_SHOTS</c>.
/// </summary>
public sealed class MenuLayoutSmoke : SmokeTest
{
    public const string TestName = "menu-layout";

    private const int SettleFrames = 6;
    private static readonly string[] Destinations =
    [
        "FindGame", "Host", "Hub", "Account", "StatsPage", "AchievementsPage",
        "Armoury", "Demos", "Tutorials", "Content", "Options", "Quit",
    ];
    private readonly Vector2I[] _sizes =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160),
    ];

    private readonly List<string> _problems = new();
    private readonly List<string> _seen = new();
    private OptionsMenu? _options;
    private MainMenu? _resizingMain;
    private int _stage;
    private int _tab;
    private int _frames;
    private int _size;
    private bool _savedMapList;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SceneTree tree = context.Tree;
        float width = tree.Root.GetVisibleRect().Size.X;
        _frames++;
        switch (_stage)
        {
            case 0:
                if (_size == 0) _savedMapList = SettingsStore.Current.MapListView;
                if (DisplayServer.GetName() != "headless")
                {
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                    DisplayServer.WindowSetSize(_sizes[_size]);
                }
                tree.Root.Size = _sizes[_size];

                FrontEnd.ShowMainMenu(tree);
                _stage = 6;
                _frames = 0;
                return SmokeResult.Running;
            case 1:
                if (_options?.Tabs is not { } tabs)
                {
                    return _frames > 120 ? SmokeResult.Fail("the options screen has no tabs") : SmokeResult.Running;
                }

                if (_frames == 1)
                {
                    tabs.CurrentTab = _tab;
                }

                if (_frames < SettleFrames)
                {
                    return SmokeResult.Running;
                }

                string title = tabs.GetTabTitle(_tab);
                Check(_options, width, "Options " + title);
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-options-{_tab}-{title}");
                _frames = 0;
                if (++_tab >= tabs.GetTabCount())
                {
                    _options.QueueFree();
                    _stage = 2;
                }

                return SmokeResult.Running;
            case 2:
                SettingsStore.Current.MapListView = false;
                FrontEnd.ShowHost(tree);
                _stage = 3;
                _frames = 0;
                return SmokeResult.Running;
            case 3:
                if (_frames < SettleFrames * 3)
                {
                    return SmokeResult.Running;
                }

                Check(tree.CurrentScene!, width, "Create Session");
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-create-session");
                if (tree.CurrentScene!.FindChild("MapPicker", true, false) is Control picker)
                {
                    for (Node? parent = picker.GetParent(); parent is not null; parent = parent.GetParent())
                        if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(picker);
                }
                _stage = 4;
                _frames = 0;
                return SmokeResult.Running;
            case 4:
                if (_frames < SettleFrames) return SmokeResult.Running;
                if (tree.CurrentScene!.FindChild("MapGrid", true, false) is Control grid)
                {
                    foreach (Node child in grid.GetChildren())
                    {
                        if (child is not Button tile || !tile.Visible || tile.FindChild("Title", true, false) is not Label label) continue;
                        if (string.IsNullOrWhiteSpace(label.Text) || label.Size.Y < 24f || !tile.GetGlobalRect().Encloses(label.GetGlobalRect()))
                            _problems.Add("Map title missing or outside its card: " + tile.Name);
                    }
                }
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-map-titles");
                SettingsStore.Current.MapListView = true;
                _stage = 5;
                _frames = 0;
                return SmokeResult.Running;
            case 5:
                if (_frames < SettleFrames) return SmokeResult.Running;
                if (tree.CurrentScene!.FindChild("MapGrid", true, false) is MapBrowserGrid list)
                {
                    if (list.Columns != 1) _problems.Add("Map list has more than one column");
                    foreach (Node child in list.GetChildren())
                    {
                        if (child is not Button tile || !tile.Visible) continue;
                        if (string.IsNullOrWhiteSpace(tile.Text) || tile.Size.Y > 48 || tile.Size.X < list.Size.X - 20)
                            _problems.Add("Map list row is not compact/full-width: " + tile.Name);
                    }
                }
                Check(tree.CurrentScene!, width, "Create Session list");
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-map-list");
                SettingsStore.Current.MapListView = _savedMapList;
                if (++_size < _sizes.Length)
                {
                    _stage = 0;
                    _tab = 0;
                    _frames = 0;
                    return SmokeResult.Running;
                }
                return _problems.Count == 0
                    ? SmokeResult.Pass($"offline/populated dashboard, changelog, announcement, options and Create Session at {_sizes.Length} viewport sizes from 800x600 to 4K")
                    : SmokeResult.Fail(string.Join("; ", _problems.Take(12)));
            case 6:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (tree.CurrentScene is not MainMenu main)
                    return SmokeResult.Fail("the main menu is not the current scene");
                Check(main, width, "Main menu");
                foreach (string name in Destinations)
                {
                    if (main.FindChild(name, recursive: true, owned: false) is not Button { Disabled: false } button ||
                        !button.IsVisibleInTree()) _problems.Add("Main menu destination missing or disabled: " + name);
                }
                if (main.FindButton is not { } find || tree.Root.GuiGetFocusOwner() != find)
                    _problems.Add("Main menu did not focus Find a Session");
                CheckDashboardNavigation(main, _sizes[_size]);
                if (main.FindChild("ReadAnnouncement", recursive: true, owned: false) is Control { Visible: true } notice &&
                    notice.IsVisibleInTree()) _problems.Add("Main menu shows an announcement without data");
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-main-offline");
                main.HostButton?.GrabFocus();
                MenuDashboardService.Get(tree).SetPreview(PreviewSnapshot());
                _stage = 7;
                _frames = 0;
                return SmokeResult.Running;
            case 7:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (tree.CurrentScene is not MainMenu { WhatsNewButton: { Disabled: false } whatsNew } populated)
                    return SmokeResult.Fail("populated main menu has no What's new button");
                Check(populated, width, "Main menu populated");
                CheckDashboardNavigation(populated, _sizes[_size]);
                if (tree.Root.GuiGetFocusOwner() != populated.HostButton)
                    _problems.Add("Dashboard data refresh stole keyboard focus");
                if (populated.FindChild("ReadAnnouncement", recursive: true, owned: false) is not Button { Disabled: false } announcement ||
                    !announcement.IsVisibleInTree()) _problems.Add("Published announcement cannot be opened");
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-main-populated");
                if (_size == _sizes.Length - 1)
                {
                    _resizingMain = populated;
                    Resize(tree, new Vector2I(800, 600));
                    _stage = 10;
                    _frames = 0;
                    return SmokeResult.Running;
                }
                whatsNew.EmitSignal(BaseButton.SignalName.Pressed);
                _stage = 8;
                _frames = 0;
                return SmokeResult.Running;
            case 8:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (tree.CurrentScene is not DashboardDetailsScreen)
                    return SmokeResult.Fail("What's new did not open the changelog details");
                Check(tree.CurrentScene, width, "Changelog");
                if (tree.CurrentScene.FindChild("Releases", true, false) is not VBoxContainer releases || releases.GetChildCount() != 5)
                    _problems.Add("Changelog did not limit the preview fixture to five releases");
                else
                {
                    for (int release = 0; release < releases.GetChildCount(); release++)
                    {
                        if (releases.GetChild(release).FindChild("Notes", true, false) is not Control notes ||
                            notes.Visible != (release == 0))
                            _problems.Add("Changelog did not initially expand only its newest release");
                    }
                }
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-changelog");
                DashboardDetailsScreen.ShowAnnouncement(tree);
                _stage = 9;
                _frames = 0;
                return SmokeResult.Running;
            case 10:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (!ReferenceEquals(tree.CurrentScene, _resizingMain))
                    return SmokeResult.Fail("live resize replaced the main menu instance");
                Check(_resizingMain!, width, "Main menu live resize down", new Vector2I(800, 600));
                if (tree.Root.GuiGetFocusOwner() != _resizingMain!.HostButton)
                    _problems.Add("Resizing the dashboard stole keyboard focus");
                MenuPage.Shot(tree.Root, "menu-layout-live-3840x2160-to-800x600");
                Resize(tree, _sizes[_size]);
                _stage = 11;
                _frames = 0;
                return SmokeResult.Running;
            case 11:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (!ReferenceEquals(tree.CurrentScene, _resizingMain))
                    return SmokeResult.Fail("live resize back replaced the main menu instance");
                Check(_resizingMain!, width, "Main menu live resize back");
                CheckDashboardNavigation(_resizingMain!, _sizes[_size]);
                MenuPage.Shot(tree.Root, "menu-layout-live-800x600-to-3840x2160");
                _resizingMain!.WhatsNewButton!.EmitSignal(BaseButton.SignalName.Pressed);
                _resizingMain = null;
                _stage = 8;
                _frames = 0;
                return SmokeResult.Running;
            case 9:
                if (_frames < SettleFrames * 3) return SmokeResult.Running;
                if (tree.CurrentScene is not DashboardDetailsScreen { TitleText: "Announcement" })
                    return SmokeResult.Fail("announcement details did not open");
                Check(tree.CurrentScene, width, "Announcement");
                MenuPage.Shot(tree.Root, $"menu-layout-{_sizes[_size].X}x{_sizes[_size].Y}-announcement");
                MenuDashboardService.Get(tree).SetPreview(null);
                _options = (OptionsMenu)FrontEnd.ShowOptions(tree);
                _stage = 1;
                _frames = 0;
                return SmokeResult.Running;
            default:
                return SmokeResult.Running;
        }
    }

    /// <summary>Deterministic display data; the preview service accepts it only in isolated smoke runs.</summary>
    internal static MenuDashboardSnapshot PreviewSnapshot() => new()
    {
        Status = new(new MasterStatus
        {
            Announcement = "Community night: meet in the hub before joining a match. " +
                "Players of every experience level are welcome, and the tutorial pages explain the spy and mercenary controls. " +
                "This deliberately long announcement also checks that the complete notice remains readable in a small window.",
        }),
        Games = new(new ServerListResponse
        {
            Servers = [new ServerEntry { Players = 4, MaxPlayers = 4 }, new ServerEntry { Players = 2, MaxPlayers = 4 }],
        }, Unavailable: true),
        Hubs = new(new ServerListResponse
        {
            Servers = [new ServerEntry { Players = 12, MaxPlayers = 50, Kind = ServerKind.Hub }],
        }),
        Changelog = new(new ChangelogResponse
        {
            Unavailable = true,
            Entries = Enumerable.Range(0, 6).Select(i => new ChangelogEntry
            {
                Tag = "v0.9." + (6 - i),
                Name = "A clearer main menu, community information, and improvements for returning players",
                Published = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddDays(-i),
                Notes = "## Improvements\n\n" +
                    "- Read recent release notes and community notices without leaving the game.\n" +
                    "- See progress toward your next level and cosmetic unlock, with clearer account details.\n" +
                    "- Keep using the main menu when the server is temporarily unavailable.\n\n" +
                    "### Fixes\n\nLong release notes wrap inside the page at every supported window size. " +
                    "[Project](https://github.com/Lumbridge/OpenSCCT) and `inline code` remain readable.",
                Url = "https://github.com/Lumbridge/OpenSCCT/releases/tag/v0.9." + (6 - i),
            }).ToArray(),
        }),
        Account = new AccountInfo
        {
            Id = 1,
            Name = "Long Display Name For Layout",
            Progress = new ProgressInfo { Level = 12, BestLevel = 12, Xp = PlayerRank.XpToReach(12) + 375 },
            Stats = new AccountStats { Matches = 23, SpyWins = 10, MercWins = 8, Kills = 42 },
            Achievements =
            [
                new AchievementInfo
                {
                    Id = "matches_25", Title = "Regular", Description = "Finish 25 matches.", Category = "General",
                    Current = 23, Target = 25, Rewards = ["spy.body.sand", "merc.body.sand"],
                },
            ],
        },
    };

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        tree.Root.Size = size;
    }

    private void CheckDashboardNavigation(MainMenu menu, Vector2I viewport)
    {
        if (viewport.X < 1280 || viewport.Y < 720 || (float)viewport.X / viewport.Y < 16f / 9f - 0.001f) return;
        if (menu.FindChild("BodyScroll", true, false) is not ScrollContainer scroll)
        {
            _problems.Add("Main menu has no scrollable body");
            return;
        }
        Rect2 visible = scroll.GetGlobalRect().Grow(1f);
        foreach (string name in Destinations.Where(name => name is not ("Options" or "Quit")))
        {
            if (menu.FindChild(name, true, false) is Button button && !visible.Encloses(button.GetGlobalRect()))
                _problems.Add($"{viewport.X}x{viewport.Y} main menu requires scrolling to reach {name}");
        }
    }

    private void Check(Node page, float width, string where, Vector2I? viewport = null)
    {
        Vector2I size = viewport ?? _sizes[_size];
        where = $"{size.X}x{size.Y} " + where;
        _seen.Add(where);
        // The dashboard's optional splash trivia deliberately ellipsizes; its content must remain readable.
        Node content = page is MainMenu ? page.FindChild("Body", true, false) ?? page : page;
        foreach (string p in LayoutCheck.Problems(content, width))
        {
            _problems.Add(where + ": " + p);
        }
        _problems.AddRange(LayoutCheck.BoundsProblems(page, size).Select(p => where + ": " + p));
    }
}
