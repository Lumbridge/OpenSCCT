using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class WebsiteNavigationTests
{
    private sealed class EmptyReleases : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    [Fact]
    public async Task PublicPagesLinkToWorkingWikiArticlesAndVersionedAssets()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), "openscct-website-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var feed = new HttpClient(new EmptyReleases());
            MasterOptions options = MasterTests.LocalOptions("website-fixture");
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = Path.Combine(directory, "accounts.db");
            options.TicketKeyFile = Path.Combine(directory, "tickets.pem");
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using MasterHost master = await MasterHost.StartAsync(options, new ReleaseChangelog(feed), ct);
            using var http = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };
            var guides = new HashSet<string>(StringComparer.Ordinal);
            var assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string route in new[] { "/", "/community", "/projects/ps2", "/projects/pc", "/projects/godot", "/projects/unreal",
                "/progress", "/mods", "/tools", "/legal", "/projects/ps2/decomp", "/projects/pc/decomp", "/help", "/download", "/improvements", "/leaderboards", "/wiki", "/wiki/game", "/wiki/editor" })
            {
                string html = await http.GetStringAsync(route, ct);
                Assert.Contains("href=\"/wiki\"", html, StringComparison.Ordinal);
                Assert.Contains("href=\"/legal\"", html, StringComparison.Ordinal);
                foreach (Match match in Regex.Matches(html, "(?:href|src)=\"([^\"]+)\""))
                {
                    string target = WebUtility.HtmlDecode(match.Groups[1].Value);
                    if (target.StartsWith("/wiki/", StringComparison.Ordinal)) guides.Add(target);
                    if (target.StartsWith("/web/assets/", StringComparison.Ordinal)) assets.Add(target);
                }
                foreach (Match match in Regex.Matches(html, "href=\"#([^\"]+)\""))
                    Assert.Contains("id=\"" + match.Groups[1].Value + "\"", html, StringComparison.Ordinal);
            }
            Assert.True(guides.Count >= 15, "Public pages should expose gameplay and editor guides.");
            foreach (WikiContent.Article article in WikiContent.Articles)
                Assert.Contains("/wiki/" + article.Slug, guides);
            foreach (string target in guides)
            {
                string[] parts = target.Split('#', 2);
                string html = await http.GetStringAsync(parts[0], ct);
                if (parts.Length == 2) Assert.Contains("id=\"" + parts[1] + "\"", html, StringComparison.Ordinal);
                if (WikiContent.Find(parts[0]["/wiki/".Length..]) is { } article)
                {
                    string mark = article.Category == "editor" ? "mark-editor.svg" : "mark.svg";
                    Assert.Contains("<link rel=\"icon\" href=\"" + WebHtml.AssetUrl("/web/assets/" + mark) + "\"", html, StringComparison.Ordinal);
                    Assert.Equal(article.Category == "editor", html.Contains("class=\"player-site editor-theme\"", StringComparison.Ordinal));
                    Assert.Contains("href=\"/wiki/" + (article.Category == "editor" ? "editor" : "game") + "\"", html, StringComparison.Ordinal);
                }
            }
            foreach (string asset in assets)
            {
                Assert.Matches("\\?v=[a-f0-9]{16}$", asset);
                using HttpResponseMessage response = await http.GetAsync(asset, ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Content.Headers.ContentLength > 0);
            }
            string home = await http.GetStringAsync("/", ct);
            string progress = await http.GetStringAsync("/progress", ct);
            string mods = await http.GetStringAsync("/mods", ct);
            foreach (string edition in new[] { "ps2", "pc", "godot", "unreal" })
            {
                Assert.Contains("href=\"/projects/" + edition + "\"", home, StringComparison.Ordinal);
                Assert.Contains("id=\"" + edition + "\"", progress, StringComparison.Ordinal);
                Assert.Contains("id=\"" + edition + "\"", mods, StringComparison.Ordinal);
            }
            string originals = Regex.Match(home, "<section[^>]+id=\"originals\".*?</section>", RegexOptions.Singleline).Value;
            string modernisations = Regex.Match(home, "<section[^>]+id=\"modernisations\".*?</section>", RegexOptions.Singleline).Value;
            Assert.Contains("/projects/ps2", originals, StringComparison.Ordinal);
            Assert.Contains("/projects/pc", originals, StringComparison.Ordinal);
            Assert.DoesNotContain("/projects/godot", originals, StringComparison.Ordinal);
            Assert.Contains("/projects/godot", modernisations, StringComparison.Ordinal);
            Assert.Contains("/projects/unreal", modernisations, StringComparison.Ordinal);
            string godot = await http.GetStringAsync("/projects/godot", ct);
            Assert.Contains("11 match maps", godot, StringComparison.Ordinal);
            Assert.Contains("your installed Steam copy", godot, StringComparison.Ordinal);
            Assert.Contains("optional Enhanced variants", godot, StringComparison.Ordinal);
            foreach (string step in new[] { "Disassemble", "Rewrite", "Verify" })
                Assert.Contains("<h3>" + step + "</h3>", home, StringComparison.Ordinal);
            foreach (string title in new[] { "Unofficial", "No game files", "Your own copy" })
                Assert.Contains("<h3>" + title + "</h3>", home, StringComparison.Ordinal);
            string search = await http.GetStringAsync("/search?q=spimdisasm", ct);
            Assert.Contains("href=\"/tools#decomp-tools\"", search, StringComparison.Ordinal);
            string searchMiss = await http.GetStringAsync("/search?q=%3Cscript%3E", ct);
            Assert.Contains("No results", searchMiss, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", searchMiss, StringComparison.Ordinal);
            Assert.Contains("Full report and sources", home, StringComparison.Ordinal);
            Assert.Contains("Report source", progress, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/projects/unknown", ct)).StatusCode);
            string help = await http.GetStringAsync("/help", ct);
            Assert.Contains("<details>", help, StringComparison.Ordinal);
            Assert.Contains("/wiki/troubleshooting", help, StringComparison.Ordinal);
            Assert.DoesNotContain("/blob/main/docs/README.md", help, StringComparison.Ordinal);
            string invalid = await http.GetStringAsync("/leaderboards?sort=%3Cscript%3E&page=invalid", ct);
            Assert.Contains("<option value=\"xp\" selected>", invalid, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", invalid, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
