using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScctGame.Online;
using OpenScctGame.Play.Demo;
using OpenScctGame.UI;
using LibraryEntry = OpenScctGame.Play.Demo.DemoEntry;
using RecentEntry = OpenScct.Net.Discovery.DemoEntry;

namespace OpenScctGame.Smoke;

/// <summary>Real map names/pictures and confirmed deletion, using only disposable fixture recordings.</summary>
public sealed class DemoLibraryCardsSmoke : SmokeTest
{
    public const string TestName = "demo-library-cards";
    public override string Name => TestName;

    private DemosScreen? _screen;
    private string? _folder;
    private string _saved = string.Empty, _cached = string.Empty, _unavailable = string.Empty;
    private string _map = string.Empty, _title = string.Empty;
    private Texture2D? _picture;
    private int _step, _wait;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_wait-- > 0) return SmokeResult.Running;
        try
        {
            switch (_step++)
            {
                case 0:
                    Present(context);
                    break;
                case 1:
                    CheckLocalCards(context);
                    Press(Card(_saved), "Delete");
                    Require(File.Exists(_saved) && File.Exists(TrainingDataFile.PathForDemo(_saved)), "Delete removed files before confirmation.");
                    ConfirmBox confirmation = Find<ConfirmBox>(_screen!, "DeleteDemoConfirmation");
                    Require(confirmation.Question.Contains(_title, StringComparison.Ordinal) && confirmation.Question.Contains(Path.GetFileName(_saved), StringComparison.Ordinal),
                        "Confirmation must identify the recording by its map and file.");
                    Require(confirmation.NoButton == context.Tree.Root.GuiGetFocusOwner(), "Cancel must receive the initial confirmation focus.");
                    break;
                case 2:
                    CheckLayout(context);
                    MenuPage.Shot(_screen!, "demo-library-delete-confirmation");
                    Press(Find<ConfirmBox>(_screen!, "DeleteDemoConfirmation"), "No");
                    Require(File.Exists(_saved) && LocalCards().Length == 3, "Cancel changed the saved recording or library.");
                    break;
                case 3:
                    Press(Card(_saved), "Delete");
                    context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                    context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false });
                    Require(context.Tree.CurrentScene == _screen && File.Exists(_saved), "Escape must cancel deletion without leaving the library.");
                    break;
                case 4:
                    Press(Card(_saved), "Delete");
                    Press(Find<ConfirmBox>(_screen!, "DeleteDemoConfirmation"), "Yes");
                    Require(!File.Exists(_saved) && !File.Exists(TrainingDataFile.PathForDemo(_saved)), "Confirmed deletion left the recording or its companion.");
                    Require(File.Exists(_cached) && LocalCards().Length == 2, "Deleting a saved demo affected another recording or left its card.");
                    break;
                case 5:
                    Press(Card(_cached), "Delete");
                    Press(Find<ConfirmBox>(_screen!, "DeleteDemoConfirmation"), "Yes");
                    Require(!File.Exists(_cached) && LocalCards().Length == 1, "Confirmed deletion did not remove the cached demo and its card.");
                    break;
                case 6:
                    Press(Card(_unavailable), "Delete");
                    Press(Find<ConfirmBox>(_screen!, "DeleteDemoConfirmation"), "Yes");
                    Require(Directory.Exists(_unavailable) && LocalCards().Length == 1, "Failed deletion must retain the card.");
                    Require(Find<Label>(_screen!, "Status").Text.StartsWith("Could not delete the demo:", StringComparison.Ordinal), "Failed deletion did not show its error.");
                    Press(_screen!, "RecentTab");
                    break;
                case 7:
                    Control card = Find<GridContainer>(_screen!, "DemoCards").GetChildren().OfType<Control>().Single();
                    CheckMap(card);
                    Require(card.FindChild("Delete", true, false) is null, "Online-only recordings must not offer server deletion.");
                    Search(_title);
                    Require(Find<GridContainer>(_screen!, "DemoCards").GetChildCount() == 1 && Find<Label>(_screen!, "DemoCount").Text.StartsWith("1 of", StringComparison.Ordinal),
                        "Search did not match the real map display name.");
                    Search(_map);
                    Require(Find<Label>(_screen!, "DemoCount").Text.StartsWith("1 of", StringComparison.Ordinal), "Search must still match the map code.");
                    break;
                default:
                    CheckLayout(context);
                    MenuPage.Shot(_screen!, "demo-library-map-card");
                    return Finish(SmokeResult.Pass("Saved/download deletion requires confirmation; Cancel/Escape preserve files; failures retain cards; catalog names, thumbnails, fallback and search verified."));
            }
            _wait = 12;
            return SmokeResult.Running;
        }
        catch (Exception error)
        {
            return Finish(SmokeResult.Fail(error.ToString()));
        }
    }

    private void Present(SmokeContext context)
    {
        Require(AccountSettings.Isolated, "Demo library deletion smoke requires isolated settings.");
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(800, 600));
        }
        context.Tree.Root.Size = new Vector2I(800, 600);
        var maps = new MapCatalog([.. context.App.Options.MapRoots, .. MapImages.Default.Roots]);
        var map = maps.GetCatalog().FirstOrDefault(entry => entry.DisplayName != entry.Id && MapImages.Preview(maps, entry.Id) is not null)
            ?? throw new InvalidOperationException("An installed map with its catalog display name and menu picture is required.");
        _map = map.Id;
        _title = MapTitles.Of(maps, _map);
        _picture = MapImages.Preview(maps, _map);
        _folder = Path.Combine(Path.GetTempPath(), "svm-demo-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _saved = Path.Combine(_folder, "Saved" + DemoFile.Extension);
        _cached = Path.Combine(_folder, "Downloaded" + DemoFile.Extension);
        _unavailable = Path.Combine(_folder, "Unavailable" + DemoFile.Extension);
        LibraryEntry saved = Recording(_saved, _map);
        LibraryEntry cached = Recording(_cached, _map);
        File.WriteAllText(TrainingDataFile.PathForDemo(_saved), "disposable companion");
        // Simulate a listed file becoming inaccessible before deletion, independently of OS file-lock semantics.
        Directory.CreateDirectory(_unavailable);
        var unavailable = new LibraryEntry(_unavailable, saved.Header with { Map = "Uninstalled_Custom_Map" }, saved.Index, saved.Bytes);
        _screen = FrontEnd.Show(context.Tree, new DemosScreen
        {
            PreviewLocal = [(saved, false), (cached, true), (unavailable, false)],
            PreviewRecent = new DemoListResponse
            {
                Demos = [new RecentEntry
                {
                    Id = 123, Map = _map, Time = saved.Recorded, Expires = DateTimeOffset.UtcNow.AddDays(6),
                    NetVersion = MasterApi.Hex(saved.Header.ProtocolHash), ServerName = "Smoke server", Size = saved.Bytes,
                }], Total = 1, PageSize = 20,
            },
        });
    }

    private static LibraryEntry Recording(string path, string map)
    {
        var header = new DemoHeader
        {
            Map = map, ProtocolHash = NetClasses.Create().ComputeProtocolHash(), GameOptions = "?GameMode=0",
            StartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TickRate = 60, ServerName = "Smoke server",
        };
        using (var writer = new DemoWriter(path, header)) writer.Finish();
        (DemoHeader recorded, DemoIndex index) = DemoFile.ReadInfo(path);
        return new LibraryEntry(path, recorded, index, new FileInfo(path).Length);
    }

    private void CheckLocalCards(SmokeContext context)
    {
        Require(LocalCards().Length == 3, "Expected saved, downloaded and unavailable fixtures.");
        CheckMap(Card(_saved));
        CheckMap(Card(_cached));
        Require(Card(_saved).FindChild("Keep", true, false) is null && Card(_cached).FindChild("Keep", true, false) is Button,
            "Only a temporary download should offer Keep.");
        Control unavailable = Card(_unavailable);
        Require(Find<Label>(unavailable, "Title").Text == "Uninstalled_Custom_Map" && Find<MapThumb>(unavailable, "MapThumb").Picture is null &&
            Find<Label>(unavailable, "Placeholder").Visible, "Unknown maps must keep their ID and a visible picture placeholder.");
        CheckLayout(context);
        MenuPage.Shot(_screen!, "demo-library-local-cards");
    }

    private void CheckMap(Control card)
    {
        Require(Find<Label>(card, "Title").Text == _title && _title != _map, "Card shows the map code instead of its catalog name.");
        Require(Find<MapThumb>(card, "MapThumb").Picture == _picture && _picture is not null, "Card does not show the map's actual menu picture.");
    }

    private void Search(string query)
    {
        LineEdit search = Find<LineEdit>(_screen!, "DemoSearch");
        search.Text = query;
        search.EmitSignal(LineEdit.SignalName.TextChanged, query);
    }

    private Control[] LocalCards() => Find<GridContainer>(_screen!, "DemoCards").GetChildren().OfType<Control>()
        .Where(card => card.FindChild("FileName", true, false) is Label).ToArray();

    private Control Card(string path) => LocalCards().Single(card => Find<Label>(card, "FileName").Text == Path.GetFileName(path));

    private void CheckLayout(SmokeContext context)
    {
        Vector2 viewport = context.Tree.Root.GetVisibleRect().Size;
        List<string> problems = LayoutCheck.Problems(_screen!, viewport.X);
        problems.AddRange(LayoutCheck.BoundsProblems(_screen!, viewport));
        Require(problems.Count == 0, string.Join("; ", problems));
    }

    private static T Find<T>(Node parent, string name) where T : Node => parent.FindChild(name, true, false) as T
        ?? throw new InvalidOperationException("Missing " + name);

    private static void Press(Node parent, string name) => Find<Button>(parent, name).EmitSignal(BaseButton.SignalName.Pressed);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private SmokeResult Finish(SmokeResult result)
    {
        _screen?.QueueFree();
        foreach (string path in new[] { _saved, _cached })
            if (path.Length > 0) DemoLibrary.Delete(path);
        if (Directory.Exists(_unavailable)) Directory.Delete(_unavailable);
        if (_folder is not null && Directory.Exists(_folder)) Directory.Delete(_folder);
        return result;
    }
}
