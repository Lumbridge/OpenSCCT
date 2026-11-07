using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class EditorChangelogTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Feeds(bool editorNewer = true) : HttpMessageHandler
    {
        public bool EditorUnavailable;
        public int GameRequests;
        public int EditorRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool editor = request.RequestUri!.AbsolutePath == "/repos/Lumbridge/OpenSCCT-Editor/releases";
            if (editor) EditorRequests++;
            else
            {
                Assert.Equal("/repos/Lumbridge/OpenSCCT/releases", request.RequestUri.AbsolutePath);
                GameRequests++;
            }
            if (editor && EditorUnavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            string product = editor ? "Editor" : "Game";
            object[] rows = [Release("v1.0.0", editor ? (editorNewer ? 11 : 9) : 10), Release("v0.9.0", editor ? 2 : 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(rows), Encoding.UTF8, "application/json"),
            });

            object Release(string tag, int day) => new
            {
                tag_name = tag, name = "", body = "- " + product + " notes " + tag,
                published_at = new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero), draft = false, prerelease = false,
            };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CombinedPageOrdersBothProductsAndLinksToEachLatestExpandedUpdate(bool editorNewer)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), "openscct-changelog-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var handler = new Feeds(editorNewer);
            using var upstream = new HttpClient(handler);
            var releases = new ReleaseChangelog(upstream);
            MasterOptions options = MasterTests.LocalOptions();
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = Path.Combine(directory, "accounts.db");
            options.TicketKeyFile = Path.Combine(directory, "tickets.pem");
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using MasterHost master = await MasterHost.StartAsync(options, releases, ct);
            using var website = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };
            string html = await website.GetStringAsync("/changelog", ct);
            string[] cards = Regex.Matches(html, "<article class=\"release-card[\\s\\S]*?</article>").Select(m => m.Value).ToArray();

            Assert.Equal(4, cards.Length);
            Assert.Equal(editorNewer, cards[0].Contains("editor-theme", StringComparison.Ordinal));
            string game = Assert.Single(cards, card => card.Contains("id=\"v1.0.0\"", StringComparison.Ordinal));
            string editor = Assert.Single(cards, card => card.Contains("id=\"editor-v1.0.0\"", StringComparison.Ordinal));
            Assert.Contains("Game changelog", game, StringComparison.Ordinal);
            Assert.Contains("<h2>OpenSCCT 1.0.0</h2>", game, StringComparison.Ordinal);
            Assert.Contains("Editor changelog", editor, StringComparison.Ordinal);
            Assert.Contains("<h2>OpenSCCT Editor 1.0.0</h2>", editor, StringComparison.Ordinal);
            Assert.Contains("id=\"game-latest\" tabindex=\"-1\"", game, StringComparison.Ordinal);
            Assert.Contains("id=\"editor-latest\" tabindex=\"-1\"", editor, StringComparison.Ordinal);
            Assert.Contains("href=\"#game-latest\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"#editor-latest\"", html, StringComparison.Ordinal);
            Assert.Contains(ReleaseChangelog.EditorReleasesUrl + "/tag/v1.0.0", editor, StringComparison.Ordinal);
            Assert.DoesNotContain("<details", game, StringComparison.Ordinal);
            Assert.DoesNotContain("<details", editor, StringComparison.Ordinal);
            Assert.All(cards.Skip(2), card => Assert.Contains("<details", card, StringComparison.Ordinal));
            string[] ids = Regex.Matches(html, " id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

            using var client = new MasterClient(master.HttpAddress);
            ChangelogResponse api = await client.ChangelogAsync(ct);
            Assert.Equal(2, api.Entries.Count);
            Assert.All(api.Entries, entry => Assert.StartsWith("- Game notes", entry.Notes, StringComparison.Ordinal));
            await website.GetStringAsync("/changelog", ct);
            Assert.Equal(1, handler.GameRequests);
            Assert.Equal(1, handler.EditorRequests);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task EditorFailureKeepsItsLastGoodNotesAndRetriesIndependentlyOfGame()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var handler = new Feeds();
        using var upstream = new HttpClient(handler);
        var clock = new Clock();
        var game = new ReleaseChangelog(upstream, clock);
        ReleaseChangelog editor = game.CreateEditorFeed();
        ChangelogResponse first = await editor.GetAsync(ct);
        await game.GetAsync(ct);
        handler.EditorUnavailable = true;
        clock.Now += TimeSpan.FromMinutes(10);

        ChangelogResponse failed = await editor.GetAsync(ct);
        Assert.True(failed.Unavailable);
        Assert.Equal(first.Entries, failed.Entries);
        Assert.False((await game.GetAsync(ct)).Unavailable);
        handler.EditorUnavailable = false;
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False((await editor.GetAsync(ct)).Unavailable);
        await game.GetAsync(ct);
        Assert.Equal(3, handler.EditorRequests);
        Assert.Equal(2, handler.GameRequests);
    }
}
