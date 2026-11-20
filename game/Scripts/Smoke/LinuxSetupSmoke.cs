using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Checks direct desktop startup guidance against an empty install without network access.</summary>
public sealed class LinuxSetupSmoke : SmokeTest
{
    public const string TestName = "linux-setup";
    private LinuxSetupScreen? _screen;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (!LinuxSetupScreen.Needed)
            return SmokeResult.Fail("use an empty launcher data directory for the first-run test");
        if (_screen is null)
        {
            _screen = FrontEnd.Show(context.Tree, new LinuxSetupScreen());
            return SmokeResult.Running;
        }

        if (context.Tree.CurrentScene != _screen ||
            _screen.FindChild("SteamInstructions", true, false) is not Label hint ||
            !hint.Text.Contains("Steam") || !hint.Text.Contains("Modernisations") ||
            _screen.FindChild("Quit", true, false) is not Button { Disabled: false } ||
            _screen.FindChildren("*", "Button", true, false).Count != 1)
            return SmokeResult.Fail("missing offline Steam setup instructions or unexpected download action");
        return SmokeResult.Pass("first launch directs the player to build their Steam game in the native launcher");
    }
}
