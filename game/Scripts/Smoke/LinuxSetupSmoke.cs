using Godot;
using OpenScctGame.Online;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// Opt-in network integration check for the Linux first-run path. Run with an empty XDG_DATA_HOME and a 600 s
/// timeout: opens setup, presses Download, then requires the shared packs and starter map before reaching the menu.
/// </summary>
public sealed class LinuxSetupSmoke : SmokeTest
{
    public const string TestName = "linux-setup";
    private LinuxSetupScreen? _screen;
    private bool _started;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_screen is null)
        {
            _screen = FrontEnd.Show(context.Tree, new LinuxSetupScreen());
            return SmokeResult.Running;
        }
        if (!_started)
        {
            if (ContentPacks.IndexError is { } error)
            {
                return SmokeResult.Fail(error);
            }
            if (ContentPacks.Index is null)
            {
                return SmokeResult.Running;
            }
            Button? button = FindDownload(_screen);
            if (button is null)
            {
                return SmokeResult.Fail("download button is missing");
            }
            button.EmitSignal(BaseButton.SignalName.Pressed);
            _started = true;
        }
        if (GodotObject.IsInstanceValid(_screen) && _screen.Failure is { } failure)
        {
            return SmokeResult.Fail(failure);
        }
        if (context.Tree.CurrentScene is MainMenu)
        {
            return !LinuxSetupScreen.Needed && ContentPacks.Store.InstalledMap("SteeE") is not null
                ? SmokeResult.Pass("shared content verified, SteeE installed, main menu opened")
                : SmokeResult.Fail("setup reached the menu without its content");
        }
        return SmokeResult.Running;
    }

    private static Button? FindDownload(Node node)
    {
        if (node is Button { Text: "Download and play" } button)
        {
            return button;
        }
        foreach (Node child in node.GetChildren())
        {
            if (FindDownload(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
