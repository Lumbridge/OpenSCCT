using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserAccountApiTests
{
    [Fact]
    public async Task ProductionRetiresBrowserTransportAndKeepsDesktopAndWebsiteAccountsIndependent()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("OpenSCCT-retired-browser-");
        try
        {
            var options = MasterTests.LocalOptions();
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = Path.Combine(directory.FullName, "accounts.db");
            options.TicketKeyFile = Path.Combine(directory.FullName, "tickets.pem");
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using var master = await MasterHost.StartAsync(options, cancellationToken: ct);
            using var store = AccountStore.Open(options.Database);
            var account = store.GetOrCreate("retired-browser-fixture");
            store.ClaimName(account.Id, "DesktopPlayer", false, out _);
            string cookie = store.CreatePlayerWebSession(account.Id);
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(master.HttpAddress) };
            http.DefaultRequestHeaders.Add("Cookie", "svm_player=" + cookie);
            http.DefaultRequestHeaders.Add("Origin", master.HttpAddress);
            http.DefaultRequestHeaders.Add("X-CSRF-Token", store.PlayerWebSession(cookie)!.Csrf);
            http.DefaultRequestHeaders.Add("X-OpenSCCT-Account", account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (string path in new[] { "/play", "/play/", "/play/content/index.json", "/play/_framework/game.wasm", "/play/profile", "/play/servers", "/play/socket/retired" })
            {
                using var result = await http.GetAsync(path, ct);
                Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
            }
            foreach (string path in new[] { "/play/join", "/play/profile" })
            {
                using var result = await http.PostAsJsonAsync(path, new { }, ct);
                Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
            }
            using (var asset = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/play/content/old.zip"), ct))
                Assert.Equal(HttpStatusCode.Gone, asset.StatusCode);
            using (var gameAccount = await http.GetAsync(MasterApi.AccountPath, ct))
                Assert.Equal(HttpStatusCode.Unauthorized, gameAccount.StatusCode);
            Assert.Contains("DesktopPlayer", await http.GetStringAsync("/account", ct), StringComparison.Ordinal);
            http.DefaultRequestHeaders.Authorization = new("Bearer", store.CreateSession(account.Id));
            using (var desktop = await http.GetAsync(MasterApi.AccountPath, ct)) desktop.EnsureSuccessStatusCode();
            using (var health = await http.GetAsync(MasterApi.StatusPath, ct)) health.EnsureSuccessStatusCode();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task NativeAccountPagesUseCookieCsrfAndExpectedAccountWithoutIssuingBearerTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("browser-native-account").Id;
        store.ClaimName(id, "BrowserAccount", false, out _);
        string token = store.CreatePlayerWebSession(id);
        string csrf = store.PlayerWebSession(token)!.Csrf;
        DirectoryInfo temp = Directory.CreateTempSubdirectory("OpenSCCT-browser-account-");
        try
        {
            using var signer = TicketSigner.LoadOrCreate(Path.Combine(temp.FullName, "tickets.pem"));
            var options = MasterTests.LocalOptions();
            var registry = new ServerRegistry(options);
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(options.HttpUrl);
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            var players = new PlayerPortal(options, store, registry, _ => IPAddress.Loopback, new ReleaseChangelog(), enableBrowserPrototype: true);
            players.Map(app);
            new AccountApi(options, store, signer, registry, _ => IPAddress.Loopback, TimeProvider.System)
            {
                BrowserAuthenticate = players.AuthenticateGameApi,
                BrowserSignOut = players.SignOutGameApi,
            }.Map(app);
            await app.StartAsync(ct);
            using var web = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            options.PublicUrl = web.BaseAddress.GetLeftPart(UriPartial.Authority);
            using (var guest = await web.GetAsync(MasterApi.AccountPath, ct)) Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
            web.DefaultRequestHeaders.Add("Cookie", "svm_player=" + token);
            using (var noCsrf = await web.GetAsync(MasterApi.AccountPath, ct)) Assert.Equal(HttpStatusCode.Unauthorized, noCsrf.StatusCode);
            web.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            web.DefaultRequestHeaders.Add("X-OpenSCCT-Account", (id + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            using (var staleTab = await web.GetAsync(MasterApi.AccountPath, ct)) Assert.Equal(HttpStatusCode.Unauthorized, staleTab.StatusCode);
            web.DefaultRequestHeaders.Remove("X-OpenSCCT-Account");
            web.DefaultRequestHeaders.Add("X-OpenSCCT-Account", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            web.DefaultRequestHeaders.Add("Origin", "https://other.example");
            using (var foreign = await web.PutAsJsonAsync(MasterApi.AppearancePath, new AppearanceUpdateRequest { ExpectedRevision = store.Appearance(id).Revision }, ct))
                Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
            web.DefaultRequestHeaders.Remove("Origin");
            web.DefaultRequestHeaders.Add("Origin", options.PublicUrl);
            using (var account = await web.GetAsync(MasterApi.AccountPath, ct))
            {
                account.EnsureSuccessStatusCode();
                var info = await account.Content.ReadFromJsonAsync<AccountInfo>(MasterApi.Json, ct);
                Assert.Equal(id, info!.Id);
                Assert.Equal("BrowserAccount", info.Name);
                Assert.True(info.AppearanceSync);
                Assert.NotNull(info.Achievements);
                Assert.DoesNotContain("sessionToken", await account.Content.ReadAsStringAsync(ct), StringComparison.OrdinalIgnoreCase);
            }
            long appearanceRevision = store.Appearance(id).Revision;
            using (var update = await web.PutAsJsonAsync(MasterApi.AppearancePath, new AppearanceUpdateRequest { ExpectedRevision = store.Appearance(id).Revision }, ct))
                update.EnsureSuccessStatusCode();
            Assert.Equal(appearanceRevision + 1, store.Appearance(id).Revision);
            using (var history = await web.GetAsync(MasterApi.MatchHistoryPath, ct)) history.EnsureSuccessStatusCode();
            using (var unsafeProfile = await web.PutAsJsonAsync(MasterApi.ProfilePath,
                new ProfileData { Data = "{}", Updated = DateTimeOffset.UtcNow }, ct))
                Assert.Equal(HttpStatusCode.Unauthorized, unsafeProfile.StatusCode);
            Assert.Null(store.Profile(id));
            web.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
            using (var invalidBearer = await web.GetAsync(MasterApi.AccountPath, ct)) Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);
            web.DefaultRequestHeaders.Authorization = new("Bearer", store.CreateSession(id));
            web.DefaultRequestHeaders.Remove("X-CSRF-Token");
            using (var desktop = await web.GetAsync(MasterApi.AccountPath, ct)) desktop.EnsureSuccessStatusCode();
            web.DefaultRequestHeaders.Authorization = null;
            web.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            using (var signOut = await web.PostAsJsonAsync(MasterApi.SignOutPath, new { }, ct))
            {
                signOut.EnsureSuccessStatusCode();
                Assert.Contains(signOut.Headers.GetValues("Set-Cookie"), c => c.StartsWith("svm_player=;", StringComparison.Ordinal));
            }
            Assert.Null(store.PlayerWebSession(token));
            using (var ended = await web.GetAsync(MasterApi.AccountPath, ct)) Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
            await app.StopAsync(ct);
        }
        finally { temp.Delete(recursive: true); }
    }
}
