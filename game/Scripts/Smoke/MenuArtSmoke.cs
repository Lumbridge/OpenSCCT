using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=menu-art</c>: a preview of the menus' look, not a check. Opens the main menu from 800 x 600
/// through ultrawide and 4K, both offline and with isolated preview data, then changelog/announcement details,
/// Create Session, Join with IP and every options tab, and saves
/// each with <c>SVM_PLAY_SHOTS</c> (windowed runs only; start them with --svm-background so the window stays off-screen).
/// <c>--svm-ui-look=original</c> previews the original look instead.
/// </summary>
public sealed class MenuArtSmoke : SmokeTest
{
    public const string TestName = "menu-art";

    private const int SettleFrames = 12;

    /// <summary>Milliseconds a menu page needs to type out its title (ten letters a second), at any frame rate.</summary>
    private const ulong TitleMs = 2600;

    private static readonly Vector2I[] Sizes =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 2160),
    ];

    private readonly List<string> _saved = new();
    private int _stage;
    private int _frames;
    private ulong _pageStart;
    private int _tab;
    private OptionsMenu? _options;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SceneTree tree = context.Tree;
        _frames++;
        if (_frames == 1)
        {
            _pageStart = Time.GetTicksMsec();
        }

        bool typed = Time.GetTicksMsec() - _pageStart >= TitleMs;
        string look = UiStyle.Look.ToString().ToLowerInvariant();
        int mainStages = Sizes.Length * 2;
        if (_stage < mainStages)
        {
            int size = _stage % Sizes.Length;
            bool populated = _stage >= Sizes.Length;
            if (_frames == 1)
            {
                Resize(tree, Sizes[size]);
                MenuDashboardService.Get(tree).SetPreview(populated ? MenuLayoutSmoke.PreviewSnapshot() : null);
                FrontEnd.ShowMainMenu(tree);
            }

            if (typed)
            {
                Save(tree, $"menu-art-{look}-main-{(populated ? "populated" : "offline")}-{Sizes[size].X}x{Sizes[size].Y}");
                _stage++;
                _frames = 0;
            }

            return SmokeResult.Running;
        }

        switch (_stage - mainStages)
        {
            case 0:
                if (_frames == 1)
                {
                    Resize(tree, new Vector2I(1920, 1080));
                    DashboardDetailsScreen.ShowChangelog(tree);
                }
                if (typed)
                {
                    Save(tree, $"menu-art-{look}-changelog");
                    Next();
                }
                return SmokeResult.Running;
            case 1:
                if (_frames == 1) DashboardDetailsScreen.ShowAnnouncement(tree);
                if (typed)
                {
                    Save(tree, $"menu-art-{look}-announcement");
                    Next();
                }
                return SmokeResult.Running;
            case 2:
                if (_frames == 1)
                {
                    MenuDashboardService.Get(tree).SetPreview(null);
                    Resize(tree, new Vector2I(1920, 1080));
                    FrontEnd.ShowHost(tree);
                }

                if (typed)
                {
                    Save(tree, $"menu-art-{look}-create-session");
                    Next();
                }

                return SmokeResult.Running;
            case 3:
                if (_frames == 1)
                {
                    FrontEnd.ShowJoinWithIp(tree);
                }

                if (typed)
                {
                    Save(tree, $"menu-art-{look}-join-ip");
                    Next();
                }

                return SmokeResult.Running;
            case 4:
                if (_frames == 1)
                {
                    _options = (OptionsMenu)FrontEnd.ShowOptions(tree);
                    _tab = 0;
                }

                if (_options?.Tabs is not { } tabs)
                {
                    return _frames > 120 ? SmokeResult.Fail("the options screen has no tabs") : SmokeResult.Running;
                }

                if (_frames % SettleFrames == 1)
                {
                    tabs.CurrentTab = _tab;
                }
                else if (_frames % SettleFrames == 0)
                {
                    Save(tree, $"menu-art-{look}-options-{_tab}-{tabs.GetTabTitle(_tab)}");
                    if (++_tab >= tabs.GetTabCount())
                    {
                        _options.QueueFree();
                        Next();
                    }
                }

                return SmokeResult.Running;
            default:
                return SmokeResult.Pass(_saved.Count == 0 ? "no shots (headless, or SVM_PLAY_SHOTS not set)" : "saved " + string.Join(", ", _saved));
        }
    }

    private void Next()
    {
        _stage++;
        _frames = 0;
    }

    private void Save(SceneTree tree, string label)
    {
        if (MenuPage.Shot(tree.Root, label.Replace(' ', '-')) is { } file)
        {
            _saved.Add(System.IO.Path.GetFileName(file));
        }
    }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() == "headless")
        {
            tree.Root.Size = size;
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(size);
        tree.Root.Size = size;
    }
}
