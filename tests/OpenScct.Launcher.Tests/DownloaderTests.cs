using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Launcher.Update;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class DownloaderTests
{
    private static readonly byte[] Data = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
    private static readonly DownloadIdentity Id = new(42, Data.Length, DateTimeOffset.UnixEpoch);

    /// <summary>A server that honours Range requests, like GitHub's storage host.</summary>
    private static HttpResponseMessage RangeServer(HttpRequestMessage request)
    {
        if (request.Headers.Range?.Ranges.FirstOrDefault() is { From: { } from })
        {
            if (from >= Data.Length)
            {
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            }

            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Data[(int)from..]) };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Data.Length - 1, Data.Length);
            return partial;
        }

        return Builds.Bytes(Data);
    }

    [Fact]
    public async Task DownloadsWholeFile()
    {
        using var dir = new TempDir();
        string target = dir.Combine("game.zip");
        using var http = new HttpClient(new FakeHandler(RangeServer));

        await new ResumableDownloader(http).DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://x/a"), target, Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(Data, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(ResumableDownloader.PartialPath(target)));
    }

    [Fact]
    public async Task ResumesAPartialOfTheSameAsset()
    {
        using var dir = new TempDir();
        string target = dir.Combine("game.zip");
        await File.WriteAllBytesAsync(ResumableDownloader.PartialPath(target), Data[..100_000], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ResumableDownloader.IdentityPath(target), JsonSerializer.Serialize(Id), TestContext.Current.CancellationToken);
        var handler = new FakeHandler(RangeServer);
        using var http = new HttpClient(handler);

        await new ResumableDownloader(http).DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://x/a"), target, Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(100_000, handler.Requests.Single().Headers.Range!.Ranges.Single().From);
        Assert.Equal(Data, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestartsWhenThePartialBelongsToAnotherUpload()
    {
        using var dir = new TempDir();
        string target = dir.Combine("game.zip");
        await File.WriteAllBytesAsync(ResumableDownloader.PartialPath(target), new byte[100_000], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ResumableDownloader.IdentityPath(target), JsonSerializer.Serialize(Id with { AssetId = 7 }), TestContext.Current.CancellationToken);
        var handler = new FakeHandler(RangeServer);
        using var http = new HttpClient(handler);

        await new ResumableDownloader(http).DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://x/a"), target, Id, null, TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests.Single().Headers.Range);
        Assert.Equal(Data, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartsOverWhenTheServerIgnoresTheRange()
    {
        using var dir = new TempDir();
        string target = dir.Combine("game.zip");
        await File.WriteAllBytesAsync(ResumableDownloader.PartialPath(target), Data[..5000], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ResumableDownloader.IdentityPath(target), JsonSerializer.Serialize(Id), TestContext.Current.CancellationToken);
        using var http = new HttpClient(new FakeHandler(_ => Builds.Bytes(Data)));

        await new ResumableDownloader(http).DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://x/a"), target, Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(Data, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResumesAfterTheConnectionDrops()
    {
        using var dir = new TempDir();
        string target = dir.Combine("game.zip");
        int calls = 0;
        var handler = new FakeHandler(request =>
        {
            calls++;
            if (calls == 1)
            {
                // First answer is cut short.
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Data[..50_000]) };
            }

            return RangeServer(request);
        });
        using var http = new HttpClient(handler);

        await new ResumableDownloader(http).DownloadAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://x/a"), target, Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal(50_000, handler.Requests[1].Headers.Range!.Ranges.Single().From);
        Assert.Equal(Data, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }
}
