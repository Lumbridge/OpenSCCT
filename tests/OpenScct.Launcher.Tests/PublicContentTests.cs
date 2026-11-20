using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class PublicContentTests
{
    private static readonly ContentDownload Full = new("https://example.test/full.zip", 1, new string('a', 64));
    private static readonly ContentDownload Sim = new("https://example.test/sim.zip", 1, new string('b', 64));
    private static readonly ContentDistribution Review = new("community-authored", "Map author", "CC0-1.0",
        "https://example.test/rights", Full.Sha256, Sim.Sha256);
    private static readonly ContentMap Approved = new() { Id = "community/Map", Code = "Map", Full = Full, Sim = Sim, Distribution = Review };

    [Fact]
    public void OnlyReviewedArchiveRevisionsAreListed()
    {
        var unreviewed = Approved with { Distribution = null };
        var retail = Approved with { Code = "AQU03" };
        var differentSim = Approved with { Sim = Full };
        var noEvidence = Approved with { Distribution = Review with { Evidence = "" } };
        var noCredit = Approved with { Distribution = Review with { Credits = " " } };
        var nav = new ContentBotNavigation(Sim.Sha256, 2, 0, Sim);
        var index = new ContentIndex { Maps = [unreviewed, retail, differentSim, noEvidence, noCredit, Approved with { BotNavigation = nav }] };
        var result = Assert.Single(index.PublicDownloads().Maps);
        Assert.Null(result.BotNavigation);
        Assert.Equal(Approved.Code, result.Code);
        var reviewedNavigation = result with { BotNavigation = nav, Distribution = Review with { NavigationSha256 = Sim.Sha256 } };
        Assert.NotNull(Assert.Single(new ContentIndex { Maps = [reviewedNavigation] }.PublicDownloads().Maps).BotNavigation);
    }

    [Fact]
    public async Task UnreviewedDirectInstallsStopBeforeNetworkOrFilesystemWrites()
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new RejectNetwork());
        var store = new ContentStore(dir.Path, http);
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallMapAsync(Approved with { Full = Sim }, ContentKind.Full, null, ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallSharedAsync(new ContentSharedPack { Id = "core", Download = Full }, null, ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallBotNavigationAsync(new ContentBotNavigation(Sim.Sha256, 2, 0, Sim), _ => false, null, ct));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("community/First Map/Packages/Maps/Demo.sdc", "Packages/Maps/Demo.sdc", true)]
    [InlineData("stock/Packages/Maps/Demo.sdc", "Packages/Maps/Demo.sdc", false)]
    [InlineData("community/First Map/Packages/Maps/AQU03.sdc", "Packages/Maps/AQU03.sdc", false)]
    [InlineData("community/First Map/Packages/Maps/Demo.sdc", "../Packages/Maps/Demo.sdc", false)]
    [InlineData("community/First Map/Packages/Maps/Demo.sdc", "Packages/Maps/Renamed.sdc", false)]
    public void SourceRecipesRequireTheirOwnPlayableMapAtAPinnedPath(string path, string destination, bool accepted)
    {
        var index = new { format = "openscct.content", formatVersion = 1, maps = new[] { new
        {
            code = "Demo", id = "community/Demo", contentHash = "0123456789abcdef", modes = new[] { "Deathmatch" },
            steamRecipe = new { commit = new string('a', 40), credits = "Demo author", evidence = "https://example.test/source",
                files = new[] { new { path, destination, size = 36, sha256 = new string('b', 64) } } },
        } } };
        var maps = ContentIndex.Parse(JsonSerializer.Serialize(index)).PublicDownloads().Maps;
        if (accepted)
        {
            var map = Assert.Single(maps);
            Assert.True(map.CanDistribute);
            Assert.Null(map.Full);
            Assert.Null(map.Sim);
            Assert.Equal(36, map.DownloadSize());
        }
        else Assert.Empty(maps);
    }

    [Fact]
    public async Task LocalMapWithTheSameCodeIsKept()
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new RejectNetwork());
        var store = new ContentStore(dir.Path, http);
        string folder = Path.Combine(store.SvMapRoot, Approved.Code);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), "local work");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.InstallMapAsync(Approved, ContentKind.Full, null, TestContext.Current.CancellationToken));
        Assert.Equal("local work", File.ReadAllText(Path.Combine(folder, "manifest.json")));
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken token) =>
            throw new InvalidOperationException("Unexpected network request");
    }
}
