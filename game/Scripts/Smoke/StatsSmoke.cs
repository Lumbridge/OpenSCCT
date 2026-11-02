using System;
using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Career and recorded-match layouts, empty/legacy states and live reflow, using isolated data.</summary>
public sealed class StatsSmoke : SmokeTest
{
    public const string TestName = "stats";
    public override string Name => TestName;
    private const int CasesPerSize = 10;
    private static readonly Vector2I[] Sizes =
    [
        new(800, 600), new(960, 720), new(1280, 720), new(1920, 1080), new(3840, 2160),
    ];
    private StatsScreen? _page;
    private int _frame;
    private int _step;
    private MatchHistoryScreen? _history;
    private int _historyStep = -1;
    private const int HistoryCasesPerSize = 4;
    private readonly AccountInfo _sample = new()
    {
        Name = "Long Player Name For Layout",
        Stats = new() { Matches = 12345, Kills = 123456, Deaths = 9876, SpyWins = 4000, MercWins = 3000, Losses = 5345 },
        Career = new() { Feats = FeatKeys.All.ToDictionary(k => k, _ => 123456), Maps = [new() { Map = "Stee", SpyWins = 100, MercWins = 200 }] },
        Progress = new() { Level = 55, Prestige = 10, TotalXp = 12345678 },
    };
    private readonly AccountInfo _empty = new() { Name = "New player", Career = new(), Progress = new() };

    private int Case => _step % CasesPerSize;
    private string Category => Case is >= 1 and <= 7 ? StatsModel.Categories[Case - 1] : Case == 9 ? "Spy" : "Overview";
    private AccountInfo? Account => Case == 0 ? null : Case == 8 ? _empty : Case == 9 ? _sample with { Career = null } : _sample;
    private string State => Case == 0 ? "signed-out" : Case == 8 ? "zero-rounds" : Case == 9 ? "legacy-spy" : Category.ToLowerInvariant();

    public override SmokeResult Step(SmokeContext context)
    {
        if (_historyStep >= 0) return StepHistory(context);
        if (_page is null)
        {
            Resize(context, Sizes[0]);
            _page = FrontEnd.ShowStats(context.Tree);
            Present();
            return SmokeResult.Running;
        }
        if (++_frame < 12) return SmokeResult.Running;
        _frame = 0;
        Vector2 viewport = context.Tree.Root.GetVisibleRect().Size;
        var problems = LayoutCheck.Problems(_page, viewport.X);
        problems.AddRange(LayoutCheck.BoundsProblems(_page, viewport));
        var labels = _page.FindChildren("*", nameof(Label), true, false).OfType<Label>()
            .Where(label => label.IsVisibleInTree()).Select(label => label.Text).ToArray();
        if (Account is { } account)
        {
            // Check presentation against the established model, including uncapped totals and undefined ratios.
            foreach (string value in StatsModel.Build(account).Where(row => row.Category == Category).Select(row => row.Value).Distinct())
                if (!labels.Contains(value)) problems.Add($"{Category}: missing displayed value {value}");
            if (_page.FindChild("Filter" + Category, true, false) is not Button { ButtonPressed: true })
                problems.Add("selected category was lost while rendering " + Category);
        }
        if (Case == 0 && !labels.Any(text => text.Contains("Sign in", StringComparison.OrdinalIgnoreCase)))
            problems.Add("signed-out view has no sign-in guidance");
        if (Case == 8 && !labels.Any(text => text.Contains("No counted rounds", StringComparison.OrdinalIgnoreCase)))
            problems.Add("zero-round view has no first-round guidance");
        if (Case == 9 && !labels.Contains("—")) problems.Add("legacy counters should display unavailable, not zero");
        Vector2I size = Sizes[_step / CasesPerSize];
        string capture = $"stats-{size.X}x{size.Y}-{State}";
        MenuPage.Shot(_page, capture);
        if (problems.Count > 0) return SmokeResult.Fail(capture + ": " + string.Join("; ", problems));
        if (++_step == Sizes.Length * CasesPerSize)
        {
            _historyStep = 0;
            PresentHistory(context);
            return SmokeResult.Running;
        }
        if (Case == 0) Resize(context, Sizes[_step / CasesPerSize]);
        Present();
        return SmokeResult.Running;
    }

    private int HistoryCase => _historyStep < Sizes.Length * HistoryCasesPerSize
        ? _historyStep % HistoryCasesPerSize : _historyStep - Sizes.Length * HistoryCasesPerSize + 4;

    private Vector2I HistorySize => _historyStep < Sizes.Length * HistoryCasesPerSize
        ? Sizes[_historyStep / HistoryCasesPerSize] : Sizes[0];

