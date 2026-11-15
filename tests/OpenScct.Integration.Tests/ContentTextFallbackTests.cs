using System;
using System.IO;
using OpenScct.Core.Uc;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

[Collection(BotMatchCollection.Name)]
public sealed class ContentTextFallbackTests
{
    [Fact]
    public void MapOnlyRootUsesPackTextAndExplicitTextStillWins()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openscct-text-" + Guid.NewGuid());
        string root = Path.Combine(folder, "content"), packs = Path.Combine(folder, "packs");
        string? oldRoot = ContentPaths.Root;
        string oldPacks = ContentPaths.PacksFolder;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(packs, "shared"));
            string table = """
                {"classes":{"Test.Item":{"properties":[{"name":"ItemName","flags":"0x8000"}],"defaults":{"ItemName":"Pack name"}}}}
                """;
            File.WriteAllText(Path.Combine(packs, UcText.RelativePath), table);
            ContentPaths.Configure(root, null, packs);
            Assert.Equal("Pack name", UcText.Get("Test.Item", "ItemName", "missing"));
            Directory.CreateDirectory(Path.Combine(root, "shared"));
            File.WriteAllText(Path.Combine(root, UcText.RelativePath), table.Replace("Pack name", "Local name"));
            ContentPaths.Configure(root, null, packs);
            Assert.Equal("Local name", UcText.Get("Test.Item", "ItemName", "missing"));
        }
        finally
        {
            ContentPaths.Configure(oldRoot, null, oldPacks);
            Directory.Delete(folder, true);
        }
    }
}
