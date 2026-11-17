using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

/// <summary>
/// OSCTDIF1 patches and the delta installer's use of them. Fixtures/Patch holds old.bin, new.bin and old-new.osdiff,
/// made by OpenSCCT-Unreal's Tools/ReleaseDelta (<c>diff old.bin new.bin old-new.osdiff</c>), the format's reference.
/// </summary>
public sealed class BinaryPatchTests
{
    private static readonly LauncherPlatform Platform = LauncherPlatform.WindowsX64;
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Patch");
    private static readonly byte[] Old = File.ReadAllBytes(Path.Combine(Fixtures, "old.bin"));
    private static readonly byte[] New = File.ReadAllBytes(Path.Combine(Fixtures, "new.bin"));
    private static readonly byte[] Patch = File.ReadAllBytes(Path.Combine(Fixtures, "old-new.osdiff"));
    private static readonly byte[] Exe = "game exe"u8.ToArray();

    [Fact]
    public void AppliesReferencePatchToNewFileAndInPlace()
    {
        using var dir = new TempDir();
        string old = Write(dir, "old.bin", Old);
        string patch = Write(dir, "p.osdiff", Patch);

        BinaryPatch.Header header = BinaryPatch.ReadHeader(patch);
        Assert.Equal(Old.Length, header.OldSize);
        Assert.Equal(New.Length, header.NewSize);
        Assert.Equal(Builds.Sha256(Old), header.OldSha256);
        Assert.Equal(Builds.Sha256(New), header.NewSha256);

        string made = dir.Combine("new.bin");
        BinaryPatch.Apply(old, patch, made, TestContext.Current.CancellationToken);
        Assert.Equal(New, File.ReadAllBytes(made));

        BinaryPatch.Apply(old, patch, old, TestContext.Current.CancellationToken);
        Assert.Equal(New, File.ReadAllBytes(old));
        Assert.False(File.Exists(old + ".partial"));
    }

