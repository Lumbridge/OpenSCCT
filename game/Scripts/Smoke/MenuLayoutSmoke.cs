using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=menu-layout</c>: options and Create Session across small, 4:3, 16:10, widescreen, ultrawide and 4K
/// viewports, checked for unreadable text and off-screen controls, and saved with <c>SVM_PLAY_SHOTS</c>.
/// </summary>
public sealed class MenuLayoutSmoke : SmokeTest
{
    public const string TestName = "menu-layout";

    private const int SettleFrames = 6;
    private readonly Vector2I[] _sizes =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160),
    ];

    private readonly List<string> _problems = new();
    private readonly List<string> _seen = new();
    private OptionsMenu? _options;
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

                _options = (OptionsMenu)FrontEnd.ShowOptions(tree);
                _stage = 1;
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
                    ? SmokeResult.Pass($"options tabs and Create Session picture/list views at {_sizes.Length} viewport sizes from 800x600 to 4K")
                    : SmokeResult.Fail(string.Join("; ", _problems.Take(12)));
            default:
                return SmokeResult.Running;
        }
    }

    private void Check(Node page, float width, string where)
    {
        where = $"{_sizes[_size].X}x{_sizes[_size].Y} " + where;
        _seen.Add(where);
        foreach (string p in LayoutCheck.Problems(page, width))
        {
            _problems.Add(where + ": " + p);
        }
        _problems.AddRange(LayoutCheck.BoundsProblems(page, _sizes[_size]).Select(p => where + ": " + p));
    }
}
