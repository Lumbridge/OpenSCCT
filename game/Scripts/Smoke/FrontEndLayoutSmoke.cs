using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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
        ("stats", () => new StatsScreen(), false),
        ("content", () => new ContentScreen(), false),
        ("armoury", () => new ArmouryScreen(), false),
        ("achievements", () => new AchievementsScreen(), false),
        ("tutorials", () => new TutorialsScreen(), false),
        ("tips", () => new TipsScreen(), false),
        ("tip-detail", () => new TipsScreen { Tip = 0 }, false),
        ("gadgets", () => new GadgetDetailsScreen(), false),
        ("gadget-detail", () => new GadgetDetailsScreen { Gadget = 0 }, false),
        ("tutorial-maps", () => new TutorialMapsScreen(), false),
        ("tutorial-map-list", () => new TutorialMapsScreen(), true),
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
    private bool _savedList;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_screen is null)
        {
            if (_page == 0) _savedList = SettingsStore.Current.MapListView;
            SettingsStore.Current.MapListView = Pages[_page].List;
            Resize(context.Tree, Sizes[_size]);
            _screen = FrontEnd.Show(context.Tree, Pages[_page].Create());
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
        MenuPage.Shot(_screen, $"front-end-{Pages[_page].Name}-{size.X}x{size.Y}");

        if (++_size < Sizes.Length)
        {
            Resize(context.Tree, Sizes[_size]);
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

    // A focused repeat or screenshot capture still uses the same resize assertions as the full suite.
    private static (string Name, Func<Control> Create, bool List)[] SelectPages(
        (string Name, Func<Control> Create, bool List)[] pages)
    {
        string? selection = System.Environment.GetEnvironmentVariable("SVM_FRONT_END_LAYOUT_PAGES");
        if (string.IsNullOrWhiteSpace(selection)) return pages;
        var names = selection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = pages.Where(page => names.Contains(page.Name)).ToArray();
        return selected.Length > 0 ? selected : throw new InvalidOperationException("No matching front-end layout pages: " + selection);
    }
}
