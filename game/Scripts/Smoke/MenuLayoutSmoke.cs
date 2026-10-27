using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=menu-layout</c>: every options tab and the Create Session page as the front end opens them, checked
/// for text a player cannot read (<see cref="LayoutCheck"/>) and saved with <c>SVM_PLAY_SHOTS</c>.
/// </summary>
public sealed class MenuLayoutSmoke : SmokeTest
{
    public const string TestName = "menu-layout";

    private const int SettleFrames = 6;

    private readonly List<string> _problems = new();
    private readonly List<string> _seen = new();
    private OptionsMenu? _options;
    private int _stage;
    private int _tab;
    private int _frames;

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
                // Headless the window is 64 px wide: lay the pages out at the game's own 1280 x 720.
                if (tree.Root.Size.X < 1280)
                {
                    tree.Root.Size = new Vector2I(1280, 720);
                }

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
                Check(tabs.GetCurrentTabControl() ?? (Node)tabs, width, "Options " + title);
                MenuPage.Shot(tree.Root, $"menu-layout-options-{_tab}-{title}");
                _frames = 0;
                if (++_tab >= tabs.GetTabCount())
                {
                    _options.QueueFree();
                    _stage = 2;
                }

                return SmokeResult.Running;
            case 2:
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
                MenuPage.Shot(tree.Root, "menu-layout-create-session");
                return _problems.Count == 0
                    ? SmokeResult.Pass("checked " + string.Join(", ", _seen))
                    : SmokeResult.Fail(string.Join("; ", _problems.Take(12)));
            default:
                return SmokeResult.Running;
        }
    }

    private void Check(Node page, float width, string where)
    {
        _seen.Add(where);
        foreach (string p in LayoutCheck.Problems(page, width))
        {
            _problems.Add(where + ": " + p);
        }
    }
}