    private SmokeResult StepHistory(SmokeContext context)
    {
        if (++_frame < 12) return SmokeResult.Running;
        _frame = 0;
        Vector2 viewport = context.Tree.Root.GetVisibleRect().Size;
        var problems = LayoutCheck.Problems(_history!, viewport.X);
        problems.AddRange(LayoutCheck.BoundsProblems(_history!, viewport));
        string[] labels = _history!.FindChildren("*", nameof(Label), true, false).OfType<Label>()
            .Where(label => label.IsVisibleInTree()).Select(label => label.Text).ToArray();
        bool Has(string text) => labels.Any(label => label.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (HistoryCase == 0 && !Has("Sign in")) problems.Add("history has no signed-out guidance");
        if (HistoryCase == 1 && !Has("No recorded matches")) problems.Add("history has no empty-state guidance");
        if (HistoryCase >= 2)
        {
            MatchHistoryDetails sample = HistorySample();
            if (!Has(sample.Match.Map) || !Has(sample.Match.Server)) problems.Add("map or server details were lost");
            if (HistoryCase == 2 && _history.FindChild("Match1", true, false) is not Button { Disabled: false })
                problems.Add("recorded match has no enabled details action");
            if (HistoryCase >= 3)
            {
                foreach (var player in sample.Players)
                {
                    if (!Has(player.Name!)) problems.Add("scoreboard is missing player " + player.Name);
                    foreach (int value in new[] { player.Score, player.Kills, player.Deaths })
                        if (!labels.Contains(StatsModel.Number(value))) problems.Add("scoreboard is missing " + value);
                }
            }
        }
        if (HistoryCase == 3 && !Has(HistorySample().Events![2].Text)) problems.Add("timeline lost the full event description");
        if (HistoryCase == 4 && !Has("Timeline unavailable")) problems.Add("missing unavailable timeline guidance");
        if (HistoryCase == 5 && !Has("No events recorded")) problems.Add("missing empty timeline guidance");
        if (HistoryCase is 6 or 7)
        {
            int expected = HistoryCase == 6 ? 100 : 105;
            int shown = labels.Count(text => text.Contains("Recorded event ", StringComparison.Ordinal));
            if (shown != expected) problems.Add($"timeline expected {expected} events, displayed {shown}");
            if (!Has("recording limit")) problems.Add("truncated timeline notice disappeared");
            bool moreVisible = _history.FindChild("MoreEvents", true, false) is Button { Visible: true };
            if (moreVisible != (HistoryCase == 6)) problems.Add("Show more events availability is incorrect");
        }
        string state = HistoryCase switch
        {
            0 => "signed-out", 1 => "empty", 2 => "list", 3 => "details", 4 => "timeline-unavailable",
            5 => "timeline-empty", 6 => "timeline-first-batch", _ => "timeline-all-events",
        };
        string capture = $"history-{HistorySize.X}x{HistorySize.Y}-{state}";
        MenuPage.Shot(_history, capture);
        if (problems.Count > 0) return SmokeResult.Fail(capture + ": " + string.Join("; ", problems));
        if (++_historyStep == Sizes.Length * HistoryCasesPerSize + 4)
            return SmokeResult.Pass("50 career and 24 match-history layouts from 800×600 through 4K, including timeline pagination and unavailable states");
        PresentHistory(context);
        return SmokeResult.Running;
    }

    private void PresentHistory(SmokeContext context)
    {
        if (HistoryCase == 7)
        {
            if (_history!.FindChild("MoreEvents", true, false) is not Button more)
                throw new InvalidOperationException("Missing timeline pagination action");
            more.EmitSignal(Button.SignalName.Pressed);
            return;
        }
        Resize(context, HistorySize);
        var sample = HistorySample();
        object? preview = HistoryCase switch
        {
            0 => null,
            1 => new MatchHistoryPage([], false),
            2 => new MatchHistoryPage([sample.Match, sample.Match with { Id = 2, Map = "Warehouse", Winner = 1, Counted = false }], true),
            4 => sample with { Events = null },
            5 => sample with { Events = [] },
            6 => sample with
            {
                Events = Enumerable.Range(0, 105).Select(i => new MatchTimelineEvent(i * 10, "objective",
                    $"Recorded event {i:000}: Objective completed at the warehouse terminal.")).ToArray(),
                TimelineTruncated = true,
            },
            _ => sample,
        };
        _history = (MatchHistoryScreen)FrontEnd.Show(context.Tree, new MatchHistoryScreen { Preview = preview });
    }

    private static MatchHistoryDetails HistorySample() => new(
        new MatchHistoryEntry
        {
            Id = 1, Ended = new DateTimeOffset(2026, 11, 1, 18, 25, 0, TimeSpan.Zero),
            Map = "Warehouse training grounds with a deliberately long map name",
            Server = "Official community server with a deliberately long descriptive name",
            Mode = 5, Winner = 0, DurationSeconds = 212, ObjectivesTaken = 2, ObjectivesNeeded = 2, Counted = true,
        },
        [new ResultsReportRow { Name = "Agent with a long player name", Team = 0, Score = 123456, Kills = 12345, Deaths = 9876 },
         new ResultsReportRow { Name = "Guard with a long player name", Team = 1, Score = 654321, Kills = 9876, Deaths = 12345, Bot = true }],
        [new(0, "start", "Round started"), new(40, "death", "Agent defeated Guard"),
         new(110, "objective", "Objective completed: Main terminal in the warehouse control room at the far end of the platform"),
         new(212, "end", "Round ended: Spies win")], false);

    private void Present()
    {
        if (_page!.FindChild("Filter" + Category, true, false) is not Button button)
            throw new InvalidOperationException("Missing category " + Category);
        button.SetPressedNoSignal(true);
        button.EmitSignal(Button.SignalName.Pressed);
        _page.RenderStats(Account);
        if (_page.FindChild("BodyScroll", true, false) is ScrollContainer scroll) scroll.ScrollVertical = 0;
    }

    private static void Resize(SmokeContext context, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        context.Tree.Root.Size = size;
    }
}
