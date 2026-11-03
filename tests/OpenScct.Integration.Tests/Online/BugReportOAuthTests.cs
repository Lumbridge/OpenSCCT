using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed partial class BugReportTests
{
    [Theory]
    [InlineData("game")]
    [InlineData("launcher")]
    public async Task PrefilledReportSurvivesDiscordSignInAndReturnsToEditableForm(string area)
    {
        await using var website = new Website();
        await website.Start();
        Dictionary<string, string> expected = OAuthPrefill(area);
        string report = OAuthReportPath(expected);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        string signIn = await website.Http.GetStringAsync(report, TestContext.Current.CancellationToken);
        string loginPath = OAuthLoginLink(signIn);
        AssertOAuthPrefill(QueryHelpers.ParseQuery(new Uri(website.Http.BaseAddress!, loginPath).Query)["returnTo"].ToString(), expected);
        using HttpResponseMessage login = await website.Http.GetAsync(loginPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        ApplyOAuthCookies(cookies, login);

        string destination = await CompleteOAuth(website, cookies, OAuthState(login));

        AssertOAuthPrefill(destination, expected);
        string form = await website.Http.GetStringAsync(destination, TestContext.Current.CancellationToken);
        AssertOAuthForm(form, expected);
        Assert.Equal(2, website.DiscordRequests);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task CancelledDiscordSignInKeepsPrefillThroughRetryAndSuccess()
    {
        await using var website = new Website();
        await website.Start();
        Dictionary<string, string> expected = OAuthPrefill("game");
        string report = OAuthReportPath(expected);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login?returnTo=" + Uri.EscapeDataString(report), TestContext.Current.CancellationToken);
        string firstState = OAuthState(login);
        ApplyOAuthCookies(cookies, login);
        SendOAuthCookies(website, cookies);

        using HttpResponseMessage cancel = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(firstState) + "&error=access_denied", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
        string retryPath = OAuthLoginLink(await cancel.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertOAuthPrefill(QueryHelpers.ParseQuery(new Uri(website.Http.BaseAddress!, retryPath).Query)["returnTo"].ToString(), expected);
        ApplyOAuthCookies(cookies, cancel);
        Assert.DoesNotContain(cookies.Keys, key => key.StartsWith("svm_player_return", StringComparison.Ordinal));
        Assert.Equal(0, website.DiscordRequests);
        SendOAuthCookies(website, cookies);
        using HttpResponseMessage retry = await website.Http.GetAsync(retryPath, TestContext.Current.CancellationToken);
        Assert.NotEqual(firstState, OAuthState(retry));
        ApplyOAuthCookies(cookies, retry);

        string destination = await CompleteOAuth(website, cookies, OAuthState(retry));

        AssertOAuthPrefill(destination, expected);
        AssertOAuthForm(await website.Http.GetStringAsync(destination, TestContext.Current.CancellationToken), expected);
        Assert.Equal(2, website.DiscordRequests);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task NearLimitReportUsesBrowserSizedCookieChunksAndPreservesAllPrefill()
    {
        await using var website = new Website();
        await website.Start();
        Dictionary<string, string> expected = LongOAuthPrefill();
        string report = OAuthReportPath(expected);
        Assert.InRange(report.Length, 4000, 4096);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        string signIn = await website.Http.GetStringAsync(report, TestContext.Current.CancellationToken);
        using HttpResponseMessage login = await website.Http.GetAsync(OAuthLoginLink(signIn), TestContext.Current.CancellationToken);
        string[] returnHeaders = login.Headers.GetValues("Set-Cookie")
            .Where(header => header.StartsWith("svm_player_return", StringComparison.Ordinal)).ToArray();
        Assert.Contains(returnHeaders, header => header.StartsWith("svm_player_return=chunks-", StringComparison.Ordinal));
        Assert.True(returnHeaders.Count(header => header.StartsWith("svm_player_returnC", StringComparison.Ordinal)) >= 2);
        Assert.All(returnHeaders, header => Assert.InRange(header.Length, 1, 4050));
        ApplyOAuthCookies(cookies, login);

        string destination = await CompleteOAuth(website, cookies, OAuthState(login));

        AssertOAuthPrefill(destination, expected);
        AssertOAuthForm(await website.Http.GetStringAsync(destination, TestContext.Current.CancellationToken), expected);
        Assert.DoesNotContain(cookies.Keys, key => key.StartsWith("svm_player_return", StringComparison.Ordinal));
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData("missing-chunk")]
    [InlineData("tampered-chunk")]
    [InlineData("stale-nonce")]
    public async Task DamagedOrStaleChunkedReturnCookiesFallBackToAccount(string damage)
    {
        await using var website = new Website();
        await website.Start();
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login?returnTo=" + Uri.EscapeDataString(OAuthReportPath(LongOAuthPrefill())), TestContext.Current.CancellationToken);
        string state = OAuthState(login);
        ApplyOAuthCookies(cookies, login);
        string[] chunks = cookies.Keys.Where(key => key.StartsWith("svm_player_returnC", StringComparison.Ordinal)).Order().ToArray();
        Assert.True(chunks.Length >= 2);
        if (damage == "missing-chunk") cookies.Remove(chunks[^1]);
        else if (damage == "tampered-chunk") cookies[chunks[0]] = "invalid-return-data";
        else
        {
            // Retain a prior attempt's report chunks but use the fresh account login's valid nonce.
            using HttpResponseMessage fresh = await website.Http.GetAsync("/auth/player/login", TestContext.Current.CancellationToken);
            var freshCookies = new Dictionary<string, string>(StringComparer.Ordinal);
            ApplyOAuthCookies(freshCookies, fresh);
            cookies["svm_player_state"] = freshCookies["svm_player_state"];
            state = OAuthState(fresh);
        }

        Assert.Equal("/account", await CompleteOAuth(website, cookies, state));
        Assert.Equal(2, website.DiscordRequests);
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData("https://outside.invalid/report")]
    [InlineData("//outside.invalid/report")]
    [InlineData("/unrecognized-report")]
    [InlineData("/help/report-bug#unexpected-fragment")]
    public async Task UnknownLoginDestinationsCannotRedirectOutsideTheAccount(string target)
    {
        await using var website = new Website();
        await website.Start();
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login?returnTo=" + Uri.EscapeDataString(target), TestContext.Current.CancellationToken);
        ApplyOAuthCookies(cookies, login);
        Assert.DoesNotContain(cookies.Keys, key => key.StartsWith("svm_player_return", StringComparison.Ordinal));

        Assert.Equal("/account", await CompleteOAuth(website, cookies, OAuthState(login)));
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData("https://outside.invalid/report")]
    [InlineData("/account/security")]
    [InlineData("/help/report-bug#unexpected-fragment")]
    public async Task CallbackRevalidatesEvenANonceMatchingEncodedReturnCookie(string target)
    {
        await using var website = new Website();
        await website.Start();
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login", TestContext.Current.CancellationToken);
        ApplyOAuthCookies(cookies, login);
        string state = OAuthState(login);
        cookies["svm_player_return"] = state["player.".Length..] + "|" + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(target));

        Assert.Equal("/account", await CompleteOAuth(website, cookies, state));
        Assert.Empty(website.GitHub.Requests);
    }

    private static Dictionary<string, string> OAuthPrefill(string area) => new(StringComparer.Ordinal)
    {
        ["area"] = area,
        ["version"] = area == "launcher" ? "OpenSCCT launcher 0.8.32" : "OpenSCCT game 0.8.77",
        ["platform"] = "Windows 11",
        ["environment"] = area == "launcher"
            ? "Source: OpenSCCT launcher\nLauncher version: 0.8.32\nInstalled game version: 0.8.76\nBuild: 20261103.1800\nCommit: abcdef0"
            : "Source: OpenSCCT game\nGame version: 0.8.77\nMap: AquaE\nMode: Story\nSide: Spy\nSession: Online client",
    };

    private static Dictionary<string, string> LongOAuthPrefill()
    {
        Dictionary<string, string> values = OAuthPrefill("game");
        while (true)
        {
            string previous = values["environment"];
            values["environment"] += "Ω";
            if (OAuthReportPath(values).Length > 4080) { values["environment"] = previous; return values; }
        }
    }

    private static string OAuthReportPath(Dictionary<string, string> fields) =>
        QueryHelpers.AddQueryString("/help/report-bug", fields.Select(field => new KeyValuePair<string, string?>(field.Key, field.Value)));

    private static string OAuthLoginLink(string html) => Assert.Single(Regex.Matches(html, "href=\"([^\"]+)\"")
        .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)),
        href => href.StartsWith("/auth/player/login", StringComparison.Ordinal));

    private static string OAuthState(HttpResponseMessage login)
    {
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();
    }

    private static void ApplyOAuthCookies(Dictionary<string, string> cookies, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return;
        foreach (string header in headers)
        {
            string pair = header.Split(';')[0];
            int equals = pair.IndexOf('=');
            string name = pair[..equals], value = pair[(equals + 1)..];
            if (value.Length == 0) cookies.Remove(name); else cookies[name] = value;
        }
    }

    private static void SendOAuthCookies(Website website, Dictionary<string, string> cookies)
    {
        website.Http.DefaultRequestHeaders.Remove("Cookie");
        if (cookies.Count > 0) website.Http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(pair => pair.Key + "=" + pair.Value)));
    }

    private static async Task<string> CompleteOAuth(Website website, Dictionary<string, string> cookies, string state)
    {
        SendOAuthCookies(website, cookies);
        using HttpResponseMessage callback = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(state) + "&code=fake-local-code", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        string html = await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Match hop = Regex.Match(html, "<meta http-equiv=\"refresh\" content=\"0;url=([^\"]+)\">");
        Assert.True(hop.Success, "Successful Discord sign-in must issue its same-site session-cookie hop.");
        Assert.DoesNotContain("test-discord-access-token", html, StringComparison.Ordinal);
        string sessionCookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), header => header.StartsWith("svm_player=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        ApplyOAuthCookies(cookies, callback);
        SendOAuthCookies(website, cookies);
        return WebUtility.HtmlDecode(hop.Groups[1].Value);
    }

    private static void AssertOAuthPrefill(string destination, Dictionary<string, string> expected)
    {
        var uri = new Uri(new Uri("https://website.invalid"), destination);
        Assert.Equal("website.invalid", uri.Host);
        Assert.Equal("/help/report-bug", uri.AbsolutePath);
        var fields = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(expected.Count, fields.Count);
        foreach (var field in expected) Assert.Equal(field.Value, fields[field.Key].ToString());
    }

    private static void AssertOAuthForm(string html, Dictionary<string, string> expected)
    {
        Assert.Contains("action=\"/help/report-bug/review\"", html, StringComparison.Ordinal);
        Assert.NotEmpty(HiddenFields(html)["csrf"]);
        Assert.Matches("<option value=\"" + expected["area"] + "\" selected(?:=\"[^\"]*\")?>", html);
        foreach (string name in new[] { "version", "platform", "environment" })
        {
            Match field = name == "environment"
                ? Regex.Match(html, "<textarea[^>]*name=\"" + name + "\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline)
                : Regex.Match(html, "<input[^>]*name=\"" + name + "\"[^>]*value=\"([^\"]*)\"");
            Assert.True(field.Success, "The report must expose an editable " + name + " field.");
            Assert.Equal(expected[name], WebUtility.HtmlDecode(field.Groups[1].Value));
        }
    }
}
