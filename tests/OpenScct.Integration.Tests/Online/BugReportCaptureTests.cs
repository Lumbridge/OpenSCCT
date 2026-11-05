using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using OpenScct.Master.Player;
using OpenScct.Project;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed partial class BugReportTests
{
    private static async Task<string> UploadCapture(Website website)
    {
        using var content = new ByteArrayContent(Convert.FromBase64String(ScreenshotPng));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var response = await website.Http.PostAsync(ReportScreenshotDrafts.UploadRoute, content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("capture").GetString()!;
    }

    [Fact]
    public async Task CapturedScreenshotAttachesAutomaticallyAfterDiscordSignIn()
    {
        await using var website = new Website();
        await website.Start();
        string capture = await UploadCapture(website);
        Dictionary<string, string> expected = OAuthPrefill("game");
        expected["capture"] = capture;
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        var ct = TestContext.Current.CancellationToken;
        string signIn = await website.Http.GetStringAsync(OAuthReportPath(expected), ct);
        string loginPath = OAuthLoginLink(signIn);
        using var login = await website.Http.GetAsync(loginPath, ct);
        ApplyOAuthCookies(cookies, login);
        string destination = await CompleteOAuth(website, cookies, OAuthState(login));
        AssertOAuthPrefill(destination, expected);
        string html = await website.Http.GetStringAsync(destination, ct);
        Assert.Equal(ScreenshotPng, HiddenFields(html)["screenshot"]);
        Assert.Contains("data-report-screenshot", html, StringComparison.Ordinal);
        string imagePath = ReportScreenshot.Route + ReportScreenshot.Id(Convert.FromBase64String(ScreenshotPng)) + ".png";
        using var unpublished = await website.Http.GetAsync(imagePath, ct);
        Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);
        Assert.Empty(website.GitHub.Requests);
        // The editable draft contains the image itself, so editing no longer depends on the temporary token.
        Assert.DoesNotContain("name=\"capture\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureUploadRejectsInvalidImagesAndLimitsRepeatedUploads()
    {
        await using var website = new Website();
        await website.Start();
        using var badContent = new ByteArrayContent([1, 2, 3]);
        badContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var bad = await website.Http.PostAsync(ReportScreenshotDrafts.UploadRoute, badContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        for (int i = 0; i < 5; i++) await UploadCapture(website);
        using var extraContent = new ByteArrayContent(Convert.FromBase64String(ScreenshotPng));
        extraContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var extra = await website.Http.PostAsync(ReportScreenshotDrafts.UploadRoute, extraContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, extra.StatusCode);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task MissingCaptureKeepsReportFormWithAHelpfulNotice()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        string html = await website.Http.GetStringAsync("/help/report-bug?area=game&version=test&capture=" + new string('a', 64), TestContext.Current.CancellationToken);
        Assert.Contains("expired or is unavailable", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/help/report-bug/review\"", html, StringComparison.Ordinal);
        Assert.Equal("", HiddenFields(html)["screenshot"]);
    }

    [Fact]
    public void PrivateCaptureDraftsExpireAndHaveABoundedCapacity()
    {
        var clock = new Clock();
        var drafts = new ReportScreenshotDrafts(clock);
        byte[] png = Convert.FromBase64String(ScreenshotPng);
        Assert.True(drafts.Begin("one"));
        string first = drafts.Add(png);
        drafts.End();
        Assert.Equal(png, drafts.Find(first));
        Assert.Null(drafts.Find(new string('a', 64)));
        for (int i = 1; i < 32; i++)
        {
            Assert.True(drafts.Begin("ip" + i)); drafts.Add(png); drafts.End();
        }
        Assert.False(drafts.Begin("full"));
        clock.Advance(901);
        Assert.Null(drafts.Find(first));
        Assert.True(drafts.Begin("expired capacity reclaimed"));
        drafts.End();
    }

    [Fact]
    public void CaptureUrlRetainsItsTokenAndBoundsLongMatchDetails()
    {
        string token = new('b', 64);
        string url = ProjectLinks.Url(ProjectDestination.BugReport, new ReportContext
        {
            Source = ReportSource.Game, Version = new string('x', 128), Platform = new string('x', 80),
            Map = new string('Ω', 128), Mode = new string('Ω', 128), Side = new string('Ω', 128),
            PlayerLocation = new string('Ω', 2000),
        }, token);
        Assert.True(url.Length <= 4096);
        Assert.Equal(token, QueryHelpers.ParseQuery(new Uri(url).Query)["capture"].ToString());
    }
}
