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
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class LinuxLauncherTests
{
    private static readonly LauncherPlatform Linux = LauncherPlatform.LinuxX64;

    [Fact]
    public void MixedReleaseSelectsLinuxAssetsAndItsVersionedLauncherChecksum()
    {
        var game = SemVersion.Parse("0.9.0");
        var launcher = SemVersion.Parse("0.8.41");
        var release = new VersionedRelease(game, new ReleaseInfo
        {
            Assets =
            [
                new() { Name = "OpenSCCT.Launcher.exe", Label = "OpenSCCT Launcher 0.8.99" },
                new() { Name = "OpenSCCT.Launcher-linux-x64", Label = "OpenSCCT Launcher 0.8.41" },
                new() { Name = "OpenSCCT-0.9.0-win-x64.zip" },
            ],
        })
        {
            Store = new ReleaseInfo
            {
                Assets =
                [
                    new() { Name = "OpenSCCT-0.9.0-linux-x64-files.json" },
                    new() { Name = "OpenSCCT.Launcher-0.8.41-linux-x64.sha256" },
                ],
            },
        };

        Assert.Equal("OpenSCCT-0.9.0-linux-x64-files.json", ReleasePicker.FilesAsset(release, Linux)!.Name);
        Assert.Null(ReleasePicker.GameAsset(release, Linux));
        ReleaseAsset asset = ReleasePicker.LauncherAsset(release, Linux)!;
        Assert.Equal("OpenSCCT.Launcher-linux-x64", asset.Name);
        Assert.Equal(launcher, ReleasePicker.LauncherVersion(release, Linux));
        Assert.Equal("OpenSCCT.Launcher-0.8.41-linux-x64.sha256", ReleasePicker.ChecksumAsset(release, asset, Linux)!.Name);
        Assert.Equal("OpenSCCT-0.9.0-linux-x64.zip", ReleasePicker.GameAssetName(game, Linux));
    }

    [Fact]
    public async Task LinuxDeltaUpdateSkipsNewerWindowsOnlyReleaseAndInstallsVerifiedExecutable()
    {
        using var dir = new TempDir();
        byte[] executable = "#!/bin/sh\nexit 0\n"u8.ToArray();
        byte[] blob;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(executable);
            }

            blob = buffer.ToArray();
        }

        string manifestName = "OpenSCCT-0.9.0-linux-x64-files.json";
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new BuildFiles
        {
            Format = 1,
            Version = "0.9.0",
            Files =
            [
                new()
                {
                    Path = "OpenSCCT.x86_64", Size = executable.Length, Sha256 = Builds.Sha256(executable),
                    Blob = new() { Tag = "game-files", Name = "native.gz", Size = blob.Length },
                },
            ],
        });
        var payloads = new Dictionary<string, byte[]>
        {
            [manifestName] = manifest,
            [manifestName + ".sha256"] = Encoding.UTF8.GetBytes(Builds.Sha256(manifest) + "  " + manifestName),
            ["native.gz"] = blob,
            ["OpenSCCT.Launcher-linux-x64"] = executable,
            ["OpenSCCT.Launcher-0.8.41-linux-x64.sha256"] = Encoding.UTF8.GetBytes(Builds.Sha256(executable) + "  OpenSCCT.Launcher-linux-x64"),
        };
        List<ReleaseAsset> assets = payloads.Select((pair, i) => new ReleaseAsset
        {
            Id = i + 1, Name = pair.Key, Size = pair.Value.Length, Url = "https://api.github.com/assets/" + pair.Key,
            Label = pair.Key == Linux.LauncherFileName ? "OpenSCCT Launcher 0.8.41" : null,
        }).ToList();
        ReleaseInfo[] releases =
        [
            new() { TagName = "v0.9.1", Assets = [new() { Name = "OpenSCCT.Launcher.exe", Label = "OpenSCCT Launcher 0.8.99" }] },
            new() { TagName = "v0.9.0", Assets = assets.Where(a => a.Name == Linux.LauncherFileName).ToList() },
            new() { TagName = "game-files", Assets = assets.Where(a => a.Name != Linux.LauncherFileName).ToList() },
        ];
        using var http = new HttpClient(new FakeHandler(request =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/releases?", StringComparison.Ordinal))
            {
                return Builds.Text(JsonSerializer.Serialize(releases));
            }

            return payloads.TryGetValue(url[(url.LastIndexOf('/') + 1)..], out byte[]? value)
                ? Builds.Bytes(value) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var layout = new LauncherLayout(dir.Combine("root"), Linux);
        var store = new VersionStore(layout);
        var github = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), layout.Cache);
        var service = new UpdateService(layout, store, github, http, new FakeGames());
        var cancel = TestContext.Current.CancellationToken;

        UpdateCheck check = await service.CheckAsync(false, SemVersion.Parse("0.8.40"), cancel);
        Assert.Equal("0.9.0", check.Latest!.Version.ToString());
        Assert.True(check.GameUpdateAvailable);
        Assert.True(check.LauncherUpdateAvailable);
        Assert.Equal(InstallOutcome.Activated, await service.InstallAsync(check.Latest, null, cancel));
        string installed = store.ExePath(SemVersion.Parse("0.9.0"));
        Assert.Equal("OpenSCCT.x86_64", Path.GetFileName(installed));
        Assert.Equal(executable, await File.ReadAllBytesAsync(installed, cancel));
        Assert.NotNull(service.LastDelta);

        string launcher = dir.Combine(Linux.LauncherFileName);
        await File.WriteAllTextAsync(launcher, "old launcher", cancel);
        var swap = new LauncherSwap(launcher, Linux);
        await service.StageLauncherAsync(check.Latest, swap, null, cancel);
        Assert.Equal(executable, await File.ReadAllBytesAsync(swap.NewPath, cancel));
        if (OperatingSystem.IsLinux())
        {
            Assert.True(File.GetUnixFileMode(installed).HasFlag(UnixFileMode.UserExecute));
            Assert.True(File.GetUnixFileMode(swap.NewPath).HasFlag(UnixFileMode.UserExecute));
            using Process process = Process.Start(GameStart.Create(installed))!;
            await process.WaitForExitAsync(cancel);
            Assert.Equal(0, process.ExitCode);
        }

        Assert.Equal(launcher, swap.Swap());
        swap.CleanupAfterStart(TimeSpan.Zero);
        Assert.False(File.Exists(swap.OldPath));
        Assert.Equal(executable, await File.ReadAllBytesAsync(launcher, cancel));
    }

    [Fact]
    public void ZipInstallUsesLinuxExecutableAndRejectsWindowsPayload()
    {
        using var dir = new TempDir();
        var store = new VersionStore(new LauncherLayout(dir.Combine("root"), Linux));
        string zipPath = dir.Combine("linux.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("build/OpenSCCT.x86_64").Open()))
        {
            writer.Write("native executable");
        }

        var version = SemVersion.Parse("0.9.0");
        store.InstallFromZip(zipPath, version);
        Assert.True(store.IsInstalled(version));
        if (OperatingSystem.IsLinux())
        {
            Assert.True(File.GetUnixFileMode(store.ExePath(version)).HasFlag(UnixFileMode.UserExecute));
        }

        string windowsZip = dir.Combine("windows.zip");
        File.WriteAllBytes(windowsZip, Builds.GameZip("0.9.1"));
        Assert.Throws<InvalidDataException>(() => store.InstallFromZip(windowsZip, SemVersion.Parse("0.9.1")));
    }

    [Fact]
    public void LinuxVersionedLauncherSwapsToCanonicalName()
    {
        using var dir = new TempDir();
        string old = dir.Combine("OpenSCCT.Launcher-0.8.40-linux-x64");
        string download = dir.Combine("download");
        File.WriteAllText(old, "old");
        File.WriteAllText(download, "new");
        var swap = new LauncherSwap(old, Linux);
        swap.Stage(download);
        string target = swap.Swap();
        Assert.Equal(dir.Combine(Linux.LauncherFileName), target);
        new LauncherSwap(target, Linux).CleanupAfterStart(TimeSpan.Zero);
        Assert.False(File.Exists(swap.OldPath));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public async Task CancelledInstallDoesNotActivateAnAlreadyDownloadedVersion()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("root"), Linux);
        var store = new VersionStore(layout);
        foreach (string version in new[] { "0.9.0", "0.9.1" })
        {
            var parsed = SemVersion.Parse(version);
            string staging = store.NewStagingDir(parsed);
            File.WriteAllText(Path.Combine(staging, Linux.GameFileName), "native executable");
            store.CommitStaging(staging, parsed);
            if (version == "0.9.0") store.Activate(parsed);
        }

        using var http = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("No network expected.")));
        var github = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), layout.Cache);
        var service = new UpdateService(layout, store, github, http, new FakeGames());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var release = new VersionedRelease(SemVersion.Parse("0.9.1"), new ReleaseInfo());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallAsync(release, null, cancel.Token));
        Assert.Equal("0.9.0", store.Current()!.ToString());
    }

    [Fact]
    public void LinuxProcessProbeFindsNativeExecutable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDir();
        string executable = dir.Combine(Linux.GameFileName);
        // Use the shell ELF rather than sleep: some distributions ship sleep as a multicall wrapper
        // that refuses a renamed executable, before the process probe can observe it.
        File.Copy("/bin/sh", executable);
        Linux.MakeExecutable(executable);
        using Process process = Process.Start(new ProcessStartInfo(executable) { ArgumentList = { "-c", "sleep 30 & wait" }, UseShellExecute = false })!;
        try
        {
            RunningGame game = Assert.Single(new GameProcessProbe(Linux).Running(), game => game.ProcessId == process.Id);
            Assert.Equal(executable, game.ExePath);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }
}

[CollectionDefinition("Launcher environment", DisableParallelization = true)]
public sealed class LauncherEnvironmentCollection;

[Collection("Launcher environment")]
public sealed class LinuxLauncherPathsTests
{
    [Fact]
    public void LinuxPathsRespectXdgAndIgnoreRelativeOverrides()
    {
        using var dir = new TempDir();
        string? previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", dir.Path);
            Assert.Equal(dir.Combine("OpenSCCT"), LauncherLayout.GetDefaultRoot(LauncherPlatform.LinuxX64));
            Assert.Equal(dir.Combine("godot", "app_userdata", "OpenSCCT"), LauncherLayout.GetGameUserData(LauncherPlatform.LinuxX64));
            if (OperatingSystem.IsLinux())
            {
                // The game discovers packs independently using .NET's LocalApplicationData mapping.
                Assert.Equal(ContentStore.DefaultRoot, new LauncherLayout(LauncherLayout.DefaultRoot).Packs);
            }
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "relative/path");
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "OpenSCCT"),
                LauncherLayout.GetDefaultRoot(LauncherPlatform.LinuxX64));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }
}
