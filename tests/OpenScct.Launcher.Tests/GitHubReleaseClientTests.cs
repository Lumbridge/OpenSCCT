using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class GitHubReleaseClientTests
{
    private const string Json = """
        [
          { "id": 2, "tag_name": "v0.2.0", "name": "0.2.0", "body": "Notes", "draft": false, "prerelease": false,
            "assets": [ { "id": 20, "name": "OpenSCCT-0.2.0-win-x64.zip", "size": 10, "url": "https://api.github.com/repos/o/r/releases/assets/20" } ] },
          { "id": 3, "tag_name": "v0.3.0", "draft": true, "assets": [] }
        ]
        """;

    private static HttpResponseMessage Ok(string etag)
    {
        HttpResponseMessage r = Builds.Text(Json);
        r.Headers.ETag = new EntityTagHeaderValue(etag);
        return r;
    }

    [Fact]
    public async Task SendsTheETagAndUsesTheCacheOn304()
    {
        using var dir = new TempDir();
        int calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? Ok("\"abc\"") : new HttpResponseMessage(HttpStatusCode.NotModified));
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path);

        ReleaseFeed first = await client.GetReleasesAsync(TestContext.Current.CancellationToken);
        ReleaseFeed second = await client.GetReleasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseFeedSource.Network, first.Source);
        Assert.Equal(ReleaseFeedSource.NotModified, second.Source);
        Assert.Equal("\"abc\"", handler.Requests[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Equal("v0.2.0", Assert.Single(second.Releases).TagName);
    }

    [Fact]
    public async Task OfflineFallsBackToTheCache()
    {
        using var dir = new TempDir();
        int calls = 0;
        using var http = new HttpClient(new FakeHandler(_ => ++calls == 1 ? Ok("\"abc\"") : throw new HttpRequestException("no network")));
        var client = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path);

        await client.GetReleasesAsync(TestContext.Current.CancellationToken);
        ReleaseFeed offline = await client.GetReleasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseFeedSource.Cache, offline.Source);
        Assert.NotNull(offline.Problem);
        Assert.Single(offline.Releases);
    }

    [Fact]
    public async Task OfflineWithNoCacheReportsNone()
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("no network")));
        ReleaseFeed feed = await new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path).GetReleasesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ReleaseFeedSource.None, feed.Source);
    }

    [Fact]
    public async Task RateLimitIsRememberedUntilItResets()
    {
        using var dir = new TempDir();
        DateTimeOffset reset = DateTimeOffset.UtcNow.AddMinutes(30);
        int calls = 0;
        var handler = new FakeHandler(_ =>
        {
            if (++calls == 1)
            {
                return Ok("\"abc\"");
            }

            var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
            limited.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            limited.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return limited;
        });
        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path);

        await client.GetReleasesAsync(TestContext.Current.CancellationToken);
        ReleaseFeed limitedFeed = await client.GetReleasesAsync(TestContext.Current.CancellationToken);
        ReleaseFeed third = await client.GetReleasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseFeedSource.Cache, limitedFeed.Source);
        Assert.Equal(reset.ToUnixTimeSeconds(), limitedFeed.RateLimitReset!.Value.ToUnixTimeSeconds());
        Assert.Equal(ReleaseFeedSource.Cache, third.Source);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TokenIsSentAsBearerAndAnonymousSendsNone()
    {
        using var dir = new TempDir();
        var handler = new FakeHandler(_ => Ok("\"a\""));
        using var http = new HttpClient(handler);

        await new GitHubReleaseClient(http, new ReleaseSource("o", "r", "secret"), dir.Path).GetReleasesAsync(TestContext.Current.CancellationToken);
        await new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path).GetReleasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Bearer secret", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Null(handler.Requests[1].Headers.Authorization);
        Assert.Contains("OpenSCCT-Launcher", handler.Requests[1].Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AssetRequestAsksForTheBytes()
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new FakeHandler(_ => Ok("\"a\"")));
        var client = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), dir.Path);
        using HttpRequestMessage request = client.CreateAssetRequest(new ReleaseAsset { Name = "a.zip", Url = "https://api.github.com/repos/o/r/releases/assets/1" });
        Assert.Equal("application/octet-stream", request.Headers.Accept.Single().MediaType);
    }
}
