using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;
using OpenScct.Net.Cosmetics;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserGameProfileTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void SeparateAppearanceDoesNotInvalidateSettingsRevisionOrReplaceAppearance()
    {
        var clock = new Clock();
        using var store = AccountStore.Open(":memory:", clock);
        long account = store.GetOrCreate("browser-separated-settings").Id;
        var profile = new ProfileData { Data = "{\"version\":1,\"settings\":{\"invert_y\":true}}", Updated = clock.Now };
        Assert.True(store.TryPutBrowserProfile(account, profile, null, out var saved, separateAppearance: true));
        var before = store.Appearance(account);
        clock.Now = clock.Now.AddMinutes(1);
        var changed = store.UpdateAppearance(account, new AppearanceUpdateRequest { ExpectedRevision = before.Revision, Spy = [], Merc = [] });
        Assert.True(changed.Success);
        Assert.Equal(saved!.Updated, store.BrowserProfile(account, separateAppearance: true)!.Updated);
        Assert.True(store.Profile(account)!.Updated > saved.Updated);
        Assert.True(store.TryPutBrowserProfile(account, profile with { Data = "{\"version\":1,\"settings\":{\"invert_y\":false}}" },
            saved.Updated, out var next, separateAppearance: true));
        Assert.True(next!.Updated > saved.Updated);
        Assert.Equal(changed.Appearance.Revision, store.Appearance(account).Revision);
        Assert.False(store.TryPutBrowserProfile(account, profile, saved.Updated, out _, separateAppearance: true));
    }

    [Fact]
    public void AppearanceRevisionReturnedToBrowserIsAcceptedForTheNextSettingsSave()
    {
        var clock = new Clock();
        using var store = AccountStore.Open(":memory:", clock);
        long account = store.GetOrCreate("browser-appearance").Id;
        var data = new ProfileData { Data = "{\"version\":1,\"settings\":{\"player_name\":\"Before\"}}", Updated = clock.Now };
        store.PutProfile(account, data);
        clock.Now = clock.Now.AddMinutes(1);
        var appearance = store.Appearance(account);
        store.UpdateAppearance(account, new AppearanceUpdateRequest { ExpectedRevision = appearance.Revision, Spy = [], Merc = [] });
        var downloaded = store.Profile(account)!;
        Assert.True(downloaded.Updated > data.Updated);
        Assert.True(store.TryPutBrowserProfile(account, data with { Data = data.Data.Replace("Before", "After", StringComparison.Ordinal) }, downloaded.Updated, out var saved));
        Assert.Contains("After", saved!.Data);
        Assert.True(saved.Updated > downloaded.Updated);
        Assert.False(store.TryPutBrowserProfile(account, data, downloaded.Updated, out _));
    }
    [Fact]
    public async Task BrowserBundleServesPackAndWasmWithEmbeddingPolicyAndCanonicalUrl()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        DirectoryInfo root = Directory.CreateTempSubdirectory("OpenSCCT-browser-bundle-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "index.html"), "<!doctype html><title>Browser bundle</title>");
            File.WriteAllBytes(Path.Combine(root.FullName, "godot.pck"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root.FullName, "godot.wasm"), [0, 97, 115, 109]);
            using (var gzip = new GZipStream(File.Create(Path.Combine(root.FullName, "godot.wasm.gz")), CompressionLevel.Optimal))
                gzip.Write([0, 97, 115, 109]);
            using var store = AccountStore.Open(":memory:");
            var options = MasterTests.LocalOptions();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            new PlayerPortal(options, store, new ServerRegistry(options), _ => IPAddress.Loopback,
                new ReleaseChangelog(), browserGameRoot: root.FullName, enableBrowserPrototype: true).Map(app);
            await app.StartAsync(ct);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            using var redirect = await client.GetAsync("/play", ct);
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Equal("/play/", redirect.Headers.Location!.OriginalString);
            using var page = await client.GetAsync("/play/", ct);
            page.EnsureSuccessStatusCode();
            Assert.Contains("frame-ancestors 'self'", page.Headers.GetValues("Content-Security-Policy").Single());
            using var pack = await client.GetAsync("/play/godot.pck", ct);
            pack.EnsureSuccessStatusCode();
            Assert.Equal("application/octet-stream", pack.Content.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, await pack.Content.ReadAsByteArrayAsync(ct));
            using var wasm = await client.GetAsync("/play/godot.wasm", ct);
            wasm.EnsureSuccessStatusCode();
            Assert.Equal("application/wasm", wasm.Content.Headers.ContentType!.MediaType);
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
            using var compressed = await client.GetAsync("/play/godot.wasm", ct);
            compressed.EnsureSuccessStatusCode();
            Assert.Equal("application/wasm", compressed.Content.Headers.ContentType!.MediaType);
            Assert.Contains("gzip", compressed.Content.Headers.ContentEncoding);
            Assert.Contains("Accept-Encoding", compressed.Headers.Vary);
            using var zipped = new MemoryStream(await compressed.Content.ReadAsByteArrayAsync(ct));
            using var decoded = new GZipStream(zipped, CompressionMode.Decompress);
            byte[] bytes = new byte[4];
            decoded.ReadExactly(bytes);
            Assert.Equal(new byte[] { 0, 97, 115, 109 }, bytes);
            await app.StopAsync(ct);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task BrowserProfileRequiresSessionAndCsrfAndRejectsStaleTabsAndChangedAccounts()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        using var store = AccountStore.Open(":memory:");
        long account = store.GetOrCreate("browser-profile").Id;
        long other = store.GetOrCreate("browser-other").Id;
        string webToken = store.CreatePlayerWebSession(account);
        string csrf = store.PlayerWebSession(webToken)!.Csrf;
        var options = MasterTests.LocalOptions();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        new PlayerPortal(options, store, new ServerRegistry(options), _ => IPAddress.Loopback, new ReleaseChangelog(), enableBrowserPrototype: true).Map(app);
        await app.StartAsync(ct);
        using var guest = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var guestState = JsonDocument.Parse(await guest.GetStringAsync("/play/profile", ct));
        Assert.False(guestState.RootElement.GetProperty("signedIn").GetBoolean());
        var profile = new ProfileData { Data = "{\"version\":1,\"settings\":{\"invert_y\":true},\"keys_spy\":\"W=MoveForward\",\"keys_merc\":\"W=MoveForward\"}", Updated = DateTimeOffset.UtcNow };
        using var denied = await guest.PostAsJsonAsync("/play/profile", new { accountId = account, profile, expectedUpdated = (DateTimeOffset?)null }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var web = new HttpClient { BaseAddress = guest.BaseAddress };
        web.DefaultRequestHeaders.Add("Cookie", "svm_player=" + webToken);
        using var state = JsonDocument.Parse(await web.GetStringAsync("/play/profile", ct));
        Assert.True(state.RootElement.GetProperty("signedIn").GetBoolean());
        Assert.Equal(account, state.RootElement.GetProperty("accountId").GetInt64());
        Assert.Equal(csrf, state.RootElement.GetProperty("csrf").GetString());
        using var noCsrf = await web.PostAsJsonAsync("/play/profile", new { accountId = account, profile, expectedUpdated = (DateTimeOffset?)null }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Null(store.Profile(account));

        web.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
        using var wrongAccount = await web.PostAsJsonAsync("/play/profile", new { accountId = other, profile, expectedUpdated = (DateTimeOffset?)null }, ct);
        Assert.Equal(HttpStatusCode.Conflict, wrongAccount.StatusCode);
        Assert.Null(store.Profile(other));
        using var response = await web.PostAsJsonAsync("/play/profile", new { accountId = account, profile, expectedUpdated = (DateTimeOffset?)null }, ct);
        response.EnsureSuccessStatusCode();
        ProfileData saved = (await response.Content.ReadFromJsonAsync<ProfileData>(new JsonSerializerOptions(JsonSerializerDefaults.Web), ct))!;
        Assert.Contains("keys_spy", saved.Data);
        using var stale = await web.PostAsJsonAsync("/play/profile", new { accountId = account, profile = profile with { Data = "stale" }, expectedUpdated = (DateTimeOffset?)null }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.DoesNotContain("stale", store.Profile(account)!.Data);
        using var newer = await web.PostAsJsonAsync("/play/profile", new { accountId = account, profile, expectedUpdated = saved.Updated }, ct);
        newer.EnsureSuccessStatusCode();
        Assert.True(store.Profile(account)!.Updated > saved.Updated);
        await app.StopAsync(ct);
    }
}
