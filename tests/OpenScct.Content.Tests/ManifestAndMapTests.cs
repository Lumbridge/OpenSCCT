using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Content.Collision;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class ManifestAndMapTests
{
    [Fact]
    public void TestBoxManifest()
    {
        SvMapManifest m = SvMapManifest.Parse(TestPaths.TestBoxFile("manifest.json"));
        Assert.Equal("1.0.0", m.FormatVersion);
        Assert.Equal("TestBox", m.Map);
        Assert.Equal("TestBox", m.Title);
        Assert.Equal("synthetic", m.Source.Kind);
        Assert.Equal("svmbuild.testbox", m.Source.Generator);
        Assert.Equal(1, m.Source.GeneratorVersion);
        Assert.False(m.Source.IsConverted);
        Assert.Null(m.Source.OriginalFile);
        Assert.Equal(["render/world.glb", "sim/collision.bin", "sim/gameplay.json", "sim/traversal.json"], m.Files.Select(f => f.Path));
        Assert.True(m.TryGetFile("sim/collision.bin", out SvMapFileDigest coll));
        Assert.Equal(13600, coll.Size);
        Assert.Equal("cdf46bb734b4372fec38327aff84439f95ba7d6bb931af9b52ab117c33b0d346", m.SimHash);
        Assert.Equal(2, m.Hashes.Count);
        Assert.Empty(m.Requires);
    }

    [Fact]
    public void ConvertedSourcesCarryTheOriginal()
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("manifest.json")).Replace(
            "\"source\": {\"generator\": \"svmbuild.testbox\", \"generatorVersion\": 1, \"kind\": \"synthetic\"}",
            "\"source\": {\"generator\": \"svmbuild.maps\", \"generatorVersion\": 2, \"kind\": \"converted\", " +
            "\"original\": {\"file\": \"Packages/Maps/X.sdc\", \"sha256\": \"" + new string('a', 64) + "\"}}",
            StringComparison.Ordinal);
        SvMapManifest m = SvMapManifest.Parse(Encoding.UTF8.GetBytes(text));
        Assert.True(m.Source.IsConverted);
        Assert.Equal("Packages/Maps/X.sdc", m.Source.OriginalFile);
        Assert.Equal(new string('a', 64), m.Source.OriginalSha256);
    }

    [Theory]
    [InlineData("\"format\": \"svmap\"", "\"format\": \"other\"", "expected 'svmap'")]
    [InlineData("\"sim\": \"cdf46bb7", "\"sim\": \"CDF46BB7", "lowercase hex")]
    [InlineData("\"hashes\": {", "\"hashes\": {\"x\": \"" + "0000000000000000000000000000000000000000000000000000000000000000" + "\", ", "")]
    [InlineData("\"kind\": \"synthetic\"", "\"kind\": \"mystery\"", "unknown source kind")]
    [InlineData("\"requires\": []", "\"requires\": {}", "expected an array")]
    public void MalformedManifestsAreRejected(string from, string to, string reason)
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("manifest.json"));
        Assert.Contains(from, text, StringComparison.Ordinal);
        byte[] changed = Encoding.UTF8.GetBytes(text.Replace(from, to, StringComparison.Ordinal));
        if (reason.Length == 0)
        {
            // Unknown groups are kept as data; the validator flags them, the reader does not.
            Assert.Equal(3, SvMapManifest.Parse(changed).Hashes.Count);
            return;
        }

        var e = Assert.Throws<SvMapFormatException>(() => SvMapManifest.Parse(changed));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.Equal("manifest.json", e.File);
    }

    [Fact]
    public void SvMapLoadsTheTestBox()
    {
        SvMap map = SvMap.Load(TestPaths.TestBox);
        Assert.Equal("TestBox", map.Id);
        Assert.Equal(map.Manifest.SimHash, map.SimHash);
        Assert.Equal(["sim/collision.bin", "sim/gameplay.json", "sim/traversal.json"], map.SimFiles.Select(f => f.Path));
        Assert.Equal(20, map.Gameplay.Actors.Count);
        Assert.Equal(3, map.Traversal.Ges.Count);
        Assert.Equal(3, map.Collision.Colliders.Length);
    }

    [Fact]
    public void SvMapRejectsMismatchedMapIdsAndUnknownCollisionActors()
    {
        using (ScratchDir copy = TestPaths.CopyTestBox())
        {
            string path = copy.File("sim/traversal.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"map\": \"TestBox\"", "\"map\": \"Other\"", StringComparison.Ordinal));
            var e = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path, new SvMapLoadOptions { VerifySimHash = false }));
            Assert.Contains("map id differs", e.Message, StringComparison.Ordinal);
        }

        using (ScratchDir copy = TestPaths.CopyTestBox())
        {
            string path = copy.File("sim/collision.bin");
            byte[] data = File.ReadAllBytes(path);
            byte[] renamed = Encoding.UTF8.GetBytes("Mover0");
            int at = data.AsSpan().IndexOf(renamed);
            data[at] = (byte)'N';
            File.WriteAllBytes(path, data);
            var e = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path, new SvMapLoadOptions { VerifySimHash = false }));
            Assert.Equal(CollisionReader.RelativePath, e.File);
            Assert.Contains("'Nover0', which is not an actor id", e.Message, StringComparison.Ordinal);
            SvMap lenient = SvMap.Load(copy.Path, new SvMapLoadOptions { VerifySimHash = false, CheckReferences = false });
            Assert.Equal("Nover0", lenient.Collision.ActorOf(lenient.Collision.Colliders[1]));
        }
    }

    [Fact]
    public void MissingSimFilesAreNamed()
    {
        using ScratchDir copy = TestPaths.CopyTestBox();
        File.Delete(copy.File("sim/traversal.json"));
        var e = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path, new SvMapLoadOptions { VerifySimHash = false }));
        Assert.Equal("sim/traversal.json", e.File);
        var hashed = Assert.Throws<SvMapFormatException>(() => SvMap.Load(copy.Path));
        Assert.Equal("sim/traversal.json", hashed.File);
    }
}
