using System;
using System.IO;
using System.Net.Http;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ContentVisualUpdateTests
{
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
            Assert.True(store.Has(updated with { Full = updated.Full with { Sha256 = "old-visuals" } }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
