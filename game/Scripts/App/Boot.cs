using Godot;
using OpenScctGame.Play;

namespace OpenScctGame;

/// <summary>
/// First scene. Shows the build and engine versions; with <c>--svm-play=Name</c> it goes straight to offline play
/// on that map through the listen server path, with <c>--svm-map=Name</c> to a local match on that map, otherwise
/// it opens the main menu.
/// </summary>
public partial class Boot : Node
{
    public bool IsReady { get; private set; }

    public override void _Ready()
    {
        var info = Engine.GetVersionInfo();
        string text = $"OpenSCCT - Godot {info["string"]} - .NET {System.Environment.Version}";
        GetNode<Label>("Status").Text = text;
        IsReady = true;

        App app = App.From(this);
        OfflinePlayEntry.Register();
        if (!app.IsSmokeRun && app.Options.PlayMap is not null)
        {
            Callable.From(() => PlayRoot.Enter(GetTree(), PlayConfig.FromOptions(app.Options))).CallDeferred();
        }
        else if (!app.IsSmokeRun && app.Options.Map is { } map)
        {
            Callable.From(() => GameRoot.Enter(GetTree(), new GameConfig { Map = map, Bots = app.Options.Bots })).CallDeferred();
        }
        else if (!app.IsSmokeRun)
        {
            Callable.From(() => OpenScctGame.UI.FrontEnd.ShowMainMenu(GetTree())).CallDeferred();
        }
    }
}
