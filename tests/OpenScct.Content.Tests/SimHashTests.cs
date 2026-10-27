using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace OpenScct.Content.Tests;

/// <summary>The C# hashes against the ones the Python pipeline wrote into fixtures/testbox/manifest.json.</summary>
public sealed class SimHashTests
{
    private const string TestBoxSimHash = "cdf46bb734b4372fec38327aff84439f95ba7d6bb931af9b52ab117c33b0d346";

    [Fact]
    public void TestBoxSimHashEqualsThePipelineHash()
    {
        SvMapManifest manifest = SvMapManifest.Load(Path.Combine(TestPaths.TestBox, "manifest.json"));
        Assert.Equal(TestBoxSimHash, manifest.SimHash);
        Assert.Equal(TestBoxSimHash, SvMapHash.ComputeSimHash(TestPaths.TestBox));
    }

    [Fact]
    public void EveryFileDigestAndGroupHashEqualsTheManifest()
    {
        SvMapManifest manifest = SvMapManifest.Load(Path.Combine(TestPaths.TestBox, "manifest.json"));
        SvMapHashes hashes = SvMapHash.HashMap(TestPaths.TestBox);
        Assert.Equal(manifest.Files.Count, hashes.Files.Count);
        for (int i = 0; i < hashes.Files.Count; i++)
        {
            Assert.Equal(manifest.Files[i], hashes.Files[i]);
        }

        Assert.Equal(manifest.Hashes, hashes.Groups);
        Assert.Equal(["render", "sim"], hashes.Groups.Keys);
        Assert.Empty(manifest.Verify(TestPaths.TestBox));
    }

    [Fact]
    public void SimJsonIsHashedCanonicallySoLayoutDoesNotMatter()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        string path = copy.File("sim/traversal.json");
        string relaid = Json.CanonicalJson.ToPrettyString(Json.SvJson.Parse(File.ReadAllBytes(path)), indent: 4);
        File.WriteAllText(path, "\r\n  " + relaid.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n");
        Assert.Equal(TestBoxSimHash, SvMapHash.ComputeSimHash(copy.Path));

        // The manifest records raw sizes, so only the size check notices.
        SvMapManifest manifest = SvMapManifest.Load(copy.File("manifest.json"));
        List<string> problems = manifest.Verify(copy.Path);
        Assert.Single(problems);
        Assert.StartsWith("sim/traversal.json: size", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingAValueChangesTheHashAndNamesTheFile()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        string path = copy.File("sim/gameplay.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"GEActivationMask\": 4", "\"GEActivationMask\": 5", StringComparison.Ordinal));
        Assert.NotEqual(TestBoxSimHash, SvMapHash.ComputeSimHash(copy.Path));

        SvMapManifest manifest = SvMapManifest.Load(copy.File("manifest.json"));
        List<string> problems = manifest.Verify(copy.Path);
        Assert.Contains(problems, p => p.StartsWith("sim/gameplay.json: digest", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("hashes/sim:", StringComparison.Ordinal));

        var e = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path));
        Assert.Equal("sim/gameplay.json", e.File);
    }

    [Fact]
    public void BinaryFilesAreHashedRaw()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        string path = copy.File("sim/collision.bin");
        byte[] data = File.ReadAllBytes(path);
        data[^1] ^= 0x01;
        File.WriteAllBytes(path, data);
        Assert.NotEqual(TestBoxSimHash, SvMapHash.ComputeSimHash(copy.Path));
        var e = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path));
        Assert.Equal("sim/collision.bin", e.File);
    }

    [Fact]
    public void ExtraSimFilesJoinTheHashAndStrayFilesAreErrors()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        File.WriteAllText(copy.File("sim/extra.txt"), "x");
        Assert.NotEqual(TestBoxSimHash, SvMapHash.ComputeSimHash(copy.Path));

        File.Delete(copy.File("sim/extra.txt"));
        Assert.Equal(TestBoxSimHash, SvMapHash.ComputeSimHash(copy.Path));
        File.WriteAllText(copy.File("stray.txt"), "x");
        Assert.Throws<SvMapFormatException>(() => SvMapHash.HashMap(copy.Path));
    }

    [Fact]
    public void DifferingFilesNamesTheChangedSimFile()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        SvMapManifest original = SvMapManifest.Load(copy.File("manifest.json"));
        string text = File.ReadAllText(copy.File("manifest.json"));
        string traversalDigest = original.TryGetFile("sim/traversal.json", out SvMapFileDigest t) ? t.Sha256 : throw new InvalidOperationException();
        SvMapManifest changed = SvMapManifest.Parse(Encoding.UTF8.GetBytes(text.Replace(traversalDigest, new string('0', 64), StringComparison.Ordinal)));
        Assert.Equal(["sim/traversal.json"], original.DifferingFiles(changed));
        Assert.Empty(original.DifferingFiles(original));
        Assert.Empty(original.DifferingFiles(changed, "render"));
    }

    [Fact]
    public void PathsSortByUtf8Bytes()
    {
        // U+FF5E sorts before U+1F600 in UTF-8 but after its surrogates in UTF-16 ordinal order.
        var paths = new List<string> { "sim/😀.json", "sim/～.json", "sim/a.json", "sim/B.json" };
        paths.Sort(Utf8Order.Compare);
        Assert.Equal(["sim/B.json", "sim/a.json", "sim/～.json", "sim/😀.json"], paths);
    }
}
