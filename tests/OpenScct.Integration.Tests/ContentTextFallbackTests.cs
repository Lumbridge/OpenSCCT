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
    public void OlderEditorUsesActiveSteamBaseAndRetainsTheAddonLibrary()
    {
        string install = Path.Combine(Path.GetTempPath(), "openscct-editor-" + Guid.NewGuid());
        string packs = Path.Combine(install, "packs");
        string content = Path.Combine(install, "content");
        string id = Guid.NewGuid().ToString("N");
        string generation = Path.Combine(content, "steam-builds", id);
        string exe = Path.Combine(install, "versions", "0.8.165");
        string? oldRoot = ContentPaths.Root;
        string oldPacks = ContentPaths.PacksFolder;
        try
        {
            Directory.CreateDirectory(Path.Combine(packs, "shared", "chars", "SPerso"));
            Directory.CreateDirectory(Path.Combine(packs, "svmap", "OptionalMap"));
            ContentPaths.ConfigureDesktop(packs, exe);
            Assert.Null(ContentPaths.Root);
            Assert.Null(ContentPaths.Shared);
            Assert.False(ContentPaths.SteamBaseReady);
            var catalog = new MapCatalog(Path.Combine(packs, "svmap"));
            Assert.Null(catalog.FindCharacterContent());

            foreach (string required in OpenScct.Maps.SteamContentInstall.RequiredFiles())
            {
                string path = Path.Combine(generation, required);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{}");
            }
            File.WriteAllText(Path.Combine(content, "steam-current.json"), $$"""{"format":1,"build":"{{id}}"}""");
            ContentPaths.ConfigureDesktop(packs, exe);
            Assert.True(ContentPaths.SteamBaseReady);
            Assert.Equal(generation, ContentPaths.Root);
            Assert.Equal(Path.Combine(generation, "svmap"), ContentPaths.MapRoots[0]);
            Assert.Equal(Path.Combine(packs, "svmap"), ContentPaths.MapRoots[1]);
            Assert.Equal(Path.Combine(generation, "shared", "chars"), catalog.FindCharacterContent());

            // Directly starting the exported game recovers the same base without launcher arguments.
            ContentPaths.ConfigureDesktop(null, exe);
            Assert.Equal(generation, ContentPaths.Root);
        }
        finally
        {
            ContentPaths.Configure(oldRoot, null, oldPacks);
            Directory.Delete(install, true);
        }
    }

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
