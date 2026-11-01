using System.Linq;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Real viewport input: dashboard details, back navigation, safe quit, and drafts that survive cancelling Escape.</summary>
public sealed class MenuEscapeSmoke : SmokeTest
{
    public const string TestName = "menu-escape";
    public override string Name => TestName;
    private int _step;
    private HostScreen? _host;
    private MenuPilot? _pilot;
    private ScrollContainer? _detailsScroll;
    private int _waitFrames;

    public override SmokeResult Step(SmokeContext context)
    {
        var tree = context.Tree;
        _pilot?.Update();
        if (_pilot?.Busy == true) return SmokeResult.Running;
        if (_waitFrames > 0)
        {
            _waitFrames--;
            return SmokeResult.Running;
        }
        void Escape(bool echo = false) => tree.Root.PushInput(new InputEventKey
            { Keycode = Key.Escape, Pressed = true, Echo = echo });
        void Press(Key key)
        {
            tree.Root.PushInput(new InputEventKey { Keycode = key, Pressed = true });
            tree.Root.PushInput(new InputEventKey { Keycode = key, Pressed = false });
        }
        switch (_step++)
        {
            case 0:
                _pilot = new MenuPilot(tree);
                FrontEnd.ShowMainMenu(tree);
                break;
            case 1:
                Escape(true);
                if (tree.CurrentScene.FindChild("ConfirmQuit", recursive: true, owned: false) is not null) return SmokeResult.Fail("key repeat opened quit");
                Escape();
                if (tree.CurrentScene.FindChild("ConfirmQuit", recursive: true, owned: false) is null) return SmokeResult.Fail("Escape did not confirm quit");
                break;
            case 2:
                Escape();
                break;
            case 3:
                if (tree.CurrentScene is not MainMenu || tree.CurrentScene.FindChild("ConfirmQuit", recursive: true, owned: false) is not null)
                    return SmokeResult.Fail("Escape did not cancel quit");
                _host = FrontEnd.ShowHost(tree);
                break;
            case 4:
                _host!.Model!.Load(_host.Model.Setup with { TimeLimit = 7 });
                Escape();
                if (_host.GetNodeOrNull<ConfirmBox>("ConfirmBox") is null) return SmokeResult.Fail("session draft was not protected");
                break;
            case 5:
                Escape();
                if (_host!.IsQueuedForDeletion() || _host.Model!.Setup.TimeLimit != 7) return SmokeResult.Fail("cancel lost session draft");
                break;
            case 6:
                Escape();
                _host!.GetNodeOrNull<ConfirmBox>("ConfirmBox")?.Answer(true);
                break;
            case 7:
                if (tree.CurrentScene is not MainMenu) return SmokeResult.Fail("confirmed discard did not return to menu");
                FrontEnd.ShowOptions(tree);
                break;
            case 8:
                Escape();
                break;
            case 9:
                if (tree.CurrentScene is not MainMenu) return SmokeResult.Fail("clean options Escape did not return to menu");
                break;
            case 10:
            case 13:
                if (tree.CurrentScene is not MainMenu { WhatsNewButton: { } whatsNew })
                    return SmokeResult.Fail("main menu has no What's new button");
                if (!_pilot!.Click(whatsNew, "What's new")) _step--;
                break;
            case 11:
                if (tree.CurrentScene is not DashboardDetailsScreen)
                    return SmokeResult.Fail("What's new did not open its details page");
                Escape(true);
                if (tree.CurrentScene is not DashboardDetailsScreen)
                    return SmokeResult.Fail("repeated Escape left the changelog");
                Escape();
                break;
            case 12:
                if (tree.CurrentScene is not MainMenu { WhatsNewButton: { } returnFocus } ||
                    tree.Root.GuiGetFocusOwner() != returnFocus)
                    return SmokeResult.Fail("changelog Escape did not restore focus to What's new");
                if (tree.CurrentScene.FindChild("ConfirmQuit", recursive: true, owned: false) is not null)
                    return SmokeResult.Fail("changelog Escape also opened Quit");
                break;
            case 14:
                if (tree.CurrentScene is not DashboardDetailsScreen details ||
                    details.FindChild("Back", recursive: true, owned: false) is not Button back)
                    return SmokeResult.Fail("changelog has no Back button");
                if (!_pilot!.Click(back, "Changelog Back")) _step--;
                break;
            case 15:
                if (tree.CurrentScene is not MainMenu { WhatsNewButton: { } backFocus } ||
                    tree.Root.GuiGetFocusOwner() != backFocus)
                    return SmokeResult.Fail("changelog Back did not restore focus to What's new");
                MenuDashboardService.Get(tree).SetPreview(MenuLayoutSmoke.PreviewSnapshot());
                DashboardDetailsScreen.ShowAnnouncement(tree);
                break;
            case 16:
                if (tree.CurrentScene is not DashboardDetailsScreen { IsAnnouncement: true })
                    return SmokeResult.Fail("announcement details did not open");
                MenuDashboardService.Get(tree).SetPreview(null);
                Escape();
                break;
            case 17:
                if (tree.CurrentScene is not MainMenu { FindButton: { } findFocus } ||
                    tree.Root.GuiGetFocusOwner() != findFocus)
                    return SmokeResult.Fail("removed announcement did not return focus to Find a Session");
                MenuDashboardService.Get(tree).SetPreview(MenuLayoutSmoke.PreviewSnapshot() with
                {
                    Status = new(new MasterStatus
                    {
                        Announcement = string.Concat(Enumerable.Repeat(
                            "Community briefing: read this update before joining a session. Keep moving down through the announcement to review every paragraph.\n\n", 20)),
                    }),
                });
                DashboardDetailsScreen.ShowAnnouncement(tree);
                _waitFrames = 6;
                break;
            case 18:
                _detailsScroll = tree.CurrentScene.FindChild("BodyScroll", true, false) as ScrollContainer;
                if (_detailsScroll is null || _detailsScroll.GetVScrollBar().MaxValue <= _detailsScroll.GetVScrollBar().Page)
                    return SmokeResult.Fail("long announcement fixture does not require scrolling");
                if (tree.Root.GuiGetFocusOwner()?.Name != "Back")
                    return SmokeResult.Fail("long announcement did not initially focus Back");
                Press(Key.Pagedown);
                _waitFrames = 3;
                break;
            case 19:
                if (_detailsScroll!.ScrollVertical <= 0)
                    return SmokeResult.Fail("Page Down did not scroll a long announcement while Back was focused");
                Press(Key.End);
                _waitFrames = 3;
                break;
            case 20:
                double bottom = _detailsScroll!.GetVScrollBar().MaxValue - _detailsScroll.GetVScrollBar().Page;
                if (System.Math.Abs(_detailsScroll.ScrollVertical - bottom) > 1d)
                    return SmokeResult.Fail("End did not scroll the announcement to its bottom");
                Press(Key.Home);
                _waitFrames = 3;
                break;
            case 21:
                if (_detailsScroll!.ScrollVertical != 0)
                    return SmokeResult.Fail("Home did not return the announcement to its beginning");
                Escape();
                _waitFrames = 3;
                break;
            case 22:
                if (tree.CurrentScene is not MainMenu || tree.Root.GuiGetFocusOwner()?.Name != "ReadAnnouncement")
                    return SmokeResult.Fail("long announcement Escape did not restore its main-menu focus");
                MenuDashboardService.Get(tree).SetPreview(null);
                return SmokeResult.Pass("viewport input: quit/cancel, session drafts, changelog click/Escape/Back, removed announcement focus, and long announcement Page Down/End/Home");
        }
        return SmokeResult.Running;
    }
}
