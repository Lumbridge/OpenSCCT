using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Real viewport input: back navigation, safe quit, and drafts that survive cancelling Escape.</summary>
public sealed class MenuEscapeSmoke : SmokeTest
{
    public const string TestName = "menu-escape";
    public override string Name => TestName;
    private int _step;
    private HostScreen? _host;

    public override SmokeResult Step(SmokeContext context)
    {
        var tree = context.Tree;
        void Escape(bool echo = false) => tree.Root.PushInput(new InputEventKey
            { Keycode = Key.Escape, Pressed = true, Echo = echo });
        switch (_step++)
        {
            case 0:
                FrontEnd.ShowMainMenu(tree);
                break;
            case 1:
                Escape(true);
                if (tree.CurrentScene.GetNodeOrNull("ConfirmQuit") is not null) return SmokeResult.Fail("key repeat opened quit");
                Escape();
                if (tree.CurrentScene.GetNodeOrNull("ConfirmQuit") is null) return SmokeResult.Fail("Escape did not confirm quit");
                break;
            case 2:
                Escape();
                break;
            case 3:
                if (tree.CurrentScene is not MainMenu || tree.CurrentScene.GetNodeOrNull("ConfirmQuit") is not null)
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
                return SmokeResult.Pass("viewport Escape: quit/cancel, repeat suppression, session draft discard, clean options back");
        }
        return SmokeResult.Running;
    }
}
