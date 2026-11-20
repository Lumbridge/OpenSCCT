using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserContentTests
{
    private static string Index(string url) => JsonSerializer.Serialize(new
    {
        format = "openscct.content", formatVersion = 1, svmapFormat = "1.2", shared = Array.Empty<object>(),
        maps = new[] { new { code = "Test", full = new { url, size = 4, sha256 = new string('a', 64) } } },
    });

    [Fact]
    public void BrowserMirrorPathsNeedExplicitSecureOriginWhileDesktopKeepsHttpsOnly()
    {
        string json = Index("/play/content/" + new string('a', 64) + ".zip");
        Assert.Empty(ContentIndex.Parse(json).Maps);
        var browser = ContentIndex.Parse(json, new Uri("https://master.example/play/content/index.json"));
        Assert.Equal("https://master.example/play/content/" + new string('a', 64) + ".zip", Assert.Single(browser.Maps).Full!.Url);
        Assert.Single(ContentIndex.Parse(json, new Uri("http://127.0.0.1:8878/play/content/index.json")).Maps);
        Assert.Empty(ContentIndex.Parse(json, new Uri("http://master.example/play/content/index.json")).Maps);
        Assert.Empty(ContentIndex.Parse(Index("//other.example/archive.zip"), new Uri("https://master.example/")).Maps);
        Assert.Empty(ContentIndex.Parse(Index("http://127.0.0.1/archive.zip")).Maps);
        Assert.Single(ContentIndex.Parse(Index("https://github.com/owner/repo/releases/download/v1/archive.zip")).Maps);
    }

    [Fact]
    public async Task MirrorServesOnlyIndexAndContentAddressedArchivesWithRangesAndImmutableCaching()
    {
        var ct = TestContext.Current.CancellationToken;
        DirectoryInfo root = Directory.CreateTempSubdirectory("OpenSCCT-browser-content-");
        try
        {
            string archive = new string('a', 64) + ".zip";
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "index.json"), Index("/play/content/" + archive), ct);
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, archive), [1, 2, 3, 4], ct);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "private.json"), "not-public", ct);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "index.pending"), "not-public", ct);
            using var store = AccountStore.Open(":memory:");
            var options = MasterTests.LocalOptions();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(options.HttpUrl);
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            new PlayerPortal(options, store, new ServerRegistry(options), _ => IPAddress.Loopback,
                new ReleaseChangelog(), browserContentRoot: root.FullName, enableBrowserPrototype: true).Map(app);
            await app.StartAsync(ct);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using (var index = await client.GetAsync("/play/content/index.json", ct))
            {
                index.EnsureSuccessStatusCode();
                Assert.Equal("application/json", index.Content.Headers.ContentType!.MediaType);
                Assert.True(index.Headers.CacheControl!.NoCache);
            }
            using (var file = await client.GetAsync("/play/content/" + archive, ct))
            {
                file.EnsureSuccessStatusCode();
                Assert.Equal("application/zip", file.Content.Headers.ContentType!.MediaType);
                Assert.Contains("immutable", file.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, await file.Content.ReadAsByteArrayAsync(ct));
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, "/play/content/" + archive);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
            using (var part = await client.SendAsync(request, ct))
            {
                Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
                Assert.Equal(new byte[] { 2, 3 }, await part.Content.ReadAsByteArrayAsync(ct));
            }
            foreach (string path in new[] { "private.json", "index.pending", "../private.json", "%2e%2e%2fprivate.json" })
            {
                using var denied = await client.GetAsync("/play/content/" + path, ct);
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                Assert.DoesNotContain("not-public", await denied.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            }
            await app.StopAsync(ct);
        }
        finally { root.Delete(recursive: true); }
    }
}
