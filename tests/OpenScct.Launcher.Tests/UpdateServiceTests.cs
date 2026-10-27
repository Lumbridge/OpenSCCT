using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class UpdateServiceTests
{
    private sealed class FakeGitHub
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public string Version { get; set; } = "0.2.0";

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/releases?", StringComparison.Ordinal))
            {
                var assets = new List<ReleaseAsset>();
                long id = 1;
                foreach ((string name, byte[] data) in Files)
                {
                    assets.Add(new ReleaseAsset { Id = id++, Name = name, Size = data.Length, Url = "https://api.github.com/assets/" + name });
                }

                var release = new ReleaseInfo { Id = 1, TagName = "v" + Version, Body = "notes", Assets = assets };
                return Builds.Text(JsonSerializer.Serialize(new[] { release }));
            }

            string file = url[(url.LastIndexOf('/') + 1)..];
            return Files.TryGetValue(file, out byte[]? bytes) ? Builds.Bytes(bytes) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static (UpdateService Service, VersionStore Store, LauncherLayout Layout, FakeGames Games, HttpClient Http) Create(TempDir dir, FakeGitHub github)
    {
        var layout = new LauncherLayout(dir.Combine("root"));
        var store = new VersionStore(layout);
        var http = new HttpClient(new FakeHandler(github.Respond));
        var games = new FakeGames();
        var client = new GitHubReleaseClient(http, new ReleaseSource("o", "r", null), layout.Cache);
        return (new UpdateService(layout, store, client, http, games), store, layout, games, http);
    }

    private static FakeGitHub Release(string version, bool withChecksum = true, bool corrupt = false)
    {
        var github = new FakeGitHub { Version = version };
        byte[] zip = Builds.GameZip(version);
        string name = $"OpenSCCT-{version}-win-x64.zip";
        github.Files[name] = zip;
        if (withChecksum)
        {
            string hash = corrupt ? new string('0', 64) : Builds.Sha256(zip);
            github.Files["checksums.txt"] = System.Text.Encoding.UTF8.GetBytes($"{hash}  {name}\n");
        }

        return github;
    }

    [Fact]
    public async Task ChecksDownloadsVerifiesInstallsAndSwitches()
    {
        using var dir = new TempDir();
        var (service, store, _, _, http) = Create(dir, Release("0.2.0"));
        using (http)
        {
            UpdateCheck check = await service.CheckAsync(false, null, TestContext.Current.CancellationToken);
            Assert.True(check.GameUpdateAvailable);

            InstallOutcome outcome = await service.InstallAsync(check.Latest!, null, TestContext.Current.CancellationToken);

            Assert.Equal(InstallOutcome.Activated, outcome);
            Assert.Equal(SemVersion.Parse("0.2.0"), store.Current());
            Assert.False((await service.CheckAsync(false, null, TestContext.Current.CancellationToken)).GameUpdateAvailable);
        }
    }

    [Fact]
    public async Task RefusesAReleaseWithoutAChecksum()
    {
        using var dir = new TempDir();
        var (service, store, _, _, http) = Create(dir, Release("0.2.0", withChecksum: false));
        using (http)
        {
            UpdateCheck check = await service.CheckAsync(false, null, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<UpdateRejectedException>(() => service.InstallAsync(check.Latest!, null, TestContext.Current.CancellationToken));
            Assert.Null(store.Current());
        }
    }

    [Fact]
    public async Task RefusesAndDeletesADownloadWithTheWrongChecksum()
    {
        using var dir = new TempDir();
        var (service, store, layout, _, http) = Create(dir, Release("0.2.0", corrupt: true));
        using (http)
        {
            UpdateCheck check = await service.CheckAsync(false, null, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<UpdateRejectedException>(() => service.InstallAsync(check.Latest!, null, TestContext.Current.CancellationToken));
            Assert.Null(store.Current());
            Assert.False(File.Exists(Path.Combine(layout.Downloads, "0.2.0", "OpenSCCT-0.2.0-win-x64.zip")));
        }
    }

    [Fact]
    public async Task WaitsForTheRunningGameBeforeSwitching()
    {
        using var dir = new TempDir();
        var (service, store, layout, games, http) = Create(dir, Release("0.2.0"));
        using (http)
        {
            store.InstallFromZip(WriteZip(dir, "0.1.0"), SemVersion.Parse("0.1.0"));
            store.Activate(SemVersion.Parse("0.1.0"));
            games.Games.Add(new RunningGame(1, Path.Combine(layout.VersionDir("0.1.0"), "OpenSCCT.exe")));

            UpdateCheck check = await service.CheckAsync(false, null, TestContext.Current.CancellationToken);
            Assert.Equal(InstallOutcome.WaitingForGameToClose, await service.InstallAsync(check.Latest!, null, TestContext.Current.CancellationToken));
            Assert.Equal(SemVersion.Parse("0.1.0"), store.Current());
            Assert.Equal(InstallOutcome.WaitingForGameToClose, service.TryFinishPending());

            games.Games.Clear();
            Assert.Equal(InstallOutcome.Activated, service.TryFinishPending());
            Assert.Equal(SemVersion.Parse("0.2.0"), store.Current());
            Assert.Equal(SemVersion.Parse("0.1.0"), store.Previous());
        }
    }

    [Fact]
    public void AGameWithAnUnknownPathCountsAsUsingEveryVersion()
    {
        using var dir = new TempDir();
        var (service, store, _, games, http) = Create(dir, Release("0.2.0"));
        using (http)
        {
            store.InstallFromZip(WriteZip(dir, "0.1.0"), SemVersion.Parse("0.1.0"));
            games.Games.Add(new RunningGame(1, null));
            Assert.Single(service.VersionDirsInUse());
        }
    }

    [Fact]
    public async Task LauncherUpdateIsOfferedOnlyWhenNewerAndPresent()
    {
        using var dir = new TempDir();
        FakeGitHub github = Release("0.2.0");
        github.Files["OpenSCCT.Launcher-0.2.0-win-x64.exe"] = [1, 2, 3];
        var (service, _, _, _, http) = Create(dir, github);
        using (http)
        {
            Assert.True((await service.CheckAsync(false, SemVersion.Parse("0.1.0"), TestContext.Current.CancellationToken)).LauncherUpdateAvailable);
            Assert.False((await service.CheckAsync(false, SemVersion.Parse("0.2.0"), TestContext.Current.CancellationToken)).LauncherUpdateAvailable);
        }
    }

    private static string WriteZip(TempDir dir, string version)
    {
        string path = dir.Combine($"local-{version}.zip");
        File.WriteAllBytes(path, Builds.GameZip(version));
        return path;
    }
}
