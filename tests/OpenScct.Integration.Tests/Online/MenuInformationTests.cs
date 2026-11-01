using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>Menu information uses local HTTP fixtures; no test contacts GitHub or Discord.</summary>
public sealed class MenuInformationTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class FeedHandler(Func<Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            return answer();
        }
    }

    private static object Release(string tag, int day, string? name = "Release", string? notes = "- A useful change",
        bool draft = false, bool prerelease = false) => new
    {
        tag_name = tag, name, body = notes,
        published_at = new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero), draft, prerelease,
    };

    private static HttpResponseMessage Feed(params object[] releases) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task FeedFiltersNonReleasesOrdersNewestFirstAndKeepsTwelve()
    {
        var rows = Enumerable.Range(1, 14).Select(i => Release("v1.0." + i, i)).ToList();
        rows.AddRange([
            Release("v9.0.0", 30, draft: true), Release("v8.0.0", 29, prerelease: true),
            Release("launcher-v2.0.0", 28), Release("vnope", 27),
            Release("v1.0.15", 15, name: "", notes: null),
            new { tag_name = "v7.0.0", published_at = (DateTimeOffset?)null },
        ]);
        using var handler = new FeedHandler(() => Task.FromResult(Feed(rows.ToArray())));
        using var http = new HttpClient(handler);
        var source = new ReleaseChangelog(http, new Clock());

        ChangelogResponse result = await source.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Unavailable);
        Assert.Equal(12, result.Entries.Count);
        Assert.Equal("v1.0.15", result.Entries[0].Tag);
        Assert.Equal("OpenSCCT 1.0.15", result.Entries[0].Name);
        Assert.Equal(string.Empty, result.Entries[0].Notes);
        Assert.Equal(ReleaseChangelog.ReleasesUrl + "/tag/v1.0.15", result.Entries[0].Url);
        Assert.Equal("v1.0.4", result.Entries[^1].Tag);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("timeout")]
    public async Task RefreshFailuresRetainLastGoodNotesAndRetryAfterTwoMinutes(string failure)
    {
        var clock = new Clock();
        int calls = 0;
        using var handler = new FeedHandler(() =>
        {
            calls++;
            if (calls != 2) return Task.FromResult(Feed(Release(calls == 1 ? "v1.0.0" : "v1.1.0", 1)));
            return failure switch
            {
                "http" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                "json" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not JSON") }),
                _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("Fixture timeout")),
            };
        });
        using var http = new HttpClient(handler);
        var source = new ReleaseChangelog(http, clock);
        CancellationToken ct = TestContext.Current.CancellationToken;
        ChangelogResponse first = await source.GetAsync(ct);
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Same(first, await source.GetAsync(ct));
        Assert.Equal(1, handler.Requests);

        clock.Advance(TimeSpan.FromMinutes(1));
        ChangelogResponse failed = await source.GetAsync(ct);
        Assert.True(failed.Unavailable);
        Assert.Equal(first.Entries, failed.Entries);
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Same(failed, await source.GetAsync(ct));
        Assert.Equal(2, handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        ChangelogResponse recovered = await source.GetAsync(ct);
        Assert.False(recovered.Unavailable);
        Assert.Equal("v1.1.0", Assert.Single(recovered.Entries).Tag);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task FirstFailureHasAnExplicitEmptyUnavailableResult()
    {
        using var handler = new FeedHandler(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var http = new HttpClient(handler);
        var source = new ReleaseChangelog(http, new Clock());
        ChangelogResponse result = await source.GetAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Unavailable);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task ConcurrentReadersShareOneUpstreamRefresh()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FeedHandler(() => answer.Task);
        using var http = new HttpClient(handler);
        var source = new ReleaseChangelog(http, new Clock());
        Task<ChangelogResponse>[] readers = Enumerable.Range(0, 8).Select(_ => source.GetAsync(TestContext.Current.CancellationToken)).ToArray();
        answer.SetResult(Feed(Release("v1.0.0", 1)));
        ChangelogResponse[] results = await Task.WhenAll(readers);
        Assert.Equal(1, handler.Requests);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public async Task ChangelogDoesNotNeedAccountsButObeysThePrivateMastersInviteKey()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var handler = new FeedHandler(() => Task.FromResult(Feed(Release("v1.0.0", 1))));
        using var upstream = new HttpClient(handler);
        var source = new ReleaseChangelog(upstream, new Clock());
        MasterOptions options = MasterTests.LocalOptions("secret");
        options.Introducer = options.Relay = options.Probe = false;
        await using MasterHost master = await MasterHost.StartAsync(options, source, ct);
        using var missing = new MasterClient(master.HttpAddress);
        using var bad = new MasterClient(master.HttpAddress, "wrong");
        using var good = new MasterClient(master.HttpAddress, "secret");

        Assert.False(master.AccountsEnabled);
        Assert.Equal(401, (await Assert.ThrowsAsync<MasterException>(() => missing.ChangelogAsync(ct))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<MasterException>(() => bad.ChangelogAsync(ct))).StatusCode);
        Assert.Equal(0, handler.Requests);
        ChangelogResponse result = await good.ChangelogAsync(ct);
        Assert.False(result.Unavailable);
        ChangelogEntry entry = Assert.Single(result.Entries);
        Assert.Equal("v1.0.0", entry.Tag);
        Assert.Equal("Release", entry.Name);
        Assert.Equal("- A useful change", entry.Notes);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), entry.Published);
        Assert.Equal(ReleaseChangelog.ReleasesUrl + "/tag/v1.0.0", entry.Url);
        Assert.Null((await missing.StatusAsync(ct)).Announcement);
    }

    [Fact]
    public async Task WebsiteAndApiShareNotesAndStatusReflectsPublishedAndClearedAnnouncements()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string database = Path.Combine(Path.GetTempPath(), "openscct-menu-" + Guid.NewGuid() + ".db");
        string key = Path.ChangeExtension(database, ".pem");
        try
        {
            using var settings = AccountStore.Open(database);
            settings.SetSetting("site.announcement", "Community night at 20:00");
            using var handler = new FeedHandler(() => Task.FromResult(Feed(Release("v1.0.0", 1, notes: "Menu fixture release note"))));
            using var upstream = new HttpClient(handler);
            var source = new ReleaseChangelog(upstream, new Clock());
            MasterOptions options = MasterTests.LocalOptions();
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = database;
            options.TicketKeyFile = key;
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using MasterHost master = await MasterHost.StartAsync(options, source, ct);
            using var client = new MasterClient(master.HttpAddress);
            using var website = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };

            Assert.Equal("Community night at 20:00", (await client.StatusAsync(ct)).Announcement);
            string html = await website.GetStringAsync("/changelog", ct);
            Assert.Contains("Menu fixture release note", html, StringComparison.Ordinal);
            Assert.Equal("Menu fixture release note", Assert.Single((await client.ChangelogAsync(ct)).Entries).Notes);
            Assert.Equal(1, handler.Requests);

            settings.SetSetting("site.announcement", "");
            Assert.Null((await client.StatusAsync(ct)).Announcement);
            settings.SetSetting("site.announcement", "New announcement");
            Assert.Equal("New announcement", (await client.StatusAsync(ct)).Announcement);
            settings.SetSetting("site.announcement", null);
            Assert.Null((await client.StatusAsync(ct)).Announcement);
        }
        finally
        {
            File.Delete(database);
            File.Delete(database + "-wal");
            File.Delete(database + "-shm");
            File.Delete(key);
        }
    }
}
