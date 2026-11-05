using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenScct.Master;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class HelpPageTests
{
    private sealed class EmptyReleaseFeed : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    [Fact]
    public async Task HelpIsPublicAndLinksToDiscordBugReporting()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), "openscct-help-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var handler = new EmptyReleaseFeed();
            using var upstream = new HttpClient(handler);
            var releases = new ReleaseChangelog(upstream);
            MasterOptions options = MasterTests.LocalOptions("private-invite-canary");
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = Path.Combine(directory, "accounts.db");
            options.TicketKeyFile = Path.Combine(directory, "tickets.pem");
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "private-secret-canary";
            await using MasterHost master = await MasterHost.StartAsync(options, releases, ct);
            using var http = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };
            http.DefaultRequestHeaders.Add("User-Agent", "private-browser-canary");
            using HttpResponseMessage response = await http.GetAsync("/help?token=private-token-canary&version=%3Cscript%3Ealert(1)%3C%2Fscript%3E&platform=private-platform-canary", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            string html = await response.Content.ReadAsStringAsync(ct);
            Assert.Contains("<h1>Help &amp; feedback</h1>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/help\" aria-current=\"page\"", html, StringComparison.Ordinal);
            Assert.Contains(MasterBuildInfo.Version, html, StringComparison.Ordinal);
            Assert.Contains("Suggest a feature", html, StringComparison.Ordinal);
            Assert.Contains("you do not need a GitHub account", html, StringComparison.Ordinal);
            Assert.DoesNotContain("private-", html, StringComparison.Ordinal);
            Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, html, StringComparison.Ordinal);
            Assert.Equal(0, handler.Requests);

            string bug = FindLink(html, "/help/report-bug");
            string suggestion = FindLink(html, "template=feature_request.yml");
            Assert.Equal("/help/report-bug", bug);
            Assert.DoesNotContain("template=bug_report.yml", html, StringComparison.Ordinal);
            Assert.Equal("github.com", new Uri(suggestion).Host);
            Assert.Contains("href=\"/wiki/contributing\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/wiki/editor-getting-started\"", html, StringComparison.Ordinal);
            Assert.Contains("/compare", html, StringComparison.Ordinal);

            foreach (string route in new[] { "/", "/download", "/changelog", "/improvements" })
            {
                string page = await http.GetStringAsync(route, ct);
                Assert.Contains("href=\"/help\"", page, StringComparison.Ordinal);
                Assert.Contains("Help &amp; feedback", page, StringComparison.Ordinal);
            }
            await CheckShowcaseImages(http, ct);
            var futurePage = Assert.IsType<ContentHttpResult>(WebHtml.Page(new DefaultHttpContext(), "Features", "improvements", "<h1>Features</h1>"));
            Assert.Contains("Help improve OpenSCCT", futurePage.ResponseContent!, StringComparison.Ordinal);
            var signedInPage = Assert.IsType<ContentHttpResult>(WebHtml.Page(new DefaultHttpContext(), "Help & feedback", "help",
                "<h1>Help &amp; feedback</h1>", name: "<Fixture player>", csrf: "fixture-csrf", isAdmin: true));
            Assert.Contains("href=\"/help\" aria-current=\"page\"", signedInPage.ResponseContent!, StringComparison.Ordinal);
            Assert.Contains("&lt;Fixture player&gt;", signedInPage.ResponseContent!, StringComparison.Ordinal);
            Assert.Contains("action=\"/account/logout\"", signedInPage.ResponseContent!, StringComparison.Ordinal);
            Assert.Contains("href=\"/admin\"", signedInPage.ResponseContent!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckShowcaseImages(HttpClient http, CancellationToken ct, [CallerFilePath] string sourceFile = "")
    {
        string images = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../docs/images/website"));
        string page = await http.GetStringAsync("/improvements", ct);
        foreach (string name in new[] { "armoury", "hub", "play", "player", "replays", "setup", "sidearm" })
        {
            string file = "showcase-" + name + ".png";
            byte[] source = await File.ReadAllBytesAsync(Path.Combine(images, file), ct);
            string hash = Convert.ToHexStringLower(SHA256.HashData(source));
            string url = "/web/assets/" + file + "?v=" + hash[..16];
            Assert.Contains(url, page, StringComparison.Ordinal);
            using HttpResponseMessage response = await http.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(await response.Content.ReadAsByteArrayAsync(ct))));
        }
    }

    private static string FindLink(string html, string marker)
    {
        foreach (Match match in Regex.Matches(html, "href=\"([^\"]+)\""))
            if (match.Groups[1].Value.Contains(marker, StringComparison.Ordinal))
                return WebUtility.HtmlDecode(match.Groups[1].Value);
        throw new InvalidOperationException("Missing link: " + marker);
    }
}
