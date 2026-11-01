using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Checks stats categories at common menu sizes, using isolated presentation data.</summary>
public sealed class StatsSmoke : SmokeTest
{
    public const string TestName = "stats";
    public override string Name => TestName;
    private StatsScreen? _page;
    private int _frame;
    private int _step;
    private readonly AccountInfo _sample = new()
    {
        Stats = new() { Matches = 12345, Kills = 123456, Deaths = 9876, SpyWins = 4000, MercWins = 3000 },
        Career = new() { Feats = FeatKeys.All.ToDictionary(k => k, _ => 123456), Maps = [new() { Map = "Stee", SpyWins = 100, MercWins = 200 }] },
        Progress = new() { Level = 55, Prestige = 10, TotalXp = 12345678 },
    };

    public override SmokeResult Step(SmokeContext context)
    {
        if (_page is null)
        {
            context.Tree.Root.Size = new Vector2I(1280, 720);
            _page = FrontEnd.ShowStats(context.Tree);
            return SmokeResult.Running;
        }
        if (++_frame < 12) return SmokeResult.Running;
        _frame = 0;
        var problems = LayoutCheck.Problems(_page, context.Tree.Root.GetVisibleRect().Size.X);
        if (problems.Count > 0) return SmokeResult.Fail(string.Join("; ", problems));
        MenuPage.Shot(_page, "stats-" + _step);
        if (_step >= StatsModel.Categories.Length * 2) return SmokeResult.Pass("signed-out page and all seven categories at 1280 and 960 pixels");
        if (_step == StatsModel.Categories.Length) context.Tree.Root.Size = new Vector2I(960, 720);
        string category = StatsModel.Categories[_step % StatsModel.Categories.Length];
        var button = _page.FindChild("Filter" + category, true, false) as Button;
        if (button is null) return SmokeResult.Fail("missing category " + category);
        button.EmitSignal(Button.SignalName.Pressed);
        _page.RenderStats(_sample);
        _step++;
        return SmokeResult.Running;
    }
}
