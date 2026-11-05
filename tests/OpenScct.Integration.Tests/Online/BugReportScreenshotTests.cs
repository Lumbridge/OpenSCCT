using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Master.Player;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed partial class BugReportTests
{
    private const string ScreenshotPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGP4z8DA8J8BAAf/Af8Bf4mnAAAAAElFTkSuQmCC";

    [Fact]
    public async Task ScreenshotSurvivesEditingAndIsPublishedOnlyAfterExplicitConsent()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        var ct = TestContext.Current.CancellationToken;
        byte[] png = Convert.FromBase64String(ScreenshotPng);
        string path = ReportScreenshot.Route + ReportScreenshot.Id(png) + ".png";
        using (var missing = await website.Http.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var fields = website.Fields();
        fields["screenshot"] = ScreenshotPng;
        using var review = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        string html = await review.Content.ReadAsStringAsync(ct);
        Assert.Contains("data:image/png;base64," + ScreenshotPng, html, StringComparison.Ordinal);
        var submission = HiddenFields(html);
        using var edit = await website.Post("edit", submission);
        Assert.Equal(ScreenshotPng, HiddenFields(await edit.Content.ReadAsStringAsync(ct))["screenshot"]);
        using var noConsent = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Empty(website.GitHub.Requests);
        Assert.False(File.Exists(ReportScreenshot.PathFor(website.Ledger.Options, ReportScreenshot.Id(png))));
        submission["public"] = "yes";
        using var sent = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.SeeOther, sent.StatusCode);
        using var payload = JsonDocument.Parse(Assert.Single(website.GitHub.Requests).Body);
        Assert.Contains("![Game screenshot](" + website.Ledger.Options.PublicBaseUrl + path + ")",
            payload.RootElement.GetProperty("body").GetString()!, StringComparison.Ordinal);
        // GitHub and readers must be able to fetch the attachment without a player cookie.
        website.Http.DefaultRequestHeaders.Remove("Cookie");
        using var image = await website.Http.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync(ct));
        Assert.Equal("nosniff", Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
    }

    [Theory]
    [InlineData("not a png")]
    [InlineData("PHN2Zz48c2NyaXB0PmFsZXJ0KDEpPC9zY3JpcHQ+PC9zdmc+")]
    public async Task InvalidScreenshotKeepsTheDraftAndDoesNotPublish(string screenshot)
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        var fields = website.Fields();
        fields["screenshot"] = screenshot;
        using var review = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.BadRequest, review.StatusCode);
        string html = await review.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Choose a valid PNG screenshot", html, StringComparison.Ordinal);
        Assert.Contains(fields["title"], html, StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public void ScreenshotDecoderRejectsCorruptionAndTrailingContent()
    {
        byte[] png = Convert.FromBase64String(ScreenshotPng);
        Assert.Equal(png, ReportScreenshot.Decode(ScreenshotPng));
        byte[] corrupt = (byte[])png.Clone();
        corrupt[20] ^= 1;
        Assert.Null(ReportScreenshot.Decode(Convert.ToBase64String(corrupt)));
        Array.Resize(ref png, png.Length + 1);
        Assert.Null(ReportScreenshot.Decode(Convert.ToBase64String(png)));
        Assert.Null(ReportScreenshot.Decode(new string('A', ReportScreenshot.MaximumText + 1)));
        Assert.False(ReportScreenshot.ValidId("../accounts.db"));
    }

    [Fact]
    public async Task ScreenshotStorageFailureDoesNotCreateAnIssue()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        File.WriteAllText(website.Ledger.Options.ResolvePath("report-screenshots"), "blocks directory creation");
        var fields = website.Fields();
        fields["screenshot"] = ScreenshotPng;
        using var review = await website.Post("review", fields);
        var submission = HiddenFields(await review.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        submission["public"] = "yes";
        using var sent = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, sent.StatusCode);
        Assert.Contains("screenshot could not be saved", await sent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);
    }
}
