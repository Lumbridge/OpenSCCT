using OpenScct.Maps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Unreal;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class UnrealServiceTests
{
    private const string Shipping = "OpenSCCTUnreal/Binaries/Win64/OpenSCCTUnreal-Win64-Shipping.exe";
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Patch");
    private static readonly byte[] Old = File.ReadAllBytes(Path.Combine(Fixtures, "old.bin"));
    private static readonly byte[] New = File.ReadAllBytes(Path.Combine(Fixtures, "new.bin"));
    private static readonly byte[] Patch = File.ReadAllBytes(Path.Combine(Fixtures, "old-new.osdiff"));

    [Fact]
    public async Task FreshInstallFromZipThenStartsWithQuotedContentFolder()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.10", Zip("0.1.10"));
        // The root has a space in it: the content argument must be quoted after the '='.
        (UnrealService service, LauncherLayout layout) = Create(dir, github, "Open SCCT");
        var cancel = TestContext.Current.CancellationToken;

        UnrealCheck check = await service.CheckAsync(false, cancel);
        Assert.Null(check.Feed.Problem);
        Assert.Equal("0.1.10", check.Latest!.Version.ToString());
        Assert.True(check.UpdateAvailable);
        Assert.Null(check.Installed);

        var phases = new List<UnrealProgress>();
        UnrealInstallResult result = await service.InstallAsync(check.Latest, new SyncProgress<UnrealProgress>(phases.Add), cancel);
        Assert.Equal(UnrealInstallOutcome.Installed, result.Outcome);
        Assert.Equal(github.Payloads["OpenSCCT-Unreal-0.1.10-win-x64.zip"].Length, result.DownloadedBytes);
        Assert.Null(result.BuildBytes);
        Assert.Contains(phases, p => p.Phase == "Downloading OpenSCCT Unreal");
        Assert.Equal("OpenSCCT Unreal installed", phases[^1].Phase);
        Assert.False(phases[^1].CanCancel);

        UnrealInstallation installed = service.Installed()!;
        Assert.Equal("0.1.10", installed.Version.ToString());
        Assert.Equal(Path.Combine(layout.Unreal, "versions", "0.1.10"), installed.Folder);
        Assert.Equal("0.8.161", installed.OpenscctVersion);
        Assert.Equal("abc123", installed.OpenscctCommit);
        Assert.True(File.Exists(Path.Combine(installed.Folder, Shipping)));
        // The Godot game's folders are untouched.
        Assert.False(Directory.Exists(layout.Versions));
        Assert.False(File.Exists(layout.Manifest));

        Assert.Equal(UnrealInstallOutcome.AlreadyCurrent, (await service.InstallAsync(check.Latest, null, cancel)).Outcome);
        Assert.False((await service.CheckAsync(false, cancel)).UpdateAvailable);

        string content = SteamFiles(layout);
        ProcessStartInfo start = service.StartInfo();
        Assert.Equal(Path.Combine(installed.Folder, UnrealService.StarterExe), start.FileName);
        Assert.Equal(installed.Folder, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.Equal("-ScctContent=\"" + content + "\"", start.Arguments);
        Assert.Contains(' ', layout.Packs);
        Assert.Empty(start.ArgumentList);
        Assert.False(start.Environment.ContainsKey("SVM_CONTENT"));
        Assert.False(start.Environment.ContainsKey("SVM_GAME_ROOT"));
    }

    [Fact]
    public async Task EnabledMapCollectionIsPassedAsOneRootAndDisabledCollectionIsIgnored()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.10", Zip("0.1.10"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);
        string content = SteamFiles(layout);
        string root = dir.Combine("User maps; nightly");
        string map = Path.Combine(root, "Training");
        Directory.CreateDirectory(Path.Combine(map, "render"));
        Directory.CreateDirectory(Path.Combine(map, "sim"));
        string manifest = Path.Combine(map, "manifest.json");
        File.WriteAllText(manifest, """{"format":"svmap","formatVersion":"1.2","map":"Training","source":{"kind":"authored"}}""");
        foreach (string name in new[] { "sim/gameplay.json", "sim/traversal.json", "sim/collision.bin", "render/world.glb" })
            File.WriteAllText(Path.Combine(map, name), "test");

        Assert.True(UnrealMapMods.TryGetMapRoot(root, out string resolved, out string error), error);
        Assert.Equal(root, resolved);
        Assert.False(UnrealMapMods.TryGetMapRoot(map, out _, out _));
        var settings = new LauncherSettings { UnrealMapModRoot = root, UnrealMapModsEnabled = true };
        settings.Save(layout.Settings);
        Assert.EndsWith(" --svm-maps=\"" + root.Replace('\\', '/') + "\"", service.StartInfo().Arguments);

        string stock = Path.Combine(content, "svmap", "Training");
        Directory.CreateDirectory(stock);
        File.WriteAllText(Path.Combine(stock, "manifest.json"), "{}");
        Assert.Contains("already installed", Assert.Throws<InvalidOperationException>(service.StartInfo).Message);
        File.Delete(Path.Combine(stock, "manifest.json"));
        File.Delete(Path.Combine(map, "render/world.glb"));
        Assert.Contains("world.glb", Assert.Throws<InvalidOperationException>(service.StartInfo).Message);
        File.WriteAllText(manifest, "{");
        Assert.False(UnrealMapMods.TryGetMapRoot(root, out _, out _));
        settings.UnrealMapModsEnabled = false;
        settings.Save(layout.Settings);
        Assert.DoesNotContain("--svm-maps", service.StartInfo().Arguments);
    }

    [Fact]
    public async Task UpdateDownloadsOnlyChangedFilesAndPatchesFromTheStoreFoundByTag()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);

        // 0.2.0: a new starter (in a pack of small files), a new version.json (gzipped), big.bin by a patch from
        // 0.1.0's, the rest unchanged. The store release is older than the first page of the list.
        byte[] starter = Encoding.UTF8.GetBytes("starter 0.2.0");
        byte[] version = VersionJson("0.2.0");
        byte[] pack = Pack(starter);
        string packName = "OpenSCCT-Unreal-0.2.0-win-x64-pack-0123456789abcdef.zip";
        var store = new Dictionary<string, byte[]>
        {
            [packName] = pack,
            [Builds.Sha256(version) + ".gz"] = Gzip(version),
            [Builds.Sha256(New) + ".gz"] = Gzip(New),
            [Builds.Sha256(Old) + "-" + Builds.Sha256(New) + ".osdiff"] = Patch,
        };
        var files = new BuildFiles
        {
            Format = 1,
            Version = "0.2.0",
            Files =
            [
                Entry(UnrealService.StarterExe, starter, new BlobRef { Tag = UnrealService.StoreTag, Name = packName, Size = pack.Length, Member = Builds.Sha256(starter) }),
                Entry(Shipping, Bytes("shipping 0.1.0"), Gz(Bytes("shipping 0.1.0"))),
                Entry("Prerequisites/vc_redist.x64.exe", Bytes("redist"), Gz(Bytes("redist"))),
                Entry("data/big.bin", New, Gz(New), new BuildPatch
                {
                    From = Builds.Sha256(Old), FromSize = Old.Length, Tag = UnrealService.StoreTag,
                    Name = Builds.Sha256(Old) + "-" + Builds.Sha256(New) + ".osdiff", Size = Patch.Length,
                }),
                Entry("version.json", version, Gz(version)),
            ],
        };
        github.AddVersion("0.2.0", zip: null, files: files);
        github.AddRelease(UnrealService.StoreTag, store, prerelease: true, inFeed: false);

        UnrealCheck check = await service.CheckAsync(false, cancel);
        Assert.Equal("0.2.0", check.Latest!.Version.ToString());
        Assert.Null(check.Latest.Store);
        Assert.True(check.UpdateAvailable);

        var phases = new List<UnrealProgress>();
        UnrealInstallResult result = await service.InstallAsync(check.Latest, new SyncProgress<UnrealProgress>(phases.Add), cancel);
        Assert.Equal(UnrealInstallOutcome.Installed, result.Outcome);
        Assert.Equal(pack.Length + Gzip(version).Length + Patch.Length, result.DownloadedBytes);
        Assert.Equal(files.Files.Sum(f => f.Size), result.BuildBytes);
        Assert.Contains(phases, p => p.Phase == "Checking installed files");
        Assert.Contains(github.Requests, r => r.EndsWith("/releases/tags/" + UnrealService.StoreTag, StringComparison.Ordinal));
        Assert.Contains(store.Keys.Single(k => k.EndsWith(".osdiff", StringComparison.Ordinal)), github.Requests.Select(Name));
        Assert.DoesNotContain(Builds.Sha256(New) + ".gz", github.Requests.Select(Name));
        Assert.DoesNotContain("OpenSCCT-Unreal-0.2.0-win-x64.zip", github.Requests.Select(Name));

        UnrealInstallation installed = service.Installed()!;
        Assert.Equal("0.2.0", installed.Version.ToString());
        Assert.Equal(starter, File.ReadAllBytes(Path.Combine(installed.Folder, UnrealService.StarterExe)));
        Assert.Equal(New, File.ReadAllBytes(Path.Combine(installed.Folder, "data", "big.bin")));
        Assert.Equal(version, File.ReadAllBytes(Path.Combine(installed.Folder, "version.json")));
        Assert.Equal("shipping 0.1.0", File.ReadAllText(Path.Combine(installed.Folder, Shipping)));
        // The previous version is kept for rollback.
        Assert.True(Directory.Exists(Path.Combine(layout.Unreal, "versions", "0.1.0")));
    }

    [Theory]
    [InlineData(2, UnrealService.Product)]
    [InlineData(1, "OpenSCCT")]
    public async Task IncompatibleHandshakeIsRejectedBeforeAnyDownload(int protocol, string product)
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"), protocol: protocol, product: product);
        (UnrealService service, _) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;

        UnrealCheck check = await service.CheckAsync(false, cancel);
        var ex = await Assert.ThrowsAsync<UpdateRejectedException>(() => service.InstallAsync(check.Latest!, null, cancel));
        Assert.Equal(UnrealService.NeedsNewerLauncherMessage, ex.Message);
        Assert.Null(service.Installed());
        Assert.DoesNotContain("OpenSCCT-Unreal-0.1.0-win-x64.zip", github.Requests.Select(Name));
    }

    [Fact]
    public async Task ReleaseWithoutHandshakeIsNotOffered()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.2.0", Zip("0.2.0"), metadata: false);
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, _) = Create(dir, github);

        UnrealCheck check = await service.CheckAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal("0.1.0", check.Latest!.Version.ToString());
    }

    [Fact]
    public async Task PrereleasesAreOfferedOnlyWhenAskedFor()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.3.0", Zip("0.3.0"), prerelease: true);
        github.AddVersion("0.2.0-beta.1", Zip("0.2.0-beta.1"));
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, _) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;

        Assert.Equal("0.1.0", (await service.CheckAsync(false, cancel)).Latest!.Version.ToString());
        Assert.Equal("0.3.0", (await service.CheckAsync(true, cancel)).Latest!.Version.ToString());
    }

    [Fact]
    public async Task MissingRepositoryOrEmptyFeedMeansNoReleaseYetWithoutAProblem()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub { RepositoryMissing = true };
        (UnrealService service, _) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;

        UnrealCheck missing = await service.CheckAsync(false, cancel);
        Assert.Null(missing.Latest);
        Assert.Null(missing.Feed.Problem);
        Assert.True(missing.Feed.RepositoryNotFound);
        Assert.False(missing.UpdateAvailable);

        github.RepositoryMissing = false;
        github.AddRelease(UnrealService.StoreTag, new Dictionary<string, byte[]> { ["x.gz"] = [1] }, prerelease: true);
        UnrealCheck empty = await service.CheckAsync(false, cancel);
        Assert.Null(empty.Latest);
        Assert.Null(empty.Feed.Problem);
        Assert.Throws<InvalidOperationException>(() => service.StartInfo());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task FailedFeedReadKeepsItsProblemAndOffersCachedReleases(int status)
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        var cancel = TestContext.Current.CancellationToken;

        // Nothing cached yet: no release, but a problem, so the tab does not say "not released yet".
        (UnrealService empty, _) = Create(dir, github, "empty");
        github.FeedFailure = (HttpStatusCode)status;
        UnrealCheck offline = await empty.CheckAsync(false, cancel);
        Assert.Null(offline.Latest);
        Assert.NotNull(offline.Feed.Problem);
        Assert.False(offline.Feed.RepositoryNotFound);
        Assert.False(offline.UpdateAvailable);

        // A good read fills the cache; a failed read then still offers its releases, with the problem.
        (UnrealService service, _) = Create(dir, github);
        github.FeedFailure = null;
        Assert.Null((await service.CheckAsync(false, cancel)).Feed.Problem);
        github.FeedFailure = (HttpStatusCode)status;
        UnrealCheck cached = await service.CheckAsync(false, cancel);
        Assert.NotNull(cached.Feed.Problem);
        Assert.False(cached.Feed.RepositoryNotFound);
        Assert.Equal("0.1.0", cached.Latest!.Version.ToString());
        Assert.True(cached.UpdateAvailable);
    }

    [Fact]
    public async Task CurrentVersionWithoutReadableVersionJsonIsInstalledAgain()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;
        VersionedRelease latest = (await service.CheckAsync(false, cancel)).Latest!;
        await service.InstallAsync(latest, null, cancel);
        string versionJson = Path.Combine(service.Installed()!.Folder, "version.json");

        foreach (string? damage in new[] { null, "{ not json" })
        {
            if (damage is null)
            {
                File.Delete(versionJson);
            }
            else
            {
                File.WriteAllText(versionJson, damage);
            }

            Assert.Null(service.Installed());
            UnrealCheck check = await service.CheckAsync(false, cancel);
            Assert.True(check.UpdateAvailable);

            // A running game may use the damaged folder: it is not moved then.
            service.RunningOverride = () => true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(check.Latest!, null, cancel));
            service.RunningOverride = () => false;

            UnrealInstallResult result = await service.InstallAsync(check.Latest!, null, cancel);
            Assert.Equal(UnrealInstallOutcome.Installed, result.Outcome);
            Assert.NotNull(result.DownloadedBytes);
            UnrealInstallation repaired = service.Installed()!;
            Assert.Equal("0.1.0", repaired.Version.ToString());
            Assert.Equal(VersionJson("0.1.0"), File.ReadAllBytes(versionJson));
            Assert.Equal(UnrealInstallOutcome.AlreadyCurrent, (await service.InstallAsync(check.Latest!, null, cancel)).Outcome);
        }

        // The damaged folders were set aside and pruned.
        Assert.Equal(new[] { "0.1.0" }, Directory.GetDirectories(Path.Combine(layout.Unreal, "versions")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ZipInstallChecksFreeSpaceBeforeDownloadingAndBeforeUnpacking()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        byte[] zip = Zip("0.1.0");
        github.AddVersion("0.1.0", zip);
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;
        VersionedRelease latest = (await service.CheckAsync(false, cancel)).Latest!;
        string name = "OpenSCCT-Unreal-0.1.0-win-x64.zip";

        // Too little room for the ZIP and its unpacked copy: nothing is downloaded.
        service.FreeSpaceOverride = _ => zip.Length;
        var ex = await Assert.ThrowsAsync<UpdateRejectedException>(() => service.InstallAsync(latest, null, cancel));
        Assert.StartsWith("Not enough free disk space for this update", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(name, github.Requests.Select(Name));
        Assert.Null(service.Installed());

        // Room for the download, then (the ZIP on disk) not for the files it unpacks to: nothing is installed.
        int calls = 0;
        service.FreeSpaceOverride = _ => ++calls == 1 ? long.MaxValue : 1;
        ex = await Assert.ThrowsAsync<UpdateRejectedException>(() => service.InstallAsync(latest, null, cancel));
        Assert.StartsWith("Not enough free disk space for this update", ex.Message, StringComparison.Ordinal);
        Assert.Contains(name, github.Requests.Select(Name));
        Assert.Equal(2, calls);
        Assert.Null(service.Installed());
        Assert.False(Directory.Exists(Path.Combine(layout.Unreal, "versions", "0.1.0")));

        service.FreeSpaceOverride = _ => null;
        Assert.Equal(UnrealInstallOutcome.Installed, (await service.InstallAsync(latest, null, cancel)).Outcome);
    }

    [Theory]
    [InlineData(1, 14, 51, 36247, null, "14.50.35719", false)]
    [InlineData(1, 14, 50, 35719, null, "14.50.35719", false)]
    [InlineData(1, 14, 50, 35718, null, "14.50.35719", true)]
    // An old 2015 or 2017 runtime writes the same key with Installed = 1.
    [InlineData(1, 14, 0, 24215, null, "14.50.35719", true)]
    [InlineData(1, 14, 16, 27033, null, "14.20.0", true)]
    [InlineData(1, null, null, null, "v14.44.35211.00", "14.40.0", false)]
    [InlineData(1, null, null, null, "v14.38.33135.00", "14.40.0", true)]
    [InlineData(1, null, null, null, null, "14.20.0", true)]
    [InlineData(0, 14, 51, 36247, null, "14.20.0", true)]
    [InlineData(null, null, null, null, null, "14.20.0", true)]
    public void VisualCppRuntimeMustBeAtLeastTheBundledVersion(int? installed, int? major, int? minor, int? build, string? version, string required, bool missing)
    {
        Version? runtime = UnrealService.ParseRuntime(installed, major, minor, build, version);
        Assert.Equal(missing, !UnrealService.RuntimeSatisfies(runtime, Version.Parse(required)));
    }

    [Fact]
    public async Task UpdateWhileTheGameRunsWaitsThenSwitches()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        SteamFiles(layout);
        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);

        github.AddVersion("0.2.0", Zip("0.2.0"));
        bool running = true;
        service.RunningOverride = () => running;
        Assert.True(service.IsRunning);
        UnrealInstallResult result = await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);

        Assert.Equal(UnrealInstallOutcome.WaitingForGameToClose, result.Outcome);
        Assert.Equal("0.1.0", service.Installed()!.Version.ToString());
        Assert.Equal("0.2.0", service.Pending!.ToString());
        Assert.Contains("waiting for the game to close", Assert.Throws<InvalidOperationException>(() => service.StartInfo()).Message, StringComparison.Ordinal);
        Assert.False(service.FinishPendingSwitch());
        Assert.Equal("0.1.0", service.Installed()!.Version.ToString());

        running = false;
        Assert.True(service.FinishPendingSwitch());
        Assert.Equal("0.2.0", service.Installed()!.Version.ToString());
        Assert.Null(service.Pending);
        Assert.False(service.FinishPendingSwitch());
        Assert.EndsWith(Path.Combine("0.2.0", UnrealService.StarterExe), service.StartInfo().FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartRefusesWithoutGameFilesAndQuotesTheContentFolderByHand()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.10", Zip("0.1.10"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        Assert.Contains("Install OpenSCCT Unreal first", Assert.Throws<InvalidOperationException>(() => service.StartInfo()).Message, StringComparison.Ordinal);

        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);
        // Old launcher installations can still have a complete shared pack catalogue.
        Directory.CreateDirectory(Path.Combine(layout.Packs, "shared"));
        Directory.CreateDirectory(Path.Combine(layout.Packs, "svmap", "AquaE"));
        File.WriteAllText(Path.Combine(layout.Packs, "shared", "classes.json"), "{}");
        File.WriteAllText(Path.Combine(layout.Packs, "svmap", "AquaE", "manifest.json"), "{}");
        Assert.Contains("Steam installation", service.PrerequisiteProblem(), StringComparison.Ordinal);
        Assert.Contains("Steam installation", Assert.Throws<InvalidOperationException>(() => service.StartInfo()).Message, StringComparison.Ordinal);
        SteamFiles(layout);
        Assert.Null(service.PrerequisiteProblem());

        Assert.Equal(@"-ScctContent=""C:\Users\A B\AppData\Local\OpenSCCT\packs""", UnrealService.ContentArgument(@"C:\Users\A B\AppData\Local\OpenSCCT\packs"));
        Assert.Equal(@"-ScctContent=""C:\x y""", UnrealService.ContentArgument(@"C:\x y\"));
        Assert.Throws<InvalidOperationException>(() => UnrealService.ContentArgument(@"C:\a""b\packs"));
    }

    [Fact]
    public async Task GodotUpdatesNeverTakeUnrealAssets()
    {
        using var dir = new TempDir();
        byte[] zip = Zip("0.9.0");
        string name = "OpenSCCT-Unreal-0.9.0-win-x64.zip";
        var github = new FakeGitHub();
        github.AddVersion("0.9.0", zip);
        var layout = new LauncherLayout(dir.Combine("root"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient(new FakeHandler(github.Respond));
        var game = new UpdateService(layout, new VersionStore(layout), new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), layout.Cache), http, new FakeGames());

        UpdateCheck check = await game.CheckAsync(false, SemVersion.Parse("0.8.0"), TestContext.Current.CancellationToken);
        Assert.False(check.GameUpdateAvailable);
        Assert.False(check.LauncherUpdateAvailable);
        Assert.Null(ReleasePicker.GameAsset(check.Latest!, LauncherPlatform.WindowsX64));
        Assert.Null(ReleasePicker.FilesAsset(check.Latest!, LauncherPlatform.WindowsX64));
        await Assert.ThrowsAsync<UpdateRejectedException>(() => game.InstallAsync(check.Latest!, null, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(name, github.Requests.Select(Name));

        // Beside a Godot build, the Godot build is chosen; a legacy Godot name is still found.
        var both = new VersionedRelease(SemVersion.Parse("0.9.0"), new ReleaseInfo
        {
            TagName = "v0.9.0",
            Assets = [new() { Name = name }, new() { Name = "OpenSCCT-0.9.0-win-x64.zip" }],
        });
        Assert.Equal("OpenSCCT-0.9.0-win-x64.zip", ReleasePicker.GameAsset(both, LauncherPlatform.WindowsX64)!.Name);
        var legacy = new VersionedRelease(SemVersion.Parse("0.9.0"), new ReleaseInfo
        {
            TagName = "v0.9.0",
            Assets = [new() { Name = name }, new() { Name = "OpenSCCT-v0.9.0-win-x64.zip" }],
        });
        Assert.Equal("OpenSCCT-v0.9.0-win-x64.zip", ReleasePicker.GameAsset(legacy, LauncherPlatform.WindowsX64)!.Name);
    }

    [Fact]
    public void UnrealOffersNeverTakeGodotAssetsOrLaunchers()
    {
        var godot = new ReleaseInfo
        {
            TagName = "v0.9.0",
            Assets =
            [
                new() { Name = "OpenSCCT-0.9.0-win-x64.zip" },
                new() { Name = "OpenSCCT-0.9.0-win-x64-files.json" },
                new() { Name = "OpenSCCT-v0.9.0-win-x64.zip" },
                new() { Name = "OpenSCCT.Launcher.exe", Label = "OpenSCCT Launcher 0.9.0" },
                new() { Name = "OpenSCCT-Unreal-0.9.0-win-x64.launcher.json" },
            ],
        };
        Assert.Null(UnrealService.Offer(godot, null, LauncherPlatform.UnrealWindowsX64));

        godot.Assets.Add(new() { Name = "OpenSCCT-Unreal-0.9.0-win-x64-files.json" });
        VersionedRelease offer = UnrealService.Offer(godot, null, LauncherPlatform.UnrealWindowsX64)!;
        Assert.Null(ReleasePicker.GameAsset(offer, LauncherPlatform.UnrealWindowsX64, ReleaseNames.Unreal));
        Assert.Equal("OpenSCCT-Unreal-0.9.0-win-x64-files.json", ReleasePicker.FilesAsset(offer, LauncherPlatform.UnrealWindowsX64, ReleaseNames.Unreal)!.Name);

        Assert.Null(UnrealService.Offer(new ReleaseInfo { TagName = UnrealService.StoreTag, Assets = godot.Assets }, null, LauncherPlatform.UnrealWindowsX64));
        Assert.Null(UnrealService.Offer(new ReleaseInfo { TagName = "v0.9.0", Draft = true, Assets = godot.Assets }, null, LauncherPlatform.UnrealWindowsX64));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UninstallRemovesOnlyTheUnrealFoldersAndRefusesWhileRunning(bool removeMapPacks)
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, LauncherLayout layout) = Create(dir, github);
        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);
        SteamFiles(layout);
        string[] kept = [layout.Packs, layout.Editor, layout.VersionDir("0.9.0"), layout.Cache];
        foreach (string folder in kept)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "x");
        }

        File.WriteAllText(layout.Settings, "{}");
        Directory.CreateDirectory(Path.Combine(layout.UnrealPacks, "5.8.4-0.1.0-1"));
        File.WriteAllText(Path.Combine(layout.UnrealPacks, "5.8.4-0.1.0-1", "installed.json"), "{}");

        service.RunningOverride = () => true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(removeMapPacks, null, cancel));
        Assert.NotNull(service.Installed());

        service.RunningOverride = () => false;
        var phases = new List<UnrealProgress>();
        UnrealUninstallResult result = await service.UninstallAsync(removeMapPacks, new SyncProgress<UnrealProgress>(phases.Add), cancel);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(layout.Unreal));
        Assert.Null(service.Installed());
        Assert.Equal(!removeMapPacks, Directory.Exists(layout.UnrealPacks));
        Assert.All(kept, folder => Assert.True(File.Exists(Path.Combine(folder, "keep.txt"))));
        Assert.True(File.Exists(layout.Settings));
        Assert.Equal("OpenSCCT Unreal uninstalled", phases[^1].Phase);
    }

    [Fact]
    public async Task ForceCloseEndsOnlyGamesStartedFromTheInstallRoot()
    {
        using var dir = new TempDir();
        (UnrealService service, LauncherLayout layout) = Create(dir, new FakeGitHub());
        service.RunningOverride = null;
        var killed = new List<int>();
        UnrealProcess Fake(int id, string? exe) => new(id, exe, _ =>
        {
            killed.Add(id);
            return Task.CompletedTask;
        });

        service.ProcessesOverride = () =>
        [
            Fake(1, Path.Combine(layout.Unreal, "versions", "0.1.0", Shipping.Replace('/', Path.DirectorySeparatorChar))),
            Fake(2, dir.Combine("elsewhere", "OpenSCCTUnreal.exe")),
            Fake(3, null),
        ];
        Assert.True(service.IsRunning);
        await service.ForceCloseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 1 }, killed);

        service.ProcessesOverride = () => [Fake(2, dir.Combine("elsewhere", "OpenSCCTUnreal.exe"))];
        Assert.False(service.IsRunning);
        service.ProcessesOverride = () => [Fake(3, null)];
        Assert.True(service.IsRunning);
        Assert.Equal(new[] { "OpenSCCTUnreal", "OpenSCCTUnreal-Win64-Shipping" }, UnrealService.ProcessNames);
    }

    [Fact]
    public async Task VisualCppRuntimeInstallerComesFromTheCurrentVersion()
    {
        using var dir = new TempDir();
        var github = new FakeGitHub();
        github.AddVersion("0.1.0", Zip("0.1.0"));
        (UnrealService service, _) = Create(dir, github);
        Assert.Null(service.VcRuntimeInstaller());

        var cancel = TestContext.Current.CancellationToken;
        await service.InstallAsync((await service.CheckAsync(false, cancel)).Latest!, null, cancel);
        ProcessStartInfo installer = service.VcRuntimeInstaller()!;
        Assert.Equal(Path.Combine(service.Installed()!.Folder, "Prerequisites", "vc_redist.x64.exe"), installer.FileName);
        Assert.Equal("/install /passive /norestart", installer.Arguments);
        Assert.True(installer.UseShellExecute);

        service.VcRuntimeOverride = () => true;
        Assert.True(service.VcRuntimeMissing());
        service.VcRuntimeOverride = () => false;
        Assert.False(service.VcRuntimeMissing());
    }

    [Theory]
    [InlineData(null, UnrealService.DefaultRepository)]
    [InlineData("", UnrealService.DefaultRepository)]
    [InlineData("o/r", "o/r")]
    [InlineData(" Some-Owner/My.Repo_1 ", "Some-Owner/My.Repo_1")]
    [InlineData("owner", UnrealService.DefaultRepository)]
    [InlineData("a/b/c", UnrealService.DefaultRepository)]
    [InlineData("-a/b", UnrealService.DefaultRepository)]
    [InlineData("a/..", UnrealService.DefaultRepository)]
    [InlineData("a/b c", UnrealService.DefaultRepository)]
    [InlineData("https://github.com/a/b", UnrealService.DefaultRepository)]
    public void RepositorySettingIsValidatedWithTheBuiltInDefault(string? setting, string expected)
    {
        using var dir = new TempDir();
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var layout = new LauncherLayout(dir.Combine("root"), LauncherPlatform.WindowsX64);
        var service = new UnrealService(layout, http, setting);
        Assert.Equal(expected, service.Repository);
        Assert.Equal($"https://github.com/{expected}/releases", service.ReleasesPage);
        Assert.Equal(layout.Unreal, service.InstallRoot);
        Assert.Equal(Path.Combine(layout.Root, "unreal"), layout.Unreal);
        Assert.Equal(Path.Combine(layout.Root, "unreal-packs"), layout.UnrealPacks);
    }

    [Fact]
    public void UnrealSettingsRoundTripAndInheritTheLegacyStartupCheck()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        new LauncherSettings
        {
            SelectedProduct = "Unreal",
            UnrealIncludePrereleases = true,
            UnrealCheckOnStart = false,
            UnrealRepository = "o/r",
            CheckOnStart = true,
        }.Save(path);

        LauncherSettings read = LauncherSettings.Load(path);
        Assert.Equal("Unreal", read.SelectedProduct);
        Assert.True(read.UnrealIncludePrereleases);
        Assert.False(read.UnrealCheckOnStart);
        Assert.Equal("o/r", read.UnrealRepository);
        Assert.True(read.CheckOnStart);
        Assert.False(read.IncludePrereleases);

        File.WriteAllText(path, """{"CheckOnStart":false,"EditorCheckOnStart":true}""");
        read = LauncherSettings.Load(path);
        Assert.False(read.UnrealCheckOnStart);
        Assert.True(read.EditorCheckOnStart);
        Assert.False(read.UnrealIncludePrereleases);
        Assert.Null(read.UnrealRepository);
        Assert.Equal("Game", read.SelectedProduct);

        Assert.True(new LauncherSettings().UnrealCheckOnStart);
    }

    private static (UnrealService Service, LauncherLayout Layout) Create(TempDir dir, FakeGitHub github, string root = "root")
    {
        var http = new HttpClient(new FakeHandler(github.Respond));
        var layout = new LauncherLayout(dir.Combine(root), LauncherPlatform.WindowsX64);
        var service = new UnrealService(layout, http, "o/r") { RunningOverride = () => false };
        return (service, layout);
    }

    /// <summary>The OpenSCCT content the Game tab downloads, as far as the Unreal build needs it.</summary>
    private static string SteamFiles(LauncherLayout layout)
    {
        string id = Guid.NewGuid().ToString("N");
        string root = Path.Combine(layout.Content, SteamContentInstall.BuildsDirectory, id);
        foreach (string relative in SteamContentInstall.RequiredFiles())
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
        }
        File.WriteAllText(Path.Combine(layout.Content, SteamContentInstall.PointerFile), JsonSerializer.Serialize(new { format = 1, build = id }));
        return root;
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Name(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static byte[] VersionJson(string version) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        format = 1,
        product = UnrealService.Product,
        version,
        launcherProtocol = 1,
        openscctVersion = "0.8.161",
        openscctCommit = "abc123",
        executable = UnrealService.StarterExe,
    });

    /// <summary>A packaged build: one "OpenSCCT Unreal" folder with the starter, the Shipping exe and big.bin = old.bin.</summary>
    private static byte[] Zip(string version)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "OpenSCCT Unreal/" + UnrealService.StarterExe, Bytes("starter " + version));
            Write(zip, "OpenSCCT Unreal/" + Shipping, Bytes("shipping " + version));
            Write(zip, "OpenSCCT Unreal/Prerequisites/vc_redist.x64.exe", Bytes("redist"));
            Write(zip, "OpenSCCT Unreal/data/big.bin", Old);
            Write(zip, "OpenSCCT Unreal/version.json", VersionJson(version));
        }

        return memory.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] data)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        stream.Write(data);
    }

    private static byte[] Pack(params byte[][] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (byte[] file in files)
            {
                Write(zip, Builds.Sha256(file), file);
            }
        }

        return memory.ToArray();
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

    private static BlobRef Gz(byte[] data) => new() { Tag = UnrealService.StoreTag, Name = Builds.Sha256(data) + ".gz", Size = Gzip(data).Length };

    private static BuildFile Entry(string path, byte[] data, BlobRef blob, BuildPatch? patch = null) => new()
    {
        Path = path, Size = data.Length, Sha256 = Builds.Sha256(data), Blob = blob, Patches = patch is null ? null : [patch],
    };

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>
    /// A GitHub repository of Unreal releases: version releases with their ZIP, checksums, launcher.json and file list,
    /// and the store release, optionally reachable by its tag only (older than the first page).
    /// </summary>
    private sealed class FakeGitHub
    {
        private readonly List<(ReleaseInfo Release, bool InFeed)> _releases = [];
        private long _nextId = 1;

        public Dictionary<string, byte[]> Payloads { get; } = [];

        public List<string> Requests { get; } = [];

        public bool RepositoryMissing { get; set; }

        /// <summary>How the release list fails: null answers it, a status code answers with that error, 0 throws as when offline.</summary>
        public HttpStatusCode? FeedFailure { get; set; }

        public void AddVersion(string version, byte[]? zip, BuildFiles? files = null, bool metadata = true, int protocol = 1,
            string product = UnrealService.Product, bool prerelease = false)
        {
            string stem = $"OpenSCCT-Unreal-{version}-win-x64";
            var assets = new Dictionary<string, byte[]>();
            if (zip is not null)
            {
                assets[stem + ".zip"] = zip;
                assets[stem + ".zip.sha256"] = Bytes($"{Builds.Sha256(zip)}  {stem}.zip\n");
            }

            if (files is not null)
            {
                byte[] list = JsonSerializer.SerializeToUtf8Bytes(files);
                assets[stem + "-files.json"] = list;
                assets[stem + "-files.json.sha256"] = Bytes($"{Builds.Sha256(list)}  {stem}-files.json\n");
            }

            if (metadata)
            {
                assets[stem + ".launcher.json"] = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    launcherProtocol = protocol, product, openscctVersion = "0.8.161", openscctCommit = "abc123",
                });
            }

            AddRelease("v" + version, assets, prerelease);
        }

        public void AddRelease(string tag, Dictionary<string, byte[]> assets, bool prerelease = false, bool inFeed = true)
        {
            var release = new ReleaseInfo { Id = _nextId++, TagName = tag, Prerelease = prerelease, Body = "notes" };
            foreach ((string name, byte[] data) in assets)
            {
                Payloads[name] = data;
                release.Assets.Add(new ReleaseAsset { Id = _nextId++, Name = name, Size = data.Length, Url = "https://api.github.com/assets/" + name });
            }

            // Newest first, as GitHub lists them.
            _releases.Insert(0, (release, inFeed));
        }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (url.Contains("/releases?", StringComparison.Ordinal))
            {
                if (FeedFailure is { } failure)
                {
                    return failure == 0 ? throw new HttpRequestException("No network.") : Builds.Text("""{"message":"Server Error"}""", failure);
                }

                return RepositoryMissing
                    ? Builds.Text("""{"message":"Not Found"}""", HttpStatusCode.NotFound)
                    : Builds.Text(JsonSerializer.Serialize(_releases.Where(r => r.InFeed).Select(r => r.Release)));
            }

            const string byTag = "/releases/tags/";
            if (url.Contains(byTag, StringComparison.Ordinal))
            {
                string tag = Uri.UnescapeDataString(url[(url.IndexOf(byTag, StringComparison.Ordinal) + byTag.Length)..]);
                ReleaseInfo? release = _releases.Select(r => r.Release).FirstOrDefault(r => r.TagName == tag);
                return release is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Builds.Text(JsonSerializer.Serialize(release));
            }

            return Payloads.TryGetValue(Name(url), out byte[]? value) ? Builds.Bytes(value) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
