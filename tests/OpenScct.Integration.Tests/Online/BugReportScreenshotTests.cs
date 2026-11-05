using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Master.Player;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed partial class BugReportTests
{
    private const string ScreenshotPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGP4z8DA8J8BAAf/Af8Bf4mnAAAAAElFTkSuQmCC";

    [Theory]
    [InlineData(800)]
    [InlineData(830)]
    public async Task LargeScreenshotReviewAndDraftRecoveryDoNotNeedWritableTemporaryStorage(int side)
    {
        await using var website = new Website();
        await website.Start();
        string screenshot = Convert.ToBase64String(LargeScreenshotPng(side));
        var fields = website.Fields();
        fields["screenshot"] = screenshot;
        using var encoded = new FormUrlEncodedContent(fields);
        Assert.InRange(encoded.Headers.ContentLength!.Value, 2 * 1024 * 1024, 4 * 1024 * 1024);
        if (side == 830) Assert.True(Uri.EscapeDataString(screenshot).Length > ReportScreenshot.MaximumText,
            "A supported near-2 MiB PNG must exercise the URL-encoded field length beyond its decoded base64 limit.");
        using var recovery = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.Unauthorized, recovery.StatusCode);
        Assert.False(website.LastReportBodyUsedTemporaryFile, "Supported screenshots must not spool to a temporary file: the deployed master has a read-only system temp folder.");
        Assert.Contains(fields["title"], ReportDraft(await recovery.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);

        website.SignIn();
        fields["csrf"] = website.Csrf;
        using var review = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.False(website.LastReportBodyUsedTemporaryFile);
        var reviewed = HiddenFields(await review.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(screenshot, reviewed["screenshot"]);
        using var edit = await website.Post("edit", reviewed);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.False(website.LastReportBodyUsedTemporaryFile);
        Assert.Equal(screenshot, HiddenFields(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))["screenshot"]);
        Assert.Empty(website.GitHub.Requests);
    }

    private static byte[] LargeScreenshotPng(int side)
    {
        int width = side, height = side;
        byte[] pixels = new byte[height * (1 + width * 3)];
        new Random(891).NextBytes(pixels);
        for (int y = 0; y < height; y++) pixels[y * (1 + width * 3)] = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true)) zlib.Write(pixels);
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8; header[9] = 2;
        WritePngChunk(png, "IHDR"u8, header);
        WritePngChunk(png, "IDAT"u8, compressed.ToArray());
        WritePngChunk(png, "IEND"u8, []);
        byte[] result = png.ToArray();
        Assert.InRange(result.Length, 1_800_000, ReportScreenshot.MaximumBytes);
        Assert.NotNull(ReportScreenshot.Validate(result));
        return result;
    }

    private static void WritePngChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        png.Write(number); png.Write(type); png.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type) Add(value);
        foreach (byte value in data) Add(value);
        void Add(byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        png.Write(number);
    }

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
        Assert.Null(ReportScreenshot.Validate(new byte[ReportScreenshot.MaximumBytes + 1]));
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
