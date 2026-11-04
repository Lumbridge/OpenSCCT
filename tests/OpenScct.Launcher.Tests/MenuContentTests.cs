using System.IO;
using System.Linq;
using System.Net.Http;
using OpenScct.Launcher.Content;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class MenuContentTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MenuBanksAreFetchedWithoutPlayableModesOnFirstDownloadAndUpdates(bool firstDownload)
    {
        using var folder = new TempDir();
        using var http = new HttpClient();
        var store = new ContentStore(folder.Path, http);
        var index = new ContentIndex { Maps = [Map("Entry"), Map("Lobby"), Map("Unselected", playable: true)] };

        string[] fetch = PackedContent.MapsToFetch(index, store, firstDownload).Select(m => m.Code).ToArray();

        Assert.Contains("Entry", fetch);
        Assert.Contains("Lobby", fetch);
        Assert.Equal(firstDownload, fetch.Contains("Unselected"));
        Assert.DoesNotContain(index.PlayableMaps(), m => m.Code is "Entry" or "Lobby");
    }

    [Fact]
    public void CurrentMenuIsRetainedAndAnOutdatedMenuIsUpdated()
    {
        using var folder = new TempDir();
        foreach (string code in new[] { "Entry", "Lobby" })
        {
            Directory.CreateDirectory(folder.Combine("svmap", code));
            File.WriteAllText(folder.Combine("svmap", code, "manifest.json"), "{}");
        }
        File.WriteAllText(folder.Combine("installed.json"), """
            [{"id":"stock/Entry","code":"Entry","sha256":"current","kind":0},
             {"id":"stock/Lobby","code":"Lobby","sha256":"old","kind":0}]
            """);
        using var http = new HttpClient();
        var store = new ContentStore(folder.Path, http);
        var index = new ContentIndex { Maps = [Map("Entry"), Map("Lobby")] };

        Assert.Equal(["Lobby"], PackedContent.MapsToFetch(index, store, firstDownload: false).Select(m => m.Code).ToArray());
    }

    private static ContentMap Map(string code, bool playable = false) => new()
    {
        Id = "stock/" + code, Code = code, Category = "stock", Modes = playable ? ["Story"] : [],
        Full = new("https://example.invalid/" + code + ".zip", 1, "current"),
        Sim = new("https://example.invalid/" + code + "-sim.zip", 1, "sim"),
    };
}
