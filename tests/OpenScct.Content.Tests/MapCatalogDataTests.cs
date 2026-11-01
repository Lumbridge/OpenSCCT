using System;
using System.IO;
using System.Text;
using OpenScct.Content.Maps;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class MapCatalogDataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bundled_loading_art_fills_missing_or_stale_shared_images(bool stale)
    {
        string root = Path.Combine(Path.GetTempPath(), "svm-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            string shared = Path.Combine(root, "shared");
            string map = Path.Combine(root, "map");
            string bundled = Path.Combine(map, "catalog");
            Directory.CreateDirectory(shared);
            Directory.CreateDirectory(bundled);
            File.WriteAllText(Path.Combine(shared, "menu.png"), "shared");
            foreach (string kind in new[] { "menu", "loading-spy", "loading-merc" })
                File.WriteAllText(Path.Combine(bundled, kind + ".png"), "bundled");
            File.WriteAllText(Path.Combine(bundled, "entry.json"), """
                {"code":"Example","images":{"menu":"menu.png","loading-spy":"loading-spy.png","loading-merc":"loading-merc.png"}}
                """);
            string extra = stale ? ",\"loading-spy\":\"missing.png\"" : "";
            var catalog = MapCatalogData.Parse(Encoding.UTF8.GetBytes(
                "{\"format\":\"svm-map-catalog\",\"images\":{\"Example\":{\"menu\":\"menu.png\"" + extra + "}}}"), shared);
            var merged = catalog.WithMapEntries([map]);
            Assert.Equal(Path.Combine(shared, "menu.png"), merged.Image("Example", "menu"));
            Assert.Equal(Path.Combine(bundled, "loading-spy.png"), merged.Image("Example", "loading-spy"));
            Assert.Equal(Path.Combine(bundled, "loading-merc.png"), merged.Image("Example", "loading-merc"));
            Assert.Null(catalog.Image("Example", "loading-spy"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
