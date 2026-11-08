using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PronounAccountTests
{
    [Fact]
    public void VersionElevenAccountsUpgradeWithoutLosingNamesOrSessionsAndPronounsPersist()
    {
        string path = Path.Combine(Path.GetTempPath(), "pronouns-" + Guid.NewGuid() + ".db");
        try
        {
            long id;
            string token;
            using (var store = AccountStore.Open(path))
            {
                id = store.GetOrCreate("pronoun-migration").Id;
                store.ClaimName(id, "Alex", false, out _);
                token = store.CreateSession(id);
            }
            using (var db = new SqliteConnection("Data Source=" + path))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "DROP TABLE account_pronouns; UPDATE schema_version SET version = 11;";
                command.ExecuteNonQuery();
            }
            using (var store = AccountStore.Open(path))
            {
                Assert.Equal("Alex", store.Authenticate(token)!.Name);
                Assert.Equal("they/them", store.Pronouns(id));
                Assert.True(store.SetPronouns(id, "he/him"));
                Assert.False(store.SetPronouns(id, "<script>"));
            }
            using var reopened = AccountStore.Open(path);
            Assert.Equal("he/him", reopened.Pronouns(id));
            Assert.Equal("he/him", reopened.LiveAppearance(id).Pronouns);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task AuthenticatedAccountApiSavesOnlyTheCallersPronounsAndRejectsInvalidForms()
    {
        var ct = TestContext.Current.CancellationToken;
        string keyPath = Path.Combine(Path.GetTempPath(), "pronouns-" + Guid.NewGuid() + ".pem");
        try
        {
            using var store = AccountStore.Open(":memory:");
            var account = store.GetOrCreate("pronoun-api");
            long other = store.GetOrCreate("other-account").Id;
            using var signer = TicketSigner.LoadOrCreate(keyPath);
            var options = MasterTests.LocalOptions();
            var api = new AccountApi(options, store, signer, new ServerRegistry(options), _ => IPAddress.Loopback, TimeProvider.System);
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            api.Map(app);
            new PlayerPortal(options, store, new ServerRegistry(options), _ => IPAddress.Loopback, new ReleaseChangelog()).Map(app);
            await app.StartAsync(ct);
            using var client = new MasterClient(app.Urls.Single());
            Assert.Equal(401, (await Assert.ThrowsAsync<MasterException>(() => client.SetPronounsAsync("he/him", ct))).StatusCode);
            client.SessionToken = store.CreateSession(account.Id);
            Assert.Equal("they/them", (await client.AccountAsync(ct)).Pronouns);
            Assert.Equal("she/her", (await client.SetPronounsAsync("she/her", ct)).Pronouns);
            Assert.Equal("she/her", (await client.AccountAsync(ct)).Pronouns);
            Assert.Equal("they/them", store.Pronouns(other));
            Assert.Equal(400, (await Assert.ThrowsAsync<MasterException>(() => client.SetPronounsAsync("she/her?Team=1", ct))).StatusCode);
            Assert.Equal("she/her", store.Pronouns(account.Id));
            Assert.Equal("they/them", (await client.SetPronounsAsync("", ct)).Pronouns);

            string webToken = store.CreatePlayerWebSession(account.Id);
            string csrf = store.PlayerWebSession(webToken)!.Csrf;
            using var web = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            web.DefaultRequestHeaders.Add("Cookie", "svm_player=" + webToken);
            string page = await web.GetStringAsync("/account", ct);
            Assert.Contains("<h2>Pronouns</h2>", page);
            Assert.Contains("name=\"pronouns\"", page);
            Assert.DoesNotContain("Custom pronoun", page);
            Assert.DoesNotContain("value=\"custom\"", page);
            using var denied = await web.PostAsync("/account/pronouns", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["csrf"] = "wrong", ["pronouns"] = "he/him" }), ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("they/them", store.Pronouns(account.Id));
            using var saved = await web.PostAsync("/account/pronouns", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["csrf"] = csrf, ["pronouns"] = "she/her",
            }), ct);
            saved.EnsureSuccessStatusCode();
            Assert.Equal("she/her", (await client.AccountAsync(ct)).Pronouns);
            Assert.Contains("Your pronouns have been saved.", await saved.Content.ReadAsStringAsync(ct));
        }
        finally { File.Delete(keyPath); }
    }

    [Fact]
    public void SignedJoinTicketsCarryPronounsAndOlderTicketsStayNeutral()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ticket = new PlayerTicket { AccountId = 42, Name = "Alex", ServerId = "test", Expires = DateTimeOffset.UtcNow.AddMinutes(1),
            Pronouns = "he/him", Rank = 27, Titles = [1, 2] };
        string signed = ticket.Sign(key);
        Assert.True(PlayerTicket.TryVerify(signed, key, "test", DateTimeOffset.UtcNow, out var read, out _));
        Assert.Equal(ticket.Pronouns, read!.Pronouns);
        string payloadText = signed.Split('.')[0];
        byte[] payload = Convert.FromBase64String(payloadText.Replace('-', '+').Replace('_', '/') + new string('=', (4 - payloadText.Length % 4) % 4));
        byte[] old = payload[..^(1 + Encoding.ASCII.GetByteCount(ticket.Pronouns))];
        string Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string oldSigned = Url(old) + "." + Url(key.SignData(old, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.True(PlayerTicket.TryVerify(oldSigned, key, "test", DateTimeOffset.UtcNow, out var legacy, out _));
        Assert.Equal("they/them", legacy!.Pronouns);
        Assert.Equal(27, legacy.Rank);
        Assert.Equal(ticket.Titles, legacy.Titles);
        payload[^1] ^= 1;
        Assert.False(PlayerTicket.TryVerify(Url(payload) + "." + signed.Split('.')[1], key, "test", DateTimeOffset.UtcNow, out _, out _));
    }

    [Fact]
    public void AccountPronounsReachOtherClientsAndLiveChangesDoNotNeedAnAppearanceChange()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 7, SimLinkProfile.A);
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with { Dedicated = false, HostKey = "pronoun-host", AutoLaunch = false,
            HostAccount = new HostAccount(42, "Alex", Pronouns: "she/her") };
        using var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        using var rig = new OnlineRig(server, clock);
        OnlineClient Join(string name, int team, string endpoint, string? hostKey = null) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Test", () => network.Bind(endpoint), "server"), OnlineRig.ClientSettings(name, team) with { HostKey = hostKey }, rig.Now));
        var host = Join("Alex", 1, "host", "pronoun-host");
        var guest = Join("Observer", 0, "guest");
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && guest.State == OnlineClientState.Lobby &&
            host.Lobby.Local?.Map == MapState.Ok && guest.Lobby.Local?.Map == MapState.Ok, 15));
        host.SetReady(true);
        guest.SetReady(true);
        Assert.True(rig.RunUntil(() => server.Players.All(p => p.Ready), 5));
        host.Launch();
        PlayerReplicationInfo? Observed()
        {
            if (guest.Session is { } session)
                foreach (var pri in session.World.Level.AllActors<PlayerReplicationInfo>())
                    if (pri.PlayerName == "Alex") return pri;
            return null;
        }
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && guest.State == OnlineClientState.Match && Observed()?.SvmPronouns == "she/her", 25),
            $"Host={host.State} ({host.Error}), guest={guest.State} ({guest.Error}), observed={Observed()?.SvmPronouns}; " + string.Join("; ", rig.Log));
        Assert.Equal("they/them", guest.Session!.LocalPlayer!.PlayerReplicationInfo!.SvmPronouns);
        typeof(OnlineServer).GetMethod("ApplyAccountAppearances", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server,
            [new Dictionary<long, LiveAccountAppearance> { [42] = new(0, [], new AccountAppearance(), "he/him") }]);
        Assert.True(rig.RunUntil(() => Observed()?.SvmPronouns == "he/him", 5));
        Assert.Equal("he/him", server.Players.Single(p => p.AccountId == 42).Pronouns);
    }
}
