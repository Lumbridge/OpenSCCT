using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>The corner label: the game's build, and the master's line kept current while the page is shown.</summary>
public partial class BuildCornerLabel : Label
{
    private string _master = string.Empty;

    public override void _Ready() => SetLines(MasterVersionCheck.Line());

    public override void _Process(double delta)
    {
        string master = MasterVersionCheck.Line();
        if (!string.Equals(master, _master, System.StringComparison.Ordinal))
        {
            SetLines(master);
        }
    }

    private void SetLines(string master)
    {
        _master = master;
        Text = BuildInfo.Text + "\n" + master;
    }
}
