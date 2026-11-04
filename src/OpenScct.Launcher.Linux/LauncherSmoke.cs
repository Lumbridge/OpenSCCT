using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;

namespace OpenScct.Launcher.Linux;

/// <summary>A repeatable, offline end-to-end launcher flow using verified HTTP fixtures and an isolated install.</summary>
internal static class LauncherSmoke
{
    public static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "openscct-launcher-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var server = new FixtureServer();
            using var http = new HttpClient(server);
            var games = new GameProbe();
            using var model = new LauncherController(new LauncherLayout(root, LauncherPlatform.LinuxX64), http, null, games);
            Require(model.Next == LauncherAction.Install, "Fresh installs must offer Install.");
            await model.RunAsync("Installing fixture", model.InstallAsync);
            Require(!model.IsError, model.Status);
            Require(model.Next == LauncherAction.Play, "A verified game and its shared packs must enable Play.");
            Require(model.Store.Current()?.ToString() == "1.0.0", "The downloaded Linux game must become current.");
            Require(Path.GetFileName(model.Store.ExePath(model.Store.Current()!)) == "OpenSCCT.x86_64", "Linux must never launch the Windows executable.");
            Require(model.Packs.Installed().Count(p => p.IsShared) == 3, "Install must fetch every required shared pack.");
            Require(model.Packs.InstalledMap("SmokeStock") is not null, "First install must include stock maps.");
            Require(model.Packs.InstalledMap("SmokeExtra") is null, "Optional maps must not be downloaded on first install.");
            ContentIndex catalogue = await model.LoadMapsAsync(CancellationToken.None);
            ContentMap extra = catalogue.FindMap("SmokeExtra")!;
            await model.RunAsync("Downloading optional map", cancel => model.DownloadMapsAsync([extra], cancel));
            Require(model.Packs.Has(extra), "Selected maps must download and verify successfully.");
            model.RemoveMaps([extra.Code]);
            Require(model.Packs.InstalledMap(extra.Code) is null && !Directory.Exists(Path.Combine(model.Packs.SvMapRoot, extra.Code)),
                "Removing a map must remove both its files and installation record.");
            if (OperatingSystem.IsLinux())
                Require((File.GetUnixFileMode(model.Store.ExePath(model.Store.Current()!)) & UnixFileMode.UserExecute) != 0,
                    "Downloaded Linux executables must be executable.");
            model.SaveSettings(false, true);
            LauncherSettings saved = LauncherSettings.Load(model.Layout.Settings);
            Require(!saved.CheckOnStart && saved.IncludePrereleases, "Settings must survive a launcher restart.");
            model.QueueJoin("test_server-42");
            Require(model.PendingJoin == "test_server-42", "Join requests must survive setup operations.");

