using Godot;

namespace OpenScctGame.UI;

public partial class MainMenu
{
    private void BuildBrowserMenu()
    {
        BuildFrame("OpenSCCT · Browser", 700f, responsive: true);
        Body.AddChild(UiStyle.Wordmark());
        var note = new Label
        {
            Text = "TestRange · WebGPU preview\nExplore the training range and practise with the desktop controls.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Body.AddChild(note);
        Body.AddChild(UiStyle.MenuButton("Multiplayer", () => BrowserPlatform.OpenMultiplayer?.Invoke(), "BrowserMultiplayer"));
        foreach (var (caption, kind) in new[]
        {
            ("Play as a spy", "spy"), ("Play as a mercenary", "merc"),
            ("Practise vision cancelling", "vision-cancelling"), ("Practise grabbing", "spy-grab"),
        })
            Body.AddChild(UiStyle.MenuButton(caption, () => BrowserPlatform.StartRound?.Invoke(kind), "Browser_" + kind));
        Body.AddChild(UiStyle.MenuButton(BrowserPlatform.AccountName is { } name ? name + " · Account" : "Sign in", () => BrowserPlatform.OpenAccount?.Invoke(), "BrowserAccount"));
        Body.AddChild(TutorialsScreen.MenuButton(this));
        OptionsButton = PageButton("Options", () => FrontEnd.ShowOptions(GetTree()), "Options");
    }
}
