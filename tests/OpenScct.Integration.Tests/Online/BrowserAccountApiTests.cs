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
            var players = new PlayerPortal(options, store, registry, _ => IPAddress.Loopback, new ReleaseChangelog());
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
