using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net.Scoreboards;
using OpenScctGame.Hud;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Scoreboard history without a recording, and the shared result/lobby details at desktop sizes.</summary>
public sealed class MatchLibrarySmoke : SmokeTest
{
    public const string TestName = "match-library";
    public override string Name => TestName;
    private static readonly Vector2I[] Sizes = [new(800, 600), new(1280, 720), new(1920, 1080), new(3840, 2160)];
    private readonly List<string> _problems = [];
    private int _size, _stage, _frames;
    private Control? _page;
    private RoundScoreboardDialog? _dialog;
    private bool _lobbyPositioned;
    private string Resolution => $"{Sizes[_size].X}x{Sizes[_size].Y}";
    private static RoundScoreboard Complete => ScoreboardSmoke.Sample() with
    {
        RoundId = "11111111111111111111111111111111", Completed = true, AsOfSeconds = null, Winner = 0, Map = "TestBox", Server = "Private server",
        StartedUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), EndedUtc = new DateTimeOffset(2026, 11, 4, 12, 2, 0, TimeSpan.Zero),
    };

    public override SmokeResult Step(SmokeContext context)
    {
        if (_page is null) { OpenLibrary(context); return SmokeResult.Running; }
        if (++_frames < 12) return SmokeResult.Running;
        _frames = 0;
        switch (_stage++)
        {
            case 0:
                CheckPage(_page, new Vector2(Sizes[_size].X, Sizes[_size].Y));
                string[] labels = Labels(_page);
                Expect(labels.Any(t => t.Contains("Incomplete")) && labels.Any(t => t.Contains("Practice")), "unfinished and practice entries are labelled");
                Expect(_page.FindChildren("ViewScoreboard", nameof(Button), true, false).Count == 3, "recording-off guest games each have a scoreboard action");
                Expect(!_page.FindChildren("Watch", nameof(Button), true, false).Any(), "no replay has no Watch action");
                Shot(_page, "history");
                Click(_page, "ViewScoreboard");
                _dialog = _page.GetChildren().OfType<RoundScoreboardDialog>().SingleOrDefault();
                Expect(_dialog is not null, "history opens shared scoreboard dialog");
                break;
            case 1:
                if (_dialog is null) return FailMissingDialog();
                CheckDialog(_dialog);
                Shot(_dialog, "history-details");
                var chooser = _dialog.FindChild("ScoreboardPlayer", true, false) as OptionButton;
                Expect(chooser is not null && chooser.ItemCount == 24, "detail selector exposes all 24 players");
                if (chooser is not null) { chooser.Selected = 12; chooser.EmitSignal(OptionButton.SignalName.ItemSelected, 12L); }
                ScrollBottom(_dialog);
                break;
            case 2:
                if (_dialog is null) return FailMissingDialog();
                CheckDialog(_dialog);
                Expect(Labels(_dialog).Contains("Environmental kills") && Labels(_dialog).Contains("Mine kills"), "selected merc displays full detailed breakdown");
                Expect(_dialog.FindChildren("*", nameof(ScrollContainer), true, false).OfType<ScrollContainer>().Any(s => s.ScrollVertical > 0), "details scroll vertically");
                Shot(_dialog, "history-breakdown");
                _dialog.QueueFree(); _dialog = null;
                var result = new MatchResult { Map = "TestBox", Winner = HudSide.Spy, LocalSide = HudSide.Spy,
                    Reason = MatchEndReason.Story, Scoreboard = Complete, LocalParticipantId = 0 };
                var results = FrontEnd.ShowResults(context.Tree, result);
                results.SkipBanner(); _page = results;
                break;
            case 3:
                CheckPage(_page, new Vector2(Sizes[_size].X, Sizes[_size].Y));
                Expect(_page.FindChild("RoundScoreboard", true, false) is RoundScoreboardView { Detailed: true }, "results use shared detailed board");
                Expect(Labels(_page).Contains("Final totals"), "results display final rather than delayed totals");
                Shot(_page, "results");
                ScrollBottom(_page);
                break;
            case 4:
                CheckPage(_page, new Vector2(Sizes[_size].X, Sizes[_size].Y));
                Shot(_page, "results-breakdown");
                _page = FrontEnd.ShowLobby(context.Tree, new LobbyLayoutSmoke.FixtureLobby { LastScoreboard = Complete, LastScoreboardLocalParticipantId = 0 });
                _lobbyPositioned = false;
                break;
            case 5:
                if (!_lobbyPositioned && _page.FindChild("LastGame", true, false) is Control lastGame)
                {
                    for (Node? parent = lastGame.GetParent(); parent is not null; parent = parent.GetParent())
                        if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(lastGame);
                    _lobbyPositioned = true; _stage--; return SmokeResult.Running;
                }
                CheckPage(_page, new Vector2(Sizes[_size].X, Sizes[_size].Y));
                Expect(_page.FindChild("LastGame", true, false) is Control { Visible: true }, "lobby retains last game card");
                Expect(Labels(_page).Any(t => t.Contains("Last game") && t.Contains("Spies win")), "lobby summary shows result");
                Shot(_page, "lobby-last-game");
                Click(_page, "LastGameScoreboard");
                _dialog = _page.GetChildren().OfType<RoundScoreboardDialog>().SingleOrDefault();
                Expect(_dialog is not null, "lobby opens shared scoreboard dialog");
                break;
            case 6:
                if (_dialog is null) return FailMissingDialog();
                CheckDialog(_dialog);
                Shot(_dialog, "lobby-details");
                _dialog.QueueFree(); _dialog = null;
                if (++_size == Sizes.Length)
                    return _problems.Count == 0 ? SmokeResult.Pass("Guest/no-replay history, partial/practice labels, detailed selection/scrolling, results and lobby passed at four sizes.")
                        : Failure();
                _stage = 0;
                OpenLibrary(context);
                break;
        }
        return SmokeResult.Running;
    }

    private void OpenLibrary(SmokeContext context)
    {
        Vector2I size = Sizes[_size];
        if (DisplayServer.GetName() == "headless") context.Tree.Root.GuiEmbedSubwindows = true;
        if (DisplayServer.GetName() != "headless") { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(size); }
        context.Tree.Root.Size = size;
        _page = FrontEnd.Show(context.Tree, new DemosScreen
        {
            StartWithHistory = true, PreviewLocal = [], PreviewHistory =
            [
                new ArchivedScoreboard { Scoreboard = Complete, LocalParticipantId = 0, PlayedLocally = true },
                new ArchivedScoreboard { Scoreboard = Complete with { RoundId = "22222222222222222222222222222222", Practice = true, StartedUtc = Complete.StartedUtc.AddMinutes(-5) }, LocalParticipantId = 0, PlayedLocally = true },
                new ArchivedScoreboard { Scoreboard = Complete with { RoundId = "33333333333333333333333333333333", Completed = false, Partial = true, StartedUtc = Complete.StartedUtc.AddMinutes(-10), AsOfSeconds = 60 }, LocalParticipantId = 0, PlayedLocally = true, LeftEarly = true },
            ],
        });
    }

    private void CheckPage(Node page, Vector2 size) => _problems.AddRange(LayoutCheck.BoundsProblems(page, size).Select(p => Resolution + ": " + p));
    private void CheckDialog(RoundScoreboardDialog dialog)
    {
        if (DisplayServer.GetName() == "headless")
        {
            var wanted = new Vector2(Math.Min(1380, (int)(Sizes[_size].X * .94f)), Math.Min(820, (int)(Sizes[_size].Y * .94f)));
            Expect(dialog.GetVisibleRect().Size == wanted, "headless dialog viewport uses the requested resolution");
        }
        CheckPage(dialog, dialog.GetVisibleRect().Size);
        foreach (var scroll in dialog.FindChildren("*", nameof(ScrollContainer), true, false).OfType<ScrollContainer>())
            Expect(!scroll.GetHScrollBar().Visible, "detail dialog does not scroll horizontally");
    }
    private void Click(Node root, string name)
    {
        if (root.FindChild(name, true, false) is Button { Disabled: false } button) button.EmitSignal(Button.SignalName.Pressed);
        else Expect(false, "missing enabled " + name);
    }
    private static void ScrollBottom(Node root)
    {
        foreach (var scroll in root.FindChildren("*", nameof(ScrollContainer), true, false).OfType<ScrollContainer>())
            scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
    }
    private static string[] Labels(Node root) => root.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToArray();
    private void Shot(Node node, string scene) => MenuPage.Shot(node, "match-library-" + Resolution + "-" + scene);
    private void Expect(bool pass, string message) { if (!pass) _problems.Add(Resolution + ": " + message); }
    private SmokeResult Failure() => SmokeResult.Fail(string.Join("; ", _problems.Take(20)) +
        (_problems.Count > 20 ? $"; {_problems.Count - 20} further layout problems" : ""));
    private SmokeResult FailMissingDialog() => Failure();
}
