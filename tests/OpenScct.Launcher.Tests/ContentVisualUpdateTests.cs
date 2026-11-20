using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Content;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ContentVisualUpdateTests
{
    [Fact]
    public async Task UnrealUpdatesAnOlderCharacterPackOnceAndKeepsOfflineInstallsPlayable()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using var root = new TempDir();
        using var zipBytes = new MemoryStream();
        using (var archive = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("chars/models.json").Open()))
            writer.Write("updated models");
        byte[] bytes = zipBytes.ToArray();
        string digest = Builds.Sha256(bytes);
        string indexJson = JsonSerializer.Serialize(new
        {
            format = ContentIndex.Format, formatVersion = 1,
            shared = new[] { new { id = "chars", version = "1", contents = new[] { "chars" },
                download = new { url = "https://example.invalid/chars.zip", size = bytes.Length, sha256 = digest } } },
        });
        bool offline = false;
        var handler = new FakeHandler(req =>
        {
            if (offline) throw new HttpRequestException("Offline");
            if (req.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
                return Builds.Bytes(bytes);
            if (req.Headers.IfNoneMatch.SingleOrDefault()?.Tag == "\"v2\"")
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            HttpResponseMessage response = Builds.Text(indexJson);
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return response;
        });
        using var http = new HttpClient(handler);
        var store = new ContentStore(root.Path, http);
        string receipt = root.Combine("installed.json");
        string oldReceipt = """[{"id":"shared/chars","code":"chars","version":"1","sha256":"old"}]""";
        await File.WriteAllTextAsync(receipt, oldReceipt, cancel);
        Directory.CreateDirectory(root.Combine("shared", "chars"));
        string model = root.Combine("shared", "chars", "models.json");
        await File.WriteAllTextAsync(model, "old models", cancel);

        ContentIndex online = await store.FetchIndexAsync(cancel);
        Assert.True(PackedContent.HasOnlineSharedUpdate(online, store));
        offline = true;
        ContentIndex cached = await store.FetchIndexAsync(cancel);
        Assert.True(cached.FromOfflineCache);
        Assert.False(PackedContent.HasOnlineSharedUpdate(cached, store));
        Assert.Equal("old models", await File.ReadAllTextAsync(model, cancel));
        offline = false;
        ContentIndex revalidated = await store.FetchIndexAsync(cancel);
        Assert.True(PackedContent.HasOnlineSharedUpdate(revalidated, store));
        await store.InstallSharedAsync(Assert.Single(PackedContent.SharedToFetch(revalidated, store)), null, cancel);
        Assert.Equal("updated models", await File.ReadAllTextAsync(model, cancel));
        Assert.Equal(digest, Assert.Single(store.Installed()).Sha256);
        Assert.False(PackedContent.HasOnlineSharedUpdate(await store.FetchIndexAsync(cancel), store));
        Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath == "/chars.zip");
    }

    [Fact]
    public void SameGameplayHashDoesNotHideAFullPackVisualUpdate()
    {
        string root = Path.Combine(Path.GetTempPath(), "openscct-visual-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "svmap", "Example"));
            File.WriteAllText(Path.Combine(root, "svmap", "Example", "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(root, "installed.json"), """
                [{"id":"stock/Example","code":"Example","version":"1.0.1","sha256":"old-visuals","kind":0,"contentHash":"same-gameplay"}]
                """);
            using var http = new HttpClient();
            var store = new ContentStore(root, http);
            var updated = new ContentMap { Id = "stock/Example", Code = "Example", Version = "1.0.2", ContentHash = "same-gameplay",
                Full = new("https://example.invalid/full.zip", 1, "new-visuals"), Sim = new("https://example.invalid/sim.zip", 1, "sim") };
            Assert.False(store.Has(updated));
            Assert.True(store.Has(updated, ContentKind.Sim));
            Assert.True(store.Has(updated with { Full = updated.Full! with { Sha256 = "old-visuals" } }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
