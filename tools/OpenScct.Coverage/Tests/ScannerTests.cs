using System;
using System.IO;
using System.Linq;
using OpenScct.Provenance;
using Xunit;

namespace OpenScct.Coverage.Tests;

public sealed class ScannerTests
{
    [Fact]
    public void ReadsProvenanceAttributesFromMetadata()
    {
        ScannedAssembly? a = AttributeScanner.Scan(typeof(ScannerTests).Assembly.Location);
        Assert.NotNull(a);
        Assert.Equal("OpenScct.Coverage.Tests", a.Name);

        FoundAttribute portOf = Assert.Single(a.Attributes, x => x.Kind == ProvenanceAttributeKind.PortOf && x.Value == "uc:SBase.SFoo:Trigger");
        Assert.Equal("spec/devices/foo.md#rules", portOf.Spec);
        Assert.Equal("OpenScct.Coverage.Tests.Sample.Ported.Trigger", portOf.Location);

        FoundAttribute uc = Assert.Single(a.Attributes, x => x.Kind == ProvenanceAttributeKind.Uc && x.Value == "SBase.SFoo.Trigger");
        Assert.Equal("aa", uc.Sha1);
        Assert.Single(a.Attributes, x => x.Kind == ProvenanceAttributeKind.UcNative && x.Value == "AActor::Tick" && x.Location.EndsWith(".Tick", StringComparison.Ordinal));
        Assert.Single(a.Attributes, x => x.Kind == ProvenanceAttributeKind.PortOf && x.Value == "def:SBase.SFoo" && x.Location == "OpenScct.Coverage.Tests.Sample.Ported.Speed");
        Assert.Single(a.Attributes, x => x.Value == "eng:Render.View" && x.Location == "OpenScct.Coverage.Tests.Sample.Ported");

        GeneratedClass g = Assert.Single(a.GeneratedClasses, c => c.Name == "SFake");
        Assert.Equal("OpenScct.Game.SBase", g.Namespace);
        Assert.Equal(["s_Active", "s_Idle"], g.States);
    }

    [Fact]
    public void SkipsFilesThatAreNotAssemblies()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);
            Assert.Null(AttributeScanner.Scan(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheRepositoryInputsAreConsistent()
    {
        string? repo = FindRepo();
        Assert.SkipWhen(repo is null, "not run from a checkout");
        Inventory inv = Inventory.Load(Path.Combine(repo!, "re", "inventory", "inventory.json"));
        Assert.Equal(inv.Units.Count, inv.Units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count());

        StatusFile status = StatusFile.Load(Path.Combine(repo!, "coverage", "status.yaml"));
        Assert.Empty(status.Errors);
        foreach (NaEntry e in status.Na)
        {
            Assert.All(e.Globs, g => Assert.True(inv.Units.Any(u => g.IsMatch(u.Id)), $"n-a glob {g.Pattern} matches no unit"));
        }

        Assert.True(SpecUnits.ReadAll(repo!).Count > 1000);
    }

    private static string? FindRepo()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return d.FullName;
            }
        }

        return null;
    }
}

/// <summary>Types carrying provenance attributes, read back by <see cref="ScannerTests"/>.</summary>
internal static class Sample
{
    [PortOf("eng:Render.View", Spec = "docs/RENDERING.md#view")]
    internal static class Ported
    {
        [PortOf("def:SBase.SFoo")]
        public static float Speed { get; } = 1f;

        [Uc("SBase.SFoo.Trigger", Sha1 = "aa")]
        [PortOf("uc:SBase.SFoo:Trigger", Spec = "spec/devices/foo.md#rules")]
        public static void Trigger()
        {
        }

        [UcNative("AActor::Tick", Exe = 0x10000000)]
        public static void Tick()
        {
        }
    }
}
