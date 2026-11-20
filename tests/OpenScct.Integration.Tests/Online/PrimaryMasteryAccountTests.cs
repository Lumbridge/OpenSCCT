using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PrimaryMasteryAccountTests
{
    private static readonly string[] LegacyMerc = ["merc.ar.golden", "merc.smg.red_tiger", "merc.shotgun.woodland"];

    [Fact]
    public void ReopenedLegacyProfilePreservesItsSelectionsAndOnlyEarnsExactRifleFinishesFromExactTotals()
    {
        var folder = Directory.CreateTempSubdirectory("OpenSCCT-primary-mastery-");
        try
        {
            string path = Path.Combine(folder.FullName, "accounts.db");
            long id;
            string session;
            using (var store = AccountStore.Open(path))
            {
                id = store.GetOrCreate("returning-merc").Id;
                Assert.Equal(NameClaim.Done, store.ClaimName(id, "ReturningMerc", false, out _));
                session = store.CreateSession(id);
                store.SetRank(id, 0, 18);
            }

            // The previous client saved v1 JSON. Its already-issued family camo and totals predate exact gun headshots.
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                db.Open();
                using var seed = db.CreateCommand();
                seed.CommandText = """
                    UPDATE schema_version SET version = 12;
                    DELETE FROM account_appearance WHERE account_id = $id;
                    UPDATE progression SET xp = $xp, total_xp = 99981 WHERE account_id = $id;
                    INSERT INTO cosmetic_grants(account_id,item,granted) VALUES ($id,'spy.body.skeleton',1234);
                    INSERT INTO achievements(account_id,achievement_id,earned,server)
                        VALUES ($id,'gilded',1234,'old'),($id,'smg_red_tiger',1234,'old'),($id,'shotgun_woodland',1234,'old');
                    INSERT INTO feat_totals(account_id,feat,total)
                        VALUES ($id,'merc.ar.kills',900),($id,'merc.ar.headshots',400),($id,'merc.smg.kills',750),
                               ($id,'merc.shotgun.kills',500),($id,'merc.m4a1s.kills',49),($id,'merc.ak47.kills',150);
                    INSERT INTO profiles(account_id,data,updated_ms) VALUES ($id,
                        '{"version":1,"settings":{"invert_y":true,"cosmetics_merc":["merc.ar.golden","merc.smg.red_tiger","merc.shotgun.woodland"]}}',1234000);
                    """;
                seed.Parameters.AddWithValue("$id", id);
                seed.Parameters.AddWithValue("$xp", PlayerRank.XpToReach(18) + 21);
                seed.ExecuteNonQuery();
            }

            using var signer = TicketSigner.LoadOrCreate(Path.Combine(folder.FullName, "ticket.pem"));
            using (var store = AccountStore.Open(path))
            {
                var api = Api(store, signer);
                var info = api.Info(store.FindById(id)!);
                var appearance = store.Appearance(id);
                Assert.Equal(LegacyMerc, appearance.Merc);
                Assert.Empty(appearance.Spy);
                Assert.Equal(18, info.Progress!.Level);
                Assert.Equal(PlayerRank.XpToReach(18) + 21, info.Progress.Xp);
                Assert.Equal(99981L, info.Progress.TotalXp);
                Assert.Contains("spy.body.skeleton", info.Unlocked);
                Assert.Equal(1234L, store.Earned(id)["gilded"]);
                Assert.All(LegacyMerc, item => Assert.Contains(item, info.Unlocked));
                Assert.DoesNotContain("merc.m4a1s.woodland", info.Unlocked);
                Assert.Contains("merc.ak47.desert", info.Unlocked);
                Assert.DoesNotContain("merc.ak47.digital", info.Unlocked);
                Assert.DoesNotContain("merc.ak47.red_tiger", info.Unlocked);
                var before = Assert.Single(info.Achievements, a => a.Id == "primary_m4a1s_woodland");
                Assert.Null(before.Earned);
                Assert.Equal((49, 50), (before.Current, before.Target));
                Assert.Equal(0, Assert.Single(info.Achievements, a => a.Id == "primary_m4a1s_red_tiger").Current);
                Assert.Equal(0, Assert.Single(info.Achievements, a => a.Id == "primary_compactsmg_woodland").Current);
                Assert.Equal(0, Assert.Single(info.Achievements, a => a.Id == "primary_pumpshotgun_woodland").Current);

                // Historical look bytes used bit + 1 in each of the three primary slots.
                var oldLook = CosmeticLook.ParseHex("47423c000000");
                var ticket = Verify(api, signer, session);
                var entitlements = new CosmeticUnlocks(ticket.Unlocks);
                Assert.Equal(oldLook, CosmeticLook.FromIds(CosmeticSide.Merc, appearance.Merc));
                Assert.Equal(oldLook, oldLook.Validate(CosmeticSide.Merc, entitlements));
                Assert.True((ticket.Unlocks[7] & 0x08) != 0);
                Assert.Equal(0x42, ticket.Unlocks[8] & 0x42);
                Assert.True(entitlements.Has(CosmeticCatalog.Find("spy.body.skeleton")!));
                Assert.Equal(750, ticket.WeaponKills[1]);
                Assert.Equal(500, ticket.WeaponKills[2]);
                Assert.Equal(49, ticket.WeaponKills[3]);
                Assert.False(store.UpdateAppearance(id, new AppearanceUpdateRequest {
                    ExpectedRevision = appearance.Revision, Merc = [.. LegacyMerc, "merc.m4a1s.woodland"],
                }).Success);

                CountRound(store, id, new() { ["merc.m4a1s.kills"] = 1 });
                Assert.Contains("primary_m4a1s_woodland", store.Earned(id).Keys);
                Assert.Equal(LegacyMerc, store.Appearance(id).Merc);
                Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest {
                    ExpectedRevision = appearance.Revision, Merc = [.. LegacyMerc, "merc.m4a1s.woodland"],
                }).Success);
                Assert.DoesNotContain("primary_m4a1s_gold", store.Earned(id).Keys);
                Assert.False(entitlements.Has(CosmeticCatalog.Find("merc.m4a1s.woodland")!));
            }

            using var reopened = AccountStore.Open(path);
            var freshTicket = Verify(Api(reopened, signer), signer, session);
            Assert.True(new CosmeticUnlocks(freshTicket.Unlocks).Has(CosmeticCatalog.Find("merc.m4a1s.woodland")!));
            Assert.Equal(50, freshTicket.WeaponKills[3]);
            var selected = reopened.Appearance(id).Merc;
            Assert.Equal([.. LegacyMerc, "merc.m4a1s.woodland"], selected);
            var liveLook = CosmeticLook.FromIds(CosmeticSide.Merc, selected)
                .Validate(CosmeticSide.Merc, new CosmeticUnlocks(freshTicket.Unlocks));
            Assert.Equal("merc.m4a1s.woodland", liveLook.WeaponItem("Svm.M4A1S")!.Id);
            Assert.Equal("merc.ar.golden", liveLook.WeaponItem("Svm.AK47")!.Id);
            Assert.All(new[] { "Svm.CompactSMG", "Svm.PumpShotgun", "Svm.BoltRifle", "Svm.DesignatedMarksman" },
                gun => Assert.Null(liveLook.WeaponItem(gun)));
            using var legacy = JsonDocument.Parse(reopened.Profile(id)!.Data);
            Assert.True(legacy.RootElement.GetProperty("settings").GetProperty("invert_y").GetBoolean());
            Assert.Equal(selected, legacy.RootElement.GetProperty("settings").GetProperty("cosmetics_merc").EnumerateArray().Select(v => v.GetString()));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void SignedServerTicketCarriesHighCosmeticBitsAndAcceptsTheOldShorterEntitlementPayload()
    {
        var folder = Directory.CreateTempSubdirectory("OpenSCCT-primary-ticket-");
        try
        {
            using var store = AccountStore.Open(":memory:");
            var account = store.GetOrCreate("signed-famas");
            Assert.Equal(NameClaim.Done, store.ClaimName(account.Id, "FamasMaster", false, out _));
            string session = store.CreateSession(account.Id);
            using var signer = TicketSigner.LoadOrCreate(Path.Combine(folder.FullName, "ticket.pem"));
            var api = Api(store, signer);
            var famasGold = CosmeticCatalog.Find("merc.famas.golden")!;
            Assert.True(famasGold.Bit >= 256);
            Assert.False(new CosmeticUnlocks(Verify(api, signer, session).Unlocks).Has(famasGold));
            for (int round = 0; round < 2; round++)
                CountRound(store, account.Id, new() { ["merc.famas.kills"] = 150, ["merc.famas.headshots"] = 100 });
            var ticket = Verify(api, signer, session);
            Assert.True(new CosmeticUnlocks(ticket.Unlocks).Has(famasGold));

            var oldBytes = new byte[32];
            oldBytes[7] = 0x08; // merc.ar.golden in a pre-mastery ticket.
            var oldTicket = ticket with { Unlocks = oldBytes, WeaponKills = ticket.WeaponKills[..12] };
            using var publicKey = PlayerTicket.ImportPublicKey(signer.PublicKey)!;
            Assert.True(PlayerTicket.TryVerify(signer.Sign(oldTicket), publicKey,
                "mastery-server", DateTimeOffset.UtcNow, out var verified, out var why), why);
            var oldRights = new CosmeticUnlocks(verified!.Unlocks);
            Assert.True(oldRights.Has(CosmeticCatalog.Find("merc.ar.golden")!));
            Assert.False(oldRights.Has(famasGold));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("ak47", true)]
    [InlineData("galil", true)]
    [InlineData("famas", true)]
    [InlineData("m4a1s", true)]
    [InlineData("compactsmg", false)]
    [InlineData("pumpshotgun", false)]
    [InlineData("boltrifle", false)]
    [InlineData("designatedmarksman", false)]
    public void EachPrimaryQualifiesOnItsOwnKillsAndHeadshotsAndOnlyApprovedModelsAwardPaint(string gun, bool hasPaint)
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("mastery-" + gun).Id;
        string kills = FeatKeys.GunKills(gun), headshots = FeatKeys.GunHeadshots(gun);
        CountRound(store, id, new() { [kills] = 150, [headshots] = 74 });
        CountRound(store, id, new() { [kills] = 150, [headshots] = 125 });
        Assert.DoesNotContain("primary_" + gun + "_blue_tiger", store.Earned(id).Keys);
        Assert.DoesNotContain("primary_" + gun + "_gold", store.Earned(id).Keys);

        CountRound(store, id, new() { [headshots] = 1, [kills] = 1 });
        string[] stages = ["woodland", "desert", "digital", "red_tiger", "blue_tiger", "gold"];
        Assert.Equal(stages.Select(s => $"primary_{gun}_{s}").Order(),
            store.Earned(id).Keys.Where(k => k.StartsWith("primary_", StringComparison.Ordinal)).Order());
        Assert.DoesNotContain("gilded", store.Earned(id).Keys);
        var earned = store.Unlocked(id);
        foreach (var stage in stages)
        {
            string reward = $"merc.{gun}." + (stage == "gold" ? "golden" : stage);
            Assert.Equal(hasPaint, CosmeticCatalog.Find(reward) != null);
            Assert.Equal(hasPaint, earned.Contains(reward));
            Assert.Equal(hasPaint, store.UpdateAppearance(id, new AppearanceUpdateRequest
            {
                ExpectedRevision = store.Appearance(id).Revision, Merc = [reward],
            }).Success);
        }
    }

    [Fact]
    public void OriginalGildedProgressStillCompletesWithItsEighteenHistoricalRequirements()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("almost-gilded").Id;
        var familyFeats = new Dictionary<string, int> { [FeatKeys.MercSniperKills] = 75, [FeatKeys.MercTerminateKills] = 49 };
        foreach (string family in new[] { "ar", "smg", "shotgun" })
        {
            familyFeats[FeatKeys.GunKills(family)] = 150;
            familyFeats[FeatKeys.GunHeadshots(family)] = 100;
        }
        CountRound(store, id, familyFeats);
        familyFeats.Remove(FeatKeys.MercSniperKills);
        familyFeats.Remove(FeatKeys.MercTerminateKills);
        CountRound(store, id, familyFeats);
        var gilded = Achievements.All.Single(a => a.Id == "gilded");
        Assert.Equal((17, 18), gilded.Goal(store.Progress(id)));
        Assert.DoesNotContain("gilded", store.Earned(id).Keys);
        CountRound(store, id, new() { [FeatKeys.MercTerminateKills] = 1 });
        Assert.Equal((18, 18), gilded.Goal(store.Progress(id)));
        Assert.Contains("gilded", store.Earned(id).Keys);
        Assert.DoesNotContain(store.Earned(id).Keys, k => k.StartsWith("primary_", StringComparison.Ordinal));
    }

    private static void CountRound(AccountStore store, long id, Dictionary<string, int> feats)
    {
        Assert.Equal(1, store.ApplyResults(new ResultsReport
        {
            Map = "MounE", WinningTeam = 1, Rows = [new() { AccountId = id, Team = 1, Feats = feats }],
        }, "Mastery test"));
    }

    private static AccountApi Api(AccountStore store, TicketSigner signer)
    {
        var options = MasterTests.LocalOptions();
        return new AccountApi(options, store, signer, new ServerRegistry(options), _ => IPAddress.Loopback, TimeProvider.System);
    }

    private static PlayerTicket Verify(AccountApi api, TicketSigner signer, string session)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + session;
        var (signed, error) = api.Ticket(context, "mastery-server");
        Assert.Null(error);
        using var publicKey = PlayerTicket.ImportPublicKey(signer.PublicKey)!;
        return Assert.IsType<PlayerTicket>(new TicketGate().Check(signed!, publicKey, "mastery-server", DateTimeOffset.UtcNow, out _));
    }
}
