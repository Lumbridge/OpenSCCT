using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Unreal;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

/// <summary>
/// Installs and updates real OpenSCCT Unreal releases made by OpenSCCT-Unreal's Tools/Package-Release.ps1, served as a
/// fake GitHub repository. Opt in by pointing OPENSCCT_UNREAL_RELEASES at a folder laid out as
/// <c>&lt;version&gt;/{OpenSCCT-Unreal-&lt;v&gt;-win-x64.zip, .zip.sha256, .launcher.json, unreal-files/{...-files.json,
/// .sha256, blobs, packs, .osdiff patches}}</c> (OpenSCCT-Unreal's Saved/Release); folders whose name is not a
/// version are ignored. Each release keeps the assets it was published with. The oldest version is installed first
/// (from its file list when it has one, as a player's first install is, else from its ZIP), then each newer one
/// from its file list. A second pass installs the newest version into an empty folder through its file list,
/// fetching every blob and pack member, which is what a new player's first install does.
/// </summary>
public sealed class UnrealLocalReleaseTests
{
    private const string Variable = "OPENSCCT_UNREAL_RELEASES";

    public static bool HasReleases => Directory.Exists(Environment.GetEnvironmentVariable(Variable));

    [Fact(Skip = "Set OPENSCCT_UNREAL_RELEASES to a folder of packaged OpenSCCT Unreal releases.", SkipUnless = nameof(HasReleases))]
    public async Task InstallsTheOldestReleaseThenUpdatesStepByStep()
    {
        string source = Environment.GetEnvironmentVariable(Variable)!;
        List<(SemVersion Version, string Folder)> versions = Directory.EnumerateDirectories(source)
            .Select(d => (Ok: SemVersion.TryParse(Path.GetFileName(d), out SemVersion? v) && v.ToString() == Path.GetFileName(d), Version: v, Folder: d))
            .Where(x => x.Ok)
            .Select(x => (x.Version!, x.Folder))
            .OrderBy(x => x.Item1)
            .ToList();
        Assert.True(versions.Count >= 2, $"{source} needs at least two versions.");

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var releases = new List<ReleaseInfo>();
        var store = new ReleaseInfo { Id = 1, TagName = UnrealService.StoreTag, Prerelease = true };
        long id = 10;
        ReleaseAsset Asset(string path)
        {
            string name = Path.GetFileName(path);
            files[name] = path;
            return new ReleaseAsset { Id = id++, Name = name, Size = new FileInfo(path).Length, Url = "https://api.github.com/assets/" + name };
        }

        foreach ((SemVersion version, string folder) in versions)
        {
            string stem = $"OpenSCCT-Unreal-{version}-win-x64";
            var release = new ReleaseInfo { Id = id++, TagName = "v" + version, Prerelease = version.IsPrerelease };
            foreach (string name in new[] { stem + ".zip", stem + ".zip.sha256", stem + ".launcher.json" })
            {
                release.Assets.Add(Asset(Path.Combine(folder, name)));
            }

            string delta = Path.Combine(folder, "unreal-files");
            if (Directory.Exists(delta))
            {
                foreach (string path in Directory.EnumerateFiles(delta))
                {
                    string name = Path.GetFileName(path);
                    bool list = name.Equals(stem + "-files.json", StringComparison.OrdinalIgnoreCase)
                        || name.Equals(stem + "-files.json.sha256", StringComparison.OrdinalIgnoreCase);
                    if (list)
                    {
                        release.Assets.Add(Asset(path));
                    }
                    else if (!store.Assets.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        store.Assets.Add(Asset(path));
                    }
                }
            }

            releases.Insert(0, release);
        }

        releases.Insert(0, store);
        using var dir = new TempDir();
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(request =>
        {
            string url = request.RequestUri!.ToString();
            lock (requests)
            {
                requests.Add(url[(url.LastIndexOf('/') + 1)..]);
            }

            if (url.Contains("/releases?", StringComparison.Ordinal))
            {
                return Builds.Text(JsonSerializer.Serialize(releases));
            }

            string name = url[(url.LastIndexOf('/') + 1)..];
            if (url.Contains("/releases/tags/", StringComparison.Ordinal))
            {
                ReleaseInfo? release = releases.FirstOrDefault(r => r.TagName == Uri.UnescapeDataString(name));
                return release is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Builds.Text(JsonSerializer.Serialize(release));
            }

            return files.TryGetValue(name, out string? path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var layout = new LauncherLayout(dir.Combine("root"), LauncherPlatform.WindowsX64);
        var service = new UnrealService(layout, http, "o/r") { RunningOverride = () => false };
        var cancel = TestContext.Current.CancellationToken;

        UnrealCheck check = await service.CheckAsync(true, cancel);
        Assert.Equal(versions[^1].Version, check.Latest!.Version);

        foreach ((SemVersion version, string folder) in versions)
        {
            ReleaseInfo release = releases.Single(r => r.TagName == "v" + version);
            VersionedRelease offer = UnrealService.Offer(release, null, LauncherPlatform.UnrealWindowsX64)!;
            UnrealInstallResult result = await service.InstallAsync(offer, null, cancel);
            Assert.Equal(UnrealInstallOutcome.Installed, result.Outcome);
            // A release with a file list is installed through it, never through the ZIP fallback.
            Assert.Equal(HasFileList(release, version), result.BuildBytes is not null);
            UnrealInstallation installed = service.Installed()!;
            Assert.Equal(version, installed.Version);
            long build = await VerifyAsync(installed.Folder, Path.Combine(folder, $"OpenSCCT-Unreal-{version}-win-x64.zip"));
            Report(version.ToString(), result, build);
        }

        Assert.False((await service.CheckAsync(true, cancel)).UpdateAvailable);

        // A new player's first install: the newest version into an empty folder, every file through the file list.
        (SemVersion newest, string newestFolder) = versions[^1];
        ReleaseInfo newestRelease = releases.Single(r => r.TagName == "v" + newest);
        if (!HasFileList(newestRelease, newest))
        {
            return;
        }

        lock (requests)
        {
            requests.Clear();
        }

        var fresh = new UnrealService(new LauncherLayout(dir.Combine("fresh"), LauncherPlatform.WindowsX64), http, "o/r") { RunningOverride = () => false };
        UnrealCheck first = await fresh.CheckAsync(true, cancel);
        Assert.Null(first.Installed);
        UnrealInstallResult firstResult = await fresh.InstallAsync(first.Latest!, null, cancel);
        Assert.Equal(UnrealInstallOutcome.Installed, firstResult.Outcome);
        Assert.NotNull(firstResult.BuildBytes);
        Assert.DoesNotContain($"OpenSCCT-Unreal-{newest}-win-x64.zip", requests);
        UnrealInstallation firstInstalled = fresh.Installed()!;
        Assert.Equal(newest, firstInstalled.Version);
        long firstBuild = await VerifyAsync(firstInstalled.Folder, Path.Combine(newestFolder, $"OpenSCCT-Unreal-{newest}-win-x64.zip"));
        Report(newest + " first install", firstResult, firstBuild);
    }

    private static bool HasFileList(ReleaseInfo release, SemVersion version) =>
        release.Assets.Any(a => a.Name.Equals($"OpenSCCT-Unreal-{version}-win-x64-files.json", StringComparison.OrdinalIgnoreCase));

    private static void Report(string label, UnrealInstallResult result, long build) =>
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{label}: {(result.BuildBytes is null ? "ZIP" : "file list")}, downloaded {result.DownloadedBytes:N0} bytes for a {build:N0}-byte build ({100.0 * (result.DownloadedBytes ?? 0) / build:F1}%)."));

    /// <summary>Checks every file against the version.json in the release's ZIP (size and SHA-256), and that nothing else is there.</summary>
    private static async Task<long> VerifyAsync(string installed, string zipPath)
    {
        byte[] versionJson;
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry entry = zip.GetEntry("OpenSCCT Unreal/version.json") ?? throw new InvalidDataException($"{zipPath} has no version.json.");
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, TestContext.Current.CancellationToken);
            versionJson = memory.ToArray();
        }

        Assert.Equal(versionJson, await File.ReadAllBytesAsync(Path.Combine(installed, "version.json"), TestContext.Current.CancellationToken));
        using JsonDocument doc = JsonDocument.Parse(versionJson);
        var expected = new Dictionary<string, (long Size, string Sha256)>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement file in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            expected[file.GetProperty("path").GetString()!] = (file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString()!);
        }

        var actual = Directory.EnumerateFiles(installed, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(installed, f).Replace('\\', '/'))
            .Where(f => !f.Equals("version.json", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(actual.SetEquals(expected.Keys), "The installed files differ from the release's version.json.");
        long total = 0;
        foreach ((string path, (long size, string sha)) in expected)
        {
            string full = Path.Combine(installed, path.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(size, new FileInfo(full).Length);
            Assert.Equal(sha, await Checksums.HashFileAsync(full, TestContext.Current.CancellationToken), StringComparer.OrdinalIgnoreCase);
            total += size;
        }

        return total + versionJson.Length;
    }
}
