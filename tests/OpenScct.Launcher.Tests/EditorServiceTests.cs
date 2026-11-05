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
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed partial class EditorServiceTests
{
    private const string Version = "0.2.0";
    private static readonly SemVersion V = SemVersion.Parse(Version);

    private sealed class ReleaseFixture
    {
        public Dictionary<string, byte[]> Assets { get; } = [];
        public ReleaseInfo Release { get; }
        public bool Offline { get; set; }

        public ReleaseFixture(byte[]? zip = null)
        {
            string name = $"OpenSCCT-Editor-{Version}-win-x64.zip";
            byte[] archive = zip ?? Archive();
            Assets[name] = archive;
            Assets[name + ".sha256"] = Encoding.UTF8.GetBytes(Builds.Sha256(archive) + "  " + name);
            Assets[name[..^4] + ".launcher.json"] = Encoding.UTF8.GetBytes("{\"launcherProtocol\":1,\"minimumGameVersion\":\"0.8.93\"}");
            Release = new ReleaseInfo
            {
                TagName = "v" + Version,
                Assets = Assets.Select((a, i) => new ReleaseAsset { Id = i + 1, Name = a.Key, Size = a.Value.Length, Url = "https://api.github.com/assets/" + a.Key }).ToList(),
            };
        }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (Offline) throw new HttpRequestException("offline");
            string url = request.RequestUri!.ToString();
            if (url.Contains("/releases?", StringComparison.Ordinal)) return Builds.Text(JsonSerializer.Serialize(new[] { Release }));
            return Assets.TryGetValue(url[(url.LastIndexOf('/') + 1)..], out byte[]? data) ? Builds.Bytes(data) : new(HttpStatusCode.NotFound);
        }
    }

    private static byte[] Archive(string? extraPath = null, bool badHash = false, int protocol = 1)
    {
        var files = new Dictionary<string, byte[]>
        {
            [EditorService.ExeName] = Encoding.UTF8.GetBytes("editor"),
            ["ose/ose.exe"] = Encoding.UTF8.GetBytes("cli"),
            ["data_OpenScctEditor_windows_x86_64/OpenScctEditor.dll"] = Encoding.UTF8.GetBytes("dll"),
        };
        string manifest = JsonSerializer.Serialize(new
        {
            format = 1, product = "OpenSCCT Editor", version = Version, platform = "win-x64", launcherProtocol = protocol,
            minimumGameVersion = "0.8.93",
            entries = new[] { EditorService.ExeName, "ose", "data_OpenScctEditor_windows_x86_64", "version.json" },
            files = files.Select(f => new { path = f.Key, size = f.Value.Length, sha256 = badHash ? new string('0', 64) : Builds.Sha256(f.Value) }),
        });
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] contents) in files)
            {
                using Stream stream = zip.CreateEntry("OpenSCCT Editor/" + path).Open();
                stream.Write(contents);
            }
            using (var writer = new StreamWriter(zip.CreateEntry("OpenSCCT Editor/version.json").Open())) writer.Write(manifest);
            if (extraPath is not null)
            {
                using var writer = new StreamWriter(zip.CreateEntry(extraPath).Open());
                writer.Write("unlisted");
            }
        }
        return output.ToArray();
    }

    private static void Prerequisites(LauncherLayout layout)
    {
        string game = layout.VersionDir("0.8.94");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "OpenSCCT.exe"), "game");
        Directory.CreateDirectory(Path.Combine(layout.Packs, "shared"));
        File.WriteAllText(Path.Combine(layout.Packs, "shared", "classes.json"), "{}");
        File.WriteAllText(Path.Combine(layout.Packs, "installed.json"), JsonSerializer.Serialize(new[] { "core", "chars", "audio" }
            .Select(code => new InstalledContent { Id = "shared/" + code, Code = code }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Directory.CreateDirectory(Path.Combine(layout.Packs, "svmap", "Test"));
        File.WriteAllText(Path.Combine(layout.Packs, "svmap", "Test", "manifest.json"), "{}");
    }

    [Fact]
    public async Task BootstrapVerifiesThenProbesBeforePublishingAndCanOpenOffline()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        var fixture = new ReleaseFixture();
        using var http = new HttpClient(new FakeHandler(fixture.Respond));
        var service = new EditorService(layout, http);
        bool probed = false;
        service.ProbeOverride = (folder, version, _) =>
        {
            Assert.False(Directory.Exists(layout.Editor));
            Assert.Equal(V, version);
            Assert.True(File.Exists(Path.Combine(folder, EditorService.ManagedMarker)));
            probed = true;
            return Task.CompletedTask;
        };
        EditorCheck check = await service.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.True(check.UpdateAvailable);
        await service.InstallOrUpdateAsync(check.Latest!, null, TestContext.Current.CancellationToken);
        Assert.True(probed);
        Assert.Equal(V, service.Installed()!.Version);
        Assert.NotNull(service.PrerequisiteProblem());
        Prerequisites(layout);
        Assert.Null(service.PrerequisiteProblem());
        fixture.Offline = true;
        EditorCheck cached = await service.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(ReleaseFeedSource.Cache, cached.Feed.Source);
        Assert.False(cached.UpdateAvailable);
        Assert.Equal(layout.Root, service.StartInfo().Environment["OSE_LAUNCHER_ROOT"]);
        Assert.False(service.StartInfo().Environment.ContainsKey("OSE_GAME_EXE"));
        Assert.Equal(Path.Combine(layout.Editor, EditorService.ExeName), service.StartInfo().FileName);
    }

    [Fact]
    public async Task FailedProbeLeavesNoInstallAndPreservesRetryDownload()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        var fixture = new ReleaseFixture();
        using var http = new HttpClient(new FakeHandler(fixture.Respond));
        var service = new EditorService(layout, http) { ProbeOverride = (_, _, _) => throw new IOException("probe failed") };
        await Assert.ThrowsAsync<IOException>(() => service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(layout.Editor));
        Assert.Empty(Directory.GetDirectories(layout.Root, ".editor-install-*"));
        Assert.Single(Directory.GetFiles(layout.EditorDownloads, "*.zip"));
        service.ProbeOverride = (_, _, _) => Task.CompletedTask;
        await service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken);
        Assert.NotNull(service.Installed());
    }

    [Theory]
    [InlineData("OpenSCCT Editor/../escape.exe")]
    [InlineData("OpenSCCT Editor/ose/../../escape.exe")]
    [InlineData("OpenSCCT Editor/extra.exe")]
    [InlineData("OpenSCCT Editor/C:/escape.exe")]
    [InlineData("Other/editor.exe")]
    [InlineData("OpenSCCT Editor/ose\\other.exe")]
    public async Task BootstrapRejectsUnsafeOrUnlistedFiles(string extra)
    {
        using var dir = new TempDir();
        string zip = dir.Combine("editor.zip");
        await File.WriteAllBytesAsync(zip, Archive(extra), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => EditorService.ExtractAndValidateAsync(zip, dir.Combine("stage"), V, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(dir.Combine("escape.exe")));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    public async Task BootstrapRejectsDamagedOrUnsupportedPackages(bool badHash, int protocol)
    {
        using var dir = new TempDir();
        string zip = dir.Combine("editor.zip");
        await File.WriteAllBytesAsync(zip, Archive(badHash: badHash, protocol: protocol), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => EditorService.ExtractAndValidateAsync(zip, dir.Combine("stage"), V, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptArchiveChecksumDoesNotExtractOrProbe()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        var fixture = new ReleaseFixture();
        string checksum = fixture.Assets.Keys.Single(k => k.EndsWith(".sha256", StringComparison.Ordinal));
        fixture.Assets[checksum] = Encoding.UTF8.GetBytes(new string('0', 64));
        using var http = new HttpClient(new FakeHandler(fixture.Respond));
        var service = new EditorService(layout, http) { ProbeOverride = (_, _, _) => throw new InvalidOperationException("must not probe") };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(layout.Editor));
        Assert.Empty(Directory.GetFiles(layout.EditorDownloads, "*.zip"));
    }

    [Fact]
    public void OffersRequireProtocolMetadataChecksumAndWindowsPackage()
    {
        var fixture = new ReleaseFixture();
        Assert.NotNull(EditorService.Offer(fixture.Release));
        fixture.Release.Assets.RemoveAll(a => a.Name.EndsWith(".launcher.json", StringComparison.Ordinal));
        Assert.Null(EditorService.Offer(fixture.Release));
        fixture = new ReleaseFixture();
        fixture.Release.Assets.RemoveAll(a => a.Name.EndsWith(".sha256", StringComparison.Ordinal));
        Assert.Null(EditorService.Offer(fixture.Release));
        fixture = new ReleaseFixture();
        fixture.Release.Draft = true;
        Assert.Null(EditorService.Offer(fixture.Release));
    }

    [Fact]
    public async Task ExistingUnrecognizedEditorDirectoryIsPreserved()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        Directory.CreateDirectory(layout.Editor);
        File.WriteAllText(Path.Combine(layout.Editor, "my-map.osmap"), "user work");
        var fixture = new ReleaseFixture();
        using var http = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("must not request")));
        var service = new EditorService(layout, http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken));
        Assert.Equal("user work", File.ReadAllText(Path.Combine(layout.Editor, "my-map.osmap")));
    }

    [Fact]
    public void PrerequisitesNeedCurrentGameAndAssetLibraryNotJustClasses()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path, LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        var service = new EditorService(layout, http);
        Assert.Contains("0.8.93", service.PrerequisiteProblem());
        Prerequisites(layout);
        Assert.Null(service.PrerequisiteProblem());
        File.Delete(Path.Combine(layout.Packs, "svmap", "Test", "manifest.json"));
        Assert.Contains("map assets", service.PrerequisiteProblem());
    }

    [Fact]
    public void RecoveryCanFindHelperAfterInstalledCliWasMovedOut()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        string oldCli = Path.Combine(layout.Editor, ".update", "old-0.1.0", "ose");
        Directory.CreateDirectory(oldCli);
        File.WriteAllText(Path.Combine(oldCli, "ose.exe"), "helper");
        File.WriteAllText(Path.Combine(layout.Editor, ".update", "journal.json"), "{\"Staged\":\"C:/untrusted\"}");
        using var http = new HttpClient();
        var service = new EditorService(layout, http);
        Assert.True(service.NeedsRecovery);
        Assert.Equal(oldCli, EditorService.RecoveryCli(layout.Editor));
        Assert.Null(service.Installed());
    }

    [Fact]
    public void SharedPackRegistryIsRequiredAndActiveSwapBlocksOpening()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path, LauncherPlatform.WindowsX64);
        Prerequisites(layout);
        File.Delete(Path.Combine(layout.Packs, "installed.json"));
        using var http = new HttpClient();
        var service = new EditorService(layout, http);
        Assert.Contains("shared game files", service.PrerequisiteProblem());
        Directory.CreateDirectory(Path.Combine(layout.Editor, ".update"));
        using var held = new FileStream(Path.Combine(layout.Editor, ".update", "swap.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Assert.True(service.UpdateInProgress);
        held.Dispose();
        Assert.False(service.UpdateInProgress);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"launcherProtocol\":\"1\",\"minimumGameVersion\":\"0.8.93\"}")]
    [InlineData("{\"launcherProtocol\":1,\"minimumGameVersion\":null}")]
    public async Task MalformedReleaseMetadataIsAnActionableError(string metadata)
    {
        using var dir = new TempDir();
        var fixture = new ReleaseFixture();
        fixture.Assets[fixture.Assets.Keys.Single(k => k.EndsWith(".launcher.json", StringComparison.Ordinal))] = Encoding.UTF8.GetBytes(metadata);
        using var http = new HttpClient(new FakeHandler(fixture.Respond));
        var service = new EditorService(new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64), http);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ExplicitNullManifestListsAreRejected()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("version.json"), "{\"format\":1,\"product\":\"OpenSCCT Editor\",\"version\":\"0.2.0\",\"platform\":\"win-x64\",\"launcherProtocol\":1,\"minimumGameVersion\":\"0.8.93\",\"entries\":null,\"files\":null}");
        Assert.Throws<InvalidDataException>(() => EditorManifest.Read(dir.Path));
    }

    [Fact]
    public void LockedTemporaryUpdaterDoesNotTurnAnInstallResultIntoAFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path, LauncherPlatform.WindowsX64);
        string helper = Path.Combine(layout.EditorDownloads, "cli-test");
        Directory.CreateDirectory(helper);
        string exe = Path.Combine(helper, "ose.exe");
        File.WriteAllText(exe, "helper");
        using var http = new HttpClient();
        var service = new EditorService(layout, http);
        using (var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            service.CleanupUpdateHelper(helper);
            Assert.True(File.Exists(exe));
        }
        service.CleanupUpdateHelper(helper);
        Assert.False(Directory.Exists(helper));
        Assert.Throws<InvalidOperationException>(() => service.CleanupUpdateHelper(dir.Path));
    }
}
