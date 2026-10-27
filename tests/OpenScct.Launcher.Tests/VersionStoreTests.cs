using System;
using System.IO;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class VersionStoreTests
{
    private static string Zip(TempDir dir, string version, bool wrap = false)
    {
        string path = dir.Combine($"OpenSCCT-{version}.zip");
        File.WriteAllBytes(path, Builds.GameZip(version, wrap));
        return path;
    }

    private static SemVersion V(string s) => SemVersion.Parse(s);

    [Fact]
    public void InstallsSideBySideAndSwitches()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("root"));
        var store = new VersionStore(layout);

        store.InstallFromZip(Zip(dir, "0.1.0"), V("0.1.0"));
        Assert.Null(store.LoadManifest().Current);
        Assert.Equal(V("0.1.0"), store.Current());

        store.Activate(V("0.1.0"));
        store.InstallFromZip(Zip(dir, "0.2.0", wrap: true), V("0.2.0"));
        Assert.Equal(V("0.1.0"), store.Current());

        store.Activate(V("0.2.0"));
        Assert.Equal(V("0.2.0"), store.Current());
        Assert.Equal(V("0.1.0"), store.Previous());
        Assert.Equal("exe 0.2.0", File.ReadAllText(store.ExePath(V("0.2.0"))));
        Assert.Equal("exe 0.1.0", File.ReadAllText(store.ExePath(V("0.1.0"))));
    }

    [Fact]
    public void RollbackSwapsCurrentAndPrevious()
    {
        using var dir = new TempDir();
        var store = new VersionStore(new LauncherLayout(dir.Combine("root")));
        store.InstallFromZip(Zip(dir, "0.1.0"), V("0.1.0"));
        store.Activate(V("0.1.0"));
        store.InstallFromZip(Zip(dir, "0.2.0"), V("0.2.0"));
        store.Activate(V("0.2.0"));

        Assert.True(store.Rollback());
        Assert.Equal(V("0.1.0"), store.Current());
        Assert.Equal(V("0.2.0"), store.Previous());
    }

    [Fact]
    public void RollbackWithoutPreviousDoesNothing()
    {
        using var dir = new TempDir();
        var store = new VersionStore(new LauncherLayout(dir.Combine("root")));
        store.InstallFromZip(Zip(dir, "0.1.0"), V("0.1.0"));
        store.Activate(V("0.1.0"));
        Assert.False(store.Rollback());
    }

    [Fact]
    public void PruneKeepsCurrentPreviousAndFoldersInUse()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("root"));
        var store = new VersionStore(layout);
        foreach (string v in new[] { "0.1.0", "0.2.0", "0.3.0", "0.4.0" })
        {
            store.InstallFromZip(Zip(dir, v), V(v));
            store.Activate(V(v));
        }

        Directory.CreateDirectory(Path.Combine(layout.Versions, ".staging-0.5.0-deadbeef"));
        store.Prune([layout.VersionDir("0.1.0")]);

        Assert.True(store.IsInstalled(V("0.4.0")));
        Assert.True(store.IsInstalled(V("0.3.0")));
        Assert.True(store.IsInstalled(V("0.1.0")));
        Assert.False(store.IsInstalled(V("0.2.0")));
        Assert.False(Directory.Exists(Path.Combine(layout.Versions, ".staging-0.5.0-deadbeef")));
    }

    [Fact]
    public void ZipWithoutTheGameIsRejectedAndLeavesNothing()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("root"));
        var store = new VersionStore(layout);
        string bad = dir.Combine("bad.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(bad, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Assert.Throws<InvalidDataException>(() => store.InstallFromZip(bad, V("0.9.0")));
        Assert.Empty(Directory.GetDirectories(layout.Versions));
    }

    [Fact]
    public void ActivateRefusesAMissingVersion()
    {
        using var dir = new TempDir();
        var store = new VersionStore(new LauncherLayout(dir.Combine("root")));
        Assert.Throws<InvalidOperationException>(() => store.Activate(V("1.0.0")));
    }

    [Fact]
    public void PendingIsClearedByActivate()
    {
        using var dir = new TempDir();
        var store = new VersionStore(new LauncherLayout(dir.Combine("root")));
        store.InstallFromZip(Zip(dir, "0.1.0"), V("0.1.0"));
        store.Activate(V("0.1.0"));
        store.InstallFromZip(Zip(dir, "0.2.0"), V("0.2.0"));
        store.MarkPending(V("0.2.0"));
        Assert.Equal(V("0.2.0"), store.Pending());

        store.Activate(V("0.2.0"));
        Assert.Null(store.Pending());
    }

    [Fact]
    public void VersionDirOfMapsAnExeToItsFolder()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("root"));
        var store = new VersionStore(layout);
        Assert.Equal(layout.VersionDir("0.2.0"), store.VersionDirOf(Path.Combine(layout.VersionDir("0.2.0"), "OpenSCCT.exe")));
        Assert.Null(store.VersionDirOf(dir.Combine("elsewhere", "OpenSCCT.exe")));
    }

    [Fact]
    public void SafeDeleteRefusesPathsOutsideTheRoot()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("root"));
        Directory.CreateDirectory(dir.Combine("userdata"));
        Assert.Throws<InvalidOperationException>(() => SafeDelete.Tree(dir.Combine("userdata"), dir.Combine("root")));
        Assert.Throws<InvalidOperationException>(() => SafeDelete.Tree(dir.Combine("root"), dir.Combine("root")));
        Assert.True(Directory.Exists(dir.Combine("userdata")));
    }

    [Fact]
    public void ManifestWriteIsAtomicAndReadable()
    {
        using var dir = new TempDir();
        string path = dir.Combine("install.json");
        new InstallManifest { Current = "0.2.0", Previous = "0.1.0" }.Save(path);
        InstallManifest read = InstallManifest.Load(path);
        Assert.Equal("0.2.0", read.Current);
        Assert.Equal("0.1.0", read.Previous);
        Assert.Single(Directory.GetFiles(dir.Path));
    }
}