    [Fact]
    public void DamagedPatchOrWrongOldFileThrowsAndLeavesNoOutput()
    {
        using var dir = new TempDir();
        string old = Write(dir, "old.bin", Old);
        string made = dir.Combine("new.bin");

        // A byte of the zlib streams changed: the streams fail or the result has another SHA-256.
        byte[] damaged = (byte[])Patch.Clone();
        damaged[BinaryPatch.HeaderSize + 40] ^= 0xff;
        Assert.ThrowsAny<Exception>(() => BinaryPatch.Apply(old, Write(dir, "damaged.osdiff", damaged), made, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(made));
        Assert.False(File.Exists(made + ".partial"));

        // Cut short: the stream lengths no longer add up.
        string cut = Write(dir, "cut.osdiff", Patch[..^10]);
        Assert.Throws<InvalidDataException>(() => BinaryPatch.Apply(old, cut, made, TestContext.Current.CancellationToken));

        // Not a patch at all.
        Assert.Throws<InvalidDataException>(() => BinaryPatch.Apply(old, Write(dir, "text.osdiff", Encoding.UTF8.GetBytes(new string('x', 200))), made, TestContext.Current.CancellationToken));

        // The old file is not the one the patch was made from.
        string other = Write(dir, "other.bin", New);
        Assert.Throws<InvalidDataException>(() => BinaryPatch.Apply(other, Write(dir, "p.osdiff", Patch), made, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(made));
        Assert.False(File.Exists(made + ".partial"));
    }

    [Fact]
    public void ParseReadsPatchesAndRejectsBadOnes()
    {
        BuildFiles files = BuildFiles.Parse(Manifest(Valid()));
        BuildPatch patch = Assert.Single(files.Files.Single(f => f.Path == "data/big.bin").Patches!);
        Assert.Equal(Builds.Sha256(Old), patch.From);
        Assert.Equal(Old.Length, patch.FromSize);
        Assert.Null(files.Files.Single(f => f.Path == Platform.GameFileName).Patches);

        Assert.Throws<UpdateRejectedException>(() => BuildFiles.Parse(Manifest(Valid(p => p.From = "abc"))));
        Assert.Throws<UpdateRejectedException>(() => BuildFiles.Parse(Manifest(Valid(p => p.Tag = " "))));
        Assert.Throws<UpdateRejectedException>(() => BuildFiles.Parse(Manifest(Valid(p => p.Name = ""))));
        Assert.Throws<UpdateRejectedException>(() => BuildFiles.Parse(Manifest(Valid(p => p.FromSize = -1))));
    }

    [Fact]
    public async Task InstallsFromBaseFileAndPatchWithoutDownloadingBlob()
    {
        using var setup = new Setup(withBase: true);
        DeltaInstaller.Plan plan = await setup.PlanAsync();

        Assert.Empty(plan.Downloads);
        DeltaInstaller.PatchStep step = Assert.Single(plan.Patches);
        Assert.Equal("data/big.bin", step.File.Path);
        Assert.NotNull(step.Fallback);
        Assert.Equal(Patch.Length, plan.DownloadBytes);

        string installed = await setup.InstallAsync(plan);
        Assert.Equal(New, await File.ReadAllBytesAsync(Path.Combine(installed, "data", "big.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(Exe, await File.ReadAllBytesAsync(Path.Combine(installed, Platform.GameFileName), TestContext.Current.CancellationToken));
        Assert.Contains("old-new.osdiff", setup.Fetched);
        Assert.DoesNotContain("big.gz", setup.Fetched);
    }

    [Fact]
    public async Task WithoutBaseFileDownloadsBlob()
    {
        using var setup = new Setup(withBase: false);
        DeltaInstaller.Plan plan = await setup.PlanAsync();

        Assert.Empty(plan.Patches);
        Assert.Equal([Platform.GameFileName, "data/big.bin"], plan.Downloads.Select(d => d.File.Path).Order(StringComparer.Ordinal));
        string installed = await setup.InstallAsync(plan);
        Assert.Equal(New, await File.ReadAllBytesAsync(Path.Combine(installed, "data", "big.bin"), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("old-new.osdiff", setup.Fetched);
    }

    [Fact]
    public async Task PatchMissingFromReleaseFallsBackToBlob()
    {
        using var setup = new Setup(withBase: true);
        setup.Payloads.Remove("old-new.osdiff");
        DeltaInstaller.Plan plan = await setup.PlanAsync();

        Assert.Empty(plan.Patches);
        Assert.Equal("data/big.bin", Assert.Single(plan.Downloads).File.Path);
        string installed = await setup.InstallAsync(plan);
        Assert.Equal(New, await File.ReadAllBytesAsync(Path.Combine(installed, "data", "big.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BlobMissingFromReleaseStillPatches()
    {
        using var setup = new Setup(withBase: true);
        setup.Payloads.Remove("big.gz");
        DeltaInstaller.Plan plan = await setup.PlanAsync();

        DeltaInstaller.PatchStep step = Assert.Single(plan.Patches);
        Assert.Null(step.Fallback);
        string installed = await setup.InstallAsync(plan);
        Assert.Equal(New, await File.ReadAllBytesAsync(Path.Combine(installed, "data", "big.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DamagedPatchFallsBackToBlob()
    {
        using var setup = new Setup(withBase: true);
        byte[] damaged = (byte[])Patch.Clone();
        damaged[BinaryPatch.HeaderSize + 40] ^= 0xff;
        setup.Payloads["old-new.osdiff"] = damaged;
        DeltaInstaller.Plan plan = await setup.PlanAsync();
        Assert.Single(plan.Patches);

        string installed = await setup.InstallAsync(plan);
        Assert.Equal(New, await File.ReadAllBytesAsync(Path.Combine(installed, "data", "big.bin"), TestContext.Current.CancellationToken));
        Assert.Contains("big.gz", setup.Fetched);
        Assert.Empty(Directory.EnumerateFiles(installed, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DamagedPatchWithoutBlobIsRejected()
    {
        using var setup = new Setup(withBase: true);
        setup.Payloads.Remove("big.gz");
        setup.Payloads["old-new.osdiff"] = Patch[..^10];
        DeltaInstaller.Plan plan = await setup.PlanAsync();

        await Assert.ThrowsAsync<UpdateRejectedException>(() => setup.InstallAsync(plan));
        Assert.False(setup.Store.IsInstalled(Setup.Target));
    }

    private static string Write(TempDir dir, string name, byte[] data)
    {
        string path = dir.Combine(name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string Manifest(BuildFiles files) => JsonSerializer.Serialize(files);

    private static BuildFiles Valid(Action<BuildPatch>? change = null)
    {
        var patch = new BuildPatch
        {
            From = Builds.Sha256(Old), FromSize = Old.Length, Tag = "store", Name = "old-new.osdiff", Size = Patch.Length,
        };
        change?.Invoke(patch);
        return new BuildFiles
        {
            Format = 1,
            Version = Setup.Target.ToString(),
            Files =
            [
                new()
                {
                    Path = Platform.GameFileName, Size = Exe.Length, Sha256 = Builds.Sha256(Exe),
                    Blob = new() { Tag = "store", Name = "exe.gz", Size = Gzip(Exe).Length },
                },
                new()
                {
                    Path = "data/big.bin", Size = New.Length, Sha256 = Builds.Sha256(New),
                    Blob = new() { Tag = "store", Name = "big.gz", Size = Gzip(New).Length },
                    Patches = [patch],
                },
            ],
        };
    }

    private static byte[] Gzip(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return buffer.ToArray();
    }

    /// <summary>A launcher folder (with version 0.1.0 holding the patch's old file, or another file) and a fake release store.</summary>
    private sealed class Setup : IDisposable
    {
        public static readonly SemVersion Target = SemVersion.Parse("0.2.0");

        private readonly TempDir _dir = new();
        private readonly HttpClient _http;
        private readonly LauncherLayout _layout;
        private readonly GitHubReleaseClient _github;

        public Setup(bool withBase)
        {
            _layout = new LauncherLayout(_dir.Combine("root"), Platform);
            Store = new VersionStore(_layout);
            var base_ = SemVersion.Parse("0.1.0");
            string folder = _layout.VersionDir(base_.ToString());
            Directory.CreateDirectory(Path.Combine(folder, "data"));
            File.WriteAllBytes(Path.Combine(folder, Platform.GameFileName), withBase ? Exe : "older exe"u8.ToArray());
            File.WriteAllBytes(Path.Combine(folder, "data", "big.bin"), withBase ? Old : Old.Reverse().ToArray());
            Store.Activate(base_);

            Payloads["exe.gz"] = Gzip(Exe);
            Payloads["big.gz"] = Gzip(New);
            Payloads["old-new.osdiff"] = Patch;
            _http = new HttpClient(new FakeHandler(request =>
            {
                string url = request.RequestUri!.ToString();
                string name = url[(url.LastIndexOf('/') + 1)..];
                Fetched.Add(name);
                return Payloads.TryGetValue(name, out byte[]? value) ? Builds.Bytes(value) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }));
            _github = new GitHubReleaseClient(_http, new ReleaseSource("o", "r", null), _layout.Cache);
        }

        public Dictionary<string, byte[]> Payloads { get; } = [];

        public List<string> Fetched { get; } = [];

        public VersionStore Store { get; }

        private DeltaInstaller Installer => new(_layout, Store, _github, new ResumableDownloader(_http));

        public Task<DeltaInstaller.Plan> PlanAsync()
        {
            ReleaseInfo store = new()
            {
                TagName = "store",
                Assets = Payloads.Select((pair, i) => new ReleaseAsset
                {
                    Id = i + 1, Name = pair.Key, Size = pair.Value.Length, Url = "https://api.github.com/assets/" + pair.Key,
                }).ToList(),
            };
            return Installer.PlanAsync(BuildFiles.Parse(Manifest(Valid())), [store], TestContext.Current.CancellationToken);
        }

        public Task<string> InstallAsync(DeltaInstaller.Plan plan) =>
            Installer.InstallAsync(Target, BuildFiles.Parse(Manifest(Valid())), plan, null, TestContext.Current.CancellationToken);

        public void Dispose()
        {
            _http.Dispose();
            _dir.Dispose();
        }
    }
}
