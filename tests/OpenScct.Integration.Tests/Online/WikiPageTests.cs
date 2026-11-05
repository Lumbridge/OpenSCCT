using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class WikiPageTests
{
    private sealed class NoReleaseRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public wiki must not fetch release data.");
    }

    [Fact]
    public void CatalogHasValidCategoriesSectionsAndRelatedGuides()
    {
        Assert.True(WikiContent.Articles.Count >= 20);
        Assert.Equal(WikiContent.Articles.Count, WikiContent.Articles.Select(a => a.Slug).Distinct(StringComparer.Ordinal).Count());
        foreach (WikiContent.Article article in WikiContent.Articles)
        {
            Assert.Contains(WikiContent.Categories, c => c.Id == article.Category);
            Assert.True(article.Sections.Length >= 3);
            Assert.Equal(article.Sections.Length, article.Sections.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
            foreach (string related in article.Related)
                Assert.NotNull(WikiContent.Find(related));
        }
    }

    [Fact]
    public void SearchUsesAllTermsAndSearchesArticleContent()
    {
        // The article title does not mention these terms: the search must find useful body text too.
        Assert.Contains(WikiContent.Search("positive energy", "gameplay"), a => a.Slug == "gadgets-and-loadouts");
        Assert.DoesNotContain(WikiContent.Search("positive energy", "editor"), a => a.Slug == "gadgets-and-loadouts");
        Assert.Empty(WikiContent.Search("cloak no-such-phrase-686", ""));
        Assert.Equal(WikiContent.Search("  POSITIVE   ENERGY  ", "gameplay").Select(a => a.Slug),
            WikiContent.Search("positive energy", "gameplay").Select(a => a.Slug));
    }

    [Fact]
    public async Task PublicSearchFiltersWorkWithoutJavascriptAndEscapeRequestText()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), "openscct-wiki-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var handler = new NoReleaseRequests();
            using var upstream = new HttpClient(handler);
            var releases = new ReleaseChangelog(upstream);
            MasterOptions options = MasterTests.LocalOptions("wiki-invite-canary");
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = Path.Combine(directory, "accounts.db");
            options.TicketKeyFile = Path.Combine(directory, "tickets.pem");
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using MasterHost master = await MasterHost.StartAsync(options, releases, ct);
            using var http = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };

            string filtered = await http.GetStringAsync("/wiki?q=positive%20energy&category=gameplay", ct);
            Assert.Contains("href=\"/wiki/gadgets-and-loadouts\"", filtered, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/wiki/editor-lighting\"", filtered, StringComparison.Ordinal);
            Assert.Contains("method=\"get\" action=\"/wiki\"", filtered, StringComparison.Ordinal);
            Assert.Contains("value=\"gameplay\" selected", filtered, StringComparison.Ordinal);

            string unknownCategory = await http.GetStringAsync("/wiki?category=invalid-category", ct);
            Assert.Contains("href=\"/wiki/getting-started\"", unknownCategory, StringComparison.Ordinal);
            Assert.Contains("href=\"/wiki/editor-getting-started\"", unknownCategory, StringComparison.Ordinal);

            using HttpResponseMessage escapedResponse = await http.GetAsync("/wiki?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E", ct);
            string escaped = await escapedResponse.Content.ReadAsStringAsync(ct);
            Assert.Contains("No guides found", escaped, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", escaped, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert(1)</script>", escaped, StringComparison.Ordinal);
            Assert.False(escapedResponse.Headers.Contains("Set-Cookie"));
            Assert.Contains("default-src 'self'", escapedResponse.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

            using HttpResponseMessage missing = await http.GetAsync("/wiki/no-such-guide", ct);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("Guide not found", await missing.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

            using HttpResponseMessage style = await http.GetAsync("/web/assets/wiki.css", ct);
            Assert.Equal(HttpStatusCode.OK, style.StatusCode);
            Assert.Equal("text/css", style.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