            server.Version = "1.1.0";
            await model.RunAsync("Checking fixture update", model.CheckAsync);
            Require(model.Next == LauncherAction.Update, "A new Linux game release must offer Update.");
            using (var cancel = new CancellationTokenSource())
            {
                bool cancelled = false;
                try
                {
                    await model.PlayAsync(false, _ => { cancel.Cancel(); return Task.FromResult(PlayUpdateChoice.PlayCurrent); }, cancel.Token);
                }
                catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled, "Cancellation during the update choice must prevent launching.");
            }
            games.InUse = true;
            await model.RunAsync("Installing pending update", model.InstallAsync);
            Require(model.Next == LauncherAction.Waiting, "A running game must defer activation.");
            Require(model.Store.Current()?.ToString() == "1.0.0", "Updating must keep the running game version current.");
            games.InUse = false;
            model.Tick();
            Require(model.Store.Current()?.ToString() == "1.1.0", "Closing the game must finish the pending update.");
            model.Rollback();
            Require(model.Store.Current()?.ToString() == "1.0.0", "The previous game build must remain available for rollback.");

            Task work = model.RunAsync("Cancelling fixture", token => Task.Delay(Timeout.Infinite, token));
            Require(model.Next == LauncherAction.Cancel, "Busy work must expose Stop.");
            model.Cancel();
            await work;
            Require(!model.Busy && !model.IsError, "Cancellation must restore usable controls without deleting the install.");
            Require(model.Playable, "Cancellation must preserve the installed game.");
            server.Offline = true;
            await model.RunAsync("Offline update check", model.CheckAsync);
            Require(model.Playable, "Failed network checks must preserve installed offline play.");
            model.Reset();
            Require(model.Next == LauncherAction.Install, "Reset must return to Install.");
            Require(File.Exists(model.Layout.Settings), "Reset must retain launcher preferences.");
            Console.WriteLine("Linux launcher smoke passed: verified game/content install, stock and optional maps, native executable permissions, update, running-game deferral, rollback, cancellation, offline readiness, settings and reset.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Linux launcher smoke failed: " + ex);
            return 1;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class GameProbe : IGameProcessProbe
    {
        public bool InUse { get; set; }
        public IReadOnlyList<RunningGame> Running() => InUse ? [new RunningGame(42, null)] : [];
    }

    private sealed class FixtureServer : HttpMessageHandler
    {
        private readonly byte[] _game = Zip("OpenSCCT.x86_64", "fixture game executable");
        private readonly byte[] _map = MapZip();
        private readonly Dictionary<string, byte[]> _packs = new[] { "core", "chars", "audio" }
            .ToDictionary(id => id, id => Zip(id + ".txt", "verified fixture " + id));
        public string Version { get; set; } = "1.0.0";
        public bool Offline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (Offline) throw new HttpRequestException("Offline smoke fixture");
            string url = request.RequestUri!.AbsoluteUri;
            if (url == ContentStore.IndexUrl)
            {
                string index = JsonSerializer.Serialize(new
                {
                    format = "openscct.content", formatVersion = 1, svmapFormat = "1.0",
                    shared = _packs.Select(p => new
                    {
                        id = p.Key, title = p.Key, version = "1", contents = new[] { p.Key + ".txt" },
                        download = new { url = "https://fixture.invalid/pack/" + p.Key, size = p.Value.Length, sha256 = Hash(p.Value) },
                    }),
                    maps = new[] { (Code: "SmokeStock", Category: "stock"), (Code: "SmokeExtra", Category: "community") }.Select(map => new
                    {
                        id = map.Category + "/" + map.Code, code = map.Code, category = map.Category, title = map.Code,
                        version = "1", modes = new[] { "Deathmatch" }, contentHash = Hash(_map),
                        full = new { url = "https://fixture.invalid/map", size = _map.Length, sha256 = Hash(_map) },
                    }),
                });
                return Task.FromResult(Text(index));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                var release = new ReleaseInfo
                {
                    TagName = "v" + Version, Name = "Fixture " + Version,
                    Assets =
                    [
                        new ReleaseAsset { Id = 1, Name = "OpenSCCT-" + Version + "-linux-x64.zip", Size = _game.Length, BrowserDownloadUrl = "https://fixture.invalid/game" },
                        new ReleaseAsset { Id = 2, Name = "checksums.txt", Size = 200, BrowserDownloadUrl = "https://fixture.invalid/sums" },
                    ],
                };
                return Task.FromResult(Text(JsonSerializer.Serialize(new[] { release })));
            }
            if (url == "https://fixture.invalid/game") return Task.FromResult(Bytes(_game));
            if (url == "https://fixture.invalid/map") return Task.FromResult(Bytes(_map));
            if (url == "https://fixture.invalid/sums") return Task.FromResult(Text(Hash(_game) + "  OpenSCCT-" + Version + "-linux-x64.zip\n"));
            if (url.StartsWith("https://fixture.invalid/pack/", StringComparison.Ordinal))
                return Task.FromResult(Bytes(_packs[request.RequestUri.Segments[^1]]));
            throw new InvalidOperationException("Unexpected smoke HTTP request: " + url);
        }

        private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
        private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
        private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
        private static byte[] Zip(string name, string contents)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8)) writer.Write(contents);
            return buffer.ToArray();
        }

        private static byte[] MapZip()
        {
            byte[] data = Encoding.UTF8.GetBytes("fixture render geometry");
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                files = new Dictionary<string, object> { ["render/fixture.bin"] = new { size = data.Length, sha256 = Hash(data) } },
            });
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                using (Stream entry = zip.CreateEntry("manifest.json").Open()) entry.Write(manifest);
                using (Stream entry = zip.CreateEntry("render/fixture.bin").Open()) entry.Write(data);
            }
            return buffer.ToArray();
        }
    }
}
